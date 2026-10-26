"""Strict reader and deliberately selected policy labels from Task 4 exports."""

from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass
from pathlib import Path

import numpy as np

SIZE = 20
RULE_VERSION = "gomoku-exact-five-v1"
SPLITS = ("train", "validation", "test")


@dataclass(frozen=True)
class Example:
    board: np.ndarray  # float32 [3,20,20], relative to side to move
    move_index: int
    color: str
    label_source: str


def _required(obj: dict, keys: tuple[str, ...], context: str) -> None:
    missing = set(keys) - obj.keys()
    if missing:
        raise ValueError(f"{context} missing: {', '.join(sorted(missing))}")


def _game_hash(row: dict) -> str:
    """Match GuestDatasetBuilder.GameHash byte for byte."""
    canonical = f'{row["ruleVersion"]}|{row["openingStone"]}|{row["result"]}'
    for move in row["moves"]:
        canonical += f'|{move["step"]}:{move["row"]}:{move["column"]}:{move["stone"]}'
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()


def _symmetry_keys(row: dict) -> set[tuple]:
    """All board rotations/reflections, including swapped X/O perspective."""
    def transform(r: int, c: int, index: int) -> tuple[int, int]:
        if index >= 4:
            c = SIZE - 1 - c
        for _ in range(index % 4):
            r, c = c, SIZE - 1 - r
        return r, c

    keys = set()
    for orientation in range(8):
        for swap in (False, True):
            color = lambda value: ({"X": "O", "O": "X"}[value] if swap else value)
            outcome = row["result"]
            if swap and outcome != "Draw":
                outcome = "OWon" if outcome == "XWon" else "XWon"
            moves = tuple((color(move["stone"]), *transform(move["row"], move["column"], orientation))
                          for move in row["moves"])
            keys.add((row["ruleVersion"], color(row["openingStone"]), outcome, moves))
    return keys


def _canonical_position(board: np.ndarray, side: str) -> bytes:
    """Canonical pre-move board and side across geometry and color swaps."""
    variants = []
    for rotated in (np.rot90(board, turns) for turns in range(4)):
        for oriented in (rotated, np.fliplr(rotated)):
            packed = oriented.astype(np.uint8, copy=False).tobytes()
            swapped = np.where(oriented == 0, 0, 3 - oriented).astype(np.uint8)
            variants.append((side.encode() + packed))
            variants.append((b"O" if side == "X" else b"X") + swapped.tobytes())
    return hashlib.sha256(min(variants)).digest()


def _guest_holdout_positions(rows: list[dict]) -> set[bytes]:
    positions: set[bytes] = set()
    for row in rows:
        if row["split"] not in ("validation", "test"):
            continue
        board = np.zeros((SIZE, SIZE), dtype=np.int8)
        for move in row["moves"]:
            positions.add(_canonical_position(board, move["stone"]))
            board[move["row"], move["column"]] = 1 if move["stone"] == "X" else 2
    return positions


def load_dataset(run_dir: Path, teacher_versions: set[str]) -> tuple[dict[str, list[Example]], dict]:
    manifest_path = run_dir / "manifest.json"
    dataset_path = run_dir / "dataset.jsonl"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    _required(manifest, ("schemaVersion", "source", "ruleVersion", "rowCount",
                         "splitCounts", "datasetSha256"), "manifest")
    if manifest["schemaVersion"] != 1 or manifest["source"] != "guest" or manifest["ruleVersion"] != RULE_VERSION:
        raise ValueError("Unsupported dataset manifest")
    data = dataset_path.read_bytes()
    digest = hashlib.sha256(data).hexdigest()
    if digest != manifest["datasetSha256"]:
        raise ValueError("Dataset SHA-256 does not match manifest")
    examples: dict[str, list[Example]] = {split: [] for split in SPLITS}
    counts = {split: 0 for split in SPLITS}
    hashes: set[str] = set()
    for line_number, line in enumerate(data.splitlines(), 1):
        row = json.loads(line)
        _required(row, ("source", "split", "ruleVersion", "aiVersion", "openingStone",
                        "result", "contentHash", "moves"), f"row {line_number}")
        split = row["split"]
        if (split not in examples or row["source"] != "guest" or
                row["ruleVersion"] != RULE_VERSION or row["openingStone"] not in ("X", "O") or
                row["result"] not in ("XWon", "OWon", "Draw")):
            raise ValueError(f"Invalid row {line_number} metadata")
        board = np.zeros((SIZE, SIZE), dtype=np.int8)
        expected = row["openingStone"]
        moves = row["moves"]
        if not isinstance(moves, list) or not moves:
            raise ValueError(f"Row {line_number} has no moves")
        for position, move in enumerate(moves):
            _required(move, ("step", "row", "column", "stone", "actor",
                             "actualAiVersion", "aiFallbackReason"), f"row {line_number} move")
            color = move["stone"]
            r, c = move["row"], move["column"]
            if (type(r) is not int or type(c) is not int or not 0 <= r < SIZE or
                    not 0 <= c < SIZE or board[r, c] or color != expected or
                    move["step"] != position + 1 or
                    move["actor"] != ("Human" if color == "X" else "AI")):
                raise ValueError(f"Invalid move in row {line_number}")
            final_x_win = color == "X" and position == len(moves) - 1 and row["result"] == "XWon"
            search_teacher = (color == "O" and move["actualAiVersion"] in teacher_versions and
                              move["aiFallbackReason"] is None)
            if search_teacher or final_x_win:
                own, opponent = (1, 2) if color == "X" else (2, 1)
                encoded = np.stack((board == own, board == opponent, board == 0)).astype(np.float32)
                examples[split].append(Example(encoded, r * SIZE + c, color,
                                               "terminal_win" if final_x_win else "search_teacher"))
            board[r, c] = 1 if color == "X" else 2
            expected = "O" if expected == "X" else "X"
        if row["result"] == "XWon" and moves[-1]["stone"] != "X" or row["result"] == "OWon" and moves[-1]["stone"] != "O":
            raise ValueError(f"Result and final mover disagree in row {line_number}")
        if row["contentHash"] != _game_hash(row):
            raise ValueError(f"Content hash does not match moves in row {line_number}")
        if row["contentHash"] in hashes:
            raise ValueError("Duplicate game across splits")
        hashes.add(row["contentHash"])
        counts[split] += 1
    if counts != {split: manifest["splitCounts"].get(split, 0) for split in SPLITS} or sum(counts.values()) != manifest["rowCount"]:
        raise ValueError("Dataset row counts do not match manifest")
    return examples, {"datasetSha256": digest, "manifestSha256": hashlib.sha256(manifest_path.read_bytes()).hexdigest(),
                      "gameCounts": counts}


def load_self_play_train(run_dir: Path, guest_run_dir: Path) -> tuple[list[Example], dict]:
    """Read a manually admitted search run; return training labels only."""
    manifest_path = run_dir / "manifest.json"
    data_path = run_dir / "self-play.jsonl"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if (manifest.get("schemaVersion") != 1 or manifest.get("source") != "self-play" or
            manifest.get("split") != "train-candidate" or
            manifest.get("trainingEligible") is not True or
            manifest.get("ruleVersion") != RULE_VERSION or
            manifest.get("boardSize") != SIZE or manifest.get("engineVersion") != "search-v1" or
            manifest.get("selectionMode") != "fixed-depth-3-v1" or
            not isinstance(manifest.get("admission"), dict) or
            manifest["admission"].get("schemaVersion") != 1 or
            not manifest["admission"].get("reviewer") or
            not isinstance(manifest["admission"].get("baselineEvidenceSha256"), str) or
            len(manifest["admission"]["baselineEvidenceSha256"]) != 64 or
            not isinstance(manifest["admission"].get("sourceManifestSha256"), str) or
            len(manifest["admission"]["sourceManifestSha256"]) != 64):
        raise ValueError("Self-play run is not admitted search data")
    data = data_path.read_bytes()
    digest = hashlib.sha256(data).hexdigest()
    if digest != manifest.get("datasetSha256"):
        raise ValueError("Self-play dataset SHA-256 mismatch")
    guest_rows = [json.loads(line) for line in
                  (guest_run_dir / "dataset.jsonl").read_bytes().splitlines()]
    guest_hashes = {row["contentHash"] for row in guest_rows}
    guest_variants = set().union(*(_symmetry_keys(row) for row in guest_rows))
    holdout_positions = _guest_holdout_positions(guest_rows)
    seen = set(guest_hashes)
    examples: list[Example] = []
    excluded_holdout_positions = 0
    rows = data.splitlines()
    if (not rows or len(rows) != manifest.get("gameCount") or
            len(rows) != manifest.get("completedGameCount")):
        raise ValueError("Self-play game count mismatch")
    for number, line in enumerate(rows, 1):
        row = json.loads(line)
        if (row.get("gameNumber") != number or row.get("source") != "self-play" or
                row.get("split") != "train-candidate" or row.get("ruleVersion") != RULE_VERSION or
                row.get("engineVersion") != "search-v1" or
                row.get("selectionMode") != "fixed-depth-3-v1" or
                row.get("openingStone") not in ("X", "O") or
                row.get("result") not in ("XWon", "OWon", "Draw") or
                not isinstance(row.get("moves"), list) or not row["moves"]):
            raise ValueError("Invalid self-play row metadata")
        if row.get("contentHash") != _game_hash(row) or row["contentHash"] in seen:
            raise ValueError("Self-play content hash mismatch or guest/duplicate game")
        if _symmetry_keys(row) & guest_variants:
            raise ValueError("Self-play game duplicates a guest game variant")
        seen.add(row["contentHash"])
        board = np.zeros((SIZE, SIZE), dtype=np.int8)
        expected = row["openingStone"]
        for step, move in enumerate(row["moves"], 1):
            r, c, color = move.get("row"), move.get("column"), move.get("stone")
            if (move.get("step") != step or color != expected or
                    type(r) is not int or type(c) is not int or
                    not 0 <= r < SIZE or not 0 <= c < SIZE or board[r, c]):
                raise ValueError("Invalid self-play move")
            if (move.get("decisionReason") not in ("Search", "ImmediateWin", "ImmediateBlock") or
                    move.get("budgetExpired") is not False or
                    type(move.get("completedDepth")) is not int or
                    move["completedDepth"] < 0 or
                    (move["decisionReason"] == "Search" and step > 1 and
                     move["completedDepth"] < 1)):
                raise ValueError("Self-play fallback or incomplete search move")
            own, opponent = (1, 2) if color == "X" else (2, 1)
            if (move["decisionReason"] != "Search" or move["completedDepth"] >= 2):
                if _canonical_position(board, color) in holdout_positions:
                    excluded_holdout_positions += 1
                else:
                    encoded = np.stack((board == own, board == opponent,
                                        board == 0)).astype(np.float32)
                    examples.append(Example(encoded, r * SIZE + c, color,
                                            "self_play_search"))
            board[r, c] = own
            expected = "O" if color == "X" else "X"
        if (row["result"] == "XWon" and row["moves"][-1]["stone"] != "X" or
                row["result"] == "OWon" and row["moves"][-1]["stone"] != "O"):
            raise ValueError("Self-play result and final move disagree")
    return examples, {"datasetSha256": digest,
                      "manifestSha256": hashlib.sha256(manifest_path.read_bytes()).hexdigest(),
                      "gameCount": len(rows), "exampleCount": len(examples),
                      "excludedHoldoutPositions": excluded_holdout_positions}
