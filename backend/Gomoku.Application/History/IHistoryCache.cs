namespace Gomoku.Application.History;

public interface IHistoryCache
{
    Task<HistoryPage?> GetPageAsync(string key, CancellationToken cancellationToken = default);
    Task SetPageAsync(string key, HistoryPage value, TimeSpan ttl,
        CancellationToken cancellationToken = default);

    Task<HistoryGameDetail?> GetGameAsync(string key, CancellationToken cancellationToken = default);
    Task SetGameAsync(string key, HistoryGameDetail value, TimeSpan ttl,
        CancellationToken cancellationToken = default);
}
