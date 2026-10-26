namespace Gomoku.Application.Datasets;

public interface IGuestDatasetReader
{
    IAsyncEnumerable<GuestMatchSource> ReadAsync(DateTimeOffset exportCutoffUtc,
        CancellationToken cancellationToken = default);
}
