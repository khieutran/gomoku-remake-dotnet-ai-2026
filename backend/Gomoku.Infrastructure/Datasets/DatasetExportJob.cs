using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gomoku.Application.Datasets;
using Gomoku.Core;

namespace Gomoku.Infrastructure.Datasets;

public sealed class DatasetExportJob(IGuestDatasetReader reader)
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

    public async Task<string> ExportAsync(DatasetExportSettings settings, string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Private dataset file modes require Unix.");
        Directory.CreateDirectory(outputDirectory, PrivateDirectory);
        if ((File.GetAttributes(outputDirectory) & FileAttributes.ReparsePoint) != 0 ||
            (File.GetUnixFileMode(outputDirectory) & OtherAccess) != 0)
            throw new InvalidOperationException("Dataset output directory must be private and not a link.");

        var stagingDirectory = Path.Combine(outputDirectory,
            ".tmp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory, PrivateDirectory);
        try
        {
            var temporaryDataset = Path.Combine(stagingDirectory, "dataset.jsonl");
            var splitCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var engineCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var builder = new GuestDatasetBuilder(settings);
            var rowCount = 0;
            await using (var stream = OpenPrivateFile(temporaryDataset))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false))
            {
                NewLine = "\n"
            })
            {
                await foreach (var source in reader.ReadAsync(settings.ExportCutoffUtc,
                    cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (var row in builder.Process(source))
                    {
                        await writer.WriteLineAsync(JsonSerializer.Serialize(row, JsonOptions));
                        rowCount++;
                        splitCounts[row.Split] = splitCounts.GetValueOrDefault(row.Split) + 1;
                        engineCounts[row.AiVersion] = engineCounts.GetValueOrDefault(row.AiVersion) + 1;
                    }
                }
            }

            string dataHash;
            await using (var data = File.OpenRead(temporaryDataset))
                dataHash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
            var rejections = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var (reason, count) in builder.Rejections)
                rejections.Add(reason, count);
            var manifest = new
            {
                schemaVersion = 1,
                source = "guest",
                ruleVersion = RuleSet.CurrentVersion,
                settings.Seed,
                settings.ExportCutoffUtc,
                settings.TestCutoffUtc,
                rowCount,
                splitCounts,
                engineCounts,
                rejections,
                datasetSha256 = dataHash
            };
            var manifestText = JsonSerializer.Serialize(manifest, JsonOptions) + "\n";
            await using (var stream = OpenPrivateFile(Path.Combine(stagingDirectory, "manifest.json")))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                await writer.WriteAsync(manifestText);

            var manifestHash = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(manifestText))).ToLowerInvariant();
            var runDirectory = Path.Combine(outputDirectory, "run-" + manifestHash);
            try
            {
                Directory.Move(stagingDirectory, runDirectory);
            }
            catch (IOException) when (Directory.Exists(runDirectory))
            {
                var existingManifest = await File.ReadAllTextAsync(
                    Path.Combine(runDirectory, "manifest.json"), cancellationToken);
                await using var existingData = File.OpenRead(
                    Path.Combine(runDirectory, "dataset.jsonl"));
                var existingDataHash = Convert.ToHexString(SHA256.HashData(existingData))
                    .ToLowerInvariant();
                if (existingManifest != manifestText || existingDataHash != dataHash)
                    throw new InvalidOperationException("Existing dataset run is inconsistent.");
            }
            return runDirectory;
        }
        finally
        {
            if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, true);
        }
    }

    private static FileStream OpenPrivateFile(string path)
    {
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Private dataset file modes require Unix.");
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
