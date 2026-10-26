using System.Text.Json;
using Gomoku.Application.History;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Gomoku.Infrastructure.History;

public sealed class RedisHistoryCache(IDistributedCache distributed,
    ILogger<RedisHistoryCache> logger) : IHistoryCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMilliseconds(750);
    private bool _unavailable;

    public Task<HistoryPage?> GetPageAsync(string key, CancellationToken cancellationToken = default) =>
        GetAsync<HistoryPage>(key, cancellationToken);

    public Task SetPageAsync(string key, HistoryPage value, TimeSpan ttl,
        CancellationToken cancellationToken = default) => SetAsync(key, value, ttl, cancellationToken);

    public Task<HistoryGameDetail?> GetGameAsync(string key,
        CancellationToken cancellationToken = default) =>
        GetAsync<HistoryGameDetail>(key, cancellationToken);

    public Task SetGameAsync(string key, HistoryGameDetail value, TimeSpan ttl,
        CancellationToken cancellationToken = default) => SetAsync(key, value, ttl, cancellationToken);

    private async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
    {
        if (_unavailable) return null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(OperationTimeout);
            var bytes = await distributed.GetAsync(key, timeout.Token).WaitAsync(timeout.Token);
            return bytes is null ? null : JsonSerializer.Deserialize<T>(bytes, JsonOptions);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            logger.LogWarning(exception, "History cache read timed out; falling back to PostgreSQL");
            return null;
        }
        catch (Exception exception)
        {
            _unavailable = true;
            logger.LogWarning(exception, "History cache read failed; falling back to PostgreSQL");
            return null;
        }
    }

    private async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken)
    {
        if (_unavailable) return;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(OperationTimeout);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
            await distributed.SetAsync(key, bytes,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, timeout.Token)
                .WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            logger.LogWarning(exception, "History cache write timed out; PostgreSQL remains authoritative");
        }
        catch (Exception exception)
        {
            _unavailable = true;
            logger.LogWarning(exception, "History cache write failed; PostgreSQL result remains authoritative");
        }
    }
}
