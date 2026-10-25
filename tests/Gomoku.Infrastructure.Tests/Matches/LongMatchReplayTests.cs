using System.Data.Common;
using System.Diagnostics;
using Gomoku.Application.Identity;
using Gomoku.Application.Matches;
using Gomoku.Infrastructure.Matches;
using Gomoku.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace Gomoku.Infrastructure.Tests.Matches;

public sealed class LongMatchReplayTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(50)]
    public async Task CompletedGamesReplayAndAcceptNextMove(int completedGames)
    {
        var postgres = Environment.GetEnvironmentVariable("GOMOKU_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(postgres)) return;
        var counter = new QueryCounter();
        var options = new DbContextOptionsBuilder<GomokuDbContext>()
            .UseNpgsql(postgres).AddInterceptors(counter).Options;
        await using var db = new GomokuDbContext(options);
        await db.Database.MigrateAsync();
        var owner = new OwnerIdentity(OwnerScope.Guest, Guid.NewGuid());
        db.GuestSessions.Add(new GuestSession
        {
            Id = owner.Id,
            TokenHash = Guid.NewGuid().ToByteArray(),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        });
        await db.SaveChangesAsync();
        var store = new EfGameFlowStore(db, TimeProvider.System);
        var flow = new GameFlowService(store);
        var matchId = (await flow.CreateMatchAsync(owner, $"long-create-{Guid.NewGuid():N}"))
            .MatchId!.Value;
        long revision = 0;
        for (var game = 0; game < completedGames; game++)
        {
            var opening = game % 2 == 0 ? PlayerActor.Human : PlayerActor.AI;
            for (var turn = 0; turn < 9; turn++)
            {
                var actor = turn % 2 == 0 ? opening
                    : opening == PlayerActor.Human ? PlayerActor.AI : PlayerActor.Human;
                var row = actor == PlayerActor.Human ? 0 : 1;
                var result = await flow.PlayAsync(owner, matchId, row, turn / 2,
                    actor, revision++, $"long-move-{Guid.NewGuid():N}");
                Assert.Equal("ok", result.Code);
            }
            Assert.Equal("ok", (await flow.StartNextGameAsync(owner, matchId,
                revision++, $"long-next-{Guid.NewGuid():N}")).Code);
        }

        counter.Reset();
        var watch = Stopwatch.StartNew();
        var stored = await store.LoadAsync(owner, matchId);
        var loadMs = watch.Elapsed.TotalMilliseconds;
        var loadQueries = counter.Count;
        Assert.NotNull(stored);
        Assert.Equal(completedGames + 1, stored.Games.Count);
        Assert.Equal(completedGames * 9, stored.Games.Sum(game => game.Moves.Count));

        watch.Restart();
        var replay = MatchReplayer.Replay(stored);
        var replayMs = watch.Elapsed.TotalMilliseconds;
        Assert.Equal(completedGames, replay.Snapshot().CompletedGames.Count);
        var nextActor = completedGames % 2 == 0 ? PlayerActor.Human : PlayerActor.AI;
        var commandId = $"long-play-{Guid.NewGuid():N}";
        counter.Reset();
        watch.Restart();
        var played = await flow.PlayAsync(owner, matchId, 2, 0, nextActor, revision, commandId);
        var playMs = watch.Elapsed.TotalMilliseconds;
        var playQueries = counter.Count;
        Assert.Equal("ok", played.Code);
        Assert.Equal(played.Body, (await flow.PlayAsync(owner, matchId, 2, 0,
            nextActor, revision, commandId)).Body);
        Assert.Equal(completedGames * 9 + 1,
            await db.Moves.CountAsync(move => db.Games
                .Where(game => game.MatchId == matchId).Select(game => game.Id).Contains(move.GameId)));
        output.WriteLine($"games={completedGames}, moves={completedGames * 9}, " +
            $"load_ms={loadMs:F3}, load_queries={loadQueries}, " +
            $"replay_ms={replayMs:F3}, play_ms={playMs:F3}, play_queries={playQueries}");
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public void Reset() => Interlocked.Exchange(ref _count, 0);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
