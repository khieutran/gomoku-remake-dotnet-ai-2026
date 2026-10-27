import unittest
import hashlib
import json
import tempfile
from pathlib import Path

from training.retrain import (comparison, evaluator_command,
                              training_command, verified_dataset_hash)


class RetrainComparisonTests(unittest.TestCase):
    def test_checkpoint_source_hash_requires_actual_dataset_bytes(self):
        with tempfile.TemporaryDirectory() as temp:
            run = Path(temp)
            data = b"fixture\n"
            (run / "dataset.jsonl").write_bytes(data)
            expected = hashlib.sha256(data).hexdigest()
            (run / "manifest.json").write_text(json.dumps({"datasetSha256": expected}))
            self.assertEqual(expected, verified_dataset_hash(run, "dataset.jsonl"))
            (run / "dataset.jsonl").write_bytes(b"tampered\n")
            with self.assertRaisesRegex(ValueError, "Dataset bytes changed"):
                verified_dataset_hash(run, "dataset.jsonl")

    def test_training_stage_uses_linux_image_and_host_owned_private_mounts(self):
        command = training_command("gomoku-training:local", Path("/private/guest"),
                                   Path("/private/config.json"), Path("/private/output"),
                                   Path("/private/self-play"))
        self.assertEqual(command[:5], ["docker", "run", "--rm", "--platform", "linux/amd64"])
        self.assertIn("--user", command)
        self.assertIn("--self-play-run", command)
        self.assertEqual(command[command.index("--self-play-run") + 1],
                         str(Path("/private/self-play").resolve()))

    def test_evaluator_stage_uses_linux_native_runtime_and_private_mounts(self):
        command = evaluator_command("gomoku-model-evaluation:local",
                                    Path("/private/output"),
                                    Path("/private/fixtures/tactics.json"), None,
                                    ["evaluate", "--report", "/private/output/report.json"])
        self.assertEqual(command[:5], ["docker", "run", "--rm", "--platform", "linux/amd64"])
        self.assertIn("/private/fixtures:/private/fixtures:ro", command)
        self.assertIn("/private/output:/private/output", command)
        self.assertEqual(command[-3:], ["evaluate", "--report", "/private/output/report.json"])

    def test_reports_guest_test_and_match_effect_separately(self):
        guest = {"candidateId": "guest", "trainer": {"metrics": {"test": {"all": {"top1": 0.4}}}},
                 "evaluation": {"report": {"matches": {"candidateScore": 0.5}}}}
        combined = {"candidateId": "combined", "trainer": {"metrics": {"test": {"all": {"top1": 0.6}}}},
                    "evaluation": {"report": {"matches": {"candidateScore": 0.7}}}}
        result = comparison(guest, combined)
        self.assertAlmostEqual(result["guestTestTop1Delta"], 0.2)
        self.assertAlmostEqual(result["matchScoreDelta"], 0.2)
        self.assertEqual(result["guestOnlyCandidate"], "guest")
        self.assertEqual(result["selfPlayCandidate"], "combined")


if __name__ == "__main__":
    unittest.main()
