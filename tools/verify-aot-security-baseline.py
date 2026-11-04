#!/usr/bin/env python3
"""Check the restored AOT compiler graph; never download or execute packages."""
import json
from pathlib import Path
import re
import sys


def verify(assets):
    compilers = []
    for identity in assets.get("libraries", {}):
        name, separator, version = identity.rpartition("/")
        if separator and (name == "Microsoft.DotNet.ILCompiler" or
                          name.endswith(".Microsoft.DotNet.ILCompiler")):
            match = re.fullmatch(r"10\.0\.(\d+)", version)
            if match is None or int(match.group(1)) < 14:
                raise ValueError(f"Unpatched or unsupported AOT compiler: {identity}")
            compilers.append(identity)
    if not compilers:
        raise ValueError("No resolved Microsoft.DotNet.ILCompiler in assets; AOT baseline is unverified")
    return sorted(compilers)


def self_test():
    for identity in ("Microsoft.DotNet.ILCompiler/10.0.14",
                     "runtime.linux-arm64.Microsoft.DotNet.ILCompiler/10.0.15"):
        assert verify({"libraries": {identity: {}}}) == [identity]
    for libraries in ({}, {"Microsoft.DotNet.ILCompiler/10.0.13": {}},
                      {"Microsoft.DotNet.ILCompiler/10.0.14-preview.1": {}},
                      {"Microsoft.DotNet.ILCompiler/11.0.0": {}},
                      {"Microsoft.DotNet.ILCompiler/10.0.14": {},
                       "runtime.linux-x64.Microsoft.DotNet.ILCompiler/10.0.13": {}}):
        try:
            verify({"libraries": libraries})
        except ValueError:
            pass
        else:
            raise AssertionError(f"Expected rejection: {libraries}")
    print("AOT baseline parser positive/negative checks passed.")


if __name__ == "__main__":
    try:
        if len(sys.argv) == 2 and sys.argv[1] == "--self-test":
            self_test()
        elif len(sys.argv) == 2:
            for identity in verify(json.loads(Path(sys.argv[1]).read_text())):
                print(f"Verified AOT compiler: {identity}")
        else:
            raise ValueError("usage: verify-aot-security-baseline.py <project.assets.json|--self-test>")
    except (ValueError, OSError) as error:
        sys.exit(str(error))
