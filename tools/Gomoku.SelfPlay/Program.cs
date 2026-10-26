using System.Globalization;
using Gomoku.SelfPlay;

if (args.Length == 1 && args[0] == "--help")
{
    Console.WriteLine("Usage: Gomoku.SelfPlay --output PRIVATE_DIR --seed VALUE --games COUNT [--mode bootstrap|search]");
    Console.WriteLine("       Gomoku.SelfPlay admit --run SEARCH_RUN --baseline-evidence JSON --output PRIVATE_DIR");
    Console.WriteLine("Search data needs separate manual admission before training.");
    return 0;
}

if (args.Length == 7 && args[0] == "admit")
{
    var admissionOptions = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var index = 1; index < args.Length; index += 2)
    {
        if (!admissionOptions.TryAdd(args[index], args[index + 1]))
        {
            Console.Error.WriteLine("Repeated admission option.");
            return 2;
        }
    }
    if (admissionOptions.Count != 3 ||
        !admissionOptions.TryGetValue("--run", out var sourceRun) ||
        !admissionOptions.TryGetValue("--baseline-evidence", out var evidence) ||
        !admissionOptions.TryGetValue("--output", out var admittedRoot) ||
        admissionOptions.Values.Any(string.IsNullOrWhiteSpace))
    {
        Console.Error.WriteLine("Invalid admission options. Use --help for usage.");
        return 2;
    }
    try
    {
        Console.WriteLine(await new SelfPlayAdmission().AdmitAsync(sourceRun,
            evidence, admittedRoot));
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Admission failed: {exception.GetType().Name}: {exception.Message}");
        return 1;
    }
}

if (args.Length is not (6 or 8))
{
    Console.Error.WriteLine("Expected --output, --seed and --games. Use --help for usage.");
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
    !options.TryGetValue("--games", out var countText) ||
    options.Keys.Any(key => key is not ("--output" or "--seed" or "--games" or "--mode")) ||
    string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(seed) ||
    !int.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ||
    count < 1 ||
    (options.TryGetValue("--mode", out var modeText) && modeText is not ("bootstrap" or "search")))
{
    Console.Error.WriteLine("Invalid self-play arguments. Use --help for usage.");
    return 2;
}

try
{
    var settings = new SelfPlaySettings(seed, count,
        options.GetValueOrDefault("--mode") == "search" ? SelfPlayMode.Search :
            SelfPlayMode.Bootstrap);
    var games = new SelfPlayGenerator().Generate(settings);
    var runDirectory = await new SelfPlayArtifactWriter().WriteAsync(settings, games, output);
    Console.WriteLine($"Self-play dataset: {Path.Combine(runDirectory, "self-play.jsonl")}");
    Console.WriteLine($"Manifest: {Path.Combine(runDirectory, "manifest.json")}");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Self-play failed: {exception.GetType().Name}");
    return 1;
}
