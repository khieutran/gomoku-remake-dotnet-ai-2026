using Gomoku.Application.Datasets;
using Gomoku.Application.Identity;
using Gomoku.Application.Matches;
using Gomoku.Infrastructure.Datasets;
using Gomoku.Infrastructure.Matches;
using Gomoku.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Gomoku.Infrastructure.Tests.Datasets;

public sealed class GuestDatasetReaderTests
{
    [Fact]
    public async Task ExportUsesOnlyCompletedGuestGamesFromStableSnapshot()
    {
        var baseConnection = Environment.GetEnvironmentVariable("GOMOKU_TEST_POSTGRES")
            ?? throw new InvalidOperationException("Set GOMOKU_TEST_POSTGRES.");
        var schema = "dataset_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(baseConnection))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin);
            await create.ExecuteNonQueryAsync();
        }
        try
        {
            var connection = baseConnection + ";Search Path=" + schema;
            await using var db = new GomokuDbContext(new DbContextOptionsBuilder<GomokuDbContext>()
                .UseNpgsql(connection).Options);
            await db.Database.MigrateAsync();
            var guestId = Guid.NewGuid();
            db.GuestSessions.Add(new GuestSession
            {
                Id = guestId, TokenHash = Guid.NewGuid().ToByteArray(),
                CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
            });
            var accountId = Guid.NewGuid();
            db.Users.Add(new ApplicationUser
            {
                Id = accountId, UserName = "dataset-" + accountId,
                NormalizedUserName = ("DATASET-" + accountId).ToUpperInvariant()
            });
            await db.SaveChangesAsync();
            var flow = new GameFlowService(new EfGameFlowStore(db, TimeProvider.System));
            var guest = new OwnerIdentity(OwnerScope.Guest, guestId);
            var account = new OwnerIdentity(OwnerScope.Account, accountId);
            var guestMatch = (await flow.CreateMatchAsync(guest, "guest-" + Guid.NewGuid())).MatchId!.Value;
            var secondGuestMatch = (await flow.CreateMatchAsync(guest, "guest-" + Guid.NewGuid())).MatchId!.Value;
            var accountMatch = (await flow.CreateMatchAsync(account, "account-" + Guid.NewGuid())).MatchId!.Value;
            await FinishAsync(flow, guest, guestMatch, 10);
            await FinishAsync(flow, account, accountMatch, 11);
            var cutoff = DateTimeOffset.UtcNow.AddHours(1);

            var reader = new EfGuestDatasetReader(db, 1);
            var sources = new List<GuestMatchSource>();
            await using (var enumeration = reader.ReadAsync(cutoff).GetAsyncEnumerator())
            {
                Assert.True(await enumeration.MoveNextAsync());
                sources.Add(enumeration.Current);
                await using (var concurrent = new GomokuDbContext(
                    new DbContextOptionsBuilder<GomokuDbContext>().UseNpgsql(connection).Options))
                {
                    var concurrentFlow = new GameFlowService(new EfGameFlowStore(concurrent,
                        TimeProvider.System));
                    _ = await concurrentFlow.CreateMatchAsync(guest, "late-" + Guid.NewGuid());
                }
                while (await enumeration.MoveNextAsync()) sources.Add(enumeration.Current);
            }

            Assert.Equal(2, sources.Count);
            Assert.True(new HashSet<Guid> { guestMatch, secondGuestMatch }
                .SetEquals(sources.Select(source => source.Match.Id)));
            Assert.All(sources, source =>
            {
                Assert.Equal(OwnerScope.Guest, source.Match.Owner.Scope);
                Assert.Equal("search-v1", Assert.Single(source.Match.Games).AiVersion);
            });
            var finished = sources.Single(source => source.Match.Id == guestMatch);
            var active = sources.Single(source => source.Match.Id == secondGuestMatch);
            Assert.NotNull(finished.FinishedAtByGame[finished.Match.CurrentGameId]);
            Assert.Null(active.FinishedAtByGame[active.Match.CurrentGameId]);

            var output = Path.Combine(Path.GetTempPath(), "gomoku-dataset-" + Guid.NewGuid());
            try
            {
                var job = new DatasetExportJob(new EfGuestDatasetReader(db, 1));
                var run = await job.ExportAsync(new DatasetExportSettings("reader-test",
                    cutoff, cutoff), output);
                var lines = await File.ReadAllLinesAsync(Path.Combine(run, "dataset.jsonl"));
                Assert.Single(lines);
                Assert.Contains("\"source\":\"guest\"", lines[0]);
            }
            finally
            {
                if (Directory.Exists(output)) Directory.Delete(output, true);
            }
        }
        finally
        {
            await using var admin = new NpgsqlConnection(baseConnection);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task FinishAsync(GameFlowService flow, OwnerIdentity owner,
        Guid matchId, int humanRow)
    {
        long revision = 0;
        for (var column = 0; column < 5; column++)
        {
            Assert.Equal("ok", (await flow.PlayAsync(owner, matchId, humanRow, column,
                PlayerActor.Human, revision++, "human-" + Guid.NewGuid())).Code);
            if (column < 4)
                Assert.Equal("ok", (await flow.PlayAsync(owner, matchId, 0, column,
                    PlayerActor.AI, revision++, "ai-" + Guid.NewGuid(),
                    aiMetadata: new AiMoveMetadata("search-v1", null))).Code);
        }
    }
}
