#!/usr/bin/env bash
set -euo pipefail

# Package the existing Release builds; the generator also bundles a private .NET tool.
version=$1
output=$2
dotnet build src/SharpPack.Tooling/SharpPack.Tooling.csproj -c Release -p:Version="$version"
for name in SharpPack.Core SharpPack.Generator SharpPack.Streaming SharpPack; do
    symbols=()
    case "$name" in
        SharpPack.Core|SharpPack.Streaming)
            symbols=(-p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg)
            ;;
    esac
    dotnet pack "src/$name/$name.csproj" -c Release --no-build -p:Version="$version" "${symbols[@]}" -o "$output"
done
