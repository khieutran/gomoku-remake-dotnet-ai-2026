using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gomoku.Core;

namespace Gomoku.SelfPlay;

public sealed class SelfPlayAdmission
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const UnixFileMode PrivateDirectory = UnixFileMode.UserRead |
        UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode OtherAccess = UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
        UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite |
        UnixFileMode.OtherExecute;

    public async Task<string> AdmitAsync(string sourceRun, string baselineEvidencePath,
        string outputRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRun);
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineEvidencePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Private artifacts require Unix.");

        var evidenceBytes = await File.ReadAllBytesAsync(baselineEvidencePath,
            cancellationToken);
        using var evidence = JsonDocument.Parse(evidenceBytes);
        var gate = evidence.RootElement;
        if (gate.ValueKind != JsonValueKind.Object ||
            !gate.TryGetProperty("approvedBy", out var reviewer) ||
            string.IsNullOrWhiteSpace(reviewer.GetString()) ||
            !Passed(gate, "tacticsPassed") || !Passed(gate, "legalMovesPassed") ||
            !Passed(gate, "latencyPassed") || !Passed(gate, "apiRecoveryPassed"))
            throw new InvalidDataException("Baseline evidence has not approved self-play.");

        var sourceManifestPath = Path.Combine(sourceRun, "manifest.json");
        var sourceManifestBytes = await File.ReadAllBytesAsync(sourceManifestPath,
            cancellationToken);
        var manifest = JsonNode.Parse(Encoding.UTF8.GetString(sourceManifestBytes)) as JsonObject
            ?? throw new InvalidDataException("Invalid self-play manifest.");
        if (manifest["schemaVersion"]?.GetValue<int>() != 1 ||
            manifest["source"]?.GetValue<string>() != "self-play" ||
            manifest["split"]?.GetValue<string>() != SelfPlaySettings.SearchSplit ||
            manifest["trainingEligible"]?.GetValue<bool>() != false ||
            manifest["ruleVersion"]?.GetValue<string>() != RuleSet.CurrentVersion ||
            manifest["engineVersion"]?.GetValue<string>() != SelfPlaySettings.SearchEngineVersion ||
            manifest["selectionMode"]?.GetValue<string>() != SelfPlaySettings.SearchSelectionMode ||
            string.IsNullOrWhiteSpace(manifest["seed"]?.GetValue<string>()))
            throw new InvalidDataException("Only unadmitted search self-play can be admitted.");
        var seed = manifest["seed"]!.GetValue<string>();

        var data = await File.ReadAllBytesAsync(Path.Combine(sourceRun,
            "self-play.jsonl"), cancellationToken);
        var dataHash = Sha256(data);
        if (manifest["datasetSha256"]?.GetValue<string>() != dataHash)
            throw new InvalidDataException("Self-play data hash mismatch.");
        var lines = Encoding.UTF8.GetString(data).Split('\n',
            StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 ||
            manifest["gameCount"]?.GetValue<int>() != lines.Length ||
            manifest["completedGameCount"]?.GetValue<int>() != lines.Length)
            throw new InvalidDataException("Self-play game count mismatch.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < lines.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = JsonSerializer.Deserialize<SelfPlayGame>(lines[i], JsonOptions)
                ?? throw new InvalidDataException("Invalid self-play row.");
            ValidateGame(row, i + 1, seed, seen, cancellationToken);
        }

        manifest["trainingEligible"] = true;
        manifest["admission"] = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["reviewer"] = reviewer.GetString(),
            ["baselineEvidenceSha256"] = Sha256(evidenceBytes),
            ["sourceManifestSha256"] = Sha256(sourceManifestBytes)
        };
        var admittedManifest = Encoding.UTF8.GetBytes(manifest.ToJsonString() + "\n");
        var destination = Path.Combine(outputRoot, "self-play-admitted-" +
            Sha256(admittedManifest));
        Directory.CreateDirectory(outputRoot, PrivateDirectory);
        if ((File.GetAttributes(outputRoot) & FileAttributes.ReparsePoint) != 0 ||
            (File.GetUnixFileMode(outputRoot) & OtherAccess) != 0)
            throw new InvalidOperationException("Admission output must be private.");
        if (Directory.Exists(destination))
        {
            if (Sha256(await File.ReadAllBytesAsync(Path.Combine(destination,
                    "manifest.json"), cancellationToken)) != Sha256(admittedManifest) ||
                Sha256(await File.ReadAllBytesAsync(Path.Combine(destination,
                    "self-play.jsonl"), cancellationToken)) != dataHash)
                throw new InvalidDataException("Existing admitted run differs.");
            return destination;
        }
        var staging = Path.Combine(outputRoot, ".tmp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging, PrivateDirectory);
        try
        {
            await WritePrivateAsync(Path.Combine(staging, "self-play.jsonl"), data,
                cancellationToken);
            await WritePrivateAsync(Path.Combine(staging, "manifest.json"),
                admittedManifest, cancellationToken);
            Directory.Move(staging, destination);
            return destination;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    private static void ValidateGame(SelfPlayGame row, int number, string seed,
        HashSet<string> seen, CancellationToken cancellationToken)
    {
        if (row.GameNumber != number || row.Source != "self-play" ||
            row.Split != SelfPlaySettings.SearchSplit ||
            row.RuleVersion != RuleSet.CurrentVersion ||
            row.EngineVersion != SelfPlaySettings.SearchEngineVersion ||
            row.SelectionMode != SelfPlaySettings.SearchSelectionMode ||
            row.Moves.Count == 0 || !seen.Add(row.ContentHash) ||
            !Enum.TryParse<Stone>(row.OpeningStone, out var opener) ||
            opener is not (Stone.X or Stone.O))
            throw new InvalidDataException("Invalid self-play game metadata.");
        var game = Game.Start(new RuleSet(SelfPlaySettings.BoardSize), opener);
        var canonical = new StringBuilder(row.RuleVersion).Append('|')
            .Append(opener).Append('|').Append(row.Result);
        foreach (var move in row.Moves)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = game.Snapshot();
            if (snapshot.Status != GameStatus.InProgress)
                throw new InvalidDataException("Self-play contains a move after game end.");
            var recomputed = SelfPlayGenerator.SelectSearchTurn(snapshot,
                SelfPlayGenerator.SeedForTurn(seed, number, move.Step), cancellationToken);
            if (snapshot.Status != GameStatus.InProgress ||
                move.Step != snapshot.MoveIndex + 1 ||
                move.Stone != snapshot.NextStone?.ToString() ||
                move.BudgetExpired ||
                move.DecisionReason is not ("Search" or "ImmediateWin" or "ImmediateBlock") ||
                (move.DecisionReason == "Search" && move.Step > 1 &&
                    move.CompletedDepth < 1) ||
                recomputed.Move != new Position(move.Row, move.Column) ||
                recomputed.Reason.ToString() != move.DecisionReason ||
                recomputed.CompletedDepth != move.CompletedDepth ||
                recomputed.BudgetExpired != move.BudgetExpired ||
                game.TryPlay(new Position(move.Row, move.Column)) != MoveResult.Played)
                throw new InvalidDataException("Illegal or fallback self-play move.");
            canonical.Append('|').Append(move.Step).Append(':').Append(move.Row)
                .Append(':').Append(move.Column).Append(':').Append(move.Stone);
        }
        if (game.Snapshot().Status == GameStatus.InProgress ||
            row.Result != game.Snapshot().Status.ToString() ||
            Sha256(Encoding.UTF8.GetBytes(canonical.ToString())) != row.ContentHash)
            throw new InvalidDataException("Incomplete or altered self-play game.");
    }

    private static bool Passed(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static async Task WritePrivateAsync(string path, byte[] bytes,
        CancellationToken token)
    {
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Private artifacts require Unix.");
        await using var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.Write,
            Share = FileShare.None, Options = FileOptions.Asynchronous,
            UnixCreateMode = PrivateFile
        });
        await stream.WriteAsync(bytes, token);
    }
}
