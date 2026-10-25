using Gomoku.Application.Ranking;
using Microsoft.Extensions.Logging;
using Gomoku.Infrastructure.Redis;
using StackExchange.Redis;

namespace Gomoku.Infrastructure.Ranking;

public sealed class RedisRankingProjection(IConnectionMultiplexer connection, RedisKeyspace keyspace,
    ILogger<RedisRankingProjection> logger) : IRankingProjection
{
    private string BoardKey => keyspace.Key("ranking:v1:alltime");
    private string ReadyKey => keyspace.Key("ranking:v1:ready");
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(750);

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await connection.GetDatabase().PingAsync().WaitAsync(Timeout, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Redis is unavailable for ranking projection");
            return false;
        }
    }

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var db = connection.GetDatabase();
            var value = await db.StringGetAsync(ReadyKey).WaitAsync(Timeout, cancellationToken);
            if (!int.TryParse(value.ToString(), out var count)) return false;
            return count == 0 || await db.KeyExistsAsync(BoardKey).WaitAsync(Timeout, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Ranking projection is unavailable");
            return false;
        }
    }

    public async Task<IReadOnlyList<RankingScore>?> ReadTopAsync(int limit,
        CancellationToken cancellationToken = default)
    {
        if (!await IsReadyAsync(cancellationToken)) return null;
        try
        {
            var rows = await connection.GetDatabase()
                .SortedSetRangeByRankWithScoresAsync(BoardKey, 0, limit - 1, Order.Descending)
                .WaitAsync(Timeout, cancellationToken);
            return rows.Select(row => new RankingScore(Guid.Parse(row.Element.ToString()),
                checked((int)row.Score))).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Ranking read failed; using PostgreSQL");
            return null;
        }
    }

    public async Task<RankingPosition?> ReadPositionAsync(Guid accountId,
        CancellationToken cancellationToken = default)
    {
        if (!await IsReadyAsync(cancellationToken)) return null;
        try
        {
            var db = connection.GetDatabase();
            var score = await db.SortedSetScoreAsync(BoardKey, accountId.ToString("N"))
                .WaitAsync(Timeout, cancellationToken);
            if (score is null) return null;
            var higher = await db.SortedSetLengthAsync(BoardKey, score.Value + 1,
                double.PositiveInfinity).WaitAsync(Timeout, cancellationToken);
            return new RankingPosition(accountId, checked((int)score.Value), checked((int)higher + 1));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Ranking position read failed; using PostgreSQL");
            return null;
        }
    }

    public async Task<bool> RebuildAsync(IReadOnlyList<RankingScore> scores,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = connection.GetDatabase();
            await db.KeyDeleteAsync(ReadyKey).WaitAsync(Timeout, cancellationToken);
            await db.KeyDeleteAsync(BoardKey).WaitAsync(Timeout, cancellationToken);
            if (scores.Count > 0)
            {
                var entries = scores.Select(score => new SortedSetEntry(
                    score.AccountId.ToString("N"), score.Points)).ToArray();
                await db.SortedSetAddAsync(BoardKey, entries).WaitAsync(Timeout, cancellationToken);
            }
            await db.StringSetAsync(ReadyKey, scores.Count.ToString())
                .WaitAsync(Timeout, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Ranking rebuild failed; PostgreSQL remains available");
            return false;
        }
    }

    public async Task<bool> PublishAsync(Guid accountId, int points,
        CancellationToken cancellationToken = default)
    {
        if (!await IsReadyAsync(cancellationToken)) return false;
        try
        {
            var db = connection.GetDatabase();
            await db.ExecuteAsync("ZADD", BoardKey, "GT", points,
                accountId.ToString("N")).WaitAsync(Timeout, cancellationToken);
            await db.StringSetAsync(ReadyKey, "1").WaitAsync(Timeout, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Ranking publish failed; outbox will retry");
            return false;
        }
    }
}
