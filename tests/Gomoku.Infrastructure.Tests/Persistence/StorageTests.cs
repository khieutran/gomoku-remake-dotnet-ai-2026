using Gomoku.Core;
using Gomoku.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Gomoku.Infrastructure.Tests.Persistence;

public sealed class StorageTests
{
    private static readonly string? ConnectionString = Environment.GetEnvironmentVariable("GOMOKU_TEST_POSTGRES");

    private static GomokuDbContext Open()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException("Set GOMOKU_TEST_POSTGRES to run PostgreSQL integration tests.");
        }

        return new GomokuDbContext(new DbContextOptionsBuilder<GomokuDbContext>()
            .UseNpgsql(ConnectionString).Options);
    }

    private static GuestSession NewGuest() => new()
    {
        Id = Guid.NewGuid(),
        TokenHash = Guid.NewGuid().ToByteArray(),
        CreatedAt = DateTimeOffset.UtcNow,
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
    };

    private static StoredMatch NewMatch(Guid guestId) => new()
    {
        Id = Guid.NewGuid(), GuestSessionId = guestId, RuleVersion = "gomoku-exact-five-v1",
        BoardSize = 20, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
    };

    private static StoredGame NewGame(Guid matchId, int number, StoredGameState state) => new()
    {
        Id = Guid.NewGuid(), MatchId = matchId, Number = number, OpeningStone = Stone.X,
        RuleStatus = GameStatus.InProgress, State = state, StartedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task InitialMigrationPersistsGuestMatchAndMovesAfterReopen()
    {
        var guest = NewGuest();
        guest.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        var match = NewMatch(guest.Id);
        var game = NewGame(match.Id, 1, StoredGameState.Active);
        var move = new StoredMove { Id = Guid.NewGuid(), GameId = game.Id, Step = 1, Row = 0, Column = 0,
            Stone = Stone.X, Actor = MoveActor.Human, PlayedAt = DateTimeOffset.UtcNow };

        await using (var db = Open())
        {
            await db.Database.MigrateAsync();
            db.GuestSessions.Add(guest);
            db.Matches.Add(match);
            db.Games.Add(game);
            db.Moves.Add(move);
            await db.SaveChangesAsync();
        }

        await using var reopened = Open();
        Assert.Equal(1, await reopened.Moves.CountAsync(x => x.GameId == game.Id));
        Assert.Equal(guest.Id, (await reopened.Matches.SingleAsync(x => x.Id == match.Id)).GuestSessionId);
    }

    [Fact]
    public async Task MatchRequiresExactlyOneOwner()
    {
        await using var db = Open();
        await db.Database.MigrateAsync();
        var ownerless = NewMatch(Guid.NewGuid());
        ownerless.GuestSessionId = null;
        db.Matches.Add(ownerless);
        var missingOwner = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("CK_Match_OneOwner", Assert.IsType<PostgresException>(missingOwner.InnerException).ConstraintName);

        db.ChangeTracker.Clear();
        var guest = NewGuest();
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "owner-" + Guid.NewGuid(),
            NormalizedUserName = "OWNER-" + Guid.NewGuid() };
        db.GuestSessions.Add(guest);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var doubleOwned = NewMatch(guest.Id);
        doubleOwned.UserId = user.Id;
        db.Matches.Add(doubleOwned);
        var twoOwners = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("CK_Match_OneOwner", Assert.IsType<PostgresException>(twoOwners.InnerException).ConstraintName);
    }

    [Fact]
    public async Task CommandIdIsUniqueWithinGuestButReusableAcrossGuests()
    {
        var first = NewGuest();
        var second = NewGuest();
        await using var db = Open();
        await db.Database.MigrateAsync();
        db.GuestSessions.AddRange(first, second);
        await db.SaveChangesAsync();
        db.CommandReceipts.AddRange(
            Receipt(first.Id, "same-command"), Receipt(second.Id, "same-command"));
        await db.SaveChangesAsync();
        db.CommandReceipts.Add(Receipt(first.Id, "same-command"));
        var duplicateCommand = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("IX_CommandReceipts_GuestSessionId_CommandId",
            Assert.IsType<PostgresException>(duplicateCommand.InnerException).ConstraintName);
    }

    [Fact]
    public async Task ReceiptPreservesOriginalResponseBodyForExactRetry()
    {
        var guest = NewGuest();
        var receipt = Receipt(guest.Id, "body-" + Guid.NewGuid());
        receipt.ResponseBody = "{\"z\":1, \"a\":2}";
        await using (var db = Open())
        {
            await db.Database.MigrateAsync();
            db.GuestSessions.Add(guest);
            db.CommandReceipts.Add(receipt);
            await db.SaveChangesAsync();
        }

        await using var reopened = Open();
        Assert.Equal("{\"z\":1, \"a\":2}",
            (await reopened.CommandReceipts.SingleAsync(x => x.Id == receipt.Id)).ResponseBody);
    }

    [Fact]
    public async Task GameRejectsDuplicateStepAndSecondActiveGame()
    {
        var guest = NewGuest();
        var match = NewMatch(guest.Id);
        var game = NewGame(match.Id, 1, StoredGameState.Active);
        await using var db = Open();
        await db.Database.MigrateAsync();
        db.GuestSessions.Add(guest);
        db.Matches.Add(match);
        db.Games.Add(game);
        db.Moves.Add(new StoredMove { Id = Guid.NewGuid(), GameId = game.Id, Step = 1, Row = 0, Column = 0,
            Stone = Stone.X, Actor = MoveActor.Human, PlayedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        db.Moves.Add(new StoredMove { Id = Guid.NewGuid(), GameId = game.Id, Step = 1, Row = 1, Column = 1,
            Stone = Stone.O, Actor = MoveActor.AI, PlayedAt = DateTimeOffset.UtcNow });
        var duplicateStep = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("IX_Moves_GameId_Step", Assert.IsType<PostgresException>(duplicateStep.InnerException).ConstraintName);

        db.ChangeTracker.Clear();
        db.Games.Add(NewGame(match.Id, 2, StoredGameState.Active));
        var secondActive = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("IX_Games_MatchId", Assert.IsType<PostgresException>(secondActive.InnerException).ConstraintName);
    }

    [Fact]
    public async Task MatchOwnerAndRulesCannotChangeAfterInsert()
    {
        var first = NewGuest();
        var second = NewGuest();
        var match = NewMatch(first.Id);
        await using var db = Open();
        await db.Database.MigrateAsync();
        db.GuestSessions.AddRange(first, second);
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        match.GuestSessionId = second.Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        db.ChangeTracker.Clear();
        var reloaded = await db.Matches.SingleAsync(x => x.Id == match.Id);
        reloaded.RuleVersion = "other-rule";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task CurrentGameMustBelongToItsMatch()
    {
        var guest = NewGuest();
        var first = NewMatch(guest.Id);
        var second = NewMatch(guest.Id);
        var secondGame = NewGame(second.Id, 1, StoredGameState.Active);
        await using var db = Open();
        await db.Database.MigrateAsync();
        db.GuestSessions.Add(guest);
        db.Matches.AddRange(first, second);
        db.Games.Add(secondGame);
        await db.SaveChangesAsync();

        first.CurrentGameId = secondGame.Id;
        var invalidPointer = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("FK_Matches_Games_CurrentGameId_Id",
            Assert.IsType<PostgresException>(invalidPointer.InnerException).ConstraintName);
    }

    private static CommandReceipt Receipt(Guid guestId, string commandId) => new()
    {
        Id = Guid.NewGuid(), GuestSessionId = guestId, CommandId = commandId,
        RequestHash = "sha256-test", HttpStatus = 201, ResponseBody = "{}",
        RevisionAfterCommit = 1, CreatedAt = DateTimeOffset.UtcNow
    };
}
