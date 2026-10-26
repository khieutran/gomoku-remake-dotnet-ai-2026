import hashlib
import json
import tempfile
import unittest
from pathlib import Path

from training.dataset import load_self_play_train
from training.tests.test_training import canonical_hash, dataset


def self_play_run(root: Path, *, eligible: bool = True, split: str = "train-candidate") -> Path:
    root.mkdir()
    coordinates = [(2, 0), (11, 10), (2, 1), (11, 11), (2, 2),
                   (11, 12), (2, 3), (11, 13), (2, 4)]
    moves = [{"step": i + 1, "row": row, "column": col,
              "stone": "X" if i % 2 == 0 else "O",
              "decisionReason": "Search", "completedDepth": 3,
              "budgetExpired": False}
             for i, (row, col) in enumerate(coordinates)]
    row = {"gameNumber": 1, "source": "self-play", "split": split,
           "ruleVersion": "gomoku-exact-five-v1", "engineVersion": "search-v1",
           "selectionMode": "fixed-depth-3-v1", "openingStone": "X",
           "result": "XWon", "moves": moves}
    row["contentHash"] = canonical_hash(row)
    data = (json.dumps(row) + "\n").encode()
    (root / "self-play.jsonl").write_bytes(data)
    manifest = {"schemaVersion": 1, "source": "self-play", "split": split,
                "trainingEligible": eligible, "ruleVersion": "gomoku-exact-five-v1",
                "boardSize": 20, "engineVersion": "search-v1",
                "selectionMode": "fixed-depth-3-v1", "seed": "test", "gameCount": 1,
                "completedGameCount": 1,
                "datasetSha256": hashlib.sha256(data).hexdigest(),
                "admission": {"schemaVersion": 1, "reviewer": "human",
                              "baselineEvidenceSha256": "a" * 64,
                              "sourceManifestSha256": "b" * 64}}
    (root / "manifest.json").write_text(json.dumps(manifest))
    return root


class SelfPlayDatasetTests(unittest.TestCase):
    def test_admitted_search_adds_train_examples_only(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            guest = dataset(root / "guest")
            self_play = self_play_run(root / "self-play")
            examples, source = load_self_play_train(self_play, guest)
        self.assertGreater(len(examples), 0)
        self.assertEqual({item.label_source for item in examples}, {"self_play_search"})
        self.assertEqual(source["gameCount"], 1)

    def test_rejects_bootstrap_and_tampering(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            guest = dataset(root / "guest")
            self_play = self_play_run(root / "self-play", eligible=False)
            with self.assertRaisesRegex(ValueError, "admitted"):
                load_self_play_train(self_play, guest)
            manifest = json.loads((self_play / "manifest.json").read_text())
            manifest["trainingEligible"] = True
            (self_play / "manifest.json").write_text(json.dumps(manifest))
            rows = [json.loads(line) for line in (self_play / "self-play.jsonl").read_text().splitlines()]
            rows[0]["moves"][0]["decisionReason"] = "Fallback"
            data = (json.dumps(rows[0]) + "\n").encode()
            (self_play / "self-play.jsonl").write_bytes(data)
            manifest["datasetSha256"] = hashlib.sha256(data).hexdigest()
            (self_play / "manifest.json").write_text(json.dumps(manifest))
            with self.assertRaisesRegex(ValueError, "fallback"):
                load_self_play_train(self_play, guest)

    def test_self_play_cannot_duplicate_frozen_guest_game(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            guest = dataset(root / "guest")
            self_play = self_play_run(root / "self-play")
            guest_hash = json.loads((guest / "dataset.jsonl").read_text().splitlines()[0])["contentHash"]
            row = json.loads((self_play / "self-play.jsonl").read_text())
            row["contentHash"] = guest_hash
            data = (json.dumps(row) + "\n").encode()
            (self_play / "self-play.jsonl").write_bytes(data)
            manifest = json.loads((self_play / "manifest.json").read_text())
            manifest["datasetSha256"] = hashlib.sha256(data).hexdigest()
            (self_play / "manifest.json").write_text(json.dumps(manifest))
            with self.assertRaises(ValueError):
                load_self_play_train(self_play, guest)

    def test_self_play_cannot_leak_guest_test_position_before_diverging(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            guest = dataset(root / "guest")
            self_play = self_play_run(root / "self-play")
            guest_test = json.loads((guest / "dataset.jsonl").read_text().splitlines()[2])
            row = json.loads((self_play / "self-play.jsonl").read_text())
            for index in (0, 1):
                row["moves"][index]["row"] = guest_test["moves"][index]["row"]
                row["moves"][index]["column"] = guest_test["moves"][index]["column"]
            row["contentHash"] = canonical_hash(row)
            data = (json.dumps(row) + "\n").encode()
            (self_play / "self-play.jsonl").write_bytes(data)
            manifest = json.loads((self_play / "manifest.json").read_text())
            manifest["datasetSha256"] = hashlib.sha256(data).hexdigest()
            (self_play / "manifest.json").write_text(json.dumps(manifest))
            examples, source = load_self_play_train(self_play, guest)
            self.assertGreater(source["excludedHoldoutPositions"], 0)
            self.assertLess(len(examples), len(row["moves"]))
