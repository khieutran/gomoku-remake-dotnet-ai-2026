using System.Text.Json;
using Gomoku.Application.Identity;
using Gomoku.Application.Matches;
using Gomoku.Infrastructure.Matches;
using Gomoku.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Gomoku.Infrastructure.Tests.Matches;

public sealed class GameFlowTests
{
    private static GomokuDbContext Open()
    {
        var connectionString = Environment.GetEnvironmentVariable("GOMOKU_TEST_POSTGRES")
            ?? throw new InvalidOperationException("Set GOMOKU_TEST_POSTGRES.");
        return new GomokuDbContext(new DbContextOptionsBuilder<GomokuDbContext>()
            .UseNpgsql(connectionString).Options);
    }

    private static async Task<OwnerIdentity> NewGuestAsync(GomokuDbContext db)
    {
        var id = Guid.NewGuid();
        db.GuestSessions.Add(new GuestSession
        {
            Id = id,
            TokenHash = Guid.NewGuid().ToByteArray(),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        });
        await db.SaveChangesAsync();
        return new OwnerIdentity(OwnerScope.Guest, id);
    }

    private static async Task<OwnerIdentity> NewAccountAsync(GomokuDbContext db)
    {
        var id = Guid.NewGuid();
        db.Users.Add(new ApplicationUser
        {
            Id = id,
            UserName = "owner-" + id,
            NormalizedUserName = ("OWNER-" + id).ToUpperInvariant()
        });
        await db.SaveChangesAsync();
        return new OwnerIdentity(OwnerScope.Account, id);
    }

    private static GameFlowService Service(GomokuDbContext db) =>
        new(new EfGameFlowStore(db, TimeProvider.System));

    [Fact]
    public async Task CreateAndRetryKeepOneOwnedMatchAndOriginalBody()
    {
        await using var db = Open();
        await db.Database.MigrateAsync();
        var owner = await NewGuestAsync(db);
        var other = await NewGuestAsync(db);
        var flow = Service(db);

        var created = await flow.CreateMatchAsync(owner, "create-" + Guid.NewGuid());
        Assert.Equal("ok", created.Code);
        Assert.Equal(201, created.HttpStatus);
        Assert.NotNull(created.MatchId);
        var retry = await flow.CreateMatchAsync(owner, created.CommandId!);
        Assert.Equal(created.Body, retry.Body);
        Assert.Equal(created.MatchId, retry.MatchId);
        Assert.Equal(1, await db.Matches.CountAsync(x => x.Id == created.MatchId));
        Assert.Equal("not_found", (await flow.GetMatchAsync(other, created.MatchId!.Value)).Code);
    }

    [Fact]
    public async Task EndingMovePersistsScoreAndNextGameReplaysAfterReopen()
    {
        Guid matchId;
        OwnerIdentity owner;
        await using (var db = Open())
        {
            await db.Database.MigrateAsync();
            owner = await NewGuestAsync(db);
            var flow = Service(db);
            var created = await flow.CreateMatchAsync(owner, "create-" + Guid.NewGuid());
            matchId = created.MatchId!.Value;
            Assert.Equal("invalid_move", (await flow.PlayAsync(owner, matchId, -1, 0,
                PlayerActor.Human, 0, "invalid-" + Guid.NewGuid())).Code);

            long revision = 0;
            for (var column = 0; column < 5; column++)
            {
                Assert.Equal("ok", (await flow.PlayAsync(owner, matchId, 0, column,
                    PlayerActor.Human, revision++, "x-" + Guid.NewGuid())).Code);
                if (column < 4)
                {
                    Assert.Equal("ok", (await flow.PlayAsync(owner, matchId, 1, column,
                        PlayerActor.AI, revision++, "o-" + Guid.NewGuid())).Code);
                }
            }

            Assert.Equal(9, revision);
            var finished = await db.Games.SingleAsync(x => x.MatchId == matchId);
            Assert.Equal(StoredGameState.Finished, finished.State);
            Assert.Equal(3, (await db.Matches.SingleAsync(x => x.Id == matchId)).XScore);
            Assert.Equal(1, (await db.GuestSessions.SingleAsync(x => x.Id == owner.Id)).HistoryRevision);
            Assert.Equal("ok", (await flow.StartNextGameAsync(owner, matchId, 9,
                "next-" + Guid.NewGuid())).Code);
        }

        await using var reopened = Open();
        var restored = await Service(reopened).GetMatchAsync(owner, matchId);
        Assert.Equal("ok", restored.Code);
        using var json = JsonDocument.Parse(restored.Body!);
        Assert.Equal(10, json.RootElement.GetProperty("revision").GetInt64());
        Assert.Equal("O", json.RootElement.GetProperty("currentGame").GetProperty("openingStone").GetString());
        Assert.Equal(3, json.RootElement.GetProperty("xScore").GetInt32());
        Assert.Equal(2, await reopened.Games.CountAsync(x => x.MatchId == matchId));
    }

    [Fact]
    public async Task StaleRevisionAndDuplicateCommandDoNotAddMoves()
    {
        await using var db = Open();
        await db.Database.MigrateAsync();
        var owner = await NewGuestAsync(db);
        var flow = Service(db);
        var created = await flow.CreateMatchAsync(owner, "create-" + Guid.NewGuid());
        var matchId = created.MatchId!.Value;
        var commandId = "move-" + Guid.NewGuid();

        var first = await flow.PlayAsync(owner, matchId, 4, 4, PlayerActor.Human, 0, commandId);
        Assert.Equal("ok", first.Code);
        Assert.Equal(first.Body, (await flow.PlayAsync(owner, matchId, 4, 4,
            PlayerActor.Human, 0, commandId)).Body);
        Assert.Equal("idempotency_conflict", (await flow.PlayAsync(owner, matchId, 5, 5,
            PlayerActor.Human, 0, commandId)).Code);
        Assert.Equal("stale_revision", (await flow.PlayAsync(owner, matchId, 5, 5,
            PlayerActor.AI, 0, "stale-" + Guid.NewGuid())).Code);
        Assert.Equal(1, await db.Moves.CountAsync(x => x.GameId == db.Games.Single(g => g.MatchId == matchId).Id));
    }

    [Fact]
    public async Task ConcurrentMovesWithSameRevisionCommitOnlyOne()
    {
        Guid matchId;
        OwnerIdentity owner;
        await using (var setup = Open())
        {
            await setup.Database.MigrateAsync();
            owner = await NewGuestAsync(setup);
            matchId = (await Service(setup).CreateMatchAsync(owner, "create-" + Guid.NewGuid())).MatchId!.Value;
        }

        await using var firstDb = Open();
        await using var secondDb = Open();
        var first = Service(firstDb).PlayAsync(owner, matchId, 4, 4,
            PlayerActor.Human, 0, "first-" + Guid.NewGuid());
        var second = Service(secondDb).PlayAsync(owner, matchId, 5, 5,
            PlayerActor.Human, 0, "second-" + Guid.NewGuid());
        var results = await Task.WhenAll(first, second);
        Assert.Single(results, x => x.Code == "ok");
        Assert.Single(results, x => x.Code == "stale_revision");

        await using var verify = Open();
        Assert.Equal(1, await verify.Moves.CountAsync(x => x.GameId == verify.Games.Single(g => g.MatchId == matchId).Id));
        Assert.Equal(1, (await verify.Matches.SingleAsync(x => x.Id == matchId)).Revision);
    }

    [Fact]
    public async Task ConcurrentNextGamesWithSameRevisionCommitOnlyOne()
    {
        Guid matchId;
        OwnerIdentity owner;
        await using (var setup = Open())
        {
            await setup.Database.MigrateAsync();
            owner = await NewGuestAsync(setup);
            var flow = Service(setup);
            matchId = (await flow.CreateMatchAsync(owner, "create-" + Guid.NewGuid())).MatchId!.Value;
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

        await using var firstDb = Open();
        await using var secondDb = Open();
        var first = Service(firstDb).StartNextGameAsync(owner, matchId, 9, "next-a-" + Guid.NewGuid());
        var second = Service(secondDb).StartNextGameAsync(owner, matchId, 9, "next-b-" + Guid.NewGuid());
        var results = await Task.WhenAll(first, second);
        Assert.Single(results, x => x.Code == "ok");
        Assert.Single(results, x => x.Code == "stale_revision");

        await using var verify = Open();
        Assert.Equal(2, await verify.Games.CountAsync(x => x.MatchId == matchId));
        Assert.Equal(1, await verify.Games.CountAsync(x => x.MatchId == matchId && x.State == StoredGameState.Active));
        Assert.Equal(10, (await verify.Matches.SingleAsync(x => x.Id == matchId)).Revision);
    }

    [Fact]
    public async Task AccountCompletionUpdatesHistoryAndOldReceiptKeepsItsBody()
    {
        await using var db = Open();
        await db.Database.MigrateAsync();
        var owner = await NewAccountAsync(db);
        var flow = Service(db);
        var created = await flow.CreateMatchAsync(owner, "create-" + Guid.NewGuid());
        var matchId = created.MatchId!.Value;
        var firstId = "first-" + Guid.NewGuid();
        var first = await flow.PlayAsync(owner, matchId, 0, 0, PlayerActor.Human, 0, firstId);
        Assert.Equal("ok", first.Code);

        long revision = 1;
        for (var column = 0; column < 4; column++)
        {
            Assert.Equal("ok", (await flow.PlayAsync(owner, matchId, 1, column,
                PlayerActor.AI, revision++, "o-" + Guid.NewGuid())).Code);
            Assert.Equal("ok", (await flow.PlayAsync(owner, matchId, 0, column + 1,
                PlayerActor.Human, revision++, "x-" + Guid.NewGuid())).Code);
        }

        Assert.Equal(1, (await db.Users.SingleAsync(x => x.Id == owner.Id)).HistoryRevision);
        var retry = await flow.PlayAsync(owner, matchId, 0, 0, PlayerActor.Human, 0, firstId);
        Assert.Equal(first.Body, retry.Body);
        Assert.Equal(first.Revision, retry.Revision);
        Assert.Equal(9, (await db.Matches.SingleAsync(x => x.Id == matchId)).Revision);
    }

    [Fact]
    public async Task FinishedAccountGameAwardsRankingOnceButGuestGameDoesNot()
    {
        await using var db = Open();
        await db.Database.MigrateAsync();
        var account = await NewAccountAsync(db);
        var guest = await NewGuestAsync(db);
        var accountMatch = (await Service(db).CreateMatchAsync(account,
            "rank-create-" + Guid.NewGuid())).MatchId!.Value;
        var guestMatch = (await Service(db).CreateMatchAsync(guest,
            "guest-create-" + Guid.NewGuid())).MatchId!.Value;

        async Task<string> FinishXAsync(OwnerIdentity owner, Guid matchId)
        {
            long revision = 0;
            string finalCommand = "";
            for (var column = 0; column < 5; column++)
            {
                finalCommand = "x-" + Guid.NewGuid();
                Assert.Equal("ok", (await Service(db).PlayAsync(owner, matchId, 0, column,
                    PlayerActor.Human, revision++, finalCommand)).Code);
                if (column < 4)
                    Assert.Equal("ok", (await Service(db).PlayAsync(owner, matchId, 1, column,
                        PlayerActor.AI, revision++, "o-" + Guid.NewGuid())).Code);
            }
            return finalCommand;
        }

        var finalCommand = await FinishXAsync(account, accountMatch);
        Assert.Equal("ok", (await Service(db).PlayAsync(account, accountMatch, 0, 4,
            PlayerActor.Human, 8, finalCommand)).Code);
        await FinishXAsync(guest, guestMatch);

        Assert.Equal(3, await db.Database.SqlQuery<int>(
            $"SELECT \"RankingPoints\" AS \"Value\" FROM \"AspNetUsers\" WHERE \"Id\" = {account.Id}")
            .SingleAsync());
        Assert.Equal(1, await db.Database.SqlQuery<int>(
            $"SELECT \"RankingGames\" AS \"Value\" FROM \"AspNetUsers\" WHERE \"Id\" = {account.Id}")
            .SingleAsync());
        Assert.Equal(1, await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*)::integer AS \"Value\" FROM \"RankingOutbox\" WHERE \"UserId\" = {account.Id}")
            .SingleAsync());
        Assert.Equal(1, await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*)::integer AS \"Value\" FROM \"RankingOutbox\"")
            .SingleAsync());
    }

    [Fact]
    public async Task DuplicateFinalReceiptRollsBackHistoryPointsAndOutbox()
    {
        await using var db = Open();
        await db.Database.MigrateAsync();
        var owner = await NewAccountAsync(db);
        var flow = Service(db);
        var matchId = (await flow.CreateMatchAsync(owner, "rollback-create-" + Guid.NewGuid()))
            .MatchId!.Value;
        var firstCommandId = "rollback-first-" + Guid.NewGuid();
        long revision = 0;
        for (var column = 0; column < 4; column++)
        {
            Assert.Equal("ok", (await flow.PlayAsync(owner, matchId, 0, column,
                PlayerActor.Human, revision++, column == 0 ? firstCommandId : "x-" + Guid.NewGuid())).Code);
            Assert.Equal("ok", (await flow.PlayAsync(owner, matchId, 1, column,
                PlayerActor.AI, revision++, "o-" + Guid.NewGuid())).Code);
        }

        var store = new EfGameFlowStore(db, TimeProvider.System);
        var stored = (await store.LoadAsync(owner, matchId))!;
        var finalMove = new MoveRecord(9, 0, 4, Gomoku.Core.Stone.X, PlayerActor.Human);
        var completedGame = stored.Games[^1] with
        {
            Moves = [..stored.Games[^1].Moves, finalMove],
            RuleStatus = Gomoku.Core.GameStatus.XWon,
            State = GameRecordState.Finished
        };
        var updated = stored with
        {
            XScore = 3,
            Revision = 9,
            Games = [..stored.Games.Take(stored.Games.Count - 1), completedGame]
        };
        var duplicateReceipt = new CommandRecord(owner, firstCommandId, matchId,
            "duplicate-receipt", 200, "{}", 9);

        Assert.Equal(CommitOutcome.DuplicateCommand,
            await store.CommitAsync(updated, 8, finalMove, null, true, duplicateReceipt));
        Assert.Equal(8, (await db.Matches.SingleAsync(x => x.Id == matchId)).Revision);
        Assert.Equal(8, await db.Moves.CountAsync(x => x.GameId == completedGame.Id));
        Assert.Equal(StoredGameState.Active,
            (await db.Games.SingleAsync(x => x.Id == completedGame.Id)).State);
        var account = await db.Users.SingleAsync(x => x.Id == owner.Id);
        Assert.Equal(0, account.HistoryRevision);
        Assert.Equal(0, account.RankingPoints);
        Assert.Equal(0, account.RankingGames);
        Assert.Equal(0, await db.RankingOutbox.CountAsync(x => x.UserId == owner.Id));

        Assert.Equal("ok", (await flow.PlayAsync(owner, matchId, 0, 4,
            PlayerActor.Human, 8, "rollback-retry-" + Guid.NewGuid())).Code);
        account = await db.Users.SingleAsync(x => x.Id == owner.Id);
        Assert.Equal(1, account.HistoryRevision);
        Assert.Equal(3, account.RankingPoints);
        Assert.Equal(1, account.RankingGames);
        Assert.Equal(1, await db.RankingOutbox.CountAsync(x => x.UserId == owner.Id));
    }

    [Fact]
    public async Task CorruptMoveAndUnsupportedRuleBlockFurtherCommands()
    {
        await using var db = Open();
        await db.Database.MigrateAsync();
        var owner = await NewGuestAsync(db);
        var flow = Service(db);
        var matchId = (await flow.CreateMatchAsync(owner, "create-" + Guid.NewGuid())).MatchId!.Value;
        Assert.Equal("ok", (await flow.PlayAsync(owner, matchId, 4, 4,
            PlayerActor.Human, 0, "first-" + Guid.NewGuid())).Code);
        var gameId = await db.Games.Where(x => x.MatchId == matchId).Select(x => x.Id).SingleAsync();
        await db.Moves.Where(x => x.GameId == gameId).ExecuteUpdateAsync(setters =>
            setters.SetProperty(x => x.Stone, Gomoku.Core.Stone.O));
        Assert.Equal("state_integrity_error", (await flow.GetMatchAsync(owner, matchId)).Code);
        Assert.Equal("state_integrity_error", (await flow.PlayAsync(owner, matchId, 4, 5,
            PlayerActor.AI, 1, "after-corruption-" + Guid.NewGuid())).Code);
        Assert.Equal(1, await db.Moves.CountAsync(x => x.GameId == gameId));

        await db.Moves.Where(x => x.GameId == gameId).ExecuteUpdateAsync(setters =>
            setters.SetProperty(x => x.Stone, Gomoku.Core.Stone.X));
        await db.Matches.Where(x => x.Id == matchId).ExecuteUpdateAsync(setters =>
            setters.SetProperty(x => x.RuleVersion, "future-rule"));
        Assert.Equal("unsupported_rule_version", (await flow.PlayAsync(owner, matchId, 4, 5,
            PlayerActor.AI, 1, "future-" + Guid.NewGuid())).Code);
    }
}
