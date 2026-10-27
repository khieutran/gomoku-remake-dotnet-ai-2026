using System.Text.Json;

namespace Gomoku.ModelEvaluation;

public sealed record ChampionRevision(string AiVersion, string? ModelPath,
    string? ModelSha256, string? ReportSha256, string Action, string Reason,
    DateTimeOffset AtUtc);

public sealed record ChampionPointer(int SchemaVersion, ChampionRevision Current,
    IReadOnlyList<ChampionRevision> History);

public static class ChampionCommands
{
    public static ChampionPointer Load(string path)
    {
        if (!File.Exists(path))
            return new(1, SearchRevision(), []);
        var pointer = JsonSerializer.Deserialize<ChampionPointer>(
            File.ReadAllText(path), EvaluationJson.Options)
            ?? throw new InvalidDataException("Empty champion pointer.");
        if (pointer.SchemaVersion != 1 || pointer.Current is null ||
            pointer.History is null ||
            string.IsNullOrWhiteSpace(pointer.Current.AiVersion))
            throw new InvalidDataException("Invalid champion pointer.");
        var revisions = pointer.History.Append(pointer.Current).ToArray();
        foreach (var revision in revisions)
        {
            if (string.IsNullOrWhiteSpace(revision.AiVersion) ||
                (revision.AiVersion == "search-v1"
                    ? revision.ModelPath is not null || revision.ModelSha256 is not null
                    : revision.ModelPath is null || revision.ModelSha256 is null ||
                      !Path.IsPathFullyQualified(revision.ModelPath) ||
                      revision.ModelSha256.Length != 64 ||
                      !revision.ModelSha256.All(Uri.IsHexDigit)))
                throw new InvalidDataException("Invalid champion revision.");
        }
        foreach (var group in revisions.GroupBy(item => item.AiVersion,
                     StringComparer.Ordinal))
            if (group.Select(item => (item.ModelPath, item.ModelSha256))
                    .Distinct().Count() != 1)
                throw new InvalidDataException("A pinned AI version points to different artifacts.");
        return pointer;
    }

    public static ChampionPointer Promote(string pointerPath, string reportPath,
        string candidateId, string approvedReportSha256, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A promotion reason is required.", nameof(reason));
        var signed = ReportStore.ReadAndVerify(reportPath);
        var report = signed.Report;
        if (!string.Equals(report.CandidateId, candidateId, StringComparison.Ordinal) ||
            !string.Equals(signed.ReportSha256, approvedReportSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Candidate or approved report hash mismatch.");
        if (!report.Gate.Passed || !ReportStore.CheckGate(report).Passed)
            throw new InvalidDataException("Candidate did not pass evaluation gate.");
        if (ReportStore.Sha256File(report.CandidateModelPath) != report.ModelSha256)
            throw new InvalidDataException("Candidate model hash mismatch.");
        var current = Load(pointerPath);
        if (current.Current.AiVersion != report.ChampionId)
            throw new InvalidDataException("Champion changed since evaluation.");
        if (current.Current.ModelSha256 != report.ChampionModelSha256)
            throw new InvalidDataException("Champion artifact changed since evaluation.");
        if (current.Current.ModelPath is not null &&
            ReportStore.Sha256File(current.Current.ModelPath) !=
            current.Current.ModelSha256)
            throw new InvalidDataException("Current champion model hash mismatch.");
        if (current.Current.AiVersion == candidateId)
            throw new InvalidDataException("Candidate is already champion.");
        var candidatePath = Path.GetFullPath(report.CandidateModelPath);
        if (current.History.Any(item => item.AiVersion == candidateId &&
                (item.ModelSha256 != report.ModelSha256 ||
                 item.ModelPath != candidatePath)))
            throw new InvalidDataException("Candidate ID was previously bound to a different artifact.");
        var next = new ChampionRevision(candidateId,
            candidatePath, report.ModelSha256,
            signed.ReportSha256, "promote", reason, DateTimeOffset.UtcNow);
        var pointer = new ChampionPointer(1, next,
            current.History.Append(current.Current).ToArray());
        ReportStore.WriteAtomically(pointerPath, pointer);
        return pointer;
    }

    public static ChampionPointer Rollback(string pointerPath, string targetVersion,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A rollback reason is required.", nameof(reason));
        var current = Load(pointerPath);
        if (current.Current.AiVersion == targetVersion)
            throw new InvalidDataException("Target is already champion.");
        var target = current.History.LastOrDefault(item => item.AiVersion == targetVersion)
            ?? throw new InvalidDataException("Rollback target is absent from history.");
        if (target.ModelPath is not null &&
            ReportStore.Sha256File(target.ModelPath) != target.ModelSha256)
            throw new InvalidDataException("Rollback model hash mismatch.");
        var next = target with
        {
            Action = "rollback", Reason = reason, AtUtc = DateTimeOffset.UtcNow
        };
        var pointer = new ChampionPointer(1, next,
            current.History.Append(current.Current).ToArray());
        ReportStore.WriteAtomically(pointerPath, pointer);
        return pointer;
    }

    private static ChampionRevision SearchRevision() => new("search-v1", null,
        null, null, "initial", "default search champion", DateTimeOffset.UnixEpoch);
}
