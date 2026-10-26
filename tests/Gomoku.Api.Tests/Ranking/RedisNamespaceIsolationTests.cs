using System.Net.Http.Json;
using System.Text.Json;
using Gomoku.Application.History;
using Gomoku.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StackExchange.Redis;
using Xunit;

namespace Gomoku.Api.Tests.Ranking;

public sealed class RedisNamespaceIsolationTests
{
    [Fact]
    public async Task IndependentDatabasesSharingRedisHaveIndependentLeaderboards()
    {
        var redis = Environment.GetEnvironmentVariable("GOMOKU_TEST_REDIS");
        var postgres = Environment.GetEnvironmentVariable("GOMOKU_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(redis) || string.IsNullOrWhiteSpace(postgres)) return;

        var firstDatabase = $"gomoku_namespace_{Guid.NewGuid():N}";
        var secondDatabase = $"gomoku_namespace_{Guid.NewGuid():N}";
        var prefix = $"gomoku:test:{Guid.NewGuid():N}";
        var firstConnection = DatabaseConnection(postgres, firstDatabase);
        var secondConnection = DatabaseConnection(postgres, secondDatabase);
        await CreateDatabaseAsync(postgres, firstDatabase);
        await CreateDatabaseAsync(postgres, secondDatabase);
        try
        {
            var firstAccount = await SeedAsync(firstConnection, 3);
            var secondAccount = await SeedAsync(secondConnection, 6);
            using var first = Factory(firstConnection, redis, $"{prefix}:first");
            using var second = Factory(secondConnection, redis, $"{prefix}:second");
            using var firstClient = first.CreateClient();
            using var secondClient = second.CreateClient();
            await using var connection = await ConnectionMultiplexer.ConnectAsync(redis);
            var redisDb = connection.GetDatabase();
            Assert.True(await WaitUntilAsync(async () =>
                await redisDb.KeyExistsAsync($"{prefix}:first:ranking:v1:ready") &&
                await redisDb.KeyExistsAsync($"{prefix}:second:ranking:v1:ready")));

            Assert.True(await WaitUntilAsync(async () =>
                await ContainsOnlyAsync(firstClient, firstAccount, secondAccount) &&
                await ContainsOnlyAsync(secondClient, secondAccount, firstAccount)));

            // A second API instance for the same deployment sees its existing projection.
            using var firstPeer = Factory(firstConnection, redis, $"{prefix}:first");
            using var firstPeerClient = firstPeer.CreateClient();
            Assert.True(await ContainsOnlyAsync(firstPeerClient, firstAccount, secondAccount));

            var historyKey = $"history:namespace-test:{Guid.NewGuid():N}";
            using var firstScope = first.Services.CreateScope();
            using var secondScope = second.Services.CreateScope();
            using var peerScope = firstPeer.Services.CreateScope();
            var firstCache = firstScope.ServiceProvider.GetRequiredService<IHistoryCache>();
            var secondCache = secondScope.ServiceProvider.GetRequiredService<IHistoryCache>();
            var peerCache = peerScope.ServiceProvider.GetRequiredService<IHistoryCache>();
            await firstCache.SetPageAsync(historyKey, new HistoryPage([], null), TimeSpan.FromMinutes(1));
            Assert.Null(await secondCache.GetPageAsync(historyKey));
            Assert.NotNull(await peerCache.GetPageAsync(historyKey));

        }
        finally
        {
            try
            {
                await using var connection = await ConnectionMultiplexer.ConnectAsync(redis);
                var redisDb = connection.GetDatabase();
                foreach (var deployment in new[] { "first", "second" })
                {
                    await redisDb.KeyDeleteAsync($"{prefix}:{deployment}:ranking:v1:alltime");
                    await redisDb.KeyDeleteAsync($"{prefix}:{deployment}:ranking:v1:ready");
                }
            }
            finally
            {
                await DropDatabaseAsync(postgres, firstDatabase);
                await DropDatabaseAsync(postgres, secondDatabase);
            }
        }
    }

    private static WebApplicationFactory<Program> Factory(string postgres, string redis, string keyspace) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Gomoku", postgres);
            builder.UseSetting("ConnectionStrings:Redis", redis);
            builder.UseSetting("GOMOKU_REDIS_NAMESPACE", keyspace);
        });

    private static async Task<Guid> SeedAsync(string connection, int points)
    {
        var options = new DbContextOptionsBuilder<GomokuDbContext>().UseNpgsql(connection).Options;
        await using var db = new GomokuDbContext(options);
        await db.Database.MigrateAsync();
        var id = Guid.NewGuid();
        db.Users.Add(new ApplicationUser
        {
            Id = id,
            UserName = $"namespace-{id:N}@example.test",
            NormalizedUserName = $"NAMESPACE-{id:N}@EXAMPLE.TEST",
            Email = $"namespace-{id:N}@example.test",
            NormalizedEmail = $"NAMESPACE-{id:N}@EXAMPLE.TEST",
            RankingPoints = points,
            RankingGames = 1
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<bool> ContainsOnlyAsync(HttpClient client, Guid expected, Guid absent)
    {
        using var response = await client.GetAsync("/api/v1/leaderboard?limit=50");
        if (!response.IsSuccessStatusCode) return false;
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = json.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("accountId").GetGuid()).ToArray();
        return ids.Contains(expected) && !ids.Contains(absent);
    }

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> predicate)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (await predicate()) return true;
            await Task.Delay(200);
        }
        return false;
    }

    private static string DatabaseConnection(string postgres, string database)
    {
        var builder = new NpgsqlConnectionStringBuilder(postgres) { Database = database };
        return builder.ConnectionString;
    }

    private static async Task CreateDatabaseAsync(string postgres, string database)
    {
        await using var connection = new NpgsqlConnection(DatabaseConnection(postgres, "postgres"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string postgres, string database)
    {
        await using var connection = new NpgsqlConnection(DatabaseConnection(postgres, "postgres"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}
