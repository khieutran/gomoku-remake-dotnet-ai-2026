using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gomoku.AI;
using Gomoku.Application.Identity;
using Gomoku.Application.Matches;
using Gomoku.Core;
using Gomoku.Infrastructure.Matches;
using Gomoku.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Gomoku.Api.Tests.Matches;

public sealed class AiMatchHttpTests
{
    private sealed class IsolatedDatabase(string baseConnection, string schema) : IAsyncDisposable
    {
        public string Schema { get; } = schema;
        public string ConnectionString => baseConnection + ";Search Path=" + Schema;

        public static async Task<IsolatedDatabase> CreateAsync()
        {
            var connection = Environment.GetEnvironmentVariable("GOMOKU_TEST_POSTGRES")
                ?? throw new InvalidOperationException("Set GOMOKU_TEST_POSTGRES.");
            var database = new IsolatedDatabase(connection, "ai_" + Guid.NewGuid().ToString("N"));
            await using (var admin = new NpgsqlConnection(connection))
            {
                await admin.OpenAsync();
                await using var create = new NpgsqlCommand($"CREATE SCHEMA \"{database.Schema}\"", admin);
                await create.ExecuteNonQueryAsync();
            }
            await using (var db = new GomokuDbContext(new DbContextOptionsBuilder<GomokuDbContext>()
                .UseNpgsql(database.ConnectionString).Options))
                await db.Database.MigrateAsync();
            return database;
        }

        public WebApplicationFactory<Program> OpenFactory(bool workerEnabled = true,
            bool blockingSelector = false, bool budgetFallbackSelector = false) =>
            new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:Gomoku", ConnectionString);
                builder.UseSetting("AiWorker:Enabled", workerEnabled.ToString());
                if (blockingSelector)
                    builder.ConfigureTestServices(services =>
                        services.AddSingleton<IAiMoveSelector>(new BlockingSelector()));
                if (budgetFallbackSelector)
                    builder.ConfigureTestServices(services =>
                        services.AddSingleton<IAiMoveSelector>(new BudgetFallbackSelector()));
            });

        public async ValueTask DisposeAsync()
        {
            await using var admin = new NpgsqlConnection(baseConnection);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{Schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class BlockingSelector : IAiMoveSelector
    {
        public Position? SelectMove(GameSnapshot game, Stone side, TimeSpan budget,
            CancellationToken cancellationToken)
        {
            Thread.Sleep(TimeSpan.FromSeconds(4));
            return new Position(10, 11);
        }
    }

    private sealed class BudgetFallbackSelector : IAiMoveAnalyzer
    {
        public Position? SelectMove(GameSnapshot game, Stone side, TimeSpan budget,
            CancellationToken cancellationToken) => throw new InvalidOperationException(
                "Worker must consume the detailed search result.");

        public SearchResult Analyze(GameSnapshot game, Stone side, TimeSpan budget,
            CancellationToken cancellationToken) => new(
                new Position(10, 11), SearchDecisionReason.Fallback, 0, 0, true);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("requestToken").GetString());
        request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task MatchCreationHonorsAllStoneAndOpeningChoices()
    {
        await using var database = await IsolatedDatabase.CreateAsync();
        using var factory = database.OpenFactory(workerEnabled: false);
        using var client = factory.CreateClient();
        using var guest = await PostAsync(client, "/api/v1/guest-sessions", new { });
        Assert.Equal(HttpStatusCode.Created, guest.StatusCode);

        foreach (var (humanStone, humanFirst, openingStone, turnStatus) in new[]
        {
            ("X", true, "X", "human"),
            ("X", false, "O", "ai_pending"),
            ("O", true, "O", "human"),
            ("O", false, "X", "ai_pending")
        })
        {
            using var response = await PostAsync(client, "/api/v1/matches?scope=guest",
                new { commandId = "setup-" + Guid.NewGuid(), humanStone, humanFirst });
            Assert.Equal(humanFirst ? HttpStatusCode.Created : HttpStatusCode.Accepted,
                response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(humanStone, body.GetProperty("humanStone").GetString());
            Assert.Equal(openingStone, body.GetProperty("currentGame").GetProperty("openingStone").GetString());
            Assert.Equal(turnStatus, body.GetProperty("currentGame").GetProperty("turnStatus").GetString());
        }
        await using var db = new GomokuDbContext(new DbContextOptionsBuilder<GomokuDbContext>()
            .UseNpgsql(database.ConnectionString).Options);
        Assert.Equal(2, await db.AiTurnJobs.CountAsync());
    }

    [Fact]
    public async Task BotCanOpenWithXThenAcceptHumanOAndReplyWithX()
    {
        await using var database = await IsolatedDatabase.CreateAsync();
        using var factory = database.OpenFactory();
        using var client = factory.CreateClient();
        using var guest = await PostAsync(client, "/api/v1/guest-sessions", new { });
        Assert.Equal(HttpStatusCode.Created, guest.StatusCode);
        using var created = await PostAsync(client, "/api/v1/matches?scope=guest",
            new { commandId = "create-o-" + Guid.NewGuid(), humanStone = "O", humanFirst = false });
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        var start = await created.Content.ReadFromJsonAsync<JsonElement>();
        var matchId = start.GetProperty("matchId").GetGuid();

        async Task<JsonElement> WaitForRevision(long expected)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!deadline.IsCancellationRequested)
            {
                var state = await client.GetFromJsonAsync<JsonElement>(
                    $"/api/v1/matches/{matchId}?scope=guest");
                if (state.GetProperty("revision").GetInt64() >= expected) return state;
                await Task.Delay(100, deadline.Token);
            }
            throw new TimeoutException("Bot did not finish its X turn.");
        }

        var opened = await WaitForRevision(1);
        Assert.Equal("X", opened.GetProperty("currentGame").GetProperty("moves")[0]
            .GetProperty("stone").GetString());
        Assert.Equal("human", opened.GetProperty("currentGame").GetProperty("turnStatus").GetString());
        using var human = await PostAsync(client, $"/api/v1/matches/{matchId}/moves?scope=guest",
            new { commandId = "human-o-" + Guid.NewGuid(), row = 0, column = 0, expectedRevision = 1 });
        Assert.Equal(HttpStatusCode.Accepted, human.StatusCode);
        var answered = await WaitForRevision(3);
        var moves = answered.GetProperty("currentGame").GetProperty("moves");
        Assert.Equal("O", moves[1].GetProperty("stone").GetString());
        Assert.Equal("X", moves[2].GetProperty("stone").GetString());
    }

    [Fact]
    public async Task HumanMoveReturnsBeforeAiAndWorkerPersistsOneReply()
    {
        await using var database = await IsolatedDatabase.CreateAsync();
        using var factory = database.OpenFactory();
        using var client = factory.CreateClient();
        using var secondFactory = database.OpenFactory();
        using var secondInstance = secondFactory.CreateClient();
        using var guest = await PostAsync(client, "/api/v1/guest-sessions", new { });
        Assert.Equal(HttpStatusCode.Created, guest.StatusCode);

        using var created = await PostAsync(client, "/api/v1/matches?scope=guest",
            new { commandId = "create-" + Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var start = await created.Content.ReadFromJsonAsync<JsonElement>();
        var matchId = start.GetProperty("matchId").GetGuid();
        Assert.Equal("human", start.GetProperty("currentGame").GetProperty("turnStatus").GetString());
        Assert.Equal("search-v1", start.GetProperty("currentGame").GetProperty("aiVersion").GetString());

        var commandId = "human-" + Guid.NewGuid();
        using var played = await PostAsync(client, $"/api/v1/matches/{matchId}/moves?scope=guest",
            new { commandId, row = 10, column = 10, expectedRevision = 0 });
        Assert.Equal(HttpStatusCode.Accepted, played.StatusCode);
        var firstBody = await played.Content.ReadAsStringAsync();
        using var firstJson = JsonDocument.Parse(firstBody);
        Assert.Equal(1, firstJson.RootElement.GetProperty("revision").GetInt64());
        Assert.Equal("ai_pending", firstJson.RootElement.GetProperty("currentGame").GetProperty("turnStatus").GetString());
        Assert.Single(firstJson.RootElement.GetProperty("currentGame").GetProperty("moves").EnumerateArray());

        using var retry = await PostAsync(client, $"/api/v1/matches/{matchId}/moves?scope=guest",
            new { commandId, row = 10, column = 10, expectedRevision = 0 });
        Assert.Equal(firstBody, await retry.Content.ReadAsStringAsync());

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        JsonElement state;
        do
        {
            using var read = await client.GetAsync($"/api/v1/matches/{matchId}?scope=guest", deadline.Token);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            state = await read.Content.ReadFromJsonAsync<JsonElement>(deadline.Token);
            if (state.GetProperty("revision").GetInt64() == 2) break;
            await Task.Delay(100, deadline.Token);
        } while (true);

        Assert.Equal("human", state.GetProperty("currentGame").GetProperty("turnStatus").GetString());
        var moves = state.GetProperty("currentGame").GetProperty("moves").EnumerateArray().ToArray();
        Assert.Equal(2, moves.Length);
        Assert.Equal("X", moves[0].GetProperty("stone").GetString());
        Assert.Equal("O", moves[1].GetProperty("stone").GetString());
        Assert.NotEqual((10, 10), (moves[1].GetProperty("row").GetInt32(),
            moves[1].GetProperty("column").GetInt32()));
        await using var verify = new GomokuDbContext(new DbContextOptionsBuilder<GomokuDbContext>()
            .UseNpgsql(database.ConnectionString).Options);
        Assert.Equal(2, await verify.Moves.CountAsync());
    }

    [Fact]
    public async Task ClientCannotSubmitAiActor()
    {
        await using var database = await IsolatedDatabase.CreateAsync();
        using var factory = database.OpenFactory();
        using var client = factory.CreateClient();
        using var guest = await PostAsync(client, "/api/v1/guest-sessions", new { });
        using var created = await PostAsync(client, "/api/v1/matches?scope=guest",
            new { commandId = "create-" + Guid.NewGuid() });
        var matchId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("matchId").GetGuid();
        using var gameRead = await client.GetAsync($"/api/v1/matches/{matchId}?scope=guest");
        var gameId = (await gameRead.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("currentGame").GetProperty("id").GetGuid();

        using var result = await PostAsync(client, $"/api/v1/matches/{matchId}/moves?scope=guest",
            new { commandId = Guid.NewGuid().ToString(), row = 1, column = 1,
                expectedRevision = 0, actor = "AI" });
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("validation_error", (await result.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString());

        using var reserved = await PostAsync(client, $"/api/v1/matches/{matchId}/moves?scope=guest",
            new { commandId = $"ai:{gameId:N}:1", row = 1, column = 1, expectedRevision = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, reserved.StatusCode);
    }

    [Fact]
    public async Task AccountCanStartNextGameAndAiOpensIt()
    {
        await using var database = await IsolatedDatabase.CreateAsync();
        Guid matchId;
        var email = $"ai-next-{Guid.NewGuid():N}@example.test";
        using (var setupFactory = database.OpenFactory(workerEnabled: false))
        using (var setupClient = setupFactory.CreateClient())
        {
            using var register = await PostAsync(setupClient, "/api/v1/auth/register",
                new { email, password = "StrongPass!123" });
            Assert.Equal(HttpStatusCode.Created, register.StatusCode);
            using var created = await PostAsync(setupClient, "/api/v1/matches",
                new { commandId = "create-" + Guid.NewGuid() });
            matchId = (await created.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("matchId").GetGuid();

            await using var db = new GomokuDbContext(new DbContextOptionsBuilder<GomokuDbContext>()
                .UseNpgsql(database.ConnectionString).Options);
            var userId = await db.Users.Where(user => user.Email == email)
                .Select(user => user.Id).SingleAsync();
            var flow = new GameFlowService(new EfGameFlowStore(db, TimeProvider.System));
            var owner = new OwnerIdentity(OwnerScope.Account, userId);
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

        using var factory = database.OpenFactory();
        using var client = factory.CreateClient();
        using var login = await PostAsync(client, "/api/v1/auth/login",
            new { email, password = "StrongPass!123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var commandId = "next-" + Guid.NewGuid();
        using var next = await PostAsync(client, $"/api/v1/matches/{matchId}/next-game",
            new { commandId, expectedRevision = 9 });
        Assert.Equal(HttpStatusCode.Accepted, next.StatusCode);
        var nextBody = await next.Content.ReadAsStringAsync();
        using var nextJson = JsonDocument.Parse(nextBody);
        Assert.Equal("O", nextJson.RootElement.GetProperty("currentGame")
            .GetProperty("openingStone").GetString());
        Assert.Equal("ai_pending", nextJson.RootElement.GetProperty("currentGame")
            .GetProperty("turnStatus").GetString());
        Assert.Equal("search-v1", nextJson.RootElement.GetProperty("currentGame")
            .GetProperty("aiVersion").GetString());
        using var duplicate = await PostAsync(client, $"/api/v1/matches/{matchId}/next-game",
            new { commandId, expectedRevision = 9 });
        Assert.Equal(nextBody, await duplicate.Content.ReadAsStringAsync());

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        JsonElement state;
        do
        {
            using var read = await client.GetAsync($"/api/v1/matches/{matchId}", deadline.Token);
            state = await read.Content.ReadFromJsonAsync<JsonElement>(deadline.Token);
            if (state.GetProperty("revision").GetInt64() == 11) break;
            await Task.Delay(100, deadline.Token);
        } while (true);
        var game = state.GetProperty("currentGame");
        Assert.Equal("human", game.GetProperty("turnStatus").GetString());
        var moves = game.GetProperty("moves").EnumerateArray().ToArray();
        Assert.Single(moves);
        Assert.Equal("O", moves[0].GetProperty("stone").GetString());
    }

    [Fact]
    public async Task BlockedSelectorUsesLegalFallbackAndPersistsReason()
    {
        await using var database = await IsolatedDatabase.CreateAsync();
        using var factory = database.OpenFactory(blockingSelector: true);
        using var client = factory.CreateClient();
        using var guest = await PostAsync(client, "/api/v1/guest-sessions", new { });
        using var created = await PostAsync(client, "/api/v1/matches?scope=guest",
            new { commandId = "create-" + Guid.NewGuid() });
        var matchId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("matchId").GetGuid();
        using var played = await PostAsync(client, $"/api/v1/matches/{matchId}/moves?scope=guest",
            new { commandId = "move-" + Guid.NewGuid(), row = 10, column = 10, expectedRevision = 0 });
        Assert.Equal(HttpStatusCode.Accepted, played.StatusCode);

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(7));
        JsonElement state;
        do
        {
            using var read = await client.GetAsync($"/api/v1/matches/{matchId}?scope=guest", deadline.Token);
            state = await read.Content.ReadFromJsonAsync<JsonElement>(deadline.Token);
            if (state.GetProperty("revision").GetInt64() == 2) break;
            await Task.Delay(100, deadline.Token);
        } while (true);
        Assert.Equal(2, state.GetProperty("currentGame").GetProperty("moves").GetArrayLength());
        await using var db = new GomokuDbContext(new DbContextOptionsBuilder<GomokuDbContext>()
            .UseNpgsql(database.ConnectionString).Options);
        var gameId = state.GetProperty("currentGame").GetProperty("id").GetGuid();
        var aiMove = await db.Moves.AsNoTracking().SingleAsync(x => x.GameId == gameId &&
            x.Stone == Stone.O);
        Assert.Equal("legal-v1", aiMove.ActualAiVersion);
        Assert.Equal("ai_timeout", aiMove.AiFallbackReason);
    }

    [Fact]
    public async Task PinnedLegalVersionUsesLegacySelectorWithoutSearch()
    {
        await using var database = await IsolatedDatabase.CreateAsync();
        using var factory = database.OpenFactory(blockingSelector: true);
        using var client = factory.CreateClient();
        using var guest = await PostAsync(client, "/api/v1/guest-sessions", new { });
        using var created = await PostAsync(client, "/api/v1/matches?scope=guest",
            new { commandId = "create-" + Guid.NewGuid() });
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();
        var matchId = createdBody.GetProperty("matchId").GetGuid();
        var gameId = createdBody.GetProperty("currentGame").GetProperty("id").GetGuid();
        await using (var db = new GomokuDbContext(new DbContextOptionsBuilder<GomokuDbContext>()
            .UseNpgsql(database.ConnectionString).Options))
            await db.Games.Where(x => x.Id == gameId).ExecuteUpdateAsync(setters =>
                setters.SetProperty(x => x.AiVersion, "legal-v1"));

        using var played = await PostAsync(client, $"/api/v1/matches/{matchId}/moves?scope=guest",
            new { commandId = "move-" + Guid.NewGuid(), row = 10, column = 10, expectedRevision = 0 });
        Assert.Equal(HttpStatusCode.Accepted, played.StatusCode);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(7));
        do
        {
            using var read = await client.GetAsync($"/api/v1/matches/{matchId}?scope=guest", deadline.Token);
            var state = await read.Content.ReadFromJsonAsync<JsonElement>(deadline.Token);
            if (state.GetProperty("revision").GetInt64() == 2) break;
            await Task.Delay(100, deadline.Token);
        } while (true);

        await using var verify = new GomokuDbContext(new DbContextOptionsBuilder<GomokuDbContext>()
            .UseNpgsql(database.ConnectionString).Options);
        var aiMove = await verify.Moves.AsNoTracking().SingleAsync(x => x.GameId == gameId &&
            x.Stone == Stone.O);
        Assert.Equal("legal-v1", aiMove.ActualAiVersion);
        Assert.Null(aiMove.AiFallbackReason);
    }

    [Fact]
    public async Task SearchBudgetFallbackPersistsActualVersionAndReason()
    {
        await using var database = await IsolatedDatabase.CreateAsync();
        using var factory = database.OpenFactory(budgetFallbackSelector: true);
        using var client = factory.CreateClient();
        using var guest = await PostAsync(client, "/api/v1/guest-sessions", new { });
        using var created = await PostAsync(client, "/api/v1/matches?scope=guest",
            new { commandId = "create-" + Guid.NewGuid() });
        var matchId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("matchId").GetGuid();
        using var played = await PostAsync(client, $"/api/v1/matches/{matchId}/moves?scope=guest",
            new { commandId = "move-" + Guid.NewGuid(), row = 10, column = 10, expectedRevision = 0 });
        Assert.Equal(HttpStatusCode.Accepted, played.StatusCode);

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(7));
        Guid gameId;
        do
        {
            using var read = await client.GetAsync($"/api/v1/matches/{matchId}?scope=guest", deadline.Token);
            var state = await read.Content.ReadFromJsonAsync<JsonElement>(deadline.Token);
            if (state.GetProperty("revision").GetInt64() == 2)
            {
                gameId = state.GetProperty("currentGame").GetProperty("id").GetGuid();
                break;
            }
            await Task.Delay(100, deadline.Token);
        } while (true);

        await using var db = new GomokuDbContext(new DbContextOptionsBuilder<GomokuDbContext>()
            .UseNpgsql(database.ConnectionString).Options);
        var aiMove = await db.Moves.AsNoTracking().SingleAsync(x => x.GameId == gameId &&
            x.Stone == Stone.O);
        Assert.Equal("legal-v1", aiMove.ActualAiVersion);
        Assert.Equal("ai_budget_expired", aiMove.AiFallbackReason);
    }
}
