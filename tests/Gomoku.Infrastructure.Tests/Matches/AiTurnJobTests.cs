using System.Text.Json;
using Gomoku.Application.Identity;
using Gomoku.Application.Matches;
using Gomoku.Infrastructure.Matches;
using Gomoku.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Gomoku.Infrastructure.Tests.Matches;

public sealed class AiTurnJobTests
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
            await using var admin = new NpgsqlConnection(connection);
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE SCHEMA \"{database.Schema}\"", admin);
            await create.ExecuteNonQueryAsync();
            return database;
        }

        public ValueTask DisposeAsync() => DropAsync();

        private async ValueTask DropAsync()
        {
            await using var admin = new NpgsqlConnection(baseConnection);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{Schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static GomokuDbContext Open(string connectionString) =>
        new(new DbContextOptionsBuilder<GomokuDbContext>().UseNpgsql(connectionString).Options);

    private static async Task<OwnerIdentity> NewGuestAsync(GomokuDbContext db)
    {
        var id = Guid.NewGuid();
        db.GuestSessions.Add(new GuestSession
        {
            Id = id, TokenHash = Guid.NewGuid().ToByteArray(),
            CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        });
        await db.SaveChangesAsync();
        return new OwnerIdentity(OwnerScope.Guest, id);
    }

    private static GameFlowService Flow(GomokuDbContext db) =>
        new(new EfGameFlowStore(db, TimeProvider.System),
            new EfAiTurnJobStore(db, TimeProvider.System));

    [Fact]
    public async Task LeaseRetryAndCrashAfterCommitNeverAddSecondAiMove()
    {
        await using var isolation = await IsolatedDatabase.CreateAsync();
        await using var db = Open(isolation.ConnectionString);
        await db.Database.MigrateAsync();
        var owner = await NewGuestAsync(db);
        var flow = Flow(db);
        var matchId = (await flow.CreateMatchAsync(owner, "create-" + Guid.NewGuid())).MatchId!.Value;
        var human = await flow.PlayAsync(owner, matchId, 10, 10, PlayerActor.Human, 0,
            "human-" + Guid.NewGuid());
        Assert.Equal(202, human.HttpStatus);
        var jobs = new EfAiTurnJobStore(db, TimeProvider.System);

        var first = await jobs.ClaimNextAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.NotNull(first);
        Assert.Equal(matchId, first.MatchId);
        Assert.Equal(1, first.ExpectedRevision);
        Assert.Null(await jobs.ClaimNextAsync(TimeSpan.FromSeconds(30), CancellationToken.None));
        await jobs.RetryAsync(first, "ai_timeout", CancellationToken.None);
        await db.AiTurnJobs.Where(x => x.Id == first.JobId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.NextAttemptAt,
                DateTimeOffset.UtcNow.AddSeconds(-1)));

        var second = await jobs.ClaimNextAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.NotNull(second);
        await jobs.RetryAsync(second, "ai_timeout", CancellationToken.None);
        await db.AiTurnJobs.Where(x => x.Id == first.JobId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.NextAttemptAt,
                DateTimeOffset.UtcNow.AddSeconds(-1)));
        var third = await jobs.ClaimNextAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.NotNull(third);
        await jobs.RetryAsync(third, "ai_timeout", CancellationToken.None);

        var pending = await flow.GetMatchAsync(owner, matchId);
        using (var json = JsonDocument.Parse(pending.Body!))
        {
            Assert.Equal("ai_retrying", json.RootElement.GetProperty("currentGame")
                .GetProperty("turnStatus").GetString());
            Assert.Equal("ai_timeout", json.RootElement.GetProperty("currentGame")
                .GetProperty("aiErrorCode").GetString());
        }

        await db.AiTurnJobs.Where(x => x.Id == first.JobId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.NextAttemptAt,
                DateTimeOffset.UtcNow.AddSeconds(-1)));
        var fourth = await jobs.ClaimNextAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.NotNull(fourth);
        var commandId = $"ai:{fourth.GameId:N}:{fourth.ExpectedRevision}";
        var played = await flow.PlayAsync(owner, matchId, 10, 11, PlayerActor.AI,
            fourth.ExpectedRevision, commandId,
            aiMetadata: new AiMoveMetadata("legal-v1", "ai_timeout"));
        Assert.Equal("ok", played.Code);
        var storedMove = await db.Moves.AsNoTracking()
            .SingleAsync(x => x.GameId == fourth.GameId && x.Stone == Gomoku.Core.Stone.O);
        Assert.Equal("legal-v1", storedMove.ActualAiVersion);
        Assert.Equal("ai_timeout", storedMove.AiFallbackReason);
        Assert.Equal(1, await db.AiTurnJobs.CountAsync(x => x.GameId == fourth.GameId));

        // Simulate process death after O committed, before CompleteAsync.
        await db.AiTurnJobs.Where(x => x.Id == fourth.JobId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LeaseUntil,
                DateTimeOffset.UtcNow.AddSeconds(-1)));
        var recovered = await jobs.ClaimNextAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.NotNull(recovered);
        var replay = await flow.PlayAsync(owner, matchId, 10, 11, PlayerActor.AI,
            recovered.ExpectedRevision, commandId);
        Assert.Equal(played.Body, replay.Body);
        await jobs.CompleteAsync(recovered, CancellationToken.None);
        Assert.Null(await jobs.ClaimNextAsync(TimeSpan.FromSeconds(30), CancellationToken.None));
        Assert.Equal(2, await db.Moves.CountAsync(x => x.GameId == fourth.GameId));
    }

    [Fact]
    public async Task NextGameOpenedByAiEnqueuesTurnWithPinnedVersion()
    {
        await using var isolation = await IsolatedDatabase.CreateAsync();
        await using var db = Open(isolation.ConnectionString);
        await db.Database.MigrateAsync();
        var owner = await NewGuestAsync(db);
        var flow = Flow(db);
        var matchId = (await flow.CreateMatchAsync(owner, "create-" + Guid.NewGuid())).MatchId!.Value;

        long revision = 0;
        for (var column = 0; column < 5; column++)
        {
            Assert.Equal("ok", (await flow.PlayAsync(owner, matchId, 0, column,
                PlayerActor.Human, revision++, "x-" + Guid.NewGuid())).Code);
            if (column < 4)
                Assert.Equal("ok", (await flow.PlayAsync(owner, matchId, 1, column,
                    PlayerActor.AI, revision++, "o-" + Guid.NewGuid())).Code);
        }

        var next = await flow.StartNextGameAsync(owner, matchId, 9, "next-" + Guid.NewGuid());
        Assert.Equal(202, next.HttpStatus);
        using var json = JsonDocument.Parse(next.Body!);
        var game = json.RootElement.GetProperty("currentGame");
        var gameId = game.GetProperty("id").GetGuid();
        Assert.Equal("O", game.GetProperty("openingStone").GetString());
        Assert.Equal("search-v1", game.GetProperty("aiVersion").GetString());
        Assert.Equal("ai_pending", game.GetProperty("turnStatus").GetString());
        Assert.Equal(1, await db.AiTurnJobs.CountAsync(x => x.GameId == gameId &&
            x.ExpectedRevision == 10 && x.CompletedAt == null));
    }
}
