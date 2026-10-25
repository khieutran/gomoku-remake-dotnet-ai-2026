using Gomoku.Application.Ranking;
using Gomoku.Infrastructure.Persistence;
using Gomoku.Infrastructure.Ranking;
using Gomoku.Infrastructure.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace Gomoku.Infrastructure.Tests.Ranking;

public sealed class RedisRankingProjectionTests
{
    [Fact]
    public async Task EqualScoresUseSameOrderAsPostgresAndKeepSharedRank()
    {
        var redis = Environment.GetEnvironmentVariable("GOMOKU_TEST_REDIS");
        var postgres = Environment.GetEnvironmentVariable("GOMOKU_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(redis) || string.IsNullOrWhiteSpace(postgres)) return;
        var options = ConfigurationOptions.Parse(redis);
        options.DefaultDatabase = 5;
        await using var connection = await ConnectionMultiplexer.ConnectAsync(options);
        await using var db = new GomokuDbContext(new DbContextOptionsBuilder<GomokuDbContext>()
            .UseNpgsql(postgres).Options);
        await db.Database.MigrateAsync();
        var keyspace = new RedisKeyspace($"gomoku:test:{Guid.NewGuid():N}");
        var projection = new RedisRankingProjection(connection, keyspace,
            NullLogger<RedisRankingProjection>.Instance);
        var users = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() }
            .Select(id => new ApplicationUser
            {
                Id = id,
                UserName = $"tie-{id:N}@example.test",
                NormalizedUserName = $"TIE-{id:N}@EXAMPLE.TEST",
                RankingPoints = 99999,
                RankingGames = 1
            }).ToArray();
        try
        {
            db.Users.AddRange(users);
            await db.SaveChangesAsync();
            var reader = new EfRankingReader(db);
            var expected = (await reader.ReadTopAsync(50))
                .Where(score => users.Any(user => user.Id == score.AccountId))
                .Select(score => score.AccountId).ToArray();
            Assert.Equal(3, expected.Length);
            Assert.True(await projection.RebuildAsync(users
                .Select(user => new RankingScore(user.Id, user.RankingPoints)).ToArray()));

            var actual = (await projection.ReadTopAsync(3))!
                .Select(score => score.AccountId).ToArray();
            Assert.Equal(expected, actual);
            var page = await new RankingService(reader, projection).TopAsync(3);
            Assert.All(page.Items, item => Assert.Equal(1, item.Rank));
        }
        finally
        {
            await connection.GetDatabase().KeyDeleteAsync(keyspace.Key("ranking:v1:alltime"));
            await connection.GetDatabase().KeyDeleteAsync(keyspace.Key("ranking:v1:ready"));
            db.Users.RemoveRange(users);
            await db.SaveChangesAsync();
        }
    }

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
