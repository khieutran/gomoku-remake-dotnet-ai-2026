using Gomoku.Application.History;

namespace Gomoku.Infrastructure.History;

public sealed class NoopHistoryCache : IHistoryCache
{
    public Task<HistoryPage?> GetPageAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult<HistoryPage?>(null);

    public Task SetPageAsync(string key, HistoryPage value, TimeSpan ttl,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<HistoryGameDetail?> GetGameAsync(string key,
        CancellationToken cancellationToken = default) => Task.FromResult<HistoryGameDetail?>(null);

    public Task SetGameAsync(string key, HistoryGameDetail value, TimeSpan ttl,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
