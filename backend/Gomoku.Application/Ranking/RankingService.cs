namespace Gomoku.Application.Ranking;

public sealed class RankingService(IRankingReader reader, IRankingProjection projection)
{
    public async Task<RankingPage> TopAsync(int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 50);
        var scores = await reader.HasPendingAsync(cancellationToken)
            ? await reader.ReadTopAsync(limit, cancellationToken)
            : await projection.ReadTopAsync(limit, cancellationToken)
                ?? await reader.ReadTopAsync(limit, cancellationToken);
        // Names live in PostgreSQL only; one lookup by primary key for at most 50 accounts.
        var names = scores.Count == 0 ? new Dictionary<Guid, string>()
            : await reader.ReadDisplayNamesAsync(scores.Select(x => x.AccountId).ToArray(), cancellationToken);
        var items = new RankingEntry[scores.Count];
        var rank = 0;
        for (var i = 0; i < scores.Count; i++)
        {
            if (i == 0 || scores[i].Points != scores[i - 1].Points) rank = i + 1;
            items[i] = new RankingEntry(scores[i].AccountId, scores[i].Points, rank,
                names.GetValueOrDefault(scores[i].AccountId));
        }
        return new RankingPage(items);
    }

    public async Task<RankingPosition?> MineAsync(Guid accountId,
        CancellationToken cancellationToken = default) =>
        await reader.HasPendingAsync(cancellationToken)
            ? await reader.ReadPositionAsync(accountId, cancellationToken)
            : await projection.ReadPositionAsync(accountId, cancellationToken)
                ?? await reader.ReadPositionAsync(accountId, cancellationToken);
}
