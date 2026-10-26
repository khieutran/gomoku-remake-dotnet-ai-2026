using System.Text.Json;
using Gomoku.Core;
using Gomoku.Infrastructure.AI;
using Gomoku.ModelEvaluation;

if (args.Length == 1 && args[0] == "--help")
{
    Console.WriteLine("evaluate --config CONFIG.json --report REPORT.json");
    Console.WriteLine("promote --pointer POINTER.json --report REPORT.json --candidate ID --approved-report-sha256 HASH --reason TEXT");
    Console.WriteLine("rollback --pointer POINTER.json --to ID --reason TEXT");
    Console.WriteLine("verify-report --report REPORT.json");
    Console.WriteLine("probe-model --model POLICY.onnx");
    return 0;
}

if (args.Length < 1 || args.Length % 2 == 0)
{
    Console.Error.WriteLine("Invalid command arguments. Use --help.");
    return 2;
}

try
{
    var command = args[0];
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 1; i < args.Length; i += 2)
        if (!args[i].StartsWith("--", StringComparison.Ordinal) ||
            !options.TryAdd(args[i], args[i + 1]))
            throw new ArgumentException("Invalid or repeated option.");

    string Required(string name) => options.TryGetValue(name, out var value) &&
        !string.IsNullOrWhiteSpace(value) ? value :
        throw new ArgumentException($"Missing {name}.");
    void Exact(params string[] names)
    {
        if (options.Count != names.Length || options.Keys.Except(names).Any())
            throw new ArgumentException("Unexpected command options.");
    }

    if (command == "evaluate")
    {
        Exact("--config", "--report");
        var configPath = Required("--config");
        var reportPath = Required("--report");
        var config = JsonSerializer.Deserialize<EvaluationConfig>(
            File.ReadAllText(configPath), EvaluationJson.Options)
            ?? throw new InvalidDataException("Empty evaluation config.");
        var fixtureFile = JsonSerializer.Deserialize<FixtureFile>(
            File.ReadAllText(config.FixturePath), EvaluationJson.Options)
            ?? throw new InvalidDataException("Empty fixture file.");
        MatchEvaluator.Validate(config, fixtureFile);
        if (File.Exists(reportPath))
            throw new IOException("Evaluation report already exists; choose a fresh path.");
        var training = JsonDocument.Parse(File.ReadAllText(config.TrainingMetricsPath));
        if (!training.RootElement.TryGetProperty("metrics", out var metrics) ||
            !metrics.TryGetProperty("train", out _) ||
            !metrics.TryGetProperty("validation", out _) ||
            !metrics.TryGetProperty("test", out _) ||
            !training.RootElement.TryGetProperty("source", out var source) ||
            !source.TryGetProperty("datasetSha256", out var dataset) ||
            dataset.GetString() != config.DatasetHash ||
            !training.RootElement.TryGetProperty("artifactSha256", out var artifacts) ||
            !artifacts.TryGetProperty("policy.onnx", out var modelHash) ||
            modelHash.GetString() != ReportStore.Sha256File(config.CandidateModelPath) ||
            !training.RootElement.TryGetProperty("configSha256", out var trainingConfigHash) ||
            !training.RootElement.TryGetProperty("trainerSha256", out var trainerHash) ||
            config.CandidateId != CandidateIdentity.FromSource(source,
                trainingConfigHash.GetString()!, trainerHash.GetString()!))
            throw new InvalidDataException("Trainer report does not match dataset/model or lacks split metrics.");

        var seed = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(config.Seed)));
        using var candidateRegistry = new OnnxPolicyModelRegistry(
            new Dictionary<string, string> { [config.CandidateId] = config.CandidateModelPath });
        IEvaluationPlayer champion;
        OnnxPolicyModelRegistry? championRegistry = null;
        try
        {
            if (config.ChampionId == "search-v1")
                champion = new SearchEvaluationPlayer(seed);
            else
            {
                championRegistry = new OnnxPolicyModelRegistry(
                    new Dictionary<string, string> { [config.ChampionId] = config.ChampionModelPath! });
                champion = new ModelEvaluationPlayer(championRegistry, seed, config.ChampionId);
            }
            var evaluation = new MatchEvaluator(
                new ModelEvaluationPlayer(candidateRegistry, seed, config.CandidateId),
                champion).Evaluate(config, fixtureFile);
            var report = new EvaluationReport(1, config.CandidateId, config.ChampionId,
                config.ChampionModelPath is null ? null :
                    Path.GetFullPath(config.ChampionModelPath),
                config.ChampionModelPath is null ? null : ReportStore.Sha256File(config.ChampionModelPath),
                Path.GetFullPath(config.CandidateModelPath),
                ReportStore.Sha256File(config.CandidateModelPath),
                config.DatasetHash, ReportStore.Sha256File(config.TrainingMetricsPath),
                metrics.Clone(), ReportStore.Sha256File(config.FixturePath),
                ReportStore.Sha256File(configPath), config.Seed, RuleSet.CurrentVersion,
                fixtureFile.BoardSize, config.MoveBudgetMs, config.MaxPlies,
                config.OpeningCount, config.MinimumCandidateScore,
                config.MinimumTacticalPassRate, config.MaximumFallbackRate,
                config.MaximumTimeouts, evaluation.Matches,
                evaluation.CandidateTactics, evaluation.ChampionTactics,
                evaluation.CandidateDecisions, evaluation.ChampionDecisions,
                new GateResult(false, []));
            var signed = ReportStore.Sign(report);
            ReportStore.WriteAtomically(reportPath, signed);
            Console.WriteLine($"Report: {Path.GetFullPath(reportPath)}");
            Console.WriteLine($"Report SHA256: {signed.ReportSha256}");
            Console.WriteLine($"Gate passed: {signed.Report.Gate.Passed}");
            return signed.Report.Gate.Passed ? 0 : 3;
        }
        finally { championRegistry?.Dispose(); }
    }
    if (command == "promote")
    {
        Exact("--pointer", "--report", "--candidate", "--approved-report-sha256", "--reason");
        var pointer = ChampionCommands.Promote(Required("--pointer"),
            Required("--report"), Required("--candidate"),
            Required("--approved-report-sha256"), Required("--reason"));
        Console.WriteLine($"Champion: {pointer.Current.AiVersion}");
        return 0;
    }
    if (command == "rollback")
    {
        Exact("--pointer", "--to", "--reason");
        var pointer = ChampionCommands.Rollback(Required("--pointer"),
            Required("--to"), Required("--reason"));
        Console.WriteLine($"Champion: {pointer.Current.AiVersion}");
        return 0;
    }
    if (command == "verify-report")
    {
        Exact("--report");
        var signed = ReportStore.ReadAndVerify(Required("--report"));
        if (ReportStore.Sha256File(signed.Report.CandidateModelPath) !=
            signed.Report.ModelSha256)
            throw new InvalidDataException("Evaluated model artifact changed.");
        Console.WriteLine($"Verified report SHA256: {signed.ReportSha256}");
        return 0;
    }
    if (command == "probe-model")
    {
        Exact("--model");
        const string probeVersion = "policy-v1-probe";
        using var registry = new OnnxPolicyModelRegistry(
            new Dictionary<string, string> { [probeVersion] = Required("--model") });
        var logits = registry.Find(probeVersion)!.Score(new Board(20).Snapshot(),
            Stone.O, CancellationToken.None);
        Console.WriteLine($"ONNX policy logits: {logits.Length}");
        return 0;
    }
    throw new ArgumentException("Unknown command.");
}
catch (Exception error)
{
    Console.Error.WriteLine($"Evaluation command failed: {error.GetType().Name}: {error.Message}");
    for (var inner = error.InnerException; inner is not null; inner = inner.InnerException)
        Console.Error.WriteLine($"Inner: {inner.GetType().Name}: {inner.Message}");
    return 1;
}
