#!/usr/bin/env python3
"""验证实际执行的 V5 TRX；发现用例、少跑、跳过或重复种子均不算通过。"""

import argparse
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET


def verify_results(path: Path, expected_seeds: int = 1000) -> int:
    if not 1 <= expected_seeds <= 1000:
        raise ValueError("Expected seed count must be 1..1000.")

    namespace = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    root = ET.parse(path).getroot()
    summary = root.find("./t:ResultSummary", namespace)
    if summary is None or summary.get("outcome") != "Completed":
        raise ValueError("V5 test run did not complete.")
    counters = root.find("./t:ResultSummary/t:Counters", namespace)
    if counters is None:
        raise ValueError("Missing TRX execution counters.")

    # 总数不能替代实际执行数；发现了 1000 个用例但未执行也必须失败。
    for name in ("total", "executed", "passed"):
        if int(counters.get(name, "-1")) != expected_seeds:
            raise ValueError(f"Expected {name}={expected_seeds}, got {counters.get(name)}.")
    for name in ("failed", "error", "timeout", "aborted", "notExecuted",
                 "notRunnable", "inconclusive", "passedButRunAborted", "pending", "inProgress"):
        if int(counters.get(name, "-1")) != 0:
            raise ValueError(f"Expected {name}=0, got {counters.get(name)}.")

    results = root.findall("./t:Results/t:UnitTestResult", namespace)
    if len(results) != expected_seeds:
        raise ValueError(f"Expected {expected_seeds} individual results, got {len(results)}.")

    # 同时核对逐条结果和实际种子，防止计数正确但少跑、重复或跑错 fixture。
    pattern = re.compile(r"ConfirmedFramesEqualIndependentOfflineOracle\(seed=(\d+)\)")
    seeds = []
    for result in results:
        name = result.get("testName", "")
        match = pattern.fullmatch(name)
        if match is None or result.get("outcome") != "Passed":
            raise ValueError(f"Unexpected or unsuccessful V5 result: {name}, {result.get('outcome')}.")
        seeds.append(int(match.group(1)))
    if len(set(seeds)) != expected_seeds or set(seeds) != set(range(expected_seeds)):
        raise ValueError("V5 results must contain each expected seed exactly once.")
    return len(seeds)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("trx", type=Path)
    parser.add_argument("--expected-seeds", type=int, default=1000)
    args = parser.parse_args()
    try:
        count = verify_results(args.trx, args.expected_seeds)
    except (OSError, ValueError, ET.ParseError) as error:
        print(f"V5_RESULTS_INVALID: {error}", file=sys.stderr)
        return 1
    print(f"V5_RESULTS_OK seeds={count} passed={count}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
