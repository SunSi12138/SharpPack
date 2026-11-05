"""Bounded ARM64 test diagnosis; no raw logs or dumps are uploaded."""
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

source = Path(sys.argv[1]).resolve()
work = Path(tempfile.mkdtemp(prefix="pipe-test-", dir=os.environ.get("RUNNER_TEMP")))
project = source / "tests/SharpPack.Tests/SharpPack.Tests.csproj"

def run(command, label):
    result = subprocess.run(["timeout", "--kill-after=5s", "90s", *command],
                            cwd=source, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (work / (label + ".log")).write_bytes(result.stdout)
    return result.returncode

code = run(["dotnet", "build", str(project), "-c", "Release"], "build")
print("Unit-test build exit=" + str(code), flush=True)
if code:
    sys.exit(1)
for iteration in range(1, 6):
    results = work / ("iteration-" + str(iteration))
    code = run(["dotnet", "test", str(project), "-c", "Release", "--no-build",
                "--logger", "trx", "--blame-hang-timeout", "45s",
                "--blame-hang-dump-type", "none", "--results-directory", str(results)],
               "test-" + str(iteration))
    print("Unit-test iteration=" + str(iteration) + "; exit=" + str(code), flush=True)
    counters = []
    for path in results.rglob("*.trx"):
        root = ET.parse(path).getroot()
        counters += [e.attrib for e in root.iter() if e.tag.split("}")[-1] == "Counters"]
    for item in counters:
        print("Counts: " + ", ".join(k + "=" + str(item.get(k)) for k in
              ["total", "executed", "passed", "failed", "notExecuted"]), flush=True)
    for path in results.rglob("*Sequence.xml"):
        for element in ET.parse(path).getroot().iter():
            if element.get("Completed") == "False":
                name = element.get("Name", "")
                if name.startswith("SharpPack.Tests.") and all(c.isalnum() or c in "._+" for c in name):
                    print("Unfinished repository test: " + name, flush=True)
                else:
                    print("Unfinished test name omitted", flush=True)
    if code or len(counters) != 1 or counters[0].get("passed") != "598" or counters[0].get("failed") != "0":
        print("Bounded unit-test diagnosis stopped; raw output stays on temporary runner", flush=True)
        sys.exit(1)
print("All five unit-test iterations passed; absence of a race is not proved", flush=True)
