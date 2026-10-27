# Offline candidate policy trainer

Task 5 consumes a private Task 4 `run-<hash>/` directory containing `dataset.jsonl` and `manifest.json`. The trainer checks the dataset hash, row counts, schema, ruleset, split names, turn order, and occupied cells before training. It uses the export's Match level train, validation, and test split without reshuffling games. It does not read the database or promote a model.

The labels are deliberately narrow. X contributes only the final move of an `XWon` game. O contributes moves explicitly attributed to a configured search teacher (`search-v1` by default) with no fallback reason. Earlier moves in a winning guest game are not treated as optimal. An empty selected split stops training, so the first real dataset must contain selected moves in all three splits. Metrics report loss and top 1/top 5 move accuracy for each split and each color. These are imitation metrics, not playing strength; Task 7 decides whether a candidate is worth promoting.

The canonical board input has shape `[N,3,20,20]` and float32 values. Channel 0 holds stones of the side to move, channel 1 the opponent, and channel 2 empty cells. The ONNX output `policy_logits` has shape `[N,400]`; cell `(row,column)` is `row*20+column`. The caller must mask occupied cells. The network produces policy logits only; no value head is trained because this dataset has no trusted position value labels.

## Linux CPU container

The host's macOS x86_64 environment does not have a supported native PyTorch build. The Dockerfile pins a Linux/amd64 Python and package set. Build and train only when dependencies and dataset access are approved:

```bash
docker build --platform linux/amd64 -f training/Dockerfile -t gomoku-training:local training
docker run --rm --platform linux/amd64 --user "$(id -u):$(id -g)" \
  -v "$PWD/docs/Detail-phase-3/datasets:/data:ro" \
  -v "$PWD/training/artifacts:/artifacts" \
  gomoku-training:local --dataset-run /data/run-<manifest-sha256> \
  --config training/config.json --output /artifacts
```

Create `training/artifacts/` privately (`mkdir -m 700`) before running. The candidate is written to `training/artifacts/policy-v1-<hash>/` with `policy.onnx`, `weights.pt`, `config.json`, and `report.json`. The candidate ID derives from dataset, config, and trainer source SHA-256; admitted self-play adds its data and admission-manifest hashes to that identity. Existing candidate directories are refused to preserve immutable provenance. Training uses a fixed seed, CPU, one Torch thread, deterministic operations, and the best validation loss checkpoint. Model quality still depends on the platform's numerical implementation; the report records package versions and hashes. Dataset and weights are ignored by Git.

## Optional admitted self-play

The `--self-play-run /private/admitted/self-play-admitted-<hash>` option adds only
manually admitted `search-v1` self-play examples to the training split. Bootstrap
or unadmitted runs are rejected. The guest validation and frozen guest test
splits stay unchanged; self-play games with a content hash already in any guest
split are rejected. Training labels that overlap guest validation or test under
board symmetry and color swap are excluded and counted; a run with no independent
labels stops training. The trainer report separates guest and self-play train
metrics, and records both source hashes. `legal-v1` fallback moves cannot be
configured as a guest teacher. Self-play labels use non-fallback tactical
decisions or search moves that completed at least depth two.

For an offline batch, run from the repository root after creating a private
output directory and an evaluation template JSON with the Task 7 champion,
fixtures, seed, and gate thresholds. Build both Linux images first. The host
orchestrator calls Docker for PyTorch and .NET ONNX inference; host .NET is only
needed when it exports a fresh guest dataset:

```bash
docker build --platform linux/amd64 -f training/Dockerfile -t gomoku-training:local training
docker build --platform linux/amd64 -f tools/Gomoku.ModelEvaluation/Dockerfile \
  -t gomoku-model-evaluation:local .
python -m training.retrain --guest-run /private/guest/run-<hash> \
  --self-play-run /private/admitted/self-play-admitted-<hash> \
  --evaluation-template /private/evaluation-template.json \
  --output-root /private/retrain-batch
```

Use `--export-output /private/guest --seed VALUE --test-cutoff UTC
--export-cutoff UTC` instead of `--guest-run` to include Task 4 export. The
batch checkpoints each stage, creates guest-only and combined candidates,
evaluates both, and writes `summary.json` with guest test and match-score
differences. It never calls `promote` or edits the champion pointer.

After approval, run `bash scripts/test-training.sh` to build the same Linux image and run the Task 5 unit and export checks. The script is intentionally not run as part of implementation.
