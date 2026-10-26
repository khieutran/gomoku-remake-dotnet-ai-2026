# Self-play bootstrap generator

Run the bootstrap smoke mode manually:

```sh
dotnet run --project tools/Gomoku.SelfPlay -- --output /private/path/self-play --seed campaign-1 --games 10
```

The output directory must be private (owner access only). Each run writes a
versioned `self-play-run-<manifest-hash>/` directory containing
`self-play.jsonl` and `manifest.json`. The manifest records the ruleset,
engine version, selection mode, seed, game count, and data SHA-256. Repeating
the same inputs returns the same run directory and bytes.

Bootstrap calls `SearchMoveSelector` with zero time budget. That invokes its
seeded legal fallback, so the artifact records
`engineVersion=legal-v1` and `selectionMode=seeded-legal-fallback-v1`.
These games are a smoke/bootstrap source with `source=self-play`,
`split=bootstrap`, and `trainingEligible=false`. The current trainer accepts
only guest exports and separately admitted search self-play; bootstrap cannot
enter training.

For real search self-play, use `--mode search`. Each move calls the depth-three
search selector with an effectively unbounded time budget, rejects fallback,
and records decision reason and completed depth. Full 20×20 games can be slow;
run a small campaign first, explicitly, after the baseline gate. No campaign
runs automatically.

```sh
dotnet run --project tools/Gomoku.SelfPlay -- --output /private/path/self-play \
  --seed campaign-1 --games 2 --mode search
```

The search run remains in `split=train-candidate` with
`trainingEligible=false`. To admit it, review baseline evidence and write a
private JSON file with `approvedBy` plus `tacticsPassed`, `legalMovesPassed`,
`latencyPassed`, and `apiRecoveryPassed` all set to `true`. Admission verifies
the data hash and every move through Core, then reruns the deterministic search
selector for every recorded move to check its position, reason, depth, and
absence of fallback;
it creates a separate private run with `trainingEligible=true` and hashes of
the source manifest and evidence. The source run remains immutable.

```sh
dotnet run --project tools/Gomoku.SelfPlay -- admit \
  --run /private/path/self-play/self-play-run-<hash> \
  --baseline-evidence /private/path/baseline-evidence.json \
  --output /private/path/admitted
```

The trainer accepts the admitted run only through `--self-play-run`; it adds
examples to training and preserves guest validation and frozen guest test.
The offline batch command in `training/retrain.py` trains both guest-only and
combined candidates, evaluates both, and reports their difference. It never
promotes either candidate.
