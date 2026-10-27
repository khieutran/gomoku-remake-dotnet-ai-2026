using System.Security.Cryptography;
using System.Text.Json;
using Gomoku.Application.Datasets;
using Gomoku.Application.Identity;
using Gomoku.Application.Matches;
using Gomoku.Core;
using Gomoku.Infrastructure.Datasets;
using Xunit;

namespace Gomoku.Infrastructure.Tests.Datasets;

public sealed class DatasetExportJobTests
{
    [Fact]
    public async Task SameInputProducesIdenticalFilesWithoutOwnerIdentifiers()
    {
        if (OperatingSystem.IsWindows()) return;
        var matchId = Guid.NewGuid();
        var guestId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        var moves = new List<MoveRecord>();
        for (var column = 0; column < 4; column++)
        {
            moves.Add(new MoveRecord(moves.Count + 1, 10, column, Stone.X, PlayerActor.Human));
            moves.Add(new MoveRecord(moves.Count + 1, 0, column, Stone.O, PlayerActor.AI,
                "search-v1"));
        }
        moves.Add(new MoveRecord(9, 10, 4, Stone.X, PlayerActor.Human));
        var record = new MatchRecord(matchId, new OwnerIdentity(OwnerScope.Guest, guestId),
            RuleSet.CurrentVersion, 20, 3, 0, gameId, 9,
            [new GameRecord(gameId, 1, Stone.X, GameStatus.XWon,
                GameRecordState.Finished, moves, "search-v1")]);
        var cutoff = new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
        var source = new GuestMatchSource(record,
            new Dictionary<Guid, DateTimeOffset?> { [gameId] = cutoff.AddHours(-1) },
            cutoff.AddDays(-1));
        var job = new DatasetExportJob(new OnePageReader(source));
        var settings = new DatasetExportSettings("fixed-seed", cutoff, cutoff);
        var first = Path.Combine(Path.GetTempPath(), "gomoku-dataset-" + Guid.NewGuid());
        var second = Path.Combine(Path.GetTempPath(), "gomoku-dataset-" + Guid.NewGuid());
        try
        {
            var firstRun = await job.ExportAsync(settings, first);
            var secondRun = await job.ExportAsync(settings, second);
            Assert.Equal(firstRun, await job.ExportAsync(settings, first));
            var dataPath = Path.Combine(firstRun, "dataset.jsonl");
            var data = await File.ReadAllBytesAsync(dataPath);
            var manifest = await File.ReadAllBytesAsync(Path.Combine(firstRun, "manifest.json"));
            Assert.Equal(data, await File.ReadAllBytesAsync(Path.Combine(secondRun, "dataset.jsonl")));
            Assert.Equal(manifest, await File.ReadAllBytesAsync(Path.Combine(secondRun, "manifest.json")));
            Assert.Single(Directory.GetDirectories(first));
            Assert.Equal(UnixFileMode.None, File.GetUnixFileMode(first) &
                (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                 UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute));
            Assert.Equal(UnixFileMode.None, File.GetUnixFileMode(dataPath) &
                (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead |
                 UnixFileMode.OtherWrite));
            var dataText = System.Text.Encoding.UTF8.GetString(data);
            Assert.DoesNotContain(matchId.ToString("N"), dataText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(guestId.ToString("N"), dataText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(gameId.ToString("N"), dataText, StringComparison.OrdinalIgnoreCase);
            using var json = JsonDocument.Parse(manifest);
            Assert.Equal(1, json.RootElement.GetProperty("rowCount").GetInt32());
            Assert.Equal(Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(),
                json.RootElement.GetProperty("datasetSha256").GetString());
        }
        finally
        {
            if (Directory.Exists(first)) Directory.Delete(first, true);
            if (Directory.Exists(second)) Directory.Delete(second, true);
        }
    }

    private sealed class OnePageReader(GuestMatchSource source) : IGuestDatasetReader
    {
        public async IAsyncEnumerable<GuestMatchSource> ReadAsync(
            DateTimeOffset exportCutoffUtc,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return source;
        }
    }
}
