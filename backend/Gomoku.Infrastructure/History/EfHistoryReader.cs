using Gomoku.Application.History;
using Gomoku.Application.Identity;
using Gomoku.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gomoku.Infrastructure.History;

public sealed class EfHistoryReader(GomokuDbContext db) : IHistoryReader
{
    public async Task<IReadOnlyList<HistoryGameSummary>> ReadCompletedAsync(OwnerIdentity owner,
        HistoryCursor? cursor, int take, CancellationToken cancellationToken = default)
    {
        var query = from game in db.Games.AsNoTracking()
                    join match in OwnedMatches(owner) on game.MatchId equals match.Id
                    where game.State == StoredGameState.Finished && game.FinishedAt != null
                    select new { Game = game, Match = match };

        if (cursor is { } point)
            query = query.Where(x => x.Game.FinishedAt < point.FinishedAt ||
                x.Game.FinishedAt == point.FinishedAt && x.Game.Id.CompareTo(point.GameId) < 0);

        var rows = await query.OrderByDescending(x => x.Game.FinishedAt)
            .ThenByDescending(x => x.Game.Id)
            .Take(take)
            .Select(x => new
            {
                GameId = x.Game.Id,
                x.Game.MatchId,
                x.Game.Number,
                x.Game.RuleStatus,
                x.Game.OpeningStone,
                x.Match.RuleVersion,
                x.Match.BoardSize,
                x.Game.AiVersion,
                x.Game.StartedAt,
                FinishedAt = x.Game.FinishedAt!.Value,
                MoveCount = db.Moves.Count(move => move.GameId == x.Game.Id)
            }).ToListAsync(cancellationToken);

        return rows.Select(x => new HistoryGameSummary(x.GameId, x.MatchId, x.Number,
            x.RuleStatus.ToString(), x.OpeningStone.ToString(), x.RuleVersion, x.BoardSize,
            x.AiVersion, x.StartedAt, x.FinishedAt, x.MoveCount)).ToArray();
    }

    public async Task<HistoryGameDetail?> ReadGameAsync(OwnerIdentity owner, Guid gameId,
        CancellationToken cancellationToken = default)
    {
        var gameQuery = from game in db.Games.AsNoTracking()
                        join match in OwnedMatches(owner) on game.MatchId equals match.Id
                        where game.Id == gameId && game.State == StoredGameState.Finished && game.FinishedAt != null
                        select new
                        {
                            GameId = game.Id,
                            game.MatchId,
                            game.Number,
                            game.RuleStatus,
                            game.OpeningStone,
                            match.RuleVersion,
                            match.BoardSize,
                            game.AiVersion,
                            game.StartedAt,
                            FinishedAt = game.FinishedAt!.Value
                        };
        var row = await gameQuery.SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;

        var moves = await (from move in db.Moves.AsNoTracking()
                           join game in db.Games.AsNoTracking() on move.GameId equals game.Id
                           join match in OwnedMatches(owner) on game.MatchId equals match.Id
                           where game.Id == gameId && game.State == StoredGameState.Finished
                           orderby move.Step
                           select new
                           {
                               move.Step, move.Row, move.Column, move.Stone, move.Actor, move.PlayedAt
                           }).ToListAsync(cancellationToken);

        return new HistoryGameDetail(row.GameId, row.MatchId, row.Number,
            row.RuleStatus.ToString(), row.OpeningStone.ToString(), row.RuleVersion,
            row.BoardSize, row.AiVersion, row.StartedAt, row.FinishedAt,
            moves.Select(move => new HistoryMove(move.Step, move.Row, move.Column,
                move.Stone.ToString(), move.Actor.ToString(), move.PlayedAt)).ToArray());
    }

    private IQueryable<StoredMatch> OwnedMatches(OwnerIdentity owner) => db.Matches.AsNoTracking().Where(x =>
        owner.Scope == OwnerScope.Account ? x.UserId == owner.Id : x.GuestSessionId == owner.Id);
}
