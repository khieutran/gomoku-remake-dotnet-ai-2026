using Gomoku.Application.Identity;
using Gomoku.Application.Matches;
using Gomoku.Application.Ranking;
using Gomoku.Core;
using Gomoku.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gomoku.Infrastructure.Matches;

internal static class CompletedGameBookkeeping
{
    public static async Task ApplyAsync(GomokuDbContext db, MatchRecord match, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var ownerRows = match.Owner.Scope == OwnerScope.Account
            ? await db.Users.Where(x => x.Id == match.Owner.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.HistoryRevision, x => x.HistoryRevision + 1), cancellationToken)
            : await db.GuestSessions.Where(x => x.Id == match.Owner.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.HistoryRevision, x => x.HistoryRevision + 1), cancellationToken);
        if (ownerRows != 1) throw new StateIntegrityException("Match owner row is missing.");

        if (match.Owner.Scope != OwnerScope.Account) return;

        var game = match.Games[^1];
        var award = RankingPoints.For(game.RuleStatus, Stone.X);
        var ranked = await db.Users.Where(x => x.Id == match.Owner.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.RankingPoints, x => x.RankingPoints + award)
                .SetProperty(x => x.RankingGames, x => x.RankingGames + 1), cancellationToken);
        if (ranked != 1) throw new StateIntegrityException("Ranking owner row is missing.");

        var points = await db.Users.AsNoTracking().Where(x => x.Id == match.Owner.Id)
            .Select(x => x.RankingPoints).SingleAsync(cancellationToken);
        db.RankingOutbox.Add(new RankingOutbox
        {
            Id = Guid.NewGuid(), GameId = game.Id, UserId = match.Owner.Id,
            Points = points, CreatedAt = now
        });
    }
}
