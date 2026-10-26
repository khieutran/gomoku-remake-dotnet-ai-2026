#!/usr/bin/env bash
set -euo pipefail

# Called by test-phase3.sh after test-training.sh builds the fixture image.
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
run_id="${1:?pass the Phase 3 run ID}"
work="$repo_root/docs/Detail-phase-3/test-results/phase3-$run_id-pipeline"
mkdir -m 700 "$work"

# The Task 5 test fixture has one independent guest Game in each split and
# selected X/O labels. It is synthetic and contains no customer data.
docker run --rm --platform linux/amd64 --user "$(id -u):$(id -g)" \
    -v "$work:/fixture" --entrypoint python gomoku-training-test:local \
    -c 'from pathlib import Path; from training.tests.test_training import dataset; dataset(Path("/fixture/run"))'

cat > "$work/config.json" <<'JSON'
{"formatVersion":1,"seed":42,"epochs":1,"batchSize":4,"learningRate":0.001,"channels":4,"teacherAiVersions":["search-v1"]}
JSON

docker run --rm --platform linux/amd64 --user "$(id -u):$(id -g)" \
    -v "$work:/fixture" \
    gomoku-training-test:local --dataset-run /fixture/run \
    --config /fixture/config.json --output /fixture/out

shopt -s nullglob
candidates=("$work"/out/policy-v1-*)
if (( ${#candidates[@]} != 1 )); then
    printf 'FAIL: expected one fixture candidate; found %s\n' "${#candidates[@]}" >&2
    exit 1
fi
candidate="${candidates[0]}"
if [[ ! -s "$candidate/policy.onnx" || ! -s "$candidate/report.json" ]]; then
    printf 'FAIL: trainer did not write policy.onnx and report.json\n' >&2
    exit 1
fi

python3 - "$work" "$candidate" "$repo_root" <<'PY'
import json
import sys
from pathlib import Path

work, candidate, root = map(Path, sys.argv[1:])
manifest = json.loads((work / "run/manifest.json").read_text())
report = json.loads((candidate / "report.json").read_text())
if report["source"]["datasetSha256"] != manifest["datasetSha256"]:
    raise SystemExit("FAIL: trainer report dataset hash differs from fixture manifest")
config = {
    "candidateId": candidate.name,
    "candidateModelPath": str(candidate / "policy.onnx"),
    "datasetHash": manifest["datasetSha256"],
    "trainingMetricsPath": str(candidate / "report.json"),
    "fixturePath": str(root / "docs/Detail-phase-3/fixtures/ai-baseline-positions.json"),
    "seed": "phase3-integration-1",
    "openingCount": 1,
    "moveBudgetMs": 1000,
    "maxPlies": 8,
    "minimumCandidateScore": 0.55,
    "minimumTacticalPassRate": 1,
    "maximumFallbackRate": 0.05,
    "maximumTimeouts": 0,
    "championId": "search-v1",
}
(work / "evaluation-config.json").write_text(json.dumps(config, indent=2) + "\n")
PY

# Exit 3 means a measured candidate failed the promotion gate, which is a
# valid evaluation outcome. The report still must prove real ONNX inference.
docker build --platform linux/amd64 \
    -f tools/Gomoku.ModelEvaluation/Dockerfile \
    -t gomoku-model-evaluation-test:local .
set +e
docker run --rm --platform linux/amd64 --user "$(id -u):$(id -g)" \
    -v "$repo_root/docs/Detail-phase-3:$repo_root/docs/Detail-phase-3" \
    gomoku-model-evaluation-test:local evaluate \
    --config "$work/evaluation-config.json" --report "$work/evaluation-report.json"
evaluation_status=$?
set -e
if [[ "$evaluation_status" != 0 && "$evaluation_status" != 3 ]]; then
    printf 'FAIL: evaluation CLI exited %s\n' "$evaluation_status" >&2
    exit "$evaluation_status"
fi

python3 - "$work/evaluation-report.json" <<'PY'
import json
import sys
from pathlib import Path

signed = json.loads(Path(sys.argv[1]).read_text())
report = signed["report"]
decisions = report["candidateDecisions"]
if not signed.get("reportSha256"):
    raise SystemExit("FAIL: unsigned evaluation report")
if (decisions["count"] < 1 or decisions["fallbacks"] >= decisions["count"] or
        decisions["modelErrors"] != 0 or decisions["illegalMoves"] != 0):
    raise SystemExit(f"FAIL: ONNX inference or legal move check failed: {decisions}")
if not report["modelSha256"] or not report["datasetHash"]:
    raise SystemExit("FAIL: report lacks model/dataset provenance")
print(f'PASS: trained ONNX used for {decisions["count"]} legal candidate decisions; '
      f'gate passed={report["gate"]["passed"]}')
PY
printf 'Private pipeline artifacts: %s\n' "$work"
