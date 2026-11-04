#!/usr/bin/env python3
"""Exercise log suppression and failure propagation using synthetic output only."""
import os
from pathlib import Path
import subprocess
import sys
import tempfile

runner = Path(__file__).with_name("ci-quiet-run.py").resolve()
with tempfile.TemporaryDirectory() as directory:
    root = Path(directory)
    env = dict(os.environ, RUNNER_TEMP=directory)
    for name, script, code, expected in (
        ("success", "printf 'RAW_SENTINEL\\n10.0.114\\n'\n", 0, "Selected SDK: 10.0.114"),
        ("failure", "echo 'RAW_SENTINEL error CS9999' >&2\nexit 7\n", 7, "compilation; exit=7"),
        ("pipeline", "false | true\necho RAW_SENTINEL\n", 1, "Validation failed:"),
        ("early-exit", "false\necho RAW_SENTINEL\n", 1, "Validation failed:"),
    ):
        source = root / f"{name}.sh"
        source.write_text(script)
        result = subprocess.run([sys.executable, str(runner), str(source)], env=env,
                                capture_output=True, text=True)
        assert result.returncode == code, name
        assert expected in result.stdout, name
        assert "RAW_SENTINEL" not in result.stdout + result.stderr, name
        assert not result.stderr, name
    assert any("RAW_SENTINEL" in p.read_text() for p in root.glob("sharppack-ci-*.log"))
print("Quiet runner suppression and failure propagation checks passed.")
