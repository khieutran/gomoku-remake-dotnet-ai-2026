using Gomoku.Infrastructure.Ranking;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace Gomoku.Infrastructure.Tests.Ranking;

public sealed class RedisRankingProjectionTests
{
    [Fact]
    public async Task FirstPublishedScoreMakesMissingBoardDetectable()
    {
        var redis = Environment.GetEnvironmentVariable("GOMOKU_TEST_REDIS");
        if (string.IsNullOrWhiteSpace(redis)) return;
        var options = ConfigurationOptions.Parse(redis);
        options.DefaultDatabase = 5;
        await using var connection = await ConnectionMultiplexer.ConnectAsync(options);
        var db = connection.GetDatabase();
        const string board = "gomoku:ranking:v1:alltime";
        const string ready = "gomoku:ranking:v1:ready";
        try
        {
            var projection = new RedisRankingProjection(connection,
                NullLogger<RedisRankingProjection>.Instance);
            await db.KeyDeleteAsync(board);
            await db.KeyDeleteAsync(ready);
            Assert.True(await projection.RebuildAsync([]));
            var accountId = Guid.NewGuid();
            Assert.True(await projection.PublishAsync(accountId, 3));
            Assert.True(await projection.PublishAsync(accountId, 0));
            Assert.Equal(3, await db.SortedSetScoreAsync(board, accountId.ToString("N")));
            await db.KeyDeleteAsync(board);

            Assert.False(await projection.IsReadyAsync());
        }
        finally
        {
            await db.KeyDeleteAsync(board);
            await db.KeyDeleteAsync(ready);
        }
    }
}
