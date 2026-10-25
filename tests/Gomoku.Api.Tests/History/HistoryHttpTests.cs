using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Gomoku.Application.Identity;
using Gomoku.Application.Matches;
using Gomoku.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Gomoku.Api.Tests.History;

public sealed class HistoryHttpTests
{
    private static WebApplicationFactory<Program> OpenFactory()
    {
        var connectionString = Environment.GetEnvironmentVariable("GOMOKU_TEST_POSTGRES")
            ?? throw new InvalidOperationException("Set GOMOKU_TEST_POSTGRES.");
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Gomoku", connectionString));
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object? body = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task<Guid> NewGuestAsync(HttpClient client)
    {
        using var response = await PostAsync(client, "/api/v1/guest-sessions");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("guestSessionId").GetGuid();
    }

    private static async Task<Guid> NewMatchAsync(HttpClient client, string scope)
    {
        using var response = await PostAsync(client, "/api/v1/matches?scope=" + scope,
            new { commandId = "history-" + Guid.NewGuid() });
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

    [Fact]
    public async Task CompletedGameAppearsWithOrderedMovesWhileActiveGameDoesNot()
    {
        using var factory = OpenFactory();
        using var client = factory.CreateClient();
        var guestId = await NewGuestAsync(client);
        var finishedMatch = await NewMatchAsync(client, "guest");
        await FinishAsync(factory, new OwnerIdentity(OwnerScope.Guest, guestId), finishedMatch);
        var activeMatch = await NewMatchAsync(client, "guest");

        using var list = await client.GetAsync("/api/v1/history?scope=guest");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var page = await list.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, page.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, page.GetProperty("nextCursor").ValueKind);
        var item = page.GetProperty("items")[0];
        Assert.Equal(finishedMatch, item.GetProperty("matchId").GetGuid());
        Assert.NotEqual(activeMatch, item.GetProperty("matchId").GetGuid());
        Assert.Equal("XWon", item.GetProperty("status").GetString());
        Assert.Equal("X", item.GetProperty("openingStone").GetString());
        Assert.Equal(9, item.GetProperty("moveCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("aiVersion").ValueKind);

        var gameId = item.GetProperty("gameId").GetGuid();
        using var detail = await client.GetAsync($"/api/v1/history/{gameId}?scope=guest");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var game = await detail.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(gameId, game.GetProperty("gameId").GetGuid());
        Assert.Equal(9, game.GetProperty("moves").GetArrayLength());
        Assert.Equal(1, game.GetProperty("moves")[0].GetProperty("step").GetInt32());
        Assert.Equal("Human", game.GetProperty("moves")[0].GetProperty("actor").GetString());
        Assert.Equal("X", game.GetProperty("moves")[0].GetProperty("stone").GetString());
        Assert.Equal(9, game.GetProperty("moves")[8].GetProperty("step").GetInt32());
        Assert.Equal("XWon", game.GetProperty("status").GetString());
    }

    [Fact]
    public async Task CursorPagesDoNotRepeatGamesAndInvalidInputsAreRejected()
    {
        using var factory = OpenFactory();
        using var client = factory.CreateClient();
        var guestId = await NewGuestAsync(client);
        var owner = new OwnerIdentity(OwnerScope.Guest, guestId);
        var firstMatch = await NewMatchAsync(client, "guest");
        await FinishAsync(factory, owner, firstMatch);
        var secondMatch = await NewMatchAsync(client, "guest");
        await FinishAsync(factory, owner, secondMatch);

        using var firstPage = await client.GetAsync("/api/v1/history?scope=guest&limit=1");
        Assert.Equal(HttpStatusCode.OK, firstPage.StatusCode);
        var first = await firstPage.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(secondMatch, first.GetProperty("items")[0].GetProperty("matchId").GetGuid());
        var cursor = first.GetProperty("nextCursor").GetString();
        Assert.False(string.IsNullOrWhiteSpace(cursor));

        using var nextPage = await client.GetAsync("/api/v1/history?scope=guest&limit=1&cursor=" + cursor);
        Assert.Equal(HttpStatusCode.OK, nextPage.StatusCode);
        var second = await nextPage.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(firstMatch, second.GetProperty("items")[0].GetProperty("matchId").GetGuid());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);

        using var badCursor = await client.GetAsync("/api/v1/history?scope=guest&cursor=broken");
        Assert.Equal(HttpStatusCode.BadRequest, badCursor.StatusCode);
        Assert.Equal("validation_error", (await badCursor.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString());
        using var badLimit = await client.GetAsync("/api/v1/history?scope=guest&limit=51");
        Assert.Equal(HttpStatusCode.BadRequest, badLimit.StatusCode);

        var forgedRaw = $"639000000000000000:{Guid.NewGuid():N}";
        var forged = Convert.ToBase64String(Encoding.UTF8.GetBytes(forgedRaw))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        using var forgedCursor = await client.GetAsync("/api/v1/history?scope=guest&cursor=" + forged);
        Assert.Equal(HttpStatusCode.BadRequest, forgedCursor.StatusCode);
        Assert.Equal("validation_error", (await forgedCursor.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GomokuDbContext>();
            var sameTime = new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
            await db.Games.Where(x => x.MatchId == firstMatch || x.MatchId == secondMatch)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.FinishedAt, sameTime));
        }
        using var tiedFirst = await client.GetAsync("/api/v1/history?scope=guest&limit=1");
        var tiedPage = await tiedFirst.Content.ReadFromJsonAsync<JsonElement>();
        using var tiedSecond = await client.GetAsync("/api/v1/history?scope=guest&limit=1&cursor=" +
            tiedPage.GetProperty("nextCursor").GetString());
        var tiedOlder = await tiedSecond.Content.ReadFromJsonAsync<JsonElement>();
        var tiedIds = new[]
        {
            tiedPage.GetProperty("items")[0].GetProperty("matchId").GetGuid(),
            tiedOlder.GetProperty("items")[0].GetProperty("matchId").GetGuid()
        };
        Assert.Contains(firstMatch, tiedIds);
        Assert.Contains(secondMatch, tiedIds);
        Assert.NotEqual(tiedIds[0], tiedIds[1]);
    }

    [Fact]
    public async Task GuestHistoryIsPrivateAndRemainsSeparateAfterLogin()
    {
        using var factory = OpenFactory();
        using var firstClient = factory.CreateClient();
        var guestId = await NewGuestAsync(firstClient);
        var matchId = await NewMatchAsync(firstClient, "guest");
        await FinishAsync(factory, new OwnerIdentity(OwnerScope.Guest, guestId), matchId);
        using var ownerList = await firstClient.GetAsync("/api/v1/history?scope=guest");
        var gameId = (await ownerList.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items")[0].GetProperty("gameId").GetGuid();

        using var otherClient = factory.CreateClient();
        await NewGuestAsync(otherClient);
        using var otherList = await otherClient.GetAsync("/api/v1/history?scope=guest");
        Assert.Equal(0, (await otherList.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").GetArrayLength());
        using var otherDetail = await otherClient.GetAsync($"/api/v1/history/{gameId}?scope=guest");
        Assert.Equal(HttpStatusCode.NotFound, otherDetail.StatusCode);

        using var register = await PostAsync(firstClient, "/api/v1/auth/register",
            new { email = $"history-{Guid.NewGuid():N}@example.test", password = "StrongPass!123" });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        using var accountList = await firstClient.GetAsync("/api/v1/history");
        Assert.Equal(0, (await accountList.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").GetArrayLength());
        using var guestList = await firstClient.GetAsync("/api/v1/history?scope=guest");
        Assert.Equal(1, (await guestList.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").GetArrayLength());

        using var noCookie = factory.CreateClient();
        using var denied = await noCookie.GetAsync($"/api/v1/history/{gameId}?scope=guest");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GomokuDbContext>();
        Assert.True(await db.Games.AnyAsync(x => x.Id == gameId));
    }

    [Fact]
    public async Task UnknownRuleHistoryRemainsReadableAndAccountCanReturnAfterLogin()
    {
        using var factory = OpenFactory();
        using var firstClient = factory.CreateClient();
        var email = $"history-{Guid.NewGuid():N}@example.test";
        using var register = await PostAsync(firstClient, "/api/v1/auth/register",
            new { email, password = "StrongPass!123" });
        var accountId = (await register.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("userId").GetGuid();
        var matchId = await NewMatchAsync(firstClient, "account");
        await FinishAsync(factory, new OwnerIdentity(OwnerScope.Account, accountId), matchId);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GomokuDbContext>();
            await db.Matches.Where(x => x.Id == matchId).ExecuteUpdateAsync(setters =>
                setters.SetProperty(x => x.RuleVersion, "future-rule"));
        }

        using var freshClient = factory.CreateClient();
        using var denied = await freshClient.GetAsync("/api/v1/history?scope=account");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var login = await PostAsync(freshClient, "/api/v1/auth/login",
            new { email, password = "StrongPass!123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var list = await freshClient.GetAsync("/api/v1/history?scope=account");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var item = (await list.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")[0];
        Assert.Equal("future-rule", item.GetProperty("ruleVersion").GetString());
        using var detail = await freshClient.GetAsync($"/api/v1/history/{item.GetProperty("gameId").GetGuid()}?scope=account");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal("future-rule", (await detail.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("ruleVersion").GetString());
    }
}
