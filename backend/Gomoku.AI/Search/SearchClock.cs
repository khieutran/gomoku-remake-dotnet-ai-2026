using System.Diagnostics;

namespace Gomoku.AI;

internal sealed class SearchBudgetExpiredException : Exception;

internal sealed class SearchClock(TimeSpan budget, CancellationToken cancellationToken)
{
    private readonly long _startedAt = Stopwatch.GetTimestamp();

    public long Nodes { get; private set; }

    public void Check()
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Stopwatch.GetElapsedTime(_startedAt) >= budget)
            throw new SearchBudgetExpiredException();
    }

    public void Visit()
    {
        Nodes++;
        Check();
    }
}
