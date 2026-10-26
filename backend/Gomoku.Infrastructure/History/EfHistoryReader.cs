using Gomoku.Application.History;
using Gomoku.Application.Identity;
using Gomoku.Core;
using Gomoku.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gomoku.Infrastructure.History;

public sealed class EfHistoryReader(GomokuDbContext db) : IHistoryReader
{
    public Task<long> ReadRevisionAsync(OwnerIdentity owner,
        CancellationToken cancellationToken = default) => owner.Scope == OwnerScope.Account
        ? db.Users.AsNoTracking().Where(x => x.Id == owner.Id)
            .Select(x => x.HistoryRevision).SingleOrDefaultAsync(cancellationToken)
        : db.GuestSessions.AsNoTracking().Where(x => x.Id == owner.Id)
            .Select(x => x.HistoryRevision).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<HistoryGameSummary>> ReadCompletedAsync(OwnerIdentity owner,
        HistoryCursor? cursor, int take, CancellationToken cancellationToken = default)
    {
        var query = CompletedGames(owner);

        if (cursor is { } point)
            query = query.Where(x => x.FinishedAt < point.FinishedAt ||
                x.FinishedAt == point.FinishedAt && x.Id.CompareTo(point.GameId) < 0);

        var rows = await ProjectSummaries(query.OrderByDescending(x => x.FinishedAt)
            .ThenByDescending(x => x.Id).Take(take), db, OwnedMatches(owner))
            .ToListAsync(cancellationToken);

        return rows.Select(ToSummary).ToArray();
    }

    public async Task<HistoryGameSummary?> ReadCompletedSummaryAsync(OwnerIdentity owner, Guid gameId,
        CancellationToken cancellationToken = default)
    {
        var row = await ProjectSummaries(CompletedGames(owner).Where(game => game.Id == gameId),
                db, OwnedMatches(owner))
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : ToSummary(row);
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

    private IQueryable<StoredGame> CompletedGames(OwnerIdentity owner) =>
        db.Games.AsNoTracking().Where(game =>
            game.State == StoredGameState.Finished && game.FinishedAt != null &&
            OwnedMatches(owner).Any(match => match.Id == game.MatchId));

    private static IQueryable<HistorySummaryRow> ProjectSummaries(IQueryable<StoredGame> games,
        GomokuDbContext db, IQueryable<StoredMatch> ownedMatches) =>
        from game in games
        join match in ownedMatches on game.MatchId equals match.Id
        orderby game.FinishedAt descending, game.Id descending
        select new HistorySummaryRow(game.Id, game.MatchId, game.Number, game.RuleStatus,
            game.OpeningStone, match.RuleVersion, match.BoardSize, game.AiVersion,
            game.StartedAt, game.FinishedAt!.Value,
            db.Moves.Count(move => move.GameId == game.Id));

    private static HistoryGameSummary ToSummary(HistorySummaryRow row) =>
        new(row.GameId, row.MatchId, row.Number, row.RuleStatus.ToString(),
            row.OpeningStone.ToString(), row.RuleVersion, row.BoardSize, row.AiVersion,
            row.StartedAt, row.FinishedAt, row.MoveCount);

    private sealed record HistorySummaryRow(Guid GameId, Guid MatchId, int Number,
        GameStatus RuleStatus, Stone OpeningStone, string RuleVersion, int BoardSize,
        string? AiVersion, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, int MoveCount);
}
