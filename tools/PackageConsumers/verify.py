#!/usr/bin/env python3
"""Verify the already-packed release directory. No package publication."""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tarfile
import tempfile
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape
import zipfile


HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
CORE = "SharpPack.Core"
GENERATOR = "SharpPack.Generator"
STREAMING = "SharpPack.Streaming"
AGGREGATE = "SharpPack"
SYMBOLS = (CORE, STREAMING)
PACKAGES = (CORE, GENERATOR, STREAMING, AGGREGATE)
PREVIOUS = "1.1.0"
ROSLYN_HOSTS = ("5.0.0", "5.6.0")
NUGET = "https://api.nuget.org/v3/index.json"


def run(args, cwd, env, expected_success=True):
    result = subprocess.run(args, cwd=cwd, env=env, text=True,
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    if (result.returncode == 0) != expected_success:
        print(result.stdout[-12000:], flush=True)
        raise RuntimeError(f"Unexpected exit {result.returncode} in {cwd.name}: {' '.join(args)}")
    return result.stdout


def normalized_version(version):
    # NuGet removes build metadata, normalizes zero padding, and compares
    # prerelease identifiers case-insensitively.
    number, _, suffix = version.split("+", 1)[0].partition("-")
    parts = [int(part) for part in number.split(".")]
    parts += [0] * (3 - len(parts))
    if len(parts) == 4 and parts[-1] == 0:
        parts.pop()
    return ".".join(map(str, parts)) + (f"-{suffix.lower()}" if suffix else "")


def inspect_feed(feed, version):
    expected = {(name, ".nupkg") for name in PACKAGES} | {(name, ".snupkg") for name in SYMBOLS}
    found = {}
    for path in sorted(feed.iterdir()):
        if not path.is_file() or path.is_symlink() or path.suffix not in {".nupkg", ".snupkg"}:
            raise RuntimeError(f"Unexpected entry in release feed: {path.name}")
        with zipfile.ZipFile(path) as archive:
            names = archive.namelist()
            manifests = [name for name in names if name.endswith(".nuspec")]
            if len(names) != len(set(names)) or len(manifests) != 1:
                raise RuntimeError(f"Invalid package inventory: {path.name}")
            metadata = ET.fromstring(archive.read(manifests[0])).find("{*}metadata")
            name = metadata.findtext("{*}id")
            identity = (name, path.suffix)
            if (identity not in expected or identity in found or
                    normalized_version(metadata.findtext("{*}version")) != version):
                raise RuntimeError(f"Unexpected package identity or version: {path.name}")
            if name == STREAMING:
                dependencies = [(item.get("id"), item.get("version"))
                                for item in metadata.findall(".//{*}dependency")]
                if dependencies != [(CORE, f"[{version}]")]:
                    raise RuntimeError(f"{path.name} must depend on {CORE} [{version}]")

            payload = {item for item in names if item != manifests[0]
                       and item not in {"[Content_Types].xml", "_rels/.rels"}
                       and not item.startswith("package/services/metadata/core-properties/")}
            if path.suffix == ".snupkg":
                pdb = f"lib/net10.0/{name}.pdb"
                if payload != {pdb} or archive.getinfo(pdb).file_size == 0:
                    raise RuntimeError(f"Missing or unexpected symbol payload: {path.name}")
            else:
                validate_package_payload(name, payload, metadata)
            found[identity] = path
    if set(found) != expected:
        raise RuntimeError(f"Missing release packages: {sorted(expected - set(found))}")
    print(f"Verified package identity, analyzer/runtime layout, symbols and Streaming lockstep: {version}", flush=True)
    return {name: found[(name, ".nupkg")] for name in PACKAGES}


def validate_package_payload(name, payload, metadata):
    required = {"README.md", "Icon.png", "NOTICE"}
    assemblies = {item for item in payload if item.endswith(".dll")}
    expected_runtime = set()
    if name in SYMBOLS:
        expected_runtime.add(f"lib/net10.0/{name}.dll")
        required |= expected_runtime | {f"lib/net10.0/{name}.xml"}
    if name == GENERATOR:
        if f"analyzers/dotnet/cs/{name}.dll" not in assemblies or not all(
                item.startswith("analyzers/dotnet/cs/") for item in assemblies):
            raise RuntimeError("Generator does not have the expected analyzer placement")
    elif assemblies != expected_runtime:
        raise RuntimeError(f"{name} contains unexpected runtime assemblies")
    if (not required <= payload or any(item.endswith(".pdb") for item in payload)
            or metadata.findtext("{*}license") != "MIT"):
        raise RuntimeError(f"{name} is missing package content or contains loose symbols")


def nuget_config(path, feed=None, network=False):
    config = ET.Element("configuration")
    sources = ET.SubElement(config, "packageSources")
    ET.SubElement(sources, "clear")
    if feed is not None:
        ET.SubElement(sources, "add", key="artifact", value=str(feed))
    if network:
        ET.SubElement(sources, "add", key="nuget", value=NUGET)
    if feed is not None and network:
        mapping = ET.SubElement(config, "packageSourceMapping")
        ET.SubElement(ET.SubElement(mapping, "packageSource", key="artifact"),
                      "package", pattern="SharpPack*")
        ET.SubElement(ET.SubElement(mapping, "packageSource", key="nuget"),
                      "package", pattern="*")
    ET.ElementTree(config).write(path, encoding="unicode")
    return path


def project(path, items, properties=""):
    path.mkdir()
    (path / "Consumer.csproj").write_text(f"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType>
    <LangVersion>14.0</LangVersion><ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks><Nullable>enable</Nullable>
    {properties}
  </PropertyGroup>
  <ItemGroup>{items}</ItemGroup>
</Project>
""")


def package_references(versions):
    return "".join(f'<PackageReference Include="{name}" Version="[{version}]" />'
                   for name, version in versions.items())


def assets(path, expected):
    libraries = json.loads((path / "obj/project.assets.json").read_text())["libraries"]
    resolved = dict(item.split("/", 1) for item in libraries if item.startswith("SharpPack"))
    if any(item["type"] != "package" for item in libraries.values()) or resolved != expected:
        raise RuntimeError(f"{path.name} resolved unexpected dependencies: {libraries.keys()}")


def consumer(path, feed, references, expected, env, architecture, *, aot=False, mismatch=False):
    project(path, package_references(references),
            "<DefineConstants>STREAMING</DefineConstants>" if STREAMING in references else "")
    for name in ("Model.cs", "Program.cs"):
        shutil.copyfile(HERE / name, path / name)
    cache = path / "packages"
    local_env = dict(env, NUGET_PACKAGES=str(cache))
    config = nuget_config(path / "NuGet.Config", feed, network=aot)
    if aot:
        output = path / "native"
        log = run(["dotnet", "publish", "-c", "Release", "-p:PublishAot=true",
                   "--configfile", str(config), "-o", str(output)], path, local_env)
        for warning in sorted({line for line in log.splitlines() if re.search(r"\bwarning IL\d+", line)}):
            print(f"{path.name}: {warning}", flush=True)
        assets(path, expected)
        executable = output / ("Consumer.exe" if os.name == "nt" else "Consumer")
        run([str(executable), architecture], path, local_env)
    else:
        run(["dotnet", "restore", "--configfile", str(config)], path, local_env)
        assets(path, expected)
        if mismatch:
            log = run(["dotnet", "build", "-c", "Release", "--no-restore", "--nologo"],
                      path, local_env, expected_success=False)
            diagnostics = {line.split(" : ", 1)[0] for line in log.splitlines()
                           if "error SHARPPACK043" in line}
            if (len(diagnostics) != 1 or "Model.cs(" not in next(iter(diagnostics))
                    or "ABI 'missing'" not in log or "[1, 1]" not in log
                    or re.search(r"\b(?:error|warning) CS\d+\b", log)):
                raise RuntimeError(f"Expected a single ABI error on the model:\n{log}")
        else:
            run(["dotnet", "run", "-c", "Release", "--no-restore", "--", architecture], path, local_env)
    print(f"{path.name}: {'SHARPPACK043 as expected' if mismatch else 'packaged round trip passed'}", flush=True)
    return cache


def pack(name, version, feed, env, *, source=ROOT, existing_build=False):
    options = ["--no-build"] if existing_build else []
    run(["dotnet", "pack", str(source / "src" / name / f"{name}.csproj"),
         "-c", "Release", f"-p:Version={version}", "-o", str(feed), *options], source, env)


def previous_packages(work, feed, env):
    source = work / "previous"
    archive = work / "previous.tar"
    with archive.open("wb") as stream:
        subprocess.run(["git", "archive", f"v{PREVIOUS}"], cwd=ROOT, stdout=stream, check=True)
    with tarfile.open(archive) as tree:
        tree.extractall(source, filter="data")
    for name in (CORE, GENERATOR):
        pack(name, PREVIOUS, feed, env, source=source)


def verify_roslyn_hosts(work, current_cache, previous_cache, version, env):
    core_library = f"lib/net10.0/{CORE}.dll"
    current_core = current_cache / CORE.lower() / version / core_library
    previous_core = previous_cache / CORE.lower() / PREVIOUS / core_library
    generator = current_cache / GENERATOR.lower() / version / f"analyzers/dotnet/cs/{GENERATOR}.dll"
    config = nuget_config(work / "Roslyn.NuGet.Config", network=True)
    sources = []
    for host in ROSLYN_HOSTS:
        path = work / f"roslyn-{host}"
        references = (
            package_references({"Microsoft.CodeAnalysis.CSharp": host})
            + f'<Reference Include="{GENERATOR}"><HintPath>{escape(str(generator))}</HintPath></Reference>'
        )
        project(path, references)
        shutil.copyfile(HERE / "RoslynHost.cs", path / "Program.cs")
        local_env = dict(env, NUGET_PACKAGES=str(path / "packages"))
        run(["dotnet", "restore", "--configfile", str(config)], path, local_env)
        run(["dotnet", "build", "-c", "Release", "--no-restore"], path, local_env)
        cmd = ["dotnet", str(path / "bin/Release/net10.0/Consumer.dll"), f"{host}.0"]
        output = path / "generated.txt"
        print(run([*cmd, str(current_core), str(HERE / "Model.cs"), str(output)], path, local_env).strip(), flush=True)
        print(run([*cmd, str(previous_core), str(HERE / "Model.cs"), "mismatch"], path, local_env).strip(), flush=True)
        sources.append(output.read_text())
    if sources[0] != sources[1]:
        raise RuntimeError("The isolated Roslyn hosts generated different source")
    print("Minimum and current Roslyn hosts generated identical source", flush=True)


def verify_lockstep(work, feed, streaming_package, version, env):
    # A resolver-only alternate package, from the same build, is never run or published.
    newer = f"{int(version.split('.')[0]) + 1}.0.0"
    alternate = work / "without-matching-core"
    alternate.mkdir()
    pack(CORE, newer, alternate, env, existing_build=True)
    for package in alternate.glob("*.nupkg"):
        shutil.copyfile(package, feed / package.name)
    shutil.copyfile(streaming_package, alternate / streaming_package.name)
    for name, source, versions, error in (
        ("lockstep-with-newer", feed, {STREAMING: version}, None),
        ("lockstep-missing-exact", alternate, {STREAMING: version}, "NU1102"),
        ("lockstep-direct-override", feed, {STREAMING: version, CORE: newer}, "NU1608"),
    ):
        path = work / name
        project(path, package_references(versions), "<WarningsAsErrors>NU1608</WarningsAsErrors>")
        config = nuget_config(path / "NuGet.Config", source)
        run(["dotnet", "restore", "--configfile", str(config)], path,
            dict(env, NUGET_PACKAGES=str(path / "packages")), expected_success=error is None)
        if error is None:
            assets(path, {STREAMING: version, CORE: version})
        else:
            diagnostics = json.loads((path / "obj/project.assets.json").read_text()).get("logs", [])
            if not any(item.get("code") == error and item.get("level") == "Error" for item in diagnostics):
                raise RuntimeError(f"Expected NuGet error {error}; found {diagnostics}")
        print(f"{name}: {'exact version resolved' if error is None else error + ' as expected'}", flush=True)


def verify_compatibility(work, packages, current_cache, version, env, architecture):
    mixed = work / "mixed-feed"
    mixed.mkdir()
    for name in (CORE, GENERATOR, STREAMING):
        shutil.copyfile(packages[name], mixed / packages[name].name)
    previous_packages(work, mixed, env)
    versions = {CORE: version, GENERATOR: PREVIOUS}
    consumer(work / "previous-generator", mixed, versions, versions, env, architecture)
    versions = {CORE: PREVIOUS, GENERATOR: version}
    previous_cache = consumer(work / "previous-runtime", mixed, versions, versions,
                              env, architecture, mismatch=True)
    verify_roslyn_hosts(work, current_cache, previous_cache, version, env)
    verify_lockstep(work, mixed, packages[STREAMING], version, env)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--feed", type=Path, required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--architecture", choices=["X64", "Arm64"], required=True)
    parser.add_argument("--compatibility", action="store_true",
                        help="Also pack the pinned predecessor and run host/version-pair checks")
    args = parser.parse_args()
    feed = args.feed.resolve()
    version = normalized_version(args.version)
    packages = inspect_feed(feed, version)
    with tempfile.TemporaryDirectory(prefix="sharppack-package-") as directory:
        work = Path(directory)
        env = os.environ.copy()
        sdk = run(["dotnet", "--version"], ROOT, env).strip()
        if not re.fullmatch(r"10\.0\.\d{3}", sdk):
            raise RuntimeError(f"Expected the repository .NET 10 SDK, got {sdk}")
        (work / "global.json").write_text(json.dumps({"sdk": {"version": sdk, "rollForward": "disable"}}))
        if sdk != run(["dotnet", "--version"], work, env).strip():
            raise RuntimeError("Package consumer SDK differs from producer SDK")
        print(f"Package consumers use .NET SDK {sdk}, C# 14, {args.architecture}", flush=True)

        runtime = {CORE: version, GENERATOR: version}
        for name, refs in (
            ("aggregate", {AGGREGATE: version}),
            ("split", runtime),
            ("streaming", {STREAMING: version, GENERATOR: version}),
        ):
            cache = consumer(work / name, feed, refs, runtime | refs, env, args.architecture)
            if name == "split":
                current_cache = cache
            if name != "streaming":
                consumer(work / f"{name}-aot", feed, refs, runtime | refs, env, args.architecture, aot=True)
        if args.compatibility:
            verify_compatibility(work, packages, current_cache, version, env, args.architecture)


if __name__ == "__main__":
    main()
