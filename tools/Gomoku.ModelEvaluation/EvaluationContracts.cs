using System.Text.Json;
using System.Text.Json.Serialization;
using Gomoku.Core;

namespace Gomoku.ModelEvaluation;

public static class EvaluationJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

public sealed class EvaluationConfig
{
    public required string CandidateId { get; init; }
    public required string CandidateModelPath { get; init; }
    public required string DatasetHash { get; init; }
    public required string TrainingMetricsPath { get; init; }
    public required string FixturePath { get; init; }
    public required string Seed { get; init; }
    public int OpeningCount { get; init; } = 4;
    public int MoveBudgetMs { get; init; } = 200;
    public int MaxPlies { get; init; } = 80;
    public double MinimumCandidateScore { get; init; } = 0.55;
    public double MinimumTacticalPassRate { get; init; } = 1;
    public double MaximumFallbackRate { get; init; } = 0.05;
    public int MaximumTimeouts { get; init; }
    public string ChampionId { get; init; } = "search-v1";
    public string? ChampionModelPath { get; init; }
}

public sealed record MoveDecision(Position? Move, bool BudgetExpired, string? FallbackReason);

public interface IEvaluationPlayer
{
    MoveDecision Choose(GameSnapshot game, Stone side, TimeSpan budget, CancellationToken token);
}

public sealed class FixtureFile
{
    public required string RuleVersion { get; init; }
    public int BoardSize { get; init; }
    public required List<TacticalFixture> Cases { get; init; }
}

public sealed class TacticalFixture
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public Stone? SideToMove { get; init; }
    public required int[][] X { get; init; }
    public required int[][] O { get; init; }
    public required int[][] ExpectedWinningMoves { get; init; }
    public required int[][] ExpectedBlockingMoves { get; init; }
    public List<FixtureCandidateCheck>? CandidateChecks { get; init; }
}

public sealed class FixtureCandidateCheck
{
    public required int[] Move { get; init; }
    public required GameStatus ExpectedStatus { get; init; }
}

public sealed record DecisionMetrics(int Count, double P50Ms, double P95Ms,
    int Timeouts, int Fallbacks, int ModelErrors, int IllegalMoves);

public sealed record TacticalMetrics(int Passed, int Total, IReadOnlyList<string> FailedCases)
{
    public double PassRate => Total == 0 ? 0 : (double)Passed / Total;
}

public sealed record ColorResult(int Wins, int Draws, int Losses);

public sealed record MatchMetrics(int Games, ColorResult CandidateAsX,
    ColorResult CandidateAsO, double CandidateScore);

public sealed record GateResult(bool Passed, IReadOnlyList<string> Failures);

public sealed record EvaluationReport(
    int SchemaVersion,
    string CandidateId,
    string ChampionId,
    string? ChampionModelPath,
    string? ChampionModelSha256,
    string CandidateModelPath,
    string ModelSha256,
    string DatasetHash,
    string TrainingMetricsSha256,
    JsonElement TrainingMetrics,
    string FixtureSha256,
    string ConfigSha256,
    string Seed,
    string RuleVersion,
    int BoardSize,
    int MoveBudgetMs,
    int MaxPlies,
    int OpeningCount,
    double MinimumCandidateScore,
    double MinimumTacticalPassRate,
    double MaximumFallbackRate,
    int MaximumTimeouts,
    MatchMetrics Matches,
    TacticalMetrics CandidateTactics,
    TacticalMetrics ChampionTactics,
    DecisionMetrics CandidateDecisions,
    DecisionMetrics ChampionDecisions,
    GateResult Gate);

public sealed record SignedEvaluationReport(EvaluationReport Report, string ReportSha256);
