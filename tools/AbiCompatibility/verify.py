#!/usr/bin/env python3
"""Exercise actual nupkg Generator/Core pairs; never publish packages."""
import json
import os
from pathlib import Path
import re
import subprocess
import tarfile
import tempfile


ROOT = Path(__file__).resolve().parents[2]
PREVIOUS = "6666ada1d878da64b2f4d2c1107fbea30fad074f"
CURRENT_VERSION = "1.1.0-abi17.current"
PREVIOUS_VERSION = "1.1.0-abi17.previous"


def run(args, cwd, env, expected_success=True):
    result = subprocess.run(args, cwd=cwd, env=env, text=True,
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    if (result.returncode == 0) != expected_success:
        print(result.stdout, flush=True)
        raise RuntimeError(f"Unexpected exit {result.returncode}: {' '.join(map(str, args))}")
    return result.stdout


def main():
    with tempfile.TemporaryDirectory(prefix="sharppack-abi17-") as temp:
        work = Path(temp)
        feed = work / "feed"
        feed.mkdir()
        old = work / "previous"
        old.mkdir()
        archive = work / "previous.tar"
        with archive.open("wb") as stream:
            subprocess.run(["git", "archive", PREVIOUS], cwd=ROOT, stdout=stream, check=True)
        with tarfile.open(archive) as tree:
            tree.extractall(old, filter="data")
        env = dict(os.environ, NUGET_PACKAGES=str(work / "packages"))
        for source, version, projects in (
            (old, PREVIOUS_VERSION, ("SharpPack.Core", "SharpPack.Generator")),
            (ROOT, CURRENT_VERSION, ("SharpPack.Core", "SharpPack.Generator", "SharpPack")),
        ):
            for project in projects:
                run(["dotnet", "pack", str(source / "src" / project / f"{project}.csproj"),
                     "-c", "Release", f"-p:Version={version}", "-o", str(feed)], source, env)
        print("Packed current and pinned predecessor Core/Generator; no packages published", flush=True)

        for name, core, generator, aggregate, success in (
            ("current-split", CURRENT_VERSION, CURRENT_VERSION, False, True),
            ("current-aggregate", CURRENT_VERSION, CURRENT_VERSION, True, True),
            ("previous-generator", CURRENT_VERSION, PREVIOUS_VERSION, False, True),
            ("previous-runtime", PREVIOUS_VERSION, CURRENT_VERSION, False, False),
        ):
            directory = work / name
            directory.mkdir()
            references = (
                f'<PackageReference Include="SharpPack" Version="[{core}]" />'
                if aggregate else
                f'<PackageReference Include="SharpPack.Core" Version="[{core}]" />'
                f'<PackageReference Include="SharpPack.Generator" Version="[{generator}]" />'
            )
            (directory / "Consumer.csproj").write_text(f"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType>
    <LangVersion>14</LangVersion><Nullable>enable</Nullable><AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>{references}</ItemGroup>
</Project>
""")
            (directory / "NuGet.Config").write_text(f"""<configuration>
  <packageSources><clear /><add key="local" value="{feed}" /></packageSources>
</configuration>""")
            (directory / "Program.cs").write_text("""using SharpPack;
using System;
public static class Program
{
    public static void Main()
    {
        var value = new Model { Value = 42, Text = "ABI" };
        var bytes = SharpPackSerializer.Serialize(value);
        var copy = SharpPackSerializer.Deserialize<Model>(bytes);
        if (copy?.Value != 42 || copy.Text != "ABI") throw new Exception("Round trip failed");
        Console.WriteLine("ABI_CONSUMER_OK");
    }
}
[SharpPackable]
public partial class Model { public int Value { get; set; } public string? Text { get; set; } }
""")
            log = run(["dotnet", "build", "-c", "Release", "--nologo"], directory, env, success)
            assets = json.loads((directory / "obj/project.assets.json").read_text())
            packages = assets["libraries"]
            assert f"SharpPack.Core/{core}" in packages, (name, packages.keys())
            assert f"SharpPack.Generator/{generator}" in packages, (name, packages.keys())
            assert all(item["type"] != "project" for item in packages.values()), name
            if success:
                output = run(["dotnet", "run", "-c", "Release", "--no-build"], directory, env)
                assert "ABI_CONSUMER_OK" in output, (name, output)
                print(f"{name}: expected Core/Generator packages resolved; build and round trip passed", flush=True)
            else:
                assert "error SHARPPACK043" in log, log
                assert "ABI 'missing'" in log and "[1, 1]" in log, log
                assert not re.search(r"\b(?:error|warning) CS\d+\b", log), log
                # MSBuild may repeat the diagnostic in its summary. It must refer
                # to one source location, and not to any generated source.
                errors = {line.split(" : ", 1)[0] for line in log.splitlines()
                          if "error SHARPPACK043" in line}
                assert len(errors) == 1 and "Program.cs(" in next(iter(errors)), log
                print(f"{name}: SHARPPACK043 at the model; no unrelated C# diagnostics", flush=True)


if __name__ == "__main__":
    main()
