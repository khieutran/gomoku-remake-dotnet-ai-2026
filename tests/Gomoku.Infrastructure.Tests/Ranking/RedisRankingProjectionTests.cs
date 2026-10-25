using Gomoku.Infrastructure.Ranking;
using Gomoku.Infrastructure.Redis;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace Gomoku.Infrastructure.Tests.Ranking;

public sealed class RedisRankingProjectionTests
{
    [Fact]
    public async Task NamespacesKeepIndependentBoardsOnSharedRedis()
    {
        var redis = Environment.GetEnvironmentVariable("GOMOKU_TEST_REDIS");
        if (string.IsNullOrWhiteSpace(redis)) return;
        var options = ConfigurationOptions.Parse(redis);
        options.DefaultDatabase = 5;
        await using var connection = await ConnectionMultiplexer.ConnectAsync(options);
        var firstNamespace = $"gomoku:test:{Guid.NewGuid():N}:first";
        var secondNamespace = $"gomoku:test:{Guid.NewGuid():N}:second";
        var first = new RedisRankingProjection(connection, new RedisKeyspace(firstNamespace),
            NullLogger<RedisRankingProjection>.Instance);
        var second = new RedisRankingProjection(connection, new RedisKeyspace(secondNamespace),
            NullLogger<RedisRankingProjection>.Instance);
        var sameEnvironment = new RedisRankingProjection(connection, new RedisKeyspace(firstNamespace),
            NullLogger<RedisRankingProjection>.Instance);
        var accountId = Guid.NewGuid();

        try
        {
            Assert.True(await first.RebuildAsync([new(accountId, 3)]));
            Assert.Equal(3, (await sameEnvironment.ReadPositionAsync(accountId))?.Points);
            Assert.Null(await second.ReadTopAsync(10));
            Assert.True(await second.RebuildAsync([]));
            Assert.Empty((await second.ReadTopAsync(10))!);
            Assert.Single((await first.ReadTopAsync(10))!);
        }
        finally
        {
            var db = connection.GetDatabase();
            foreach (var key in new[] { firstNamespace, secondNamespace })
            {
                await db.KeyDeleteAsync($"{key}:ranking:v1:alltime");
                await db.KeyDeleteAsync($"{key}:ranking:v1:ready");
            }
        }
    }

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
            var projection = new RedisRankingProjection(connection, new RedisKeyspace("gomoku"),
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
