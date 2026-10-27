"""Train and export a private candidate. This command never promotes it."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import random
from pathlib import Path

import numpy as np
import torch
from torch import nn

from training.dataset import Example, load_dataset, load_self_play_train
from training.model import PolicyNet


def set_seed(seed: int) -> None:
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    torch.set_num_threads(1)
    torch.use_deterministic_algorithms(True)


def batch(examples: list[Example]) -> tuple[torch.Tensor, torch.Tensor]:
    return (torch.from_numpy(np.stack([item.board for item in examples])),
            torch.tensor([item.move_index for item in examples], dtype=torch.long))


def legal_logits(model: PolicyNet, boards: torch.Tensor) -> torch.Tensor:
    logits = model(boards)
    return logits.masked_fill(boards[:, 2].flatten(start_dim=1) == 0, -1.0e9)


@torch.no_grad()
def evaluate(model: PolicyNet, examples: list[Example], batch_size: int) -> dict:
    model.eval()
    result = {}
    for color in ("all", "X", "O"):
        subset = examples if color == "all" else [item for item in examples if item.color == color]
        if not subset:
            result[color] = {"count": 0, "loss": None, "top1": None, "top5": None}
            continue
        losses, hits1, hits5 = 0.0, 0, 0
        for offset in range(0, len(subset), batch_size):
            x, y = batch(subset[offset:offset + batch_size])
            logits = legal_logits(model, x)
            losses += nn.functional.cross_entropy(logits, y, reduction="sum").item()
            top = logits.topk(5, dim=1).indices
            hits1 += (top[:, 0] == y).sum().item()
            hits5 += (top == y[:, None]).any(dim=1).sum().item()
        result[color] = {"count": len(subset), "loss": losses / len(subset),
                         "top1": hits1 / len(subset), "top5": hits5 / len(subset)}
    return result


def train(run_dir: Path, config_path: Path, output_root: Path,
          self_play_run: Path | None = None) -> Path:
    config_bytes = config_path.read_bytes()
    config = json.loads(config_bytes)
    required = {"formatVersion", "seed", "epochs", "batchSize", "learningRate", "channels", "teacherAiVersions"}
    if (set(config) != required or config["formatVersion"] != 1 or
            type(config["seed"]) is not int or type(config["epochs"]) is not int or
            type(config["batchSize"]) is not int or type(config["channels"]) is not int or
            config["epochs"] < 1 or config["batchSize"] < 1 or config["channels"] < 1 or
            not 0 < config["learningRate"] <= 1 or
            not isinstance(config["teacherAiVersions"], list) or
            not all(isinstance(v, str) for v in config["teacherAiVersions"]) or
            set(config["teacherAiVersions"]) != {"search-v1"}):
        raise ValueError("Invalid training configuration")
    examples, source = load_dataset(run_dir, set(config["teacherAiVersions"]))
    guest_train = list(examples["train"])
    self_play_examples: list[Example] = []
    if self_play_run is not None:
        self_play_examples, self_play_source = load_self_play_train(self_play_run, run_dir)
        if not self_play_examples:
            raise ValueError("Admitted self-play has no independent training labels")
        examples["train"].extend(self_play_examples)
        source["selfPlay"] = self_play_source
    if not examples["train"]:
        raise ValueError("Dataset has no selected train labels")
    if not examples["validation"] or not examples["test"]:
        raise ValueError("Selected labels are required in validation and test splits")
    set_seed(config["seed"])
    model = PolicyNet(config["channels"])
    optimizer = torch.optim.Adam(model.parameters(), lr=config["learningRate"])
    history = []
    best_loss = float("inf")
    best_state = None
    for epoch in range(config["epochs"]):
        model.train()
        indices = np.random.permutation(len(examples["train"]))
        for offset in range(0, len(indices), config["batchSize"]):
            items = [examples["train"][i] for i in indices[offset:offset + config["batchSize"]]]
            x, y = batch(items)
            optimizer.zero_grad(set_to_none=True)
            loss = nn.functional.cross_entropy(legal_logits(model, x), y)
            loss.backward()
            optimizer.step()
        metrics = evaluate(model, examples["validation"], config["batchSize"])
        history.append({"epoch": epoch + 1, "validation": metrics})
        if metrics["all"]["loss"] < best_loss:
            best_loss = metrics["all"]["loss"]
            best_state = {name: tensor.detach().clone() for name, tensor in model.state_dict().items()}
    assert best_state is not None
    model.load_state_dict(best_state)
    trainer_hash = hashlib.sha256(b"".join(path.read_bytes() for path in
                                  (Path(__file__).with_name(name) for name in
                                   ("dataset.py", "model.py", "train.py")))).hexdigest()
    report = {"status": "candidate", "source": source, "configSha256": hashlib.sha256(config_bytes).hexdigest(),
              "trainerSha256": trainer_hash,
              "selection": {"guestX": "final move of XWon game only",
                            "guestO": "non-fallback search-v1 teacher moves",
                            "selfPlay": "non-fallback tactical moves or search depth at least two"},
              "history": history, "metrics": {split: evaluate(model, examples[split], config["batchSize"])
                                                 for split in ("train", "validation", "test")},
              "sourceMetrics": {"guestTrain": evaluate(model, guest_train, config["batchSize"]),
                                "selfPlayTrain": evaluate(model, self_play_examples, config["batchSize"])},
              "environment": {"python": os.sys.version.split()[0], "torch": torch.__version__,
                              "numpy": np.__version__, "device": "cpu"},
              "modelIO": {"input": "board float32 [N,3,20,20]", "output": "policy_logits float32 [N,400]",
                          "channels": ["side to move", "opponent", "empty"], "index": "row*20+column"}}
    candidate_material = source["datasetSha256"] + report["configSha256"] + trainer_hash
    if "selfPlay" in source:
        candidate_material += source["selfPlay"]["datasetSha256"] + source["selfPlay"]["manifestSha256"]
    candidate_id = "policy-v1-" + hashlib.sha256(candidate_material.encode()).hexdigest()[:16]
    output_root.mkdir(mode=0o700, parents=True, exist_ok=True)
    if output_root.is_symlink() or output_root.stat().st_mode & 0o077:
        raise ValueError("Candidate output directory must be private and not a symlink")
    output = output_root / candidate_id
    previous_umask = os.umask(0o077)
    try:
        output.mkdir(mode=0o700, exist_ok=False)
        torch.save(model.state_dict(), output / "weights.pt")
        model.eval()
        torch.onnx.export(model, torch.zeros(1, 3, 20, 20), output / "policy.onnx",
                          input_names=["board"], output_names=["policy_logits"],
                          dynamic_axes={"board": {0: "batch"}, "policy_logits": {0: "batch"}},
                          opset_version=17)
        report["artifactSha256"] = {
            name: hashlib.sha256((output / name).read_bytes()).hexdigest()
            for name in ("weights.pt", "policy.onnx")
        }
        (output / "config.json").write_bytes(config_bytes)
        (output / "report.json").write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        for path in output.iterdir():
            path.chmod(0o600)
    finally:
        os.umask(previous_umask)
    return output


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dataset-run", type=Path, required=True)
    parser.add_argument("--config", type=Path, default=Path("training/config.json"))
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--self-play-run", type=Path,
                        help="Manually admitted search self-play run; adds training examples only")
    args = parser.parse_args()
    print(train(args.dataset_run, args.config, args.output, args.self_play_run))


if __name__ == "__main__":
    main()
