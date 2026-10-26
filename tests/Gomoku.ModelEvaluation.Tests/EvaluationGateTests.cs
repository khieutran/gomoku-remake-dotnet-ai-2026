using System.Text.Json;
using Gomoku.Core;
using Gomoku.ModelEvaluation;

namespace Gomoku.ModelEvaluation.Tests;

public sealed class EvaluationGateTests
{
    [Fact]
    public void AdmittedSelfPlayChangesCandidateIdentity()
    {
        using var guest = JsonDocument.Parse("{\"datasetSha256\":\"guest\"}");
        using var combined = JsonDocument.Parse("""
            {"datasetSha256":"guest","selfPlay":{"datasetSha256":"self-play",
            "manifestSha256":"admission"}}
            """);

        Assert.NotEqual(CandidateIdentity.FromSource(guest.RootElement, "config", "trainer"),
            CandidateIdentity.FromSource(combined.RootElement, "config", "trainer"));
    }

    [Fact]
    public void PairedMatchesExerciseBothCandidateColorsAndCountFallbacks()
    {
        var candidate = new FirstLegalPlayer(fallback: true);
        var champion = new FirstLegalPlayer(fallback: false);
        var evaluator = new MatchEvaluator(candidate, champion);
        var result = evaluator.Evaluate(Config(), Fixtures());

        Assert.Equal(4, result.Matches.Games);
        Assert.Equal(2, result.Matches.CandidateAsX.Draws);
        Assert.Equal(2, result.Matches.CandidateAsO.Draws);
        Assert.True(result.CandidateDecisions.Count > 0);
        Assert.Equal(result.CandidateDecisions.Count,
            result.CandidateDecisions.Fallbacks);
        Assert.Equal(result.CandidateDecisions.Count,
            result.CandidateDecisions.Timeouts);
        Assert.Equal(0, result.CandidateDecisions.IllegalMoves);
    }

    [Fact]
    public void LosingCandidateCannotPassGate()
    {
        var report = Report("candidate", CandidateScore: 0);
        var signed = ReportStore.Sign(report);
        Assert.False(signed.Report.Gate.Passed);
        Assert.Contains("candidate_score", signed.Report.Gate.Failures);
    }

    [Fact]
    public void TacticalWinFixtureRejectsAValidButWrongMove()
    {
        var fixture = new TacticalFixture
        {
            Id = "O-WIN-NOW", Category = "immediate-win", SideToMove = Stone.O,
            X = [[10, 6], [0, 0], [0, 2], [0, 4]],
            O = [[10, 7], [10, 8], [10, 9], [10, 10]],
            ExpectedWinningMoves = [[10, 11]], ExpectedBlockingMoves = []
        };
        var fixtures = new FixtureFile
        {
            RuleVersion = RuleSet.CurrentVersion, BoardSize = 20, Cases = [fixture]
        };
        var result = new MatchEvaluator(new FirstLegalPlayer(false),
            new FirstLegalPlayer(false)).Evaluate(Config(), fixtures);
        Assert.Equal(0, result.CandidateTactics.Passed);
        Assert.Equal(new[] { "O-WIN-NOW" }, result.CandidateTactics.FailedCases);
        Assert.Equal(0, result.CandidateDecisions.IllegalMoves);
    }

    [Fact]
    public void EvaluationReportAloneNeverPromotesAndExplicitPromotionCanRollback()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gomoku-gate-" +
            Guid.NewGuid().ToString("N"));
        CreatePrivateDirectory(directory);
        try
        {
            var modelPath = Path.Combine(directory, "candidate.onnx");
            File.WriteAllBytes(modelPath, [1, 2, 3]);
            var pointerPath = Path.Combine(directory, "champion.json");
            var reportPath = Path.Combine(directory, "evaluation.json");
            var report = Report("candidate", modelPath: modelPath);
            var signed = ReportStore.Sign(report);
            ReportStore.WriteAtomically(reportPath, signed);

            Assert.False(File.Exists(pointerPath));
            Assert.Equal("search-v1", ChampionCommands.Load(pointerPath).Current.AiVersion);
            Assert.Throws<InvalidDataException>(() => ChampionCommands.Promote(
                pointerPath, reportPath, "candidate", "wrong-hash", "manual approval"));
            Assert.False(File.Exists(pointerPath));

            var promoted = ChampionCommands.Promote(pointerPath, reportPath,
                "candidate", signed.ReportSha256, "manual approval");
            Assert.Equal("candidate", promoted.Current.AiVersion);
            Assert.Equal(ReportStore.Sha256File(modelPath), promoted.Current.ModelSha256);
            Assert.Contains(promoted.History, item => item.AiVersion == "search-v1");

            var rolledBack = ChampionCommands.Rollback(pointerPath,
                "search-v1", "regression");
            Assert.Equal("search-v1", rolledBack.Current.AiVersion);
            Assert.Contains(rolledBack.History, item => item.AiVersion == "candidate");

            var copiedPath = Path.Combine(directory, "candidate-copy.onnx");
            File.Copy(modelPath, copiedPath);
            var copiedReport = ReportStore.Sign(Report("candidate", modelPath: copiedPath));
            ReportStore.WriteAtomically(reportPath, copiedReport);
            Assert.Throws<InvalidDataException>(() => ChampionCommands.Promote(
                pointerPath, reportPath, "candidate", copiedReport.ReportSha256,
                "same weights at a different path"));
            Assert.Equal("search-v1", ChampionCommands.Load(pointerPath).Current.AiVersion);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void TamperedModelOrFailedReportCannotPromote()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gomoku-gate-" +
            Guid.NewGuid().ToString("N"));
        CreatePrivateDirectory(directory);
        try
        {
            var modelPath = Path.Combine(directory, "candidate.onnx");
            File.WriteAllBytes(modelPath, [1, 2, 3]);
            var pointerPath = Path.Combine(directory, "champion.json");
            var reportPath = Path.Combine(directory, "evaluation.json");
            var failed = ReportStore.Sign(Report("candidate", 0, modelPath));
            ReportStore.WriteAtomically(reportPath, failed);
            Assert.Throws<InvalidDataException>(() => ChampionCommands.Promote(
                pointerPath, reportPath, "candidate", failed.ReportSha256, "manual"));
            var passing = ReportStore.Sign(Report("candidate", modelPath: modelPath));
            ReportStore.WriteAtomically(reportPath, passing);
            File.WriteAllBytes(modelPath, [9, 9, 9]);
            Assert.Throws<InvalidDataException>(() => ChampionCommands.Promote(
                pointerPath, reportPath, "candidate", passing.ReportSha256, "manual"));
            Assert.False(File.Exists(pointerPath));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static EvaluationConfig Config() => new()
    {
        CandidateId = "candidate", CandidateModelPath = "unused",
        DatasetHash = "dataset", TrainingMetricsPath = "unused",
        FixturePath = "unused", Seed = "fixed", OpeningCount = 2,
        MoveBudgetMs = 1000, MaxPlies = 4
    };

    private static void CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, UnixFileMode.UserRead |
            UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static FixtureFile Fixtures() => new()
    {
        RuleVersion = RuleSet.CurrentVersion, BoardSize = 20,
        Cases =
        [
            new TacticalFixture
            {
                Id = "opening", Category = "opening", SideToMove = Stone.X,
                X = [], O = [], ExpectedWinningMoves = [], ExpectedBlockingMoves = []
            }
        ]
    };

    private static EvaluationReport Report(string candidate, double CandidateScore = 1,
        string? modelPath = null) => new(1, candidate, "search-v1", null, null,
        modelPath ?? "model.onnx", modelPath is null ? "hash" :
            ReportStore.Sha256File(modelPath), "dataset", "training",
        JsonDocument.Parse("{}").RootElement.Clone(), "fixtures", "config", "seed",
        RuleSet.CurrentVersion, 20, 100, 80, 1, .55, 1, .05, 0,
        new MatchMetrics(2, new ColorResult(CandidateScore == 1 ? 1 : 0, 0,
                CandidateScore == 1 ? 0 : 1),
            new ColorResult(CandidateScore == 1 ? 1 : 0, 0,
                CandidateScore == 1 ? 0 : 1), CandidateScore),
        new TacticalMetrics(1, 1, []), new TacticalMetrics(1, 1, []),
        new DecisionMetrics(2, 1, 1, 0, 0, 0, 0),
        new DecisionMetrics(2, 1, 1, 0, 0, 0, 0), new GateResult(false, []));

    private sealed class FirstLegalPlayer(bool fallback) : IEvaluationPlayer
    {
        public MoveDecision Choose(GameSnapshot game, Stone side,
            TimeSpan budget, CancellationToken token)
        {
            for (var row = 0; row < game.Board.Size; row++)
            for (var column = 0; column < game.Board.Size; column++)
            {
                var move = new Position(row, column);
                if (game.Board.Get(move) == Stone.Empty)
                    return new(move, fallback, fallback ? "model_budget_expired" : null);
            }
            return new(null, false, null);
        }
    }
}
