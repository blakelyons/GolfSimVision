"""Structural coverage for scenarios/ -- every exp*.py must satisfy the
scenario contract in SPEC.md (a QUESTION string and a run(client) callable).
Doesn't connect to anything, so it runs without GSPro.

Run: python -m unittest (from prototypes/gspro-harness/, either machine).
"""

import importlib
import inspect
import json
import pkgutil
import unittest

import scenarios
from protocol import ConnectionState, GSProDirect
from test_protocol import FakeGSProServer


def _experiment_modules():
    return sorted(
        name
        for _, name, is_pkg in pkgutil.iter_modules(scenarios.__path__)
        if not is_pkg and name.startswith("exp")
    )


class TestConnectAndAnnounce(unittest.TestCase):
    """Covers the fix from the 2026-09-16 live session: a bare TCP connect wasn't
    enough for GSPro to register the client (no 201s, Connect window stayed "Not
    Connected") until one heartbeat went out. connect_and_announce() sends that
    heartbeat; this confirms it actually does, with the requested ready value."""

    def setUp(self):
        self.server = FakeGSProServer()
        self.client = GSProDirect(
            host="127.0.0.1", port=self.server.port, device_id="GolfSimVision",
            backoff_seconds=(0.05, 0.05, 0.1),
        )
        self.addCleanup(self.client.disconnect)
        self.addCleanup(self.server.close)

    def test_sends_exactly_one_heartbeat_with_requested_ready_value(self):
        scenarios.connect_and_announce(self.client, ready=False)

        self.assertEqual(self.client.state, ConnectionState.CONNECTED)
        frame = json.loads(self.server.recv_frame())
        self.assertEqual(frame["ShotDataOptions"]["IsHeartBeat"], True)
        self.assertEqual(frame["ShotDataOptions"]["LaunchMonitorIsReady"], False)


class TestScenarioContract(unittest.TestCase):
    def test_twenty_experiment_modules_present(self):
        self.assertEqual(len(_experiment_modules()), 20)

    def test_each_module_satisfies_the_scenario_contract(self):
        for name in _experiment_modules():
            module = importlib.import_module(f"scenarios.{name}")
            with self.subTest(module=name):
                self.assertIsInstance(getattr(module, "QUESTION", None), str)
                self.assertTrue(module.QUESTION.strip())
                run = getattr(module, "run", None)
                self.assertTrue(callable(run))
                params = list(inspect.signature(run).parameters)
                self.assertEqual(params, ["client"])


if __name__ == "__main__":
    unittest.main()
