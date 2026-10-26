using System.Security.Cryptography;
using System.Text.Json;
using Gomoku.Core;

namespace Gomoku.SelfPlay.Tests;

public sealed class SelfPlayTests
{
    [Fact]
    public void SameSeedProducesSameCompletedLegalGames()
    {
        var settings = new SelfPlaySettings("repeatable-seed", 2);
        var generator = new SelfPlayGenerator();

        var first = generator.Generate(settings);
        var second = generator.Generate(settings);

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Equal(new[] { "X", "O" }, first.Select(game => game.OpeningStone));
        foreach (var generated in first)
        {
            var opener = Enum.Parse<Stone>(generated.OpeningStone);
            var replay = Game.Start(new RuleSet(SelfPlaySettings.BoardSize), opener);
            foreach (var move in generated.Moves)
            {
                Assert.Equal(replay.Snapshot().MoveIndex + 1, move.Step);
                Assert.Equal(replay.Snapshot().NextStone!.Value, Enum.Parse<Stone>(move.Stone));
                Assert.Equal(MoveResult.Played, replay.TryPlay(new Position(move.Row, move.Column)));
            }
            Assert.NotEqual(GameStatus.InProgress, replay.Snapshot().Status);
            Assert.Equal(replay.Snapshot().Status.ToString(), generated.Result);
        }
    }

    [Fact]
    public async Task ArtifactIsRepeatablePrivateAndExcludedFromGuestTest()
    {
        if (OperatingSystem.IsWindows()) return;
        var output = Path.Combine(Path.GetTempPath(), "gomoku-self-play-test-" +
            Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new SelfPlaySettings("artifact-seed", 1);
            var games = new SelfPlayGenerator().Generate(settings);
            var writer = new SelfPlayArtifactWriter();

            var first = await writer.WriteAsync(settings, games, output);
            var second = await writer.WriteAsync(settings, games, output);

            Assert.Equal(first, second);
            Assert.Equal("self-play", games[0].Source);
            Assert.Equal("bootstrap", games[0].Split);
            Assert.Equal("legal-v1", games[0].EngineVersion);
            Assert.DoesNotContain(games, game => game.Split == "test" || game.Source == "guest");
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.UserExecute, File.GetUnixFileMode(first));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(Path.Combine(first, "self-play.jsonl")));

            var data = await File.ReadAllBytesAsync(Path.Combine(first, "self-play.jsonl"));
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(first, "manifest.json")));
            Assert.Equal("self-play", manifest.RootElement.GetProperty("source").GetString());
            Assert.Equal("bootstrap", manifest.RootElement.GetProperty("split").GetString());
            Assert.False(manifest.RootElement.GetProperty("trainingEligible").GetBoolean());
            Assert.Equal("legal-v1", manifest.RootElement.GetProperty("engineVersion").GetString());
            Assert.Equal(1, manifest.RootElement.GetProperty("completedGameCount").GetInt32());
            Assert.Equal(Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(),
                manifest.RootElement.GetProperty("datasetSha256").GetString());
            Assert.Single(File.ReadAllLines(Path.Combine(first, "self-play.jsonl")));
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }

    [Fact]
    public async Task WriterRejectsGuestAndUnfinishedRows()
    {
        var settings = new SelfPlaySettings("source-separation", 1);
        var game = new SelfPlayGenerator().Generate(settings)[0];
        var writer = new SelfPlayArtifactWriter();
        var output = Path.Combine(Path.GetTempPath(), "gomoku-self-play-reject-" +
            Guid.NewGuid().ToString("N"));

        await Assert.ThrowsAsync<ArgumentException>(() => writer.WriteAsync(settings,
            [game with { Source = "guest" }], output));
        await Assert.ThrowsAsync<ArgumentException>(() => writer.WriteAsync(settings,
            [game with { Split = "test" }], output));
        await Assert.ThrowsAsync<ArgumentException>(() => writer.WriteAsync(settings,
            [game with { Split = "train" }], output));
        await Assert.ThrowsAsync<ArgumentException>(() => writer.WriteAsync(settings,
            [game with { Result = nameof(GameStatus.InProgress) }], output));
    }

    [Fact]
    public void SearchTurnCompletesFixedDepthWithoutFallback()
    {
        var game = Game.Start(new RuleSet(20), Stone.X);
        Assert.Equal(MoveResult.Played, game.TryPlay(new Position(10, 10)));

        var first = SelfPlayGenerator.SelectSearchTurn(game.Snapshot(), 2026,
            CancellationToken.None);
        var second = SelfPlayGenerator.SelectSearchTurn(game.Snapshot(), 2026,
            CancellationToken.None);

        Assert.Equal(first, second);
        Assert.NotNull(first.Move);
        Assert.NotEqual("Fallback", first.Reason.ToString());
        Assert.False(first.BudgetExpired);
        Assert.Equal(Stone.Empty, game.Snapshot().Board.Get(first.Move!.Value));
    }

    [Fact]
    public async Task SearchArtifactRequiresManualAdmission()
    {
        if (OperatingSystem.IsWindows()) return;
        var output = Path.Combine(Path.GetTempPath(), "gomoku-self-play-search-" +
            Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new SelfPlaySettings("search-seed", 1, SelfPlayMode.Search);
            // A bounded artifact fixture; the real search path is exercised above
            // for one turn. Full 20x20 search games run only as explicit campaigns.
            var bootstrap = new SelfPlayGenerator().Generate(new SelfPlaySettings(
                settings.Seed, settings.GameCount));
            var games = bootstrap.Select(game => game with
            {
                Split = SelfPlaySettings.SearchSplit,
                EngineVersion = SelfPlaySettings.SearchEngineVersion,
                SelectionMode = SelfPlaySettings.SearchSelectionMode,
                Moves = game.Moves.Select(move => move with
                {
                    DecisionReason = "Search", CompletedDepth = 3,
                    BudgetExpired = false
                }).ToArray()
            }).ToArray();
            Assert.All(games.SelectMany(game => game.Moves), move =>
            {
                Assert.NotEqual("Fallback", move.DecisionReason);
                Assert.False(move.BudgetExpired);
            });
            var run = await new SelfPlayArtifactWriter().WriteAsync(settings, games, output);
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(run, "manifest.json")));
            Assert.Equal("search-v1", manifest.RootElement.GetProperty("engineVersion").GetString());
            Assert.Equal("train-candidate", manifest.RootElement.GetProperty("split").GetString());
            Assert.False(manifest.RootElement.GetProperty("trainingEligible").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }
}
