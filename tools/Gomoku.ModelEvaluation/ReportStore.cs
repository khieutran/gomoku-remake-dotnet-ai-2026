using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Gomoku.ModelEvaluation;

public static class ReportStore
{
    public static string Sha256File(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    public static string Sha256Text(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static GateResult CheckGate(EvaluationReport report)
    {
        var failures = new List<string>();
        if (report.SchemaVersion != 1 || report.RuleVersion != Gomoku.Core.RuleSet.CurrentVersion ||
            report.BoardSize != 20 || report.MoveBudgetMs < 1 || report.MaxPlies < 4 ||
            report.OpeningCount < 1 || report.MinimumCandidateScore < 0.55 ||
            report.MinimumTacticalPassRate != 1 ||
            report.MaximumFallbackRate is < 0 or > 0.05 ||
            report.MaximumTimeouts != 0 ||
            report.CandidateId == report.ChampionId ||
            string.IsNullOrWhiteSpace(report.CandidateId) ||
            string.IsNullOrWhiteSpace(report.ChampionId))
            failures.Add("invalid_gate_policy");
        if (report.Matches.Games != report.OpeningCount * 2 ||
            report.Matches.CandidateAsX.Wins + report.Matches.CandidateAsX.Draws +
            report.Matches.CandidateAsX.Losses != report.OpeningCount ||
            report.Matches.CandidateAsO.Wins + report.Matches.CandidateAsO.Draws +
            report.Matches.CandidateAsO.Losses != report.OpeningCount)
            failures.Add("incomplete_color_paired_matches");
        var derivedScore = (report.Matches.CandidateAsX.Wins +
            report.Matches.CandidateAsO.Wins +
            0.5 * (report.Matches.CandidateAsX.Draws +
                   report.Matches.CandidateAsO.Draws)) / Math.Max(1, report.Matches.Games);
        if (Math.Abs(derivedScore - report.Matches.CandidateScore) > 0.000001)
            failures.Add("inconsistent_match_score");
        if (report.CandidateTactics.Total == 0 ||
            report.CandidateTactics.PassRate < report.MinimumTacticalPassRate)
            failures.Add("tactical_pass_rate");
        if (report.ChampionTactics.Total == 0 ||
            report.ChampionTactics.PassRate < report.MinimumTacticalPassRate)
            failures.Add("invalid_champion_tactics");
        if (report.CandidateDecisions.IllegalMoves > 0)
            failures.Add("illegal_move");
        if (report.ChampionDecisions.IllegalMoves > 0 ||
            report.ChampionDecisions.ModelErrors > 0)
            failures.Add("invalid_champion_run");
        if (report.CandidateDecisions.Timeouts > report.MaximumTimeouts)
            failures.Add("timeout_limit");
        if (report.CandidateDecisions.Count == 0 ||
            (double)report.CandidateDecisions.Fallbacks / report.CandidateDecisions.Count >
            report.MaximumFallbackRate)
            failures.Add("fallback_rate");
        if (report.CandidateDecisions.ModelErrors > 0)
            failures.Add("model_error");
        if (report.Matches.CandidateScore < report.MinimumCandidateScore)
            failures.Add("candidate_score");
        return new(failures.Count == 0, failures);
    }

    public static SignedEvaluationReport Sign(EvaluationReport report)
    {
        var gate = CheckGate(report);
        report = report with { Gate = gate };
        return new(report, Sha256Text(JsonSerializer.Serialize(report, EvaluationJson.Options)));
    }

    public static SignedEvaluationReport ReadAndVerify(string path)
    {
        var signed = JsonSerializer.Deserialize<SignedEvaluationReport>(
            File.ReadAllText(path), EvaluationJson.Options)
            ?? throw new InvalidDataException("Empty evaluation report.");
        var expected = Sha256Text(JsonSerializer.Serialize(signed.Report,
            EvaluationJson.Options));
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected),
                Convert.FromHexString(signed.ReportSha256)))
            throw new InvalidDataException("Report hash mismatch.");
        if (!EquivalentGate(CheckGate(signed.Report), signed.Report.Gate))
            throw new InvalidDataException("Report gate mismatch.");
        return signed;
    }

    private static bool EquivalentGate(GateResult left, GateResult right) =>
        left.Passed == right.Passed && left.Failures.SequenceEqual(right.Failures);

    public static void WriteAtomically<T>(string path, T value)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(directory);
        else
        {
            Directory.CreateDirectory(directory, UnixFileMode.UserRead |
                UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var mode = File.GetUnixFileMode(directory);
            if ((mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
                UnixFileMode.GroupExecute | UnixFileMode.OtherRead |
                UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0 ||
                (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Evaluation output directory must be private.");
        }
        var temporary = Path.Combine(directory, "." + Path.GetFileName(path) +
            "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            if (OperatingSystem.IsWindows())
                File.WriteAllText(temporary,
                    JsonSerializer.Serialize(value, EvaluationJson.Options));
            else
            {
                using var stream = new FileStream(temporary, new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                });
                JsonSerializer.Serialize(stream, value, EvaluationJson.Options);
            }
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
