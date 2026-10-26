using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gomoku.Application.Identity;
using Gomoku.Application.Matches;
using Gomoku.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StackExchange.Redis;
using Xunit;

namespace Gomoku.Api.Tests.Ranking;

public sealed class RankingHttpTests
{
    private static WebApplicationFactory<Program> OpenFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Gomoku",
                Environment.GetEnvironmentVariable("GOMOKU_TEST_POSTGRES")
                ?? throw new InvalidOperationException("Set GOMOKU_TEST_POSTGRES.")));

    [Fact]
    public async Task CompletedAccountWinAppearsOnLeaderboardButGuestWinDoesNot()
    {
        using var factory = OpenFactory();
        using var accountClient = factory.CreateClient();
        var email = $"rank-{Guid.NewGuid():N}@example.test";
        using var register = await PostAsync(accountClient, "/api/v1/auth/register",
            new { email, password = "StrongPass!123" });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        Guid accountId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GomokuDbContext>();
            accountId = await db.Users.Where(x => x.Email == email).Select(x => x.Id).SingleAsync();
        }
        using var accountMatch = await PostAsync(accountClient, "/api/v1/matches?scope=account",
            new { commandId = "rank-" + Guid.NewGuid() });
        var matchId = (await accountMatch.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("matchId").GetGuid();
        await FinishXAsync(factory, new OwnerIdentity(OwnerScope.Account, accountId), matchId);

        using var guestClient = factory.CreateClient();
        using var guest = await PostAsync(guestClient, "/api/v1/guest-sessions");
        var guestId = (await guest.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("guestSessionId").GetGuid();
        using var guestMatch = await PostAsync(guestClient, "/api/v1/matches?scope=guest",
            new { commandId = "guest-rank-" + Guid.NewGuid() });
        await FinishXAsync(factory, new OwnerIdentity(OwnerScope.Guest, guestId),
            (await guestMatch.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("matchId").GetGuid());

        using var publicClient = factory.CreateClient();
        using var response = await publicClient.GetAsync("/api/v1/leaderboard?limit=50");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = page.GetProperty("items").EnumerateArray().ToArray();
        var accountItem = Assert.Single(items, item =>
            item.GetProperty("accountId").GetGuid() == accountId);
        Assert.Equal(3, accountItem.GetProperty("points").GetInt32());
        Assert.Equal(1, accountItem.GetProperty("rank").GetInt32());
        Assert.DoesNotContain(items, item => item.GetProperty("accountId").GetGuid() == guestId);

        using var mine = await accountClient.GetAsync("/api/v1/leaderboard/me");
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        var mineBody = await mine.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, mineBody.GetProperty("points").GetInt32());
        Assert.Equal(1, mineBody.GetProperty("rank").GetInt32());
        using var guestMine = await guestClient.GetAsync("/api/v1/leaderboard/me");
        Assert.Equal(HttpStatusCode.Unauthorized, guestMine.StatusCode);
    }

    [Fact]
    public async Task RedisProjectionPublishesAndRebuildsAfterKeyLoss()
    {
        var redis = Environment.GetEnvironmentVariable("GOMOKU_TEST_REDIS");
        if (string.IsNullOrWhiteSpace(redis)) return;
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Gomoku",
                Environment.GetEnvironmentVariable("GOMOKU_TEST_POSTGRES")
                ?? throw new InvalidOperationException("Set GOMOKU_TEST_POSTGRES."));
            builder.UseSetting("ConnectionStrings:Redis", redis);
        });
        using var client = factory.CreateClient();
        var email = $"redis-rank-{Guid.NewGuid():N}@example.test";
        using var register = await PostAsync(client, "/api/v1/auth/register",
            new { email, password = "StrongPass!123" });
        Guid accountId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GomokuDbContext>();
            accountId = await db.Users.Where(x => x.Email == email).Select(x => x.Id).SingleAsync();
        }
        using var match = await PostAsync(client, "/api/v1/matches?scope=account",
            new { commandId = "redis-rank-" + Guid.NewGuid() });
        await FinishXAsync(factory, new OwnerIdentity(OwnerScope.Account, accountId),
            (await match.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("matchId").GetGuid());

        await using var connection = await ConnectionMultiplexer.ConnectAsync(redis);
        var redisDb = connection.GetDatabase();
        const string board = "gomoku:development:ranking:v1:alltime";
        const string ready = "gomoku:development:ranking:v1:ready";
        Assert.True(await WaitUntilAsync(async () =>
            await redisDb.KeyExistsAsync(ready) &&
            await redisDb.SortedSetScoreAsync(board, accountId.ToString("N")) == 3));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GomokuDbContext>();
            Assert.Equal(1, await db.RankingOutbox.CountAsync(x =>
                x.UserId == accountId && x.PublishedAt != null));
        }

        var postgres = Environment.GetEnvironmentVariable("GOMOKU_TEST_POSTGRES")!;
        await using var blocker = new NpgsqlConnection(postgres);
        await blocker.OpenAsync();
        await using var lockTransaction = await blocker.BeginTransactionAsync();
        await using (var acquire = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(773982431234)", blocker, lockTransaction))
            await acquire.ExecuteNonQueryAsync();
        await redisDb.KeyDeleteAsync(board);
        await redisDb.KeyDeleteAsync(ready);
        await Task.Delay(1500);
        Assert.False(await redisDb.KeyExistsAsync(ready));
        await lockTransaction.CommitAsync();
        Assert.True(await WaitUntilAsync(async () =>
            await redisDb.KeyExistsAsync(ready) &&
            await redisDb.SortedSetScoreAsync(board, accountId.ToString("N")) == 3));
        using var response = await client.GetAsync("/api/v1/leaderboard?limit=50");
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(page.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("accountId").GetGuid() == accountId);
    }

    [Fact]
    public async Task RedisOutageStillReturnsAwardedPointsFromPostgres()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Gomoku",
                Environment.GetEnvironmentVariable("GOMOKU_TEST_POSTGRES")
                ?? throw new InvalidOperationException("Set GOMOKU_TEST_POSTGRES."));
            builder.UseSetting("ConnectionStrings:Redis",
                "127.0.0.1:1,abortConnect=false,connectTimeout=100,asyncTimeout=100");
        });
        using var client = factory.CreateClient();
        var email = $"outage-rank-{Guid.NewGuid():N}@example.test";
        using var register = await PostAsync(client, "/api/v1/auth/register",
            new { email, password = "StrongPass!123" });
        Guid accountId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GomokuDbContext>();
            accountId = await db.Users.Where(x => x.Email == email).Select(x => x.Id).SingleAsync();
        }
        using var match = await PostAsync(client, "/api/v1/matches?scope=account",
            new { commandId = "outage-rank-" + Guid.NewGuid() });
        await FinishXAsync(factory, new OwnerIdentity(OwnerScope.Account, accountId),
            (await match.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("matchId").GetGuid());

        var timer = Stopwatch.StartNew();
        using var response = await client.GetAsync("/api/v1/leaderboard?limit=50");
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(3),
            $"Redis outage delayed ranking fallback by {timer.Elapsed}.");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray();
        Assert.Contains(items, x => x.GetProperty("accountId").GetGuid() == accountId &&
            x.GetProperty("points").GetInt32() == 3);
    }

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> predicate)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (await predicate()) return true;
            await Task.Delay(200);
        }
        return false;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object? body = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task FinishXAsync(WebApplicationFactory<Program> factory,
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
