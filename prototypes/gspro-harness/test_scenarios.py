"""Structural coverage for scenarios/ -- every exp*.py must satisfy the
scenario contract in SPEC.md (a QUESTION string and a run(client) callable).
Doesn't connect to anything, so it runs without GSPro.

Run: python -m unittest (from prototypes/gspro-harness/, either machine).
"""

import importlib
import inspect
import pkgutil
import unittest

import scenarios


def _experiment_modules():
    return sorted(
        name
        for _, name, is_pkg in pkgutil.iter_modules(scenarios.__path__)
        if not is_pkg and name.startswith("exp")
    )


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
