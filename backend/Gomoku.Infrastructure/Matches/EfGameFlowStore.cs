using System.Data;
using Gomoku.Application.Identity;
using Gomoku.Application.Matches;
using Gomoku.Application.Ranking;
using Gomoku.Core;
using Gomoku.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Gomoku.Infrastructure.Matches;

public sealed class EfGameFlowStore(GomokuDbContext db, TimeProvider clock) : IGameFlowStore
{
    public async Task<MatchRecord?> LoadAsync(OwnerIdentity owner, Guid matchId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        var match = await OwnedMatches(owner).AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == matchId, cancellationToken);
        if (match is null) return null;

        var games = await db.Games.AsNoTracking().Where(x => x.MatchId == matchId)
            .OrderBy(x => x.Number).ToListAsync(cancellationToken);
        var gameIds = games.Select(x => x.Id).ToArray();
        var moves = await db.Moves.AsNoTracking().Where(x => gameIds.Contains(x.GameId))
            .OrderBy(x => x.Step).ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new MatchRecord(match.Id, owner, match.RuleVersion, match.BoardSize,
            match.XScore, match.OScore, match.CurrentGameId ?? Guid.Empty, match.Revision,
            games.Select(game => new GameRecord(game.Id, game.Number, game.OpeningStone,
                game.RuleStatus, (GameRecordState)game.State,
                moves.Where(move => move.GameId == game.Id)
                    .Select(move => new MoveRecord(move.Step, move.Row, move.Column,
                        move.Stone, (PlayerActor)move.Actor)).ToArray())).ToArray());
    }

    public async Task<CommandRecord?> FindReceiptAsync(OwnerIdentity owner, string commandId,
        CancellationToken cancellationToken = default)
    {
        var receipt = await db.CommandReceipts.AsNoTracking()
            .Where(x => x.CommandId == commandId)
            .Where(x => owner.Scope == OwnerScope.Account ? x.UserId == owner.Id : x.GuestSessionId == owner.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (receipt is null) return null;
        if (receipt.MatchId is null) throw new StateIntegrityException("Receipt has no Match ID.");
        return new CommandRecord(owner, receipt.CommandId, receipt.MatchId.Value,
            receipt.RequestHash, receipt.HttpStatus, receipt.ResponseBody, receipt.RevisionAfterCommit);
    }

    public async Task<CommitOutcome> CreateAsync(MatchRecord match, CommandRecord receipt,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var now = clock.GetUtcNow();
            var row = new StoredMatch
            {
                Id = match.Id, UserId = UserId(match.Owner), GuestSessionId = GuestId(match.Owner),
                RuleVersion = match.RuleVersion, BoardSize = match.BoardSize,
                XScore = 0, OScore = 0, CurrentGameId = null, Revision = 0,
                CreatedAt = now, UpdatedAt = now
            };
            db.Matches.Add(row);
            await db.SaveChangesAsync(cancellationToken);
            db.Games.Add(ToStoredGame(match.Id, match.Games[0], now));
            await db.SaveChangesAsync(cancellationToken);
            row.CurrentGameId = match.CurrentGameId;
            db.CommandReceipts.Add(ToStoredReceipt(receipt, now));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return CommitOutcome.Committed;
        }
        catch (DbUpdateException exception) when (IsReceiptDuplicate(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return CommitOutcome.DuplicateCommand;
        }
    }

    public async Task<CommitOutcome> CommitAsync(MatchRecord updated, long expectedRevision,
        MoveRecord? addedMove, GameRecord? addedGame, bool completedGame, CommandRecord receipt,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var now = clock.GetUtcNow();
            if (addedGame is not null)
            {
                // Serialize next-game creation on the Match row before touching the
                // unique active-game and game-number indexes.
                var locked = await OwnedMatches(updated.Owner)
                    .Where(x => x.Id == updated.Id && x.Revision == expectedRevision)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.Revision, x => x.Revision), cancellationToken);
                if (locked == 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    db.ChangeTracker.Clear();
                    return CommitOutcome.StaleRevision;
                }
                db.Games.Add(ToStoredGame(updated.Id, addedGame, now));
                await db.SaveChangesAsync(cancellationToken);
            }

            var affected = await OwnedMatches(updated.Owner)
                .Where(x => x.Id == updated.Id && x.Revision == expectedRevision)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Revision, updated.Revision)
                    .SetProperty(x => x.XScore, updated.XScore)
                    .SetProperty(x => x.OScore, updated.OScore)
                    .SetProperty(x => x.CurrentGameId, updated.CurrentGameId)
                    .SetProperty(x => x.UpdatedAt, now), cancellationToken);
            if (affected == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
                return CommitOutcome.StaleRevision;
            }

            if (addedMove is not null)
            {
                var game = updated.Games[^1];
                db.Moves.Add(new StoredMove
                {
                    Id = Guid.NewGuid(), GameId = game.Id, Step = addedMove.Step,
                    Row = addedMove.Row, Column = addedMove.Column,
                    Stone = addedMove.Stone, Actor = (MoveActor)addedMove.Actor,
                    PlayedAt = now
                });
                var gameRows = await db.Games.Where(x => x.Id == game.Id && x.MatchId == updated.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.RuleStatus, game.RuleStatus)
                        .SetProperty(x => x.State, (StoredGameState)game.State)
                        .SetProperty(x => x.FinishedAt, completedGame ? now : (DateTimeOffset?)null),
                        cancellationToken);
                if (gameRows != 1) throw new StateIntegrityException("Current Game row is missing.");
            }

            if (completedGame)
            {
                var ownerRows = updated.Owner.Scope == OwnerScope.Account
                    ? await db.Users.Where(x => x.Id == updated.Owner.Id)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(x => x.HistoryRevision, x => x.HistoryRevision + 1), cancellationToken)
                    : await db.GuestSessions.Where(x => x.Id == updated.Owner.Id)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(x => x.HistoryRevision, x => x.HistoryRevision + 1), cancellationToken);
                if (ownerRows != 1) throw new StateIntegrityException("Match owner row is missing.");

                if (updated.Owner.Scope == OwnerScope.Account)
                {
                    var game = updated.Games[^1];
                    var award = RankingPoints.For(game.RuleStatus, Stone.X);
                    var ranked = await db.Users.Where(x => x.Id == updated.Owner.Id)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(x => x.RankingPoints, x => x.RankingPoints + award)
                            .SetProperty(x => x.RankingGames, x => x.RankingGames + 1), cancellationToken);
                    if (ranked != 1) throw new StateIntegrityException("Ranking owner row is missing.");
                    var points = await db.Users.AsNoTracking().Where(x => x.Id == updated.Owner.Id)
                        .Select(x => x.RankingPoints).SingleAsync(cancellationToken);
                    db.RankingOutbox.Add(new RankingOutbox
                    {
                        Id = Guid.NewGuid(), GameId = game.Id, UserId = updated.Owner.Id,
                        Points = points, CreatedAt = now
                    });
                }
            }

            db.CommandReceipts.Add(ToStoredReceipt(receipt, now));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return CommitOutcome.Committed;
        }
        catch (DbUpdateException exception) when (IsReceiptDuplicate(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return CommitOutcome.DuplicateCommand;
        }
    }

    private IQueryable<StoredMatch> OwnedMatches(OwnerIdentity owner) => db.Matches.Where(x =>
        owner.Scope == OwnerScope.Account ? x.UserId == owner.Id : x.GuestSessionId == owner.Id);

    private static Guid? UserId(OwnerIdentity owner) => owner.Scope == OwnerScope.Account ? owner.Id : null;
    private static Guid? GuestId(OwnerIdentity owner) => owner.Scope == OwnerScope.Guest ? owner.Id : null;

    private static StoredGame ToStoredGame(Guid matchId, GameRecord game, DateTimeOffset now) => new()
    {
        Id = game.Id, MatchId = matchId, Number = game.Number, OpeningStone = game.OpeningStone,
        RuleStatus = game.RuleStatus, State = (StoredGameState)game.State, StartedAt = now
    };

    private static CommandReceipt ToStoredReceipt(CommandRecord receipt, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), UserId = UserId(receipt.Owner), GuestSessionId = GuestId(receipt.Owner),
        CommandId = receipt.CommandId, MatchId = receipt.MatchId,
        RequestHash = receipt.RequestHash, HttpStatus = receipt.HttpStatus,
        ResponseBody = receipt.ResponseBody, RevisionAfterCommit = receipt.Revision,
        CreatedAt = now
    };

    private static bool IsReceiptDuplicate(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_CommandReceipts_UserId_CommandId" or "IX_CommandReceipts_GuestSessionId_CommandId"
        };
}
