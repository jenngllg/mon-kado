"""Strict local state and legacy adoption without inferring a successful verification."""

import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src/Operations.Frontend"))
import frontend_state as state


class StateTests(unittest.TestCase):
    def test_valid_state_and_manifest_authorization(self):
        # Arrange
        value = state.initial("a" * 40)
        value.update(updatedAt=state.now(), checks={"files": True, "https": False})
        # Act / Assert
        self.assertEqual(value, state.validate(value))
        self.assertEqual("legacy", value["phase"])
        self.assertIsNone(value["lastVerifiedRevision"])
        self.assertEqual(state.resume_digest({"a": 1, "b": 2}), state.resume_digest({"b": 2, "a": 1}))
        self.assertNotEqual(state.resume_digest({"a": 1}), state.resume_digest({"a": 2}))

    def test_rejects_invalid_fields_and_diagnostic_values(self):
        # Arrange
        cases = [{"schemaVersion": True}, {"phase": "private-canary"}, {"error": "private-canary"},
                 {"paused": 1}, {"resumeDigest": "bad"}, {"updatedAt": 1}, {"updatedAt": "x" * 41},
                 {"updatedAt": "2026-09-26T10:00:00+02:00"}, {"updatedAt": "invalid"},
                 {"checks": []}, {"checks": {"private-canary": True}}, {"checks": {"files": 1}},
                 {name: "bad" for name in ("revision",)}, {"lastVerifiedRevision": "bad"}, {"candidateRevision": "bad"}]
        # Act / Assert
        for change in cases:
            with self.subTest(change=change), self.assertRaises(ValueError):
                state.validate(state.initial() | change)
        for value in ([], {}, state.initial() | {"extra": 1}):
            with self.assertRaises(ValueError):
                state.validate(value)

    def test_read_bounds_regular_files_and_never_trusts_legacy_health(self):
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "status.json"
            # Act / Assert
            self.assertEqual(state.initial(), state.read(path))
            for legacy in ("healthy", "failed"):
                path.write_text(json.dumps({"state": legacy}))
                result = state.read(path, "a" * 40)
                self.assertIsNone(result["lastVerifiedRevision"])
                self.assertEqual(legacy == "failed", result["paused"])
            path.write_text(json.dumps(state.initial()))
            self.assertEqual(state.initial(), state.read(path))
            for raw in ("[]", '{"state":"private-canary"}', "x" * 16385):
                path.write_text(raw)
                with self.assertRaises(ValueError):
                    state.read(path)
            path.unlink()
            path.symlink_to(Path(directory) / "absent")
            with self.assertRaises(ValueError):
                state.read(path)
            path.unlink()
            path.mkdir()
            with self.assertRaises(ValueError):
                state.read(path)
