#!/usr/bin/env python3
"""Paired ABI-wrapper characterization. Retains all samples; timing is not a CI gate."""
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import random
import re
import statistics
import subprocess
import tarfile
import tempfile

ROOT = Path(__file__).resolve().parents[2]
BASELINE = "d4c15564af7c754e87dd019ca3cc25997097cda3"
PRESERVED = "42488401cf4f78ba0b2320c6da2701deef574702"
RESULTS = Path(os.environ.get("ABI_PERF_RESULTS", ROOT / "artifacts/abi-performance")).resolve()


def run(args, cwd, env, log, affinity=None):
    def pin():
        os.sched_setaffinity(0, {affinity})
    result = subprocess.run(args, cwd=cwd, env=env, text=True, stdout=subprocess.PIPE,
                            stderr=subprocess.STDOUT,
                            preexec_fn=pin if affinity is not None else None)
    log.write_text(result.stdout)
    if result.returncode:
        print(result.stdout, flush=True)
        raise RuntimeError(f"Command exited {result.returncode}: {args}")
    return result.stdout


def bootstrap_interval(log_ratios):
    rng = random.Random(17017)
    means = sorted(math.exp(statistics.mean(rng.choices(log_ratios, k=len(log_ratios))))
                   for _ in range(20000))
    return means[500], means[19499]


def main():
    RESULTS.mkdir(parents=True, exist_ok=True)
    candidate = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    cpu = min(os.sched_getaffinity(0))
    metadata = {"baseline": BASELINE, "candidate": candidate, "preservedWrapper": PRESERVED, "cpuAffinity": cpu,
                "os": platform.platform(), "machine": platform.machine(),
                "protocol": "8 independent case-isolated process blocks per case, rotating/reversed three-way order; 5s warmup and 9 batches after >=100ms calibration; default tiered PGO; process medians; paired log-mean bootstrap95% CI",
                "harnessSha256": hashlib.sha256((ROOT / "tools/AbiPerformance/Program.cs").read_bytes()).hexdigest()}
    run(["dotnet", "--info"], ROOT, os.environ, RESULTS / "dotnet-info.txt")
    run(["lscpu"], ROOT, os.environ, RESULTS / "cpu-info.txt")
    with tempfile.TemporaryDirectory(prefix="sharppack-abi-perf-") as temporary:
        work = Path(temporary)
        executables = {}
        env = dict(os.environ, DOTNET_TieredCompilation="1", DOTNET_TieredPGO="1",
                   DOTNET_ReadyToRun="0", DOTNET_PROCESSOR_COUNT="1",
                   DOTNET_CLI_TELEMETRY_OPTOUT="1")
        for label, revision in (("baseline", BASELINE), ("preserved-wrapper", PRESERVED), ("candidate", candidate)):
            source = work / label
            source.mkdir()
            archive = work / f"{label}.tar"
            with archive.open("wb") as output:
                subprocess.run(["git", "archive", revision], cwd=ROOT, stdout=output, check=True)
            with tarfile.open(archive) as tree:
                tree.extractall(source, filter="data")
            harness = source / "abi-perf-harness"
            harness.mkdir()
            (harness / "Program.cs").write_bytes((ROOT / "tools/AbiPerformance/Program.cs").read_bytes())
            (harness / "AbiPerf.csproj").write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../src/SharpPack.Core/SharpPack.Core.csproj" />
    <ProjectReference Include="../src/SharpPack.Generator/SharpPack.Generator.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>''')
            run(["dotnet", "build", "-c", "Release", "--nologo"], harness, env, RESULTS / f"{label}-build.txt")
            generated = "\n".join(p.read_text() for p in (harness / "obj").rglob("*.g.cs"))
            expected = (["SharpPack.Internal.SharpPackExactArrayBufferWriter", "SharpPack.Internal.ReusableLinkedArrayBufferWriterPool"]
                        if label == "baseline" else
                        ["SharpPack.CompilerServices.SharpPackExactBufferWriter", "SharpPack.CompilerServices.SharpPackPooledBufferWriter"])
            assert all(name in generated for name in expected), (label, "Expected generated wrapper paths absent")
            (RESULTS / f"{label}-generated.cs.txt").write_text(generated)
            executables[label] = harness / "bin/Release/net10.0/AbiPerf.dll"
        names = ["exact-small", "exact-large", "buffered-version-tolerant", "circular-reference"]
        summary = {}
        for name in names:
            pairs = []
            for pair in range(8):
                result = {}
                labels = ["baseline", "preserved-wrapper", "candidate"]
                shift = pair % 3
                order = labels[shift:] + labels[:shift]
                if pair % 2: order.reverse()
                for label in order:
                    log = RESULTS / f"{name}-pair-{pair:02}-{label}.json"
                    output = run(["dotnet", str(executables[label]), "--case=" + name], work, env, log, cpu)
                    result[label] = json.loads(output.strip().splitlines()[-1])
                    print(f"Finished {name} pair {pair + 1}/8 {label}", flush=True)
                pairs.append(result)
            ratios, baseline_ns, candidate_ns, allocated = [], [], [], []
            preserved_ns, candidate_preserved, preserved_baseline = [], [], []
            for pair in pairs:
                before, after = (pair[label]["results"][0] for label in ("baseline", "candidate"))
                preserved = pair["preserved-wrapper"]["results"][0]
                assert before["name"] == after["name"] == preserved["name"] == name
                assert preserved["sha256"] == before["sha256"] and preserved["payloadBytes"] == before["payloadBytes"], name
                assert before["sha256"] == after["sha256"] and before["payloadBytes"] == after["payloadBytes"], name
                b = statistics.median(x["Nanoseconds"] for x in before["samples"])
                a = statistics.median(x["Nanoseconds"] for x in after["samples"])
                p = statistics.median(x["Nanoseconds"] for x in preserved["samples"])
                preserved_ns.append(p); candidate_preserved.append(math.log(a / p)); preserved_baseline.append(math.log(p / b))
                baseline_ns.append(b); candidate_ns.append(a); ratios.append(math.log(a / b))
                b_alloc = statistics.median(x["AllocatedBytes"] for x in before["samples"])
                a_alloc = statistics.median(x["AllocatedBytes"] for x in after["samples"])
                p_alloc = statistics.median(x["AllocatedBytes"] for x in preserved["samples"])
                assert abs(a_alloc - b_alloc) <= .01 and abs(p_alloc - b_alloc) <= .01, (name, "Allocation changed", b_alloc, p_alloc, a_alloc)
                allocated.append([b_alloc, a_alloc])
            case = pairs[0]["baseline"]["results"][0]
            low, high = bootstrap_interval(ratios)
            summary[name] = {"candidateBaselineRatio": math.exp(statistics.mean(ratios)),
                "bootstrap95": [low, high],
                "candidatePreservedRatio": math.exp(statistics.mean(candidate_preserved)),
                "candidatePreservedBootstrap95": bootstrap_interval(candidate_preserved),
                "preservedBaselineRatio": math.exp(statistics.mean(preserved_baseline)),
                "preservedBaselineBootstrap95": bootstrap_interval(preserved_baseline),
                "preservedProcessMediansNs": preserved_ns, "baselineProcessMediansNs": baseline_ns,
                "candidateProcessMediansNs": candidate_ns, "allocatedBytesPerOp": allocated,
                "payloadBytes": case["payloadBytes"], "sha256": case["sha256"],
                "interpretation": "slower interval" if low > 1 else "faster interval" if high < 1 else "inconclusive interval"}
        # Diagnostics are separate from timing. Keep fully optimized no-tier code
        # and the actual tiered-PGO exact-small code, not just the former.
        jit = {}
        for mode in ("no-tier", "tiered-exact-small"):
            jit[mode] = {}
            for label, executable in executables.items():
                jit_env = dict(env, DOTNET_JitDisasm="*SerializeExact* *WriteToAndReset*")
                args = ["dotnet", str(executable)]
                if mode == "no-tier":
                    jit_env["DOTNET_TieredCompilation"] = "0"
                    args += ["--jit"]
                else:
                    args += ["--jit-tiered", "--case=exact-small"]
                output = run(args, work, jit_env, RESULTS / f"{label}-{mode}-jit.txt", cpu)
                listings = [line for line in output.splitlines() if "Assembly listing for method" in line]
                assert any("SerializeExact" in line for line in listings), (label, mode, "JIT assembly missing")
                jit[mode][label] = {"listings": listings,
                    "codeSizes": [line for line in output.splitlines() if "Total bytes of code" in line],
                    "compilerServicesReferencedCallSites": [line.strip() for line in output.splitlines()
                        if re.search(r"\b(?:call|bl|blr)\s+", line) and "CompilerServices.SharpPack" in line]}
        report = {"metadata": metadata, "summary": summary, "jit": jit}
        (RESULTS / "summary.json").write_text(json.dumps(report, indent=2) + "\n")
        print("ABI_PERF_SUMMARY " + json.dumps(report), flush=True)
        print("Payloads, roundtrips and generated paths verified; median allocated bytes/op match within 0.01 B/op. Timing is descriptive, not a performance acceptance gate.", flush=True)


if __name__ == "__main__":
    main()
