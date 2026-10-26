using System.Data;
using System.Runtime.CompilerServices;
using Gomoku.Application.Datasets;
using Gomoku.Application.Identity;
using Gomoku.Application.Matches;
using Gomoku.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gomoku.Infrastructure.Datasets;

public sealed class EfGuestDatasetReader : IGuestDatasetReader
{
    private readonly GomokuDbContext _db;
    private readonly int _pageSize;

    public EfGuestDatasetReader(GomokuDbContext db, int pageSize = 200)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (pageSize is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(pageSize));
        _db = db;
        _pageSize = pageSize;
    }

    public async IAsyncEnumerable<GuestMatchSource> ReadAsync(DateTimeOffset exportCutoffUtc,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        var hasCursor = false;
        var cursorCreatedAt = DateTimeOffset.MinValue;
        var cursorId = Guid.Empty;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matches = await _db.Matches.FromSqlInterpolated($"""
                SELECT * FROM "Matches"
                WHERE "GuestSessionId" IS NOT NULL AND "UserId" IS NULL
                  AND "CreatedAt" <= {exportCutoffUtc}
                  AND ({hasCursor} = FALSE OR "CreatedAt" > {cursorCreatedAt}
                    OR ("CreatedAt" = {cursorCreatedAt} AND "Id" > {cursorId}))
                ORDER BY "CreatedAt", "Id"
                LIMIT {_pageSize}
                """).AsNoTracking().ToListAsync(cancellationToken);
            if (matches.Count == 0) break;

            var matchIds = matches.Select(match => match.Id).ToArray();
            var games = await _db.Games.AsNoTracking()
                .Where(game => matchIds.Contains(game.MatchId))
                .OrderBy(game => game.MatchId).ThenBy(game => game.Number)
                .ToListAsync(cancellationToken);
            var gameIds = games.Select(game => game.Id).ToArray();
            var moves = await _db.Moves.AsNoTracking()
                .Where(move => gameIds.Contains(move.GameId))
                .OrderBy(move => move.GameId).ThenBy(move => move.Step)
                .ToListAsync(cancellationToken);
            foreach (var match in matches)
            {
                var matchGames = games.Where(game => game.MatchId == match.Id).ToArray();
                var record = new MatchRecord(match.Id,
                    new OwnerIdentity(OwnerScope.Guest, match.GuestSessionId!.Value),
                    match.RuleVersion, match.BoardSize, match.XScore, match.OScore,
                    match.CurrentGameId ?? Guid.Empty, match.Revision,
                    matchGames.Select(game => new GameRecord(game.Id, game.Number,
                        game.OpeningStone, game.RuleStatus, (GameRecordState)game.State,
                        moves.Where(move => move.GameId == game.Id)
                            .Select(move => new MoveRecord(move.Step, move.Row, move.Column,
                                move.Stone, (PlayerActor)move.Actor, move.ActualAiVersion,
                                move.AiFallbackReason)).ToArray(), game.AiVersion)).ToArray());
                yield return new GuestMatchSource(record,
                    matchGames.ToDictionary(game => game.Id, game => game.FinishedAt),
                    match.CreatedAt);
            }
            var last = matches[^1];
            hasCursor = true;
            cursorCreatedAt = last.CreatedAt;
            cursorId = last.Id;
        }
        await transaction.CommitAsync(cancellationToken);
    }
}
