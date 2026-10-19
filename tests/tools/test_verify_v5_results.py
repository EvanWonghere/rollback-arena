"""只验证结果检查器，不用合成报告冒充真实 V5 验收证据。"""

import importlib.util
from pathlib import Path
import tempfile
import subprocess
import sys
import unittest
import xml.etree.ElementTree as ET


SCRIPT = Path(__file__).resolve().parents[2] / "tools" / "verify_v5_results.py"
SPEC = importlib.util.spec_from_file_location("verify_v5_results", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)
NS = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"


class VerifyV5ResultsTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.path = Path(self.directory.name) / "results.trx"

    def report(self, count=1000):
        root = ET.Element(f"{{{NS}}}TestRun")
        results = ET.SubElement(root, f"{{{NS}}}Results")
        for seed in range(count):
            ET.SubElement(results, f"{{{NS}}}UnitTestResult", {
                "testName": f"ConfirmedFramesEqualIndependentOfflineOracle(seed={seed})",
                "outcome": "Passed",
            })
        summary = ET.SubElement(root, f"{{{NS}}}ResultSummary", {"outcome": "Completed"})
        counters = ET.SubElement(summary, f"{{{NS}}}Counters", {
            "total": str(count), "executed": str(count), "passed": str(count),
            "failed": "0", "error": "0", "timeout": "0", "aborted": "0",
            "notExecuted": "0", "notRunnable": "0", "inconclusive": "0",
            "passedButRunAborted": "0", "pending": "0", "inProgress": "0",
        })
        return root, results, counters

    def check(self, root, expected=1000):
        ET.ElementTree(root).write(self.path, encoding="utf-8", xml_declaration=True)
        return MODULE.verify_results(self.path, expected)

    def test_accepts_exactly_one_thousand_passed_seeds(self):
        root, _, _ = self.report()
        self.assertEqual(self.check(root), 1000)

    def test_accepts_explicit_quick_suite_count(self):
        root, _, _ = self.report(16)
        self.assertEqual(self.check(root, 16), 16)

    def test_rejects_empty_execution(self):
        root, _, _ = self.report(0)
        with self.assertRaises(ValueError):
            self.check(root)

    def test_rejects_discovery_without_execution(self):
        root, _, counters = self.report(0)
        counters.set("total", "1000")
        with self.assertRaises(ValueError):
            self.check(root)

    def test_rejects_partial_seed_run(self):
        root, _, _ = self.report(999)
        with self.assertRaises(ValueError):
            self.check(root)

    def test_rejects_skipped_seed(self):
        root, results, _ = self.report()
        results[-1].set("outcome", "NotExecuted")
        with self.assertRaises(ValueError):
            self.check(root)

    def test_rejects_failed_seed_even_when_counters_claim_success(self):
        root, results, _ = self.report()
        results[-1].set("outcome", "Failed")
        with self.assertRaises(ValueError):
            self.check(root)

    def test_rejects_duplicate_seed(self):
        root, results, _ = self.report()
        results[-1].set("testName", results[0].get("testName"))
        with self.assertRaises(ValueError):
            self.check(root)

    def test_rejects_seed_outside_expected_range(self):
        root, results, _ = self.report()
        results[-1].set("testName", "ConfirmedFramesEqualIndependentOfflineOracle(seed=1000)")
        with self.assertRaises(ValueError):
            self.check(root)

    def test_rejects_unrelated_test_results(self):
        root, results, _ = self.report()
        results[-1].set("testName", "SomeOtherTest(seed=999)")
        with self.assertRaises(ValueError):
            self.check(root)

    def test_rejects_inconsistent_counters(self):
        root, _, counters = self.report()
        counters.set("executed", "999")
        with self.assertRaises(ValueError):
            self.check(root)

    def test_rejects_invalid_expected_seed_counts(self):
        for count in (0, -1, 1001):
            with self.subTest(count=count), self.assertRaises(ValueError):
                MODULE.verify_results(self.path, count)

    def test_missing_report_is_an_error(self):
        with self.assertRaises(OSError):
            MODULE.verify_results(self.path)

    def test_rejects_aborted_run_even_when_results_claim_success(self):
        root, _, _ = self.report()
        root.find(f"{{{NS}}}ResultSummary").set("outcome", "Aborted")
        with self.assertRaises(ValueError):
            self.check(root)

    def test_cli_accepts_explicit_quick_suite(self):
        root, _, _ = self.report(16)
        self.check(root, 16)
        result = subprocess.run([sys.executable, str(SCRIPT), str(self.path), "--expected-seeds", "16"],
                                text=True, capture_output=True, check=False)
        self.assertEqual(result.returncode, 0)
        self.assertIn("V5_RESULTS_OK seeds=16 passed=16", result.stdout)

    def test_cli_missing_report_exits_nonzero(self):
        result = subprocess.run([sys.executable, str(SCRIPT), str(self.path)],
                                text=True, capture_output=True, check=False)
        self.assertEqual(result.returncode, 1)
        self.assertIn("V5_RESULTS_INVALID:", result.stderr)


if __name__ == "__main__":
    unittest.main()
