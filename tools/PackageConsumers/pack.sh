#!/usr/bin/env bash
set -euo pipefail

# Package the existing Release builds; callers build all four projects at this version.
version=$1
output=$2
for name in SharpPack.Core SharpPack.Generator SharpPack.Streaming SharpPack; do
    symbols=()
    case "$name" in
        SharpPack.Core|SharpPack.Streaming)
            symbols=(-p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg)
            ;;
    esac
    dotnet pack "src/$name/$name.csproj" -c Release --no-build -p:Version="$version" "${symbols[@]}" -o "$output"
done
