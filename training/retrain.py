"""Offline export/train/evaluate batch. It never promotes a candidate."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import subprocess
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verified_dataset_hash(run: Path, data_name: str) -> str:
    manifest = json.loads((run / "manifest.json").read_text(encoding="utf-8"))
    actual = sha256(run / data_name)
    if manifest.get("datasetSha256") != actual:
        raise ValueError(f"Dataset bytes changed or manifest is invalid: {run}")
    return actual


def run_command(args: list[str], *, evaluation: bool = False) -> str:
    completed = subprocess.run(args, cwd=REPO_ROOT, capture_output=True,
                               text=True, check=False)
    if completed.returncode not in ((0, 3) if evaluation else (0,)):
        raise RuntimeError(f"Offline stage failed ({completed.returncode}): {completed.stderr}")
    return completed.stdout


def save_checkpoint(path: Path, state: dict) -> None:
    temporary = path.with_suffix(".tmp")
    temporary.write_text(json.dumps(state, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    temporary.chmod(0o600)
    os.replace(temporary, path)


def comparison(guest_only: dict, combined: dict) -> dict:
    guest_test = guest_only["trainer"]["metrics"]["test"]["all"]
    combined_test = combined["trainer"]["metrics"]["test"]["all"]
    guest_score = guest_only["evaluation"]["report"]["matches"]["candidateScore"]
    combined_score = combined["evaluation"]["report"]["matches"]["candidateScore"]
    return {"guestOnlyCandidate": guest_only["candidateId"],
            "selfPlayCandidate": combined["candidateId"],
            "guestTestTop1Delta": combined_test["top1"] - guest_test["top1"],
            "matchScoreDelta": combined_score - guest_score,
            "guestOnlyGuestTest": guest_test, "selfPlayGuestTest": combined_test,
            "guestOnlyMatchScore": guest_score, "selfPlayMatchScore": combined_score}


def training_command(image: str, guest_run: Path, config: Path,
                     output_root: Path, self_play_run: Path | None) -> list[str]:
    """Run PyTorch in Linux while keeping private artifacts owned by the host."""
    if not hasattr(os, "getuid") or not image or any(char.isspace() for char in image):
        raise ValueError("A Linux/macOS host and a valid training image are required")
    guest_run, config, output_root = (path.resolve() for path in
                                      (guest_run, config, output_root))
    command = ["docker", "run", "--rm", "--platform", "linux/amd64",
               "--user", f"{os.getuid()}:{os.getgid()}",
               "-v", f"{guest_run}:{guest_run}:ro",
               "-v", f"{config}:{config}:ro",
               "-v", f"{output_root}:{output_root}"]
    if self_play_run is not None:
        self_play_run = self_play_run.resolve()
        command += ["-v", f"{self_play_run}:{self_play_run}:ro"]
    command += [image, "--dataset-run", str(guest_run), "--config", str(config),
                "--output", str(output_root)]
    if self_play_run is not None:
        command += ["--self-play-run", str(self_play_run)]
    return command


def evaluator_command(image: str, output_root: Path, fixture: Path,
                      champion_model: Path | None, arguments: list[str]) -> list[str]:
    """Run .NET ONNX inference in Linux; this also supports Intel macOS hosts."""
    if not hasattr(os, "getuid") or not image or any(char.isspace() for char in image):
        raise ValueError("A Linux/macOS host and a valid evaluator image are required")
    output_root = output_root.resolve()
    command = ["docker", "run", "--rm", "--platform", "linux/amd64",
               "--user", f"{os.getuid()}:{os.getgid()}"]
    mounts = {str(output_root)}
    for path in (fixture, champion_model):
        if path is None:
            continue
        directory = path.resolve().parent
        if directory.is_relative_to(output_root) or str(directory) in mounts:
            continue
        command += ["-v", f"{directory}:{directory}:ro"]
        mounts.add(str(directory))
    command += ["-v", f"{output_root}:{output_root}"]
    return command + [image, *arguments]


def retrain(*, guest_run: Path | None, export_output: Path | None,
            seed: str | None, test_cutoff: str | None, export_cutoff: str | None,
            self_play_run: Path | None, config: Path, evaluation_template: Path,
            output_root: Path, training_image: str = "gomoku-training:local",
            evaluator_image: str = "gomoku-model-evaluation:local",
            dotnet_bin: str | None = None) -> Path:
    dotnet_bin = dotnet_bin or os.environ.get("GOMOKU_DOTNET_BIN", "dotnet")
    if (guest_run is None) == (export_output is None):
        raise ValueError("Supply exactly one of guest_run or export_output")
    if export_output is not None and not all((seed, test_cutoff, export_cutoff)):
        raise ValueError("Export requires seed and both cutoffs")
    output_root.mkdir(mode=0o700, parents=True, exist_ok=True)
    if output_root.is_symlink() or output_root.stat().st_mode & 0o077:
        raise ValueError("Retrain output must be private and not a symlink")
    checkpoint = output_root / "checkpoint.json"
    state = json.loads(checkpoint.read_text()) if checkpoint.exists() else {"schemaVersion": 1}
    if state.get("schemaVersion") != 1:
        raise ValueError("Unsupported retrain checkpoint")

    if export_output is not None:
        export_key = [seed, test_cutoff, export_cutoff, str(export_output.resolve())]
        if (state.get("exportKey") != export_key or not state.get("guestRun") or
                not Path(state["guestRun"]).is_dir()):
            output = run_command([dotnet_bin, "run", "--project", "tools/Gomoku.DatasetExport",
                                  "--", "--output", str(export_output), "--seed", seed,
                                  "--test-cutoff", test_cutoff,
                                  "--export-cutoff", export_cutoff])
            dataset_line = next((line for line in output.splitlines()
                                 if line.startswith("Dataset: ")), None)
            if dataset_line is None:
                raise RuntimeError("Dataset export did not return a run path")
            guest_run = Path(dataset_line.removeprefix("Dataset: ")).parent
            state.update(exportKey=export_key, guestRun=str(guest_run.resolve()))
            save_checkpoint(checkpoint, state)
        else:
            guest_run = Path(state["guestRun"])
    assert guest_run is not None
    guest_run = guest_run.resolve()
    guest_hash = sha256(guest_run / "manifest.json")
    self_play_hash = sha256(self_play_run / "manifest.json") if self_play_run else None
    inputs = {"guestManifestSha256": guest_hash,
              "guestDatasetSha256": verified_dataset_hash(guest_run, "dataset.jsonl"),
              "selfPlayManifestSha256": self_play_hash,
              "selfPlayDatasetSha256": verified_dataset_hash(
                  self_play_run, "self-play.jsonl") if self_play_run else None,
              "configSha256": sha256(config),
              "evaluationTemplateSha256": sha256(evaluation_template)}
    if state.get("inputs") not in (None, inputs):
        raise ValueError("Checkpoint inputs changed; choose a new output directory")
    state["inputs"] = inputs
    save_checkpoint(checkpoint, state)

    template = json.loads(evaluation_template.read_text(encoding="utf-8"))
    results = {}
    for label, admitted in (("guestOnly", None), ("withSelfPlay", self_play_run)):
        if label == "withSelfPlay" and admitted is None:
            continue
        candidate_root = output_root / label / "candidates"
        report_path = output_root / label / "evaluation.json"
        (output_root / label).mkdir(mode=0o700, exist_ok=True)
        if (output_root / label).stat().st_mode & 0o077:
            raise ValueError("Retrain stage directory must be private")
        candidate_root.mkdir(mode=0o700, exist_ok=True)
        stage = state.get(label)
        if stage is None:
            command = training_command(training_image, guest_run, config,
                                       candidate_root, admitted)
            candidate = Path(run_command(command).strip().splitlines()[-1]).resolve()
            stage = {"candidate": str(candidate),
                     "trainerReportSha256": sha256(candidate / "report.json")}
            state[label] = stage
            save_checkpoint(checkpoint, state)
        candidate = Path(stage["candidate"])
        if sha256(candidate / "report.json") != stage["trainerReportSha256"]:
            raise ValueError("Checkpoint candidate report changed")
        trainer = json.loads((candidate / "report.json").read_text(encoding="utf-8"))
        evaluation_config = dict(template)
        fixture = Path(evaluation_config["fixturePath"])
        fixture = (REPO_ROOT / fixture).resolve() if not fixture.is_absolute() else fixture.resolve()
        evaluation_config["fixturePath"] = str(fixture)
        champion_path = evaluation_config.get("championModelPath")
        champion_model = Path(champion_path).resolve() if champion_path else None
        if champion_model is not None:
            evaluation_config["championModelPath"] = str(champion_model)
        evaluation_config.update(candidateId=candidate.name,
                                 candidateModelPath=str(candidate / "policy.onnx"),
                                 datasetHash=trainer["source"]["datasetSha256"],
                                 trainingMetricsPath=str(candidate / "report.json"))
        config_path = output_root / label / "evaluation-config.json"
        config_path.write_text(json.dumps(evaluation_config, indent=2) + "\n", encoding="utf-8")
        config_path.chmod(0o600)
        if not report_path.exists():
            run_command(evaluator_command(evaluator_image, output_root, fixture,
                        champion_model, ["evaluate", "--config", str(config_path),
                                         "--report", str(report_path)]), evaluation=True)
        run_command(evaluator_command(evaluator_image, output_root, fixture,
                    champion_model, ["verify-report", "--report", str(report_path)]))
        evaluation = json.loads(report_path.read_text(encoding="utf-8"))
        measured = evaluation["report"]
        if (not evaluation.get("reportSha256") or
                measured["candidateId"] != candidate.name or
                measured["candidateModelPath"] != str(candidate / "policy.onnx") or
                measured["modelSha256"] != sha256(candidate / "policy.onnx") or
                measured["datasetHash"] != trainer["source"]["datasetSha256"] or
                measured["trainingMetricsSha256"] != sha256(candidate / "report.json") or
                measured["configSha256"] != sha256(config_path) or
                measured["championId"] != evaluation_config.get("championId", "search-v1")):
            raise ValueError("Evaluation report provenance does not match retrain stage")
        results[label] = {"candidateId": candidate.name, "trainer": trainer,
                          "evaluation": evaluation, "evaluationReport": str(report_path)}
    summary = {"schemaVersion": 1, "status": "candidate-only",
               "inputs": inputs, "runs": results,
               "selfPlayEffect": comparison(results["guestOnly"], results["withSelfPlay"])
               if "withSelfPlay" in results else None}
    summary_path = output_root / "summary.json"
    summary_path.write_text(json.dumps(summary, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    summary_path.chmod(0o600)
    return summary_path


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--guest-run", type=Path)
    parser.add_argument("--export-output", type=Path)
    parser.add_argument("--seed")
    parser.add_argument("--test-cutoff")
    parser.add_argument("--export-cutoff")
    parser.add_argument("--self-play-run", type=Path)
    parser.add_argument("--config", type=Path, default=Path("training/config.json"))
    parser.add_argument("--evaluation-template", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--training-image", default="gomoku-training:local")
    parser.add_argument("--evaluator-image", default="gomoku-model-evaluation:local")
    parser.add_argument("--dotnet-bin")
    args = parser.parse_args()
    print(retrain(guest_run=args.guest_run, export_output=args.export_output,
                  seed=args.seed, test_cutoff=args.test_cutoff,
                  export_cutoff=args.export_cutoff, self_play_run=args.self_play_run,
                  config=args.config, evaluation_template=args.evaluation_template,
                  output_root=args.output_root, training_image=args.training_image,
                  evaluator_image=args.evaluator_image, dotnet_bin=args.dotnet_bin))


if __name__ == "__main__":
    main()
