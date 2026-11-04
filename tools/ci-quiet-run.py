#!/usr/bin/env python3
"""Run CI commands without publishing raw build, test, or benchmark output."""
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile


def main():
    if len(sys.argv) != 2:
        print("Validation failed: invalid quiet-runner invocation.")
        return 2
    with tempfile.NamedTemporaryFile(prefix="sharppack-ci-", suffix=".log",
                                     dir=os.environ["RUNNER_TEMP"], delete=False) as log:
        result = subprocess.run(["bash", "--noprofile", "--norc", "-e", "-o",
                                 "pipefail", sys.argv[1]], stdout=log,
                                stderr=subprocess.STDOUT, check=False)
        log_path = Path(log.name)
    summaries = set()
    categories = set()
    with log_path.open(errors="replace") as lines:
        for line in lines:
            line = line.strip()
            if re.fullmatch(r"10\.0\.\d{3}", line):
                summaries.add(f"Selected SDK: {line}")
            tests = re.fullmatch(r"Passed!\s+- Failed:\s+(\d+), Passed:\s+(\d+), Skipped:\s+(\d+), Total:\s+(\d+),.*", line)
            if tests:
                failed, passed, skipped, total = tests.groups()
                summaries.add(f"Tests: passed={passed}, failed={failed}, skipped={skipped}, total={total}")
            runtime = re.fullmatch(r"NativeAOT runtime: (10\.0\.\d+); architecture: (X64|Arm64)", line)
            if runtime:
                summaries.add(f"NativeAOT runtime: {runtime[1]}; architecture: {runtime[2]}")
            compiler = re.fullmatch(r"Verified AOT compiler: ((?:runtime\.linux-(?:x64|arm64)\.)?Microsoft\.DotNet\.ILCompiler)/(10\.0\.\d+)", line)
            if compiler:
                summaries.add(f"AOT compiler: {compiler[1]}/{compiler[2]}")
            if line == "SharpPack NativeAOT verification passed.":
                summaries.add("NativeAOT verification including Brotli round-trips: passed")
            if re.fullmatch(r"Package audit: projects=\d+, vulnerable_packages=\d+, advisories=\d+", line):
                summaries.add(line)
                if not line.endswith("vulnerable_packages=0, advisories=0"):
                    categories.add("package vulnerabilities")
            if line == "Package audit rejected: invalid or incomplete report.":
                categories.add("invalid or incomplete package audit")
            if "error CS" in line or "error MSB" in line:
                categories.add("compilation")
            if "error NU" in line:
                categories.add("package restore")
            if "Failed!" in line or "[FAIL]" in line:
                categories.add("tests")
            if "AssertionError" in line or "ValueError" in line:
                categories.add("validation assertion")
    for summary in sorted(summaries):
        print(summary)
    if result.returncode:
        classification = ", ".join(sorted(categories)) or "command or validation failure"
        print(f"Validation failed: {classification}; exit={result.returncode}. Raw output remains only on the temporary runner.")
        return result.returncode if result.returncode > 0 else 1
    print("Validation step passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
