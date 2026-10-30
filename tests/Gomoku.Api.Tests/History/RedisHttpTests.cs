using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gomoku.Application.History;
using Gomoku.Application.Identity;
using Gomoku.Application.Matches;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace Gomoku.Api.Tests.History;

public sealed class RedisHttpTests
{
    private static string PostgresConnection() =>
        Environment.GetEnvironmentVariable("GOMOKU_TEST_POSTGRES")
        ?? throw new InvalidOperationException("Set GOMOKU_TEST_POSTGRES.");

    [Fact]
    public async Task RedisOutageDoesNotChangeHistoryResponse()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Gomoku", PostgresConnection());
            builder.UseSetting("ConnectionStrings:Redis",
                "127.0.0.1:1,abortConnect=false,connectTimeout=100,asyncTimeout=100");
        });
        using var client = factory.CreateClient();
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/guest-sessions");
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        using var guest = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, guest.StatusCode);

        var timer = Stopwatch.StartNew();
        using var list = await client.GetAsync("/api/v1/history?scope=guest");
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(3),
            $"Redis outage delayed PostgreSQL fallback by {timer.Elapsed}.");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(0, (await list.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task ConfiguredRedisStoresHistoryDataOnTheRealServer()
    {
        var redis = Environment.GetEnvironmentVariable("GOMOKU_TEST_REDIS");
        if (string.IsNullOrWhiteSpace(redis)) return; // Live gate is run by scripts/test-cache.sh.

        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Gomoku", PostgresConnection());
            builder.UseSetting("ConnectionStrings:Redis", redis);
        });
        using var scope = factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IHistoryCache>();
        var redisCache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var key = "task7-live-" + Guid.NewGuid().ToString("N");
        await cache.SetPageAsync(key, new HistoryPage([], null), TimeSpan.FromMinutes(1));

        Assert.NotNull(await redisCache.GetAsync(key));
        HistoryPage? cachedPage = null;
        for (var attempt = 0; attempt < 3 && cachedPage is null; attempt++)
        {
            // A scoped cache falls back after a transient timeout. A new request
            // gets a new scope and can read the value already stored in Redis.
            using var readScope = factory.Services.CreateScope();
            cachedPage = await readScope.ServiceProvider.GetRequiredService<IHistoryCache>()
                .GetPageAsync(key);
        }
        Assert.NotNull(cachedPage);
        Assert.Empty(cachedPage.Items);

        using var client = factory.CreateClient();
        using var guest = await PostAsync(client, "/api/v1/guest-sessions");
        Assert.Equal(HttpStatusCode.Created, guest.StatusCode);
        var guestId = (await guest.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("guestSessionId").GetGuid();
        var owner = new OwnerIdentity(OwnerScope.Guest, guestId);

        var firstMatch = await CreateMatchAsync(client);
        await FinishAsync(factory, owner, firstMatch);
        using var firstList = await client.GetAsync("/api/v1/history?scope=guest");
        Assert.Equal(HttpStatusCode.OK, firstList.StatusCode);
        var firstPage = await firstList.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, firstPage.GetProperty("items").GetArrayLength());
        Assert.NotNull(await redisCache.GetAsync(
            $"history:v2:page:Guest:{guestId:N}:r1:l20:cfirst"));
        await using (var connection = await ConnectionMultiplexer.ConnectAsync(redis))
            Assert.True(await connection.GetDatabase().KeyExistsAsync(
                $"gomoku:development:history:v2:page:Guest:{guestId:N}:r1:l20:cfirst"));

        var gameId = firstPage.GetProperty("items")[0].GetProperty("gameId").GetGuid();
        using var ownerDetail = await client.GetAsync($"/api/v1/history/{gameId}?scope=guest");
        Assert.Equal(HttpStatusCode.OK, ownerDetail.StatusCode);
        using var otherClient = factory.CreateClient();
        using var otherGuest = await PostAsync(otherClient, "/api/v1/guest-sessions");
        using var otherDetail = await otherClient.GetAsync($"/api/v1/history/{gameId}?scope=guest");
        Assert.Equal(HttpStatusCode.NotFound, otherDetail.StatusCode);

        var secondMatch = await CreateMatchAsync(client);
        await FinishAsync(factory, owner, secondMatch);
        using var updatedList = await client.GetAsync("/api/v1/history?scope=guest");
        var updatedPage = await updatedList.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, updatedPage.GetProperty("items").GetArrayLength());
        Assert.Equal(secondMatch, updatedPage.GetProperty("items")[0].GetProperty("matchId").GetGuid());
        Assert.NotNull(await redisCache.GetAsync(
            $"history:v2:page:Guest:{guestId:N}:r2:l20:cfirst"));
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object? body = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task<Guid> CreateMatchAsync(HttpClient client)
    {
        using var response = await PostAsync(client, "/api/v1/matches?scope=guest",
            new { commandId = "redis-live-" + Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("matchId").GetGuid();
    }

    private static async Task FinishAsync(WebApplicationFactory<Program> factory,
        OwnerIdentity owner, Guid matchId)
    {
        using var scope = factory.Services.CreateScope();
        var flow = scope.ServiceProvider.GetRequiredService<GameFlowService>();
        long revision = 0;
        for (var column = 0; column < 5; column++)
        {
            Assert.Equal("ok", (await flow.PlayAsync(owner, matchId, 0, column,
                PlayerActor.Human, revision++, "x-" + Guid.NewGuid())).Code);
            if (column < 4)
                Assert.Equal("ok", (await flow.PlayAsync(owner, matchId, 1, column,
                    PlayerActor.AI, revision++, "o-" + Guid.NewGuid())).Code);
        }
    }
}
