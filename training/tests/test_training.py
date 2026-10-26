import hashlib
import json
import tempfile
import unittest
from pathlib import Path

import numpy as np
import onnx
import torch

from training.dataset import load_dataset
from training.model import PolicyNet
from training.train import legal_logits, train


def canonical_hash(row: dict) -> str:
    canonical = f'{row["ruleVersion"]}|{row["openingStone"]}|{row["result"]}' + "".join(
        f'|{move["step"]}:{move["row"]}:{move["column"]}:{move["stone"]}' for move in row["moves"])
    return hashlib.sha256(canonical.encode()).hexdigest()


def game(split: str, suffix: str) -> dict:
    shift = int(suffix)
    coordinates = [(0, shift), (10, 10 + shift), (0, 1 + shift),
                   (10, 11 + shift), (0, 2 + shift), (10, 12 + shift),
                   (0, 3 + shift), (10, 13 + shift), (0, 4 + shift)]
    moves = []
    for index, (row, column) in enumerate(coordinates):
        color = "X" if index % 2 == 0 else "O"
        moves.append({"step": index + 1, "row": row, "column": column,
                      "stone": color, "actor": "Human" if color == "X" else "AI",
                      "actualAiVersion": None if color == "X" else "search-v1",
                      "aiFallbackReason": None})
    # One teacher move is a fallback and must never become a label.
    moves[3]["actualAiVersion"] = "legal-v1"
    moves[3]["aiFallbackReason"] = "timeout"
    row = {"source": "guest", "split": split, "ruleVersion": "gomoku-exact-five-v1",
            "aiVersion": "search-v1", "openingStone": "X", "result": "XWon",
            "moves": moves}
    row["contentHash"] = canonical_hash(row)
    return row


def dataset(directory: Path) -> Path:
    directory.mkdir()
    rows = [game(split, str(index)) for index, split in
            enumerate(("train", "validation", "test"))]
    data = "".join(json.dumps(row) + "\n" for row in rows).encode()
    (directory / "dataset.jsonl").write_bytes(data)
    manifest = {"schemaVersion": 1, "source": "guest", "ruleVersion": "gomoku-exact-five-v1",
                "rowCount": 3, "splitCounts": {name: 1 for name in ("train", "validation", "test")},
                "datasetSha256": hashlib.sha256(data).hexdigest()}
    (directory / "manifest.json").write_text(json.dumps(manifest))
    return directory


class DatasetTests(unittest.TestCase):
    def test_selection_split_and_side_to_move_encoding(self):
        with tempfile.TemporaryDirectory() as temp:
            examples, source = load_dataset(dataset(Path(temp) / "run"), {"search-v1"})
        self.assertEqual(source["gameCounts"], {"train": 1, "validation": 1, "test": 1})
        for shift, split in enumerate(("train", "validation", "test")):
            items = examples[split]
            self.assertEqual([item.color for item in items], ["O", "O", "O", "X"])
            first_o = items[0]
            self.assertEqual(first_o.move_index, 10 * 20 + 10 + shift)
            self.assertEqual(first_o.board[1, 0, shift], 1)  # X is opponent to O.
            self.assertEqual(first_o.board[0, 0, shift], 0)
            self.assertEqual(first_o.board[2, 0, shift], 0)
            final_x = items[-1]
            self.assertEqual(final_x.move_index, 4 + shift)
            self.assertEqual(final_x.board[0, 0, shift], 1)  # X is self when X moves.
            self.assertEqual(final_x.board[1, 10, 10 + shift], 1)
            self.assertEqual(final_x.label_source, "terminal_win")

    def test_rejects_tamper_and_move_collision(self):
        with tempfile.TemporaryDirectory() as temp:
            run = dataset(Path(temp) / "run")
            (run / "dataset.jsonl").write_bytes(b"tampered\n")
            with self.assertRaisesRegex(ValueError, "SHA-256"):
                load_dataset(run, {"search-v1"})
        with tempfile.TemporaryDirectory() as temp:
            run = dataset(Path(temp) / "run")
            lines = [json.loads(line) for line in (run / "dataset.jsonl").read_text().splitlines()]
            lines[0]["moves"][1]["column"] = 0
            lines[0]["moves"][1]["row"] = 0
            lines[0]["contentHash"] = canonical_hash(lines[0])
            data = "".join(json.dumps(row) + "\n" for row in lines).encode()
            (run / "dataset.jsonl").write_bytes(data)
            manifest = json.loads((run / "manifest.json").read_text())
            manifest["datasetSha256"] = hashlib.sha256(data).hexdigest()
            (run / "manifest.json").write_text(json.dumps(manifest))
            with self.assertRaisesRegex(ValueError, "Invalid move"):
                load_dataset(run, {"search-v1"})

    def test_forged_unique_hash_cannot_hide_duplicate_game_across_splits(self):
        with tempfile.TemporaryDirectory() as temp:
            run = dataset(Path(temp) / "run")
            lines = [json.loads(line) for line in (run / "dataset.jsonl").read_text().splitlines()]
            lines[1]["moves"] = lines[0]["moves"]
            # Keep the distinct hash from the old second game to imitate a forged export.
            self.assertNotEqual(lines[0]["contentHash"], lines[1]["contentHash"])
            data = "".join(json.dumps(row) + "\n" for row in lines).encode()
            (run / "dataset.jsonl").write_bytes(data)
            manifest = json.loads((run / "manifest.json").read_text())
            manifest["datasetSha256"] = hashlib.sha256(data).hexdigest()
            (run / "manifest.json").write_text(json.dumps(manifest))
            with self.assertRaisesRegex(ValueError, "Content hash does not match moves"):
                load_dataset(run, {"search-v1"})


class PolicyTests(unittest.TestCase):
    def test_rejects_legal_fallback_as_teacher(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            run = dataset(root / "run")
            config = {"formatVersion": 1, "seed": 42, "epochs": 1, "batchSize": 4,
                      "learningRate": 0.001, "channels": 4,
                      "teacherAiVersions": ["legal-v1"]}
            config_file = root / "config.json"
            config_file.write_text(json.dumps(config))
            with self.assertRaisesRegex(ValueError, "Invalid training configuration"):
                train(run, config_file, root / "out")

    def test_output_contract_and_legal_mask(self):
        model = PolicyNet(4)
        board = torch.zeros(1, 3, 20, 20)
        board[:, 2] = 1
        board[:, 2, 0, 0] = 0
        self.assertEqual(tuple(model(board).shape), (1, 400))
        self.assertLess(legal_logits(model, board)[0, 0].item(), -1e8)

    def test_candidate_export_and_per_color_metrics(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            run = dataset(root / "run")
            config = {"formatVersion": 1, "seed": 42, "epochs": 1, "batchSize": 4,
                      "learningRate": 0.001, "channels": 4,
                      "teacherAiVersions": ["search-v1"]}
            config_file = root / "config.json"
            config_file.write_text(json.dumps(config))
            candidate = train(run, config_file, root / "out")
            report = json.loads((candidate / "report.json").read_text())
            self.assertEqual(report["status"], "candidate")
            for split in ("train", "validation", "test"):
                self.assertEqual(report["metrics"][split]["X"]["count"], 1)
                self.assertEqual(report["metrics"][split]["O"]["count"], 3)
            graph = onnx.load(str(candidate / "policy.onnx"))
            onnx.checker.check_model(graph)
            self.assertEqual(graph.graph.input[0].name, "board")
            self.assertEqual(graph.graph.output[0].name, "policy_logits")
            self.assertFalse((candidate / "promoted.json").exists())
            self.assertTrue(np.isfinite(report["metrics"]["test"]["all"]["loss"]))


if __name__ == "__main__":
    unittest.main()
