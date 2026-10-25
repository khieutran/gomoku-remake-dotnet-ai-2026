using System.Reflection;
using Gomoku.Application.Ranking;
using Gomoku.Infrastructure.Ranking;
using Gomoku.Infrastructure.Redis;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace Gomoku.Infrastructure.Tests.Ranking;

public sealed class RedisRankingRaceTests
{
    [Fact]
    public async Task BoardLostBetweenReadinessAndRangeFallsBackToPostgres()
    {
        var redis = Environment.GetEnvironmentVariable("GOMOKU_TEST_REDIS");
        if (string.IsNullOrWhiteSpace(redis)) return;
        var options = ConfigurationOptions.Parse(redis);
        options.DefaultDatabase = 5;
        await using var connection = await ConnectionMultiplexer.ConnectAsync(options);
        var keyspace = new RedisKeyspace($"gomoku:test:{Guid.NewGuid():N}");
        var boardKey = keyspace.Key("ranking:v1:alltime");
        var readyKey = keyspace.Key("ranking:v1:ready");
        var original = new RedisRankingProjection(connection, keyspace,
            NullLogger<RedisRankingProjection>.Instance);
        try
        {
            Assert.True(await original.RebuildAsync([new RankingScore(Guid.NewGuid(), 3)]));
            var database = DispatchProxy.Create<IDatabase, DeleteBoardBeforeRange>();
            var databaseProxy = (DeleteBoardBeforeRange)(object)database;
            databaseProxy.Inner = connection.GetDatabase();
            databaseProxy.BoardKey = boardKey;
            var multiplexer = DispatchProxy.Create<IConnectionMultiplexer, DatabaseMultiplexer>();
            var multiplexerProxy = (DatabaseMultiplexer)(object)multiplexer;
            multiplexerProxy.Inner = connection;
            multiplexerProxy.Database = database;
            var racing = new RedisRankingProjection(multiplexer, keyspace,
                NullLogger<RedisRankingProjection>.Instance);

            Assert.Null(await racing.ReadTopAsync(10));
            Assert.True(databaseProxy.DeletedBoard);
        }
        finally
        {
            await connection.GetDatabase().KeyDeleteAsync(boardKey);
            await connection.GetDatabase().KeyDeleteAsync(readyKey);
        }
    }

    public class DatabaseMultiplexer : DispatchProxy
    {
        public IConnectionMultiplexer Inner { get; set; } = null!;
        public IDatabase Database { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IConnectionMultiplexer.GetDatabase)
                ? Database
                : targetMethod?.Invoke(Inner, args);
    }

    public class DeleteBoardBeforeRange : DispatchProxy
    {
        public IDatabase Inner { get; set; } = null!;
        public string BoardKey { get; set; } = null!;
        public bool DeletedBoard { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IDatabase.SortedSetRangeByRankWithScoresAsync))
            {
                Inner.KeyDelete(BoardKey);
                DeletedBoard = true;
            }
            return targetMethod?.Invoke(Inner, args);
        }
    }
}
