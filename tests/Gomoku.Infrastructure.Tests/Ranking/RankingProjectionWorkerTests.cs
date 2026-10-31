using Gomoku.Application.Ranking;
using Gomoku.Infrastructure.Persistence;
using Gomoku.Infrastructure.Ranking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Gomoku.Infrastructure.Tests.Ranking;

public sealed class RankingProjectionWorkerTests
{
    [Fact]
    public async Task UnavailableRedisDoesNotReadAllAccountsOnEveryPass()
    {
        var postgres = Environment.GetEnvironmentVariable("GOMOKU_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(postgres)) return;
        var reader = new CountingReader();
        var projection = new ToggleProjection();
        var services = new ServiceCollection();
        services.AddDbContext<GomokuDbContext>(options => options.UseNpgsql(postgres));
        services.AddSingleton<IRankingReader>(reader);
        services.AddSingleton<IRankingProjection>(projection);
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<GomokuDbContext>()
                .Database.MigrateAsync();

        using var worker = new RankingProjectionWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System, NullLogger<RankingProjectionWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2.2));
            Assert.Equal(0, reader.ReadAllCalls);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RedisRecoveryRebuildsOnceAfterOutage()
    {
        var postgres = Environment.GetEnvironmentVariable("GOMOKU_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(postgres)) return;
        var reader = new CountingReader();
        var projection = new ToggleProjection();
        var services = new ServiceCollection();
        services.AddDbContext<GomokuDbContext>(options => options.UseNpgsql(postgres));
        services.AddSingleton<IRankingReader>(reader);
        services.AddSingleton<IRankingProjection>(projection);
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<GomokuDbContext>()
                .Database.MigrateAsync();

        using var worker = new RankingProjectionWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System, NullLogger<RankingProjectionWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1.2));
            Assert.Equal(0, reader.ReadAllCalls);
            projection.Available = true;
            for (var attempt = 0; attempt < 50 && projection.RebuildCalls == 0; attempt++)
                await Task.Delay(100);
            Assert.Equal(1, projection.RebuildCalls);
            Assert.Equal(1, reader.ReadAllCalls);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RebuildDoesNotHoldPostgresTransactionWhileWaitingForRedis()
    {
        var postgres = Environment.GetEnvironmentVariable("GOMOKU_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(postgres)) return;
        var applicationName = $"gomoku-ranking-worker-{Guid.NewGuid():N}";
        var connection = new NpgsqlConnectionStringBuilder(postgres)
        {
            ApplicationName = applicationName
        }.ConnectionString;
        var projection = new ToggleProjection { Available = true };
        projection.BlockNextRebuild();
        var services = new ServiceCollection();
        services.AddDbContext<GomokuDbContext>(options => options.UseNpgsql(connection));
        services.AddSingleton<IRankingReader>(new CountingReader());
        services.AddSingleton<IRankingProjection>(projection);
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<GomokuDbContext>()
                .Database.MigrateAsync();

        using var worker = new RankingProjectionWorker(provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System, NullLogger<RankingProjectionWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await projection.RebuildStarted.WaitAsync(TimeSpan.FromSeconds(5));
            await using var observer = new NpgsqlConnection(postgres);
            await observer.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE application_name = @name AND state = 'idle in transaction'",
                observer);
            command.Parameters.AddWithValue("name", applicationName);
            Assert.Equal(0L, await command.ExecuteScalarAsync());
        }
        finally
        {
            projection.ReleaseRebuild();
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private sealed class CountingReader : IRankingReader
    {
        private int _readAllCalls;
        public int ReadAllCalls => Volatile.Read(ref _readAllCalls);
        public Task<IReadOnlyList<RankingScore>> ReadAllAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _readAllCalls);
            return Task.FromResult<IReadOnlyList<RankingScore>>([]);
        }
        public Task<bool> HasPendingAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
        public Task<IReadOnlyList<RankingScore>> ReadTopAsync(int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RankingScore>>([]);
        public Task<RankingPosition?> ReadPositionAsync(Guid accountId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<RankingPosition?>(null);
        public Task<IReadOnlyDictionary<Guid, string>> ReadDisplayNamesAsync(IReadOnlyCollection<Guid> accountIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class ToggleProjection : IRankingProjection
    {
        private int _available;
        private int _ready;
        private int _rebuildCalls;
        private TaskCompletionSource? _rebuildStarted;
        private TaskCompletionSource? _releaseRebuild;
        public bool Available
        {
            get => Volatile.Read(ref _available) != 0;
            set => Volatile.Write(ref _available, value ? 1 : 0);
        }
        public int RebuildCalls => Volatile.Read(ref _rebuildCalls);
        public Task RebuildStarted => _rebuildStarted?.Task ?? Task.CompletedTask;
        public void BlockNextRebuild()
        {
            _rebuildStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _releaseRebuild = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        public void ReleaseRebuild() => _releaseRebuild?.TrySetResult();
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Available);
        public Task<bool> IsReadyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Volatile.Read(ref _ready) != 0);
        public async Task<bool> RebuildAsync(IReadOnlyList<RankingScore> scores,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _rebuildCalls);
            _rebuildStarted?.TrySetResult();
            if (_releaseRebuild is not null)
                await _releaseRebuild.Task.WaitAsync(cancellationToken);
            Volatile.Write(ref _ready, 1);
            return true;
        }
        public Task<bool> PublishAsync(Guid accountId, int points,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<IReadOnlyList<RankingScore>?> ReadTopAsync(int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RankingScore>?>(null);
        public Task<RankingPosition?> ReadPositionAsync(Guid accountId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<RankingPosition?>(null);
    }
}
