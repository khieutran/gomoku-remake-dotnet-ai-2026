using Gomoku.Application.History;
using Gomoku.Application.Identity;
using Xunit;

namespace Gomoku.Application.Tests.History;

public sealed class HistoryCacheTests
{
    private static readonly DateTimeOffset FinishedAt =
        new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ListCachesOnlyCompletedPageForTheCurrentRevision()
    {
        var owner = new OwnerIdentity(OwnerScope.Guest, Guid.NewGuid());
        var first = Summary(Guid.NewGuid());
        var reader = new FakeReader { Revision = 1, Rows = [first] };
        var cache = new FakeCache();
        var service = new HistoryService(reader, cache);

        Assert.Single((await service.ListAsync(owner, null, 20)).Items);
        Assert.Single((await service.ListAsync(owner, null, 20)).Items);
        Assert.Equal(1, reader.PageReads);
        Assert.Equal(2, reader.RevisionReads);

        var newGame = Summary(Guid.NewGuid());
        reader.Revision = 2;
        reader.Rows = [newGame, first];
        var refreshed = await service.ListAsync(owner, null, 20);
        Assert.Equal(2, refreshed.Items.Count);
        Assert.Equal(newGame.GameId, refreshed.Items[0].GameId);
        Assert.Equal(2, reader.PageReads);
    }

    [Fact]
    public async Task PageCacheSeparatesScopeOwnerCursorAndLimit()
    {
        var id = Guid.NewGuid();
        var guest = new OwnerIdentity(OwnerScope.Guest, id);
        var account = new OwnerIdentity(OwnerScope.Account, id);
        var reader = new FakeReader { Revision = 3, Rows = [Summary(Guid.NewGuid())] };
        var service = new HistoryService(reader, new FakeCache());
        var cursor = new HistoryCursor(FinishedAt, Guid.NewGuid());

        await service.ListAsync(guest, null, 20);
        await service.ListAsync(account, null, 20);
        await service.ListAsync(guest, cursor, 20);
        await service.ListAsync(guest, null, 1);
        Assert.Equal(4, reader.PageReads);
        await service.ListAsync(guest, null, 20);
        Assert.Equal(4, reader.PageReads);
    }

    [Fact]
    public async Task DetailVerifiesOwnershipBeforeEveryCacheLookup()
    {
        var owner = new OwnerIdentity(OwnerScope.Guest, Guid.NewGuid());
        var other = new OwnerIdentity(OwnerScope.Guest, Guid.NewGuid());
        var summary = Summary(Guid.NewGuid());
        var reader = new FakeReader { OwnedBy = owner, OwnedSummary = summary,
            Detail = Detail(summary) };
        var cache = new FakeCache();
        var service = new HistoryService(reader, cache);

        Assert.NotNull(await service.GetAsync(owner, summary.GameId));
        Assert.NotNull(await service.GetAsync(owner, summary.GameId));
        Assert.Equal(1, reader.DetailReads);
        Assert.Equal(2, reader.SummaryReads);

        Assert.Null(await service.GetAsync(other, summary.GameId));
        Assert.Equal(2, cache.GameReads);
        Assert.Equal(1, reader.DetailReads);
    }

    [Fact]
    public async Task CacheMissAndUnavailableCacheStillReadPostgresPort()
    {
        var owner = new OwnerIdentity(OwnerScope.Account, Guid.NewGuid());
        var summary = Summary(Guid.NewGuid());
        var reader = new FakeReader { Revision = 1, Rows = [summary], OwnedBy = owner,
            OwnedSummary = summary, Detail = Detail(summary) };
        var service = new HistoryService(reader, new FakeCache { Unavailable = true });

        Assert.Single((await service.ListAsync(owner, null, 20)).Items);
        Assert.Single((await service.ListAsync(owner, null, 20)).Items);
        Assert.NotNull(await service.GetAsync(owner, summary.GameId));
        Assert.Equal(2, reader.PageReads);
        Assert.Equal(1, reader.DetailReads);
    }

    [Fact]
    public async Task CompletionDuringPagePopulationCannotPoisonNewRevisionKey()
    {
        var owner = new OwnerIdentity(OwnerScope.Guest, Guid.NewGuid());
        var oldGame = Summary(Guid.NewGuid());
        var newGame = Summary(Guid.NewGuid());
        var readStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRead = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new FakeReader
        {
            Revision = 1, Rows = [oldGame], PageReadStarted = readStarted,
            ReleasePageRead = releaseRead
        };
        var service = new HistoryService(reader, new FakeCache());

        var pending = service.ListAsync(owner, null, 20);
        await readStarted.Task;
        reader.Revision = 2;
        reader.Rows = [newGame, oldGame];
        releaseRead.SetResult(true);
        Assert.Single((await pending).Items);

        reader.PageReadStarted = null;
        reader.ReleasePageRead = null;
        var afterCommit = await service.ListAsync(owner, null, 20);
        Assert.Equal(2, afterCommit.Items.Count);
        Assert.Equal(newGame.GameId, afterCommit.Items[0].GameId);
        Assert.Equal(2, reader.PageReads);
    }

    private static HistoryGameSummary Summary(Guid gameId) => new(gameId, Guid.NewGuid(), 1,
        "XWon", "X", "gomoku-exact-five-v1", 20, null,
        FinishedAt.AddMinutes(-1), FinishedAt, 9);

    private static HistoryGameDetail Detail(HistoryGameSummary summary) => new(
        summary.GameId, summary.MatchId, summary.Number, summary.Status,
        summary.OpeningStone, summary.RuleVersion, summary.BoardSize,
        summary.AiVersion, summary.StartedAt, summary.FinishedAt, []);

    private sealed class FakeReader : IHistoryReader
    {
        public long Revision { get; set; }
        public IReadOnlyList<HistoryGameSummary> Rows { get; set; } = [];
        public OwnerIdentity OwnedBy { get; set; }
        public HistoryGameSummary? OwnedSummary { get; set; }
        public HistoryGameDetail? Detail { get; set; }
        public int PageReads { get; private set; }
        public int RevisionReads { get; private set; }
        public int SummaryReads { get; private set; }
        public int DetailReads { get; private set; }
        public TaskCompletionSource<bool>? PageReadStarted { get; set; }
        public TaskCompletionSource<bool>? ReleasePageRead { get; set; }

        public Task<long> ReadRevisionAsync(OwnerIdentity owner,
            CancellationToken cancellationToken = default)
        {
            RevisionReads++;
            return Task.FromResult(Revision);
        }

        public async Task<IReadOnlyList<HistoryGameSummary>> ReadCompletedAsync(OwnerIdentity owner,
            HistoryCursor? cursor, int take, CancellationToken cancellationToken = default)
        {
            PageReads++;
            var snapshot = Rows.Take(take).ToArray();
            PageReadStarted?.TrySetResult(true);
            if (ReleasePageRead is { } gate) await gate.Task.WaitAsync(cancellationToken);
            return snapshot;
        }

        public Task<HistoryGameSummary?> ReadCompletedSummaryAsync(OwnerIdentity owner,
            Guid gameId, CancellationToken cancellationToken = default)
        {
            SummaryReads++;
            return Task.FromResult(owner == OwnedBy && OwnedSummary?.GameId == gameId
                ? OwnedSummary : null);
        }

        public Task<HistoryGameDetail?> ReadGameAsync(OwnerIdentity owner, Guid gameId,
            CancellationToken cancellationToken = default)
        {
            DetailReads++;
            return Task.FromResult(Detail);
        }
    }

    private sealed class FakeCache : IHistoryCache
    {
        private readonly Dictionary<string, HistoryPage> _pages = new();
        private readonly Dictionary<string, HistoryGameDetail> _games = new();
        public bool Unavailable { get; set; }
        public int GameReads { get; private set; }

        public Task<HistoryPage?> GetPageAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Unavailable ? null : _pages.GetValueOrDefault(key));

        public Task SetPageAsync(string key, HistoryPage value, TimeSpan ttl,
            CancellationToken cancellationToken = default)
        {
            if (!Unavailable) _pages[key] = value;
            return Task.CompletedTask;
        }

        public Task<HistoryGameDetail?> GetGameAsync(string key,
            CancellationToken cancellationToken = default)
        {
            GameReads++;
            return Task.FromResult(Unavailable ? null : _games.GetValueOrDefault(key));
        }

        public Task SetGameAsync(string key, HistoryGameDetail value, TimeSpan ttl,
            CancellationToken cancellationToken = default)
        {
            if (!Unavailable) _games[key] = value;
            return Task.CompletedTask;
        }
    }
}
