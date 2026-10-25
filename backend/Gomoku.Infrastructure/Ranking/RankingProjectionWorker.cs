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
        var consecutiveFailures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            var succeeded = false;
            try
            {
                succeeded = await UpdateAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Ranking projection pass failed; will retry");
            }
            consecutiveFailures = succeeded ? 0 : Math.Min(consecutiveFailures + 1, 6);
            var delay = succeeded
                ? TimeSpan.FromSeconds(1)
                : TimeSpan.FromSeconds(Math.Min(30,
                    Math.Pow(2, consecutiveFailures - 1) + Random.Shared.NextDouble()));
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task<bool> UpdateAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var projection = scope.ServiceProvider.GetRequiredService<IRankingProjection>();
        if (!await projection.IsAvailableAsync(cancellationToken)) return false;

        var reader = scope.ServiceProvider.GetRequiredService<IRankingReader>();
        var db = scope.ServiceProvider.GetRequiredService<GomokuDbContext>();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var ownsProjection = false;
        try
        {
            ownsProjection = await db.Database.SqlQueryRaw<bool>(
                "SELECT pg_try_advisory_lock(773982431234) AS \"Value\"")
                .SingleAsync(cancellationToken);
            if (!ownsProjection) return true;

            if (!await projection.IsReadyAsync(cancellationToken) &&
                !await projection.RebuildAsync(await reader.ReadAllAsync(cancellationToken), cancellationToken))
                return false;

            var pending = await db.RankingOutbox.AsNoTracking().Where(x => x.PublishedAt == null)
                .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(100)
                .ToListAsync(cancellationToken);
            var publishedAll = true;
            foreach (var item in pending)
            {
                if (!await projection.PublishAsync(item.UserId, item.Points, cancellationToken))
                {
                    publishedAll = false;
                    break;
                }
                await db.RankingOutbox.Where(x => x.Id == item.Id && x.PublishedAt == null)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.PublishedAt, clock.GetUtcNow()), cancellationToken);
            }
            return publishedAll;
        }
        finally
        {
            try
            {
                if (ownsProjection)
                    await db.Database.SqlQueryRaw<bool>(
                        "SELECT pg_advisory_unlock(773982431234) AS \"Value\"")
                        .SingleAsync(CancellationToken.None);
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }
}
