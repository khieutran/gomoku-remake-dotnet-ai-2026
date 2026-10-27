using System.Data;
using Gomoku.Application.Identity;
using Gomoku.Application.Matches;
using Gomoku.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gomoku.Infrastructure.Matches;

public sealed class EfAiTurnJobStore(GomokuDbContext db, TimeProvider clock) : IAiTurnJobStore
{
    public async Task<AiTurnClaim?> ClaimNextAsync(TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var now = clock.GetUtcNow();
        var jobs = await db.AiTurnJobs.FromSqlInterpolated($"""
            SELECT * FROM "AiTurnJobs"
            WHERE "CompletedAt" IS NULL
              AND "NextAttemptAt" <= {now}
              AND ("LeaseUntil" IS NULL OR "LeaseUntil" <= {now})
            ORDER BY "CreatedAt", "Id"
            LIMIT 1 FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);
        var job = jobs.FirstOrDefault();
        if (job is null) return null;

        var match = await db.Matches.AsNoTracking().SingleAsync(x => x.Id == job.MatchId,
            cancellationToken);
        var owner = match.UserId is { } userId
            ? new OwnerIdentity(OwnerScope.Account, userId)
            : new OwnerIdentity(OwnerScope.Guest, match.GuestSessionId!.Value);
        job.LeaseId = Guid.NewGuid();
        job.LeaseUntil = now + leaseDuration;
        job.Attempts++;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var claim = new AiTurnClaim(job.Id, job.LeaseId.Value, owner, job.MatchId,
            job.GameId, job.ExpectedRevision, job.Attempts);
        db.ChangeTracker.Clear();
        return claim;
    }

    public async Task CompleteAsync(AiTurnClaim claim, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await db.AiTurnJobs.Where(x => x.Id == claim.JobId && x.LeaseId == claim.LeaseId &&
                x.CompletedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.CompletedAt, now)
                .SetProperty(x => x.LeaseId, (Guid?)null)
                .SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null), cancellationToken);
    }

    public async Task RetryAsync(AiTurnClaim claim, string errorCode,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var delay = TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(claim.Attempts, 5)));
        await db.AiTurnJobs.Where(x => x.Id == claim.JobId && x.LeaseId == claim.LeaseId &&
                x.CompletedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.NextAttemptAt, now + delay)
                .SetProperty(x => x.LastErrorCode, errorCode)
                .SetProperty(x => x.LeaseId, (Guid?)null)
                .SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null), cancellationToken);
    }

    public async Task<AiTurnProgress?> ReadProgressAsync(Guid gameId, long expectedRevision,
        CancellationToken cancellationToken)
    {
        var job = await db.AiTurnJobs.AsNoTracking()
            .Where(x => x.GameId == gameId && x.ExpectedRevision == expectedRevision &&
                x.CompletedAt == null)
            .Select(x => new { x.Attempts, x.LastErrorCode, x.NextAttemptAt })
            .SingleOrDefaultAsync(cancellationToken);
        return job is null ? null : new AiTurnProgress(job.Attempts, job.LastErrorCode,
            job.NextAttemptAt);
    }
}
