using Gomoku.Application.Ranking;
using Gomoku.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gomoku.Infrastructure.Ranking;

public sealed class EfRankingReader(GomokuDbContext db) : IRankingReader
{
    public Task<bool> HasPendingAsync(CancellationToken cancellationToken = default) =>
        db.RankingOutbox.AsNoTracking().AnyAsync(x => x.PublishedAt == null, cancellationToken);

    public async Task<IReadOnlyList<RankingScore>> ReadTopAsync(int limit,
        CancellationToken cancellationToken = default) =>
        await db.Users.AsNoTracking().Where(x => x.RankingGames > 0)
            .OrderByDescending(x => x.RankingPoints).ThenByDescending(x => x.Id)
            .Take(limit).Select(x => new RankingScore(x.Id, x.RankingPoints))
            .ToListAsync(cancellationToken);

    public async Task<RankingPosition?> ReadPositionAsync(Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking().Where(x => x.Id == accountId)
            .Select(x => new { x.Id, x.RankingPoints, x.RankingGames })
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null) return null;
        if (user.RankingGames == 0) return new RankingPosition(accountId, 0, null);
        var higher = await db.Users.AsNoTracking().CountAsync(x =>
            x.RankingGames > 0 && x.RankingPoints > user.RankingPoints, cancellationToken);
        return new RankingPosition(accountId, user.RankingPoints, higher + 1);
    }

    public async Task<IReadOnlyList<RankingScore>> ReadAllAsync(
        CancellationToken cancellationToken = default) =>
        await db.Users.AsNoTracking().Where(x => x.RankingGames > 0)
            .OrderByDescending(x => x.RankingPoints).ThenByDescending(x => x.Id)
            .Select(x => new RankingScore(x.Id, x.RankingPoints))
            .ToListAsync(cancellationToken);
}
