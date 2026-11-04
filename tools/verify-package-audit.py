#!/usr/bin/env python3
"""Fail closed on incomplete NuGet JSON audits; never print report contents."""
import copy
import json
from pathlib import Path
import re
import sys
import subprocess
import tempfile


def require(condition):
    if not condition:
        raise ValueError("invalid audit report")


def verify(report, expected):
    require(isinstance(report, dict) and type(report.get("version")) is int and report["version"] == 1)
    parameters = report.get("parameters")
    require(isinstance(parameters, str) and "--vulnerable" in parameters.split()
            and "--include-transitive" in parameters.split())
    require(isinstance(report.get("sources"), list) and report["sources"]
            and all(isinstance(s, str) and s for s in report["sources"]))
    require(report.get("problems", []) == [])
    projects = report.get("projects")
    require(isinstance(projects, list) and projects and expected)
    seen = set()
    packages = advisories = 0
    for project in projects:
        require(isinstance(project, dict) and isinstance(project.get("path"), str))
        path = str(Path(project["path"]).resolve())
        require(path in expected and path not in seen and project.get("problems", []) == [])
        seen.add(path)
        frameworks = project.get("frameworks", [])
        require(isinstance(frameworks, list))
        framework_names = set()
        for framework in frameworks:
            require(isinstance(framework, dict) and isinstance(framework.get("framework"), str)
                    and framework["framework"] and framework["framework"] not in framework_names
                    and framework.get("problems", []) == [])
            framework_names.add(framework["framework"])
            for kind in ("topLevelPackages", "transitivePackages"):
                entries = framework.get(kind, [])
                require(isinstance(entries, list))
                for package in entries:
                    require(isinstance(package, dict) and isinstance(package.get("id"), str)
                            and package["id"] and isinstance(package.get("resolvedVersion"), str)
                            and package["resolvedVersion"])
                    vulnerabilities = package.get("vulnerabilities")
                    require(isinstance(vulnerabilities, list) and vulnerabilities)
                    for vulnerability in vulnerabilities:
                        require(isinstance(vulnerability, dict)
                                and vulnerability.get("severity") in ("Low", "Moderate", "High", "Critical")
                                and isinstance(vulnerability.get("advisoryurl"), str)
                                and vulnerability["advisoryurl"].startswith("https://"))
                    packages += 1
                    advisories += len(vulnerabilities)
    require(seen == expected)
    return len(seen), packages, advisories


def read_report(text):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result)
            result[key] = value
        return result
    return json.loads(text, object_pairs_hook=unique)


def self_test():
    expected = {str(Path("synthetic.csproj").resolve())}
    clean = {"version": 1, "parameters": "--vulnerable --include-transitive",
             "sources": ["https://example.invalid/index.json"],
             "projects": [{"path": "synthetic.csproj"}]}
    require(verify(clean, expected) == (1, 0, 0))
    for kind in ("topLevelPackages", "transitivePackages"):
        vulnerable = copy.deepcopy(clean)
        vulnerable["projects"][0]["frameworks"] = [{"framework": "net10.0", kind: [
            {"id": "Synthetic", "resolvedVersion": "1.0.0", "vulnerabilities": [
                {"severity": "High", "advisoryurl": "https://example.invalid/advisory"}]}]}]
        require(verify(vulnerable, expected) == (1, 1, 1))
    bad = [None, {}, dict(clean, version=2), dict(clean, version=True),
           dict(clean, projects=[]), dict(clean, sources=[]),
           dict(clean, problems=[{"level": "warning", "text": "RAW_SENTINEL"}]),
           dict(clean, parameters="--include-transitive"),
           dict(clean, projects=clean["projects"] * 2),
           dict(clean, projects=[{"path": "missing.csproj"}]),
           dict(clean, projects=[{"path": "synthetic.csproj", "frameworks": {}}])]
    broken = copy.deepcopy(vulnerable)
    broken["projects"][0]["frameworks"][0]["transitivePackages"][0].pop("vulnerabilities")
    bad.append(broken)
    for report in bad:
        try:
            verify(report, expected)
        except ValueError:
            pass
        else:
            raise AssertionError("invalid report accepted")
    for text in ("", "not JSON RAW_SENTINEL", '{"version":1,"version":1}', '{"version":'):
        try:
            read_report(text)
        except ValueError:
            pass
        else:
            raise AssertionError("bad JSON accepted")
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        solution = root / "synthetic.sln"
        solution.write_text('Project("{synthetic}") = "Synthetic", "synthetic.csproj", "{synthetic}"\n')
        for report, code in ((clean, 0), (vulnerable, 1), ({}, 2)):
            report = copy.deepcopy(report)
            if report:
                report["projects"][0]["path"] = str(root / "synthetic.csproj")
            report_file = root / "report.json"
            report_file.write_text(json.dumps(report))
            result = subprocess.run([sys.executable, str(Path(__file__).resolve()),
                                     str(report_file), str(solution)], capture_output=True, text=True)
            require(result.returncode == code and not result.stderr)
            require("RAW_SENTINEL" not in result.stdout and "Synthetic" not in result.stdout)
    print("Package audit parser positive and negative checks passed.")


def main():
    try:
        if sys.argv[1:] == ["--self-test"]:
            self_test()
            return 0
        require(len(sys.argv) == 3)
        solution = Path(sys.argv[2]).resolve()
        paths = re.findall(r'^Project\([^\n]+? = "[^"]+", "([^"]+\.csproj)"',
                           solution.read_text(), re.MULTILINE)
        expected = {str((solution.parent / p.replace("\\", "/")).resolve()) for p in paths}
        projects, packages, advisories = verify(read_report(Path(sys.argv[1]).read_text()), expected)
        print(f"Package audit: projects={projects}, vulnerable_packages={packages}, advisories={advisories}")
        return 1 if packages or advisories else 0
    except (ValueError, OSError, TypeError, KeyError):
        print("Package audit rejected: invalid or incomplete report.")
        return 2


if __name__ == "__main__":
    sys.exit(main())
