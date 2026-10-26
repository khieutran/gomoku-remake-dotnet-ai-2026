using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gomoku.Core;

namespace Gomoku.SelfPlay;

public sealed class SelfPlayArtifactWriter
{
    private const UnixFileMode PrivateDirectory = UnixFileMode.UserRead |
        UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode OtherAccess = UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
        UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite |
        UnixFileMode.OtherExecute;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<string> WriteAsync(SelfPlaySettings settings,
        IReadOnlyList<SelfPlayGame> games, string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(games);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        var search = settings.Mode == SelfPlayMode.Search;
        if (!Enum.IsDefined(settings.Mode))
            throw new ArgumentOutOfRangeException(nameof(settings));
        var expectedSplit = search ? SelfPlaySettings.SearchSplit : SelfPlaySettings.Split;
        var expectedEngine = search ? SelfPlaySettings.SearchEngineVersion :
            SelfPlaySettings.EngineVersion;
        var expectedMode = search ? SelfPlaySettings.SearchSelectionMode :
            SelfPlaySettings.SelectionMode;
        if (games.Count != settings.GameCount || games.Any(game =>
            game.Source != "self-play" || game.Split != expectedSplit ||
            game.Result is not (nameof(GameStatus.XWon) or nameof(GameStatus.OWon) or
                nameof(GameStatus.Draw)) ||
            game.RuleVersion != RuleSet.CurrentVersion ||
            game.EngineVersion != expectedEngine ||
            game.SelectionMode != expectedMode ||
            (search && game.Moves.Any(move => move.DecisionReason is "Fallback" or
                "NoMove" || move.BudgetExpired)) ))
            throw new ArgumentException("Only complete self-play games with valid provenance may be written.",
                nameof(games));
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Private artifact modes require Unix.");

        Directory.CreateDirectory(outputDirectory, PrivateDirectory);
        if ((File.GetAttributes(outputDirectory) & FileAttributes.ReparsePoint) != 0 ||
            (File.GetUnixFileMode(outputDirectory) & OtherAccess) != 0)
            throw new InvalidOperationException("Output directory must be private and not a link.");

        var staging = Path.Combine(outputDirectory, ".tmp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging, PrivateDirectory);
        try
        {
            var dataPath = Path.Combine(staging, "self-play.jsonl");
            await using (var writer = new StreamWriter(OpenPrivateFile(dataPath),
                new UTF8Encoding(false)) { NewLine = "\n" })
            {
                foreach (var game in games)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await writer.WriteLineAsync(JsonSerializer.Serialize(game, JsonOptions));
                }
            }

            string dataHash;
            await using (var data = File.OpenRead(dataPath))
                dataHash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
            var manifest = new
            {
                schemaVersion = 1,
                source = "self-play",
                split = expectedSplit,
                trainingEligible = false,
                ruleVersion = RuleSet.CurrentVersion,
                boardSize = SelfPlaySettings.BoardSize,
                engineVersion = expectedEngine,
                selectionMode = expectedMode,
                settings.Seed,
                gameCount = games.Count,
                completedGameCount = games.Count,
                datasetSha256 = dataHash
            };
            var manifestText = JsonSerializer.Serialize(manifest, JsonOptions) + "\n";
            await using (var writer = new StreamWriter(OpenPrivateFile(
                Path.Combine(staging, "manifest.json")), new UTF8Encoding(false)))
                await writer.WriteAsync(manifestText);

            var manifestHash = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(manifestText))).ToLowerInvariant();
            var runDirectory = Path.Combine(outputDirectory, "self-play-run-" + manifestHash);
            try { Directory.Move(staging, runDirectory); }
            catch (IOException) when (Directory.Exists(runDirectory))
            {
                var existingManifest = await File.ReadAllTextAsync(
                    Path.Combine(runDirectory, "manifest.json"), cancellationToken);
                await using var existingData = File.OpenRead(Path.Combine(runDirectory,
                    "self-play.jsonl"));
                var existingHash = Convert.ToHexString(SHA256.HashData(existingData))
                    .ToLowerInvariant();
                if (existingManifest != manifestText || existingHash != dataHash)
                    throw new InvalidOperationException("Existing self-play run is inconsistent.");
            }
            return runDirectory;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    private static FileStream OpenPrivateFile(string path)
    {
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Private artifact modes require Unix.");
        return new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous,
            UnixCreateMode = PrivateFile
        });
    }
}
