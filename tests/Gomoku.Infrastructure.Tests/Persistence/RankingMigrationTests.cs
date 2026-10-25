using Gomoku.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Gomoku.Infrastructure.Tests.Persistence;

public sealed class RankingMigrationTests
{
    [Fact]
    public async Task UpgradeCountsExistingAccountWinsDrawsAndLosses()
    {
        var source = Environment.GetEnvironmentVariable("GOMOKU_TEST_POSTGRES")
            ?? throw new InvalidOperationException("Set GOMOKU_TEST_POSTGRES.");
        var name = "ranking_upgrade_" + Guid.NewGuid().ToString("N");
        var adminBuilder = new NpgsqlConnectionStringBuilder(source) { Database = "postgres" };
        await using var admin = new NpgsqlConnection(adminBuilder.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin))
            await create.ExecuteNonQueryAsync();

        try
        {
            var databaseBuilder = new NpgsqlConnectionStringBuilder(source) { Database = name };
            var options = new DbContextOptionsBuilder<GomokuDbContext>()
                .UseNpgsql(databaseBuilder.ConnectionString).Options;
            await using var db = new GomokuDbContext(options);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261024084226_InitialStorage");
            var userId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "AspNetUsers" ("Id", "EmailConfirmed", "PhoneNumberConfirmed",
                  "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
                VALUES ({userId}, false, false, false, false, 0)
                """);

            var statuses = new[] { "XWon", "Draw", "OWon" };
            foreach (var status in statuses)
            {
                var matchId = Guid.NewGuid();
                var gameId = Guid.NewGuid();
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "Matches" ("Id", "UserId", "RuleVersion", "BoardSize",
                      "XScore", "OScore", "Revision", "CreatedAt", "UpdatedAt")
                    VALUES ({matchId}, {userId}, 'gomoku-exact-five-v1', 20, 0, 0, 0, {now}, {now})
                    """);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "Games" ("Id", "MatchId", "Number", "OpeningStone",
                      "RuleStatus", "State", "StartedAt", "FinishedAt")
                    VALUES ({gameId}, {matchId}, 1, 'X', {status}, 'Finished', {now}, {now})
                    """);
            }

            await migrator.MigrateAsync();
            var user = await db.Users.AsNoTracking().Where(x => x.Id == userId)
                .Select(x => new { x.RankingPoints, x.RankingGames }).SingleAsync();
            Assert.Equal(4, user.RankingPoints);
            Assert.Equal(3, user.RankingGames);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
