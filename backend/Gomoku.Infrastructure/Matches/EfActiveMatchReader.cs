using Gomoku.Application.Identity;
using Gomoku.Application.Matches;
using Gomoku.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gomoku.Infrastructure.Matches;

public sealed class EfActiveMatchReader(GomokuDbContext db) : IActiveMatchReader
{
    public async Task<IReadOnlyList<ActiveMatchSummary>> ReadAsync(OwnerIdentity owner,
        int offset, int take, CancellationToken cancellationToken = default) =>
        await (from match in db.Matches.AsNoTracking()
               join game in db.Games.AsNoTracking() on match.Id equals game.MatchId
               where match.CurrentGameId == game.Id && game.State == StoredGameState.Active &&
                   (owner.Scope == OwnerScope.Account
                       ? match.UserId == owner.Id : match.GuestSessionId == owner.Id)
               orderby match.UpdatedAt descending, match.Id descending
               select new ActiveMatchSummary(match.Id, game.Id, match.Revision, match.UpdatedAt))
            .Skip(offset).Take(take).ToListAsync(cancellationToken);
}
