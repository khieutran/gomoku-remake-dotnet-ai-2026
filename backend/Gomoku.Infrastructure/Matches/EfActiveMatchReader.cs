using Gomoku.Application.Identity;
using Gomoku.Application.Matches;
using Gomoku.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gomoku.Infrastructure.Matches;

public sealed class EfActiveMatchReader(GomokuDbContext db) : IActiveMatchReader
{
    public async Task<IReadOnlyList<ActiveMatchSummary>> ReadAsync(OwnerIdentity owner,
        ActiveMatchCursor? cursor, int take, CancellationToken cancellationToken = default)
    {
        var query = from match in db.Matches.AsNoTracking()
               join game in db.Games.AsNoTracking() on match.Id equals game.MatchId
               where match.CurrentGameId == game.Id && game.State == StoredGameState.Active &&
                   (owner.Scope == OwnerScope.Account
                       ? match.UserId == owner.Id : match.GuestSessionId == owner.Id)
               select new { Match = match, Game = game };
        if (cursor is { } point)
            query = query.Where(x => x.Match.CreatedAt < point.CreatedAt ||
                x.Match.CreatedAt == point.CreatedAt && x.Match.Id.CompareTo(point.MatchId) < 0);

        return await query.OrderByDescending(x => x.Match.CreatedAt)
            .ThenByDescending(x => x.Match.Id)
            .Take(take)
            .Select(x => new ActiveMatchSummary(x.Match.Id, x.Game.Id, x.Match.Revision,
                x.Match.UpdatedAt, x.Match.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
