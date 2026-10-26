using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gomoku.Core;

namespace Gomoku.SelfPlay.Tests;

public sealed class SelfPlayAdmissionTests
{
    [Fact]
    public async Task RelabeledLegalGameCannotPretendToBeSearchSelfPlay()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "gomoku-admission-test-" +
            Guid.NewGuid().ToString("N"));
        try
        {
            // The moves are legal but fabricated search provenance. Admission
            // must recompute the deterministic selector and reject the forgery.
            var coordinates = new (int Row, int Column)[]
            {
                (2, 0), (11, 10), (2, 1), (11, 11), (2, 2),
                (11, 12), (2, 3), (11, 13), (2, 4)
            };
            var moves = coordinates.Select((point, index) => new SelfPlayMove(
                index + 1, point.Row, point.Column, index % 2 == 0 ? "X" : "O",
                index is 0 or 8 ? "ImmediateWin" : "Search", 3, false)).ToArray();
            var canonical = new StringBuilder(RuleSet.CurrentVersion)
                .Append("|X|XWon");
            foreach (var move in moves)
                canonical.Append('|').Append(move.Step).Append(':').Append(move.Row)
                    .Append(':').Append(move.Column).Append(':').Append(move.Stone);
            var hash = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
            var settings = new SelfPlaySettings("fixture", 1, SelfPlayMode.Search);
            var row = new SelfPlayGame(1, "self-play", SelfPlaySettings.SearchSplit,
                RuleSet.CurrentVersion, SelfPlaySettings.SearchEngineVersion,
                SelfPlaySettings.SearchSelectionMode, "X", "XWon", hash, moves);
            var run = await new SelfPlayArtifactWriter().WriteAsync(settings,
                [row], Path.Combine(root, "source"));
            var evidence = Path.Combine(root, "evidence.json");
            await File.WriteAllTextAsync(evidence, JsonSerializer.Serialize(new
            {
                approvedBy = "reviewer", tacticsPassed = true,
                legalMovesPassed = true, latencyPassed = true,
                apiRecoveryPassed = true
            }));

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                new SelfPlayAdmission().AdmitAsync(run, evidence,
                    Path.Combine(root, "admitted")));
            using var sourceManifest = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(run, "manifest.json")));
            Assert.False(sourceManifest.RootElement.GetProperty("trainingEligible").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task BootstrapRunCannotBeAdmitted()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "gomoku-admission-test-" +
            Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new SelfPlaySettings("bootstrap", 1);
            var run = await new SelfPlayArtifactWriter().WriteAsync(settings,
                new SelfPlayGenerator().Generate(settings), Path.Combine(root, "source"));
            var evidence = Path.Combine(root, "evidence.json");
            await File.WriteAllTextAsync(evidence, JsonSerializer.Serialize(new
            {
                approvedBy = "reviewer",
                tacticsPassed = true,
                legalMovesPassed = true,
                latencyPassed = true,
                apiRecoveryPassed = true
            }));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                new SelfPlayAdmission().AdmitAsync(run, evidence,
                    Path.Combine(root, "admitted")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
