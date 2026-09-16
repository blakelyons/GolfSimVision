"""Stdlib unittest coverage for harness.py's pure helpers (parse_kv, parse_bool)
and Transcript's on-disk format. No GSPro connection or socket needed — the REPL
and scenario dispatch themselves are exercised by hand against real GSPro.

Run: python -m unittest (from prototypes/gspro-harness/, either machine).
"""

import json
import tempfile
import unittest
from pathlib import Path

from harness import Transcript, parse_bool, parse_kv


class TestParseKv(unittest.TestCase):
    def test_parses_key_value_pairs(self):
        self.assertEqual(
            parse_kv("speed=5.0 hla=-1.5 vla=0"),
            {"speed": "5.0", "hla": "-1.5", "vla": "0"},
        )

    def test_empty_string_returns_empty_dict(self):
        self.assertEqual(parse_kv(""), {})

    def test_ignores_tokens_without_equals(self):
        self.assertEqual(parse_kv("speed=5.0 garbage"), {"speed": "5.0"})


class TestParseBool(unittest.TestCase):
    def test_recognizes_true_values(self):
        for s in ("1", "true", "True", "t", "yes", "Y"):
            self.assertTrue(parse_bool(s), s)

    def test_recognizes_false_values(self):
        for s in ("0", "false", "False", "f", "no", "n", ""):
            self.assertFalse(parse_bool(s), s)


class TestTranscript(unittest.TestCase):
    def test_writes_log_and_jsonl_with_direction_markers(self):
        with tempfile.TemporaryDirectory() as tmp:
            session_dir = Path(tmp)
            transcript = Transcript(session_dir)
            transcript.outbound(b'{"a":1}')
            transcript.inbound(b'{"b":2}')
            transcript.close()

            log_text = (session_dir / "transcript.log").read_text(encoding="utf-8")
            self.assertIn('GSPro << {"a":1}', log_text)
            self.assertIn('GSPro >> {"b":2}', log_text)

            lines = (session_dir / "transcript.jsonl").read_text(encoding="utf-8").splitlines()
            self.assertEqual(len(lines), 2)
            first = json.loads(lines[0])
            self.assertEqual(first["direction"], "<<")
            self.assertEqual(first["raw"], '{"a":1}')
            second = json.loads(lines[1])
            self.assertEqual(second["direction"], ">>")
            self.assertEqual(second["raw"], '{"b":2}')


if __name__ == "__main__":
    unittest.main()
