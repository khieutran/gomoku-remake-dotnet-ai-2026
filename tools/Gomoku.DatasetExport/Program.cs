using System.Globalization;
using Gomoku.Application.Datasets;
using Gomoku.Infrastructure.Datasets;
using Gomoku.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

if (args.Length == 1 && args[0] == "--help")
{
    Console.WriteLine("Usage: Gomoku.DatasetExport --output DIR --seed VALUE " +
        "--test-cutoff UTC --export-cutoff UTC");
    Console.WriteLine("Database: GOMOKU_POSTGRES_CONNECTION environment variable.");
    return 0;
}

if (args.Length != 8)
{
    Console.Error.WriteLine("Expected --output, --seed, --test-cutoff and --export-cutoff.");
    return 2;
}

var options = new Dictionary<string, string>(StringComparer.Ordinal);
for (var index = 0; index < args.Length; index += 2)
{
    if (!args[index].StartsWith("--", StringComparison.Ordinal) ||
        !options.TryAdd(args[index], args[index + 1]))
    {
        Console.Error.WriteLine("Invalid or repeated option.");
        return 2;
    }
}
if (!options.TryGetValue("--output", out var output) ||
    !options.TryGetValue("--seed", out var seed) ||
    !options.TryGetValue("--test-cutoff", out var testCutoffText) ||
    !options.TryGetValue("--export-cutoff", out var exportCutoffText) ||
    !DateTimeOffset.TryParse(testCutoffText, CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal, out var testCutoff) ||
    !DateTimeOffset.TryParse(exportCutoffText, CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal, out var exportCutoff) ||
    string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(seed) ||
    testCutoff > exportCutoff ||
    options.Keys.Any(key => key is not ("--output" or "--seed" or
        "--test-cutoff" or "--export-cutoff")))
{
    Console.Error.WriteLine("Invalid export arguments. Use --help for usage.");
    return 2;
}

var connection = Environment.GetEnvironmentVariable("GOMOKU_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connection))
{
    Console.Error.WriteLine("GOMOKU_POSTGRES_CONNECTION is required.");
    return 2;
}

try
{
    await using var db = new GomokuDbContext(new DbContextOptionsBuilder<GomokuDbContext>()
        .UseNpgsql(connection).Options);
    var job = new DatasetExportJob(new EfGuestDatasetReader(db));
    var runDirectory = await job.ExportAsync(new DatasetExportSettings(seed, exportCutoff.ToUniversalTime(),
        testCutoff.ToUniversalTime()), output);
    Console.WriteLine($"Dataset: {Path.Combine(runDirectory, "dataset.jsonl")}");
    Console.WriteLine($"Manifest: {Path.Combine(runDirectory, "manifest.json")}");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Dataset export failed: {exception.GetType().Name}");
    return 1;
}
