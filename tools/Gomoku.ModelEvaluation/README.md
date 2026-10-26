# Offline model evaluation and manual release

`evaluate` reads a Task 5 trainer report and ONNX candidate. It checks the
dataset and model hashes, plays seeded openings twice with colors swapped,
checks the independent tactical fixture, and writes a JSON report with a
SHA-256 integrity hash (not a digital signature). It
does not read or modify the champion pointer.

Write a private config JSON such as:

```json
{
  "candidateId": "policy-v1-<trainer-derived-id>",
  "candidateModelPath": "/private/models/policy-v1-<id>/policy.onnx",
  "datasetHash": "<datasetSha256 from trainer report>",
  "trainingMetricsPath": "/private/models/policy-v1-<id>/report.json",
  "fixturePath": "docs/Detail-phase-3/fixtures/ai-baseline-positions.json",
  "seed": "phase3-release-1",
  "openingCount": 4,
  "moveBudgetMs": 200,
  "maxPlies": 80,
  "championId": "search-v1"
}
```

For a model champion, set `championId` to its pinned AI version and
`championModelPath` to the immutable ONNX artifact. Use the champion pointer's
current version and hash. Both players receive the same move budget and
rules. Score must reach 0.55, all tactical checks must pass, there may be no
invalid move or timeout, and candidate fallback rate must be at most 5%.
Config may tighten these limits. A gate failure is a valid evaluation outcome;
the CLI exits with code 3 after writing the report.

```bash
dotnet run --project tools/Gomoku.ModelEvaluation -- evaluate --config /private/models/evaluation.json --report /private/models/evaluation-report.json
```

The report prints a SHA256 to review. Only after reviewing and approving a
specific report, run the separate command with that exact hash:

```bash
dotnet run --project tools/Gomoku.ModelEvaluation -- promote --pointer /private/models/champion.json --report /private/models/evaluation-report.json --candidate policy-v1-<id> --approved-report-sha256 <report-sha256> --reason "approved evaluation"
```

`promote` rechecks the report gate and ONNX hash, then atomically replaces the
private pointer. `rollback --pointer /private/models/champion.json --to search-v1
--reason "regression"` selects a historical revision. The pointer retains
every prior version, path and model hash for Games pinned before the switch.
The API reads `AiModels:ChampionPointerPath` for new Games and pinned model
resolution. It falls back to `search-v1` when the pointer is absent.
`verify-report --report /private/models/evaluation-report.json` checks a saved
report's integrity, gate, and current candidate artifact before a retrain batch
reuses it.

For Intel macOS hosts, the current ONNX Runtime package has no `osx-x64`
native binary. Build `tools/Gomoku.ModelEvaluation/Dockerfile` and run the
evaluator in Linux/amd64, mounting the private model/report and fixture paths
at the same absolute paths. `scripts/test-phase3-pipeline.sh` and
`training/retrain.py` do this automatically after their images are built.

To serve a promoted model with Compose, keep `champion.json` and every
referenced candidate directory under one private absolute host directory.
Set `GOMOKU_AI_MODELS_DIR` to that directory and use the opt-in overlay:

```bash
GOMOKU_AI_MODELS_DIR=/absolute/private/models \
  docker compose -f compose.yaml -f compose.ai.yaml up -d api
```

Use a `--pointer` path inside that directory when promoting. The overlay
mounts the directory read-only at the same absolute path in the API container,
so the pointer's absolute `modelPath` entries remain resolvable. Keep old ONNX
files in place while any Game is pinned to them. The normal Compose stack
continues to use the search baseline until this overlay is enabled.
