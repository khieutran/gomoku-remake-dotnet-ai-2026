using Gomoku.Application.Ranking;
using Gomoku.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gomoku.Infrastructure.Ranking;

public sealed class RankingProjectionWorker(IServiceScopeFactory scopes,
    TimeProvider clock, ILogger<RankingProjectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await UpdateAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Ranking projection pass failed; will retry");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task UpdateAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IRankingReader>();
        var projection = scope.ServiceProvider.GetRequiredService<IRankingProjection>();
        var db = scope.ServiceProvider.GetRequiredService<GomokuDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var ownsProjection = await db.Database.SqlQueryRaw<bool>(
            "SELECT pg_try_advisory_xact_lock(773982431234) AS \"Value\"")
            .SingleAsync(cancellationToken);
        if (!ownsProjection) return;

        if (!await projection.IsReadyAsync(cancellationToken) &&
            !await projection.RebuildAsync(await reader.ReadAllAsync(cancellationToken), cancellationToken))
            return;

        var pending = await db.RankingOutbox.AsNoTracking().Where(x => x.PublishedAt == null)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(100)
            .ToListAsync(cancellationToken);
        foreach (var item in pending)
        {
            if (!await projection.PublishAsync(item.UserId, item.Points, cancellationToken)) break;
            await db.RankingOutbox.Where(x => x.Id == item.Id && x.PublishedAt == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.PublishedAt, clock.GetUtcNow()), cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }
}
