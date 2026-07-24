#!/usr/bin/env bash
set -euo pipefail

readonly source_commit="85ab9ad76c380aca48c09ff3a0ad955ee5a2902b"
readonly script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly repository_root="$(cd "${script_dir}/../.." && pwd)"
readonly output_path="${1:-${repository_root}/tests/MemoryPack.Tests/Compatibility/original-head-golden.json}"
readonly baseline_root="$(mktemp -d /tmp/memorypack-golden-XXXXXXXX)"

cleanup() {
    rm -rf "${baseline_root}"
}
trap cleanup EXIT

git -C "${repository_root}" cat-file -e "${source_commit}^{commit}"
git -C "${repository_root}" archive "${source_commit}" | tar -x -C "${baseline_root}"

mkdir -p "${baseline_root}/baseline"
cp -R "${script_dir}/fixture" "${baseline_root}/baseline/GoldenCorpus"

readonly fixture_project="${baseline_root}/baseline/GoldenCorpus/GoldenCorpus.csproj"
readonly build_log="${baseline_root}/baseline-build.log"
readonly current_project="${script_dir}/current/CurrentPayloads.csproj"
readonly current_payloads="${baseline_root}/current-writer-payloads.json"

if ! dotnet build "${fixture_project}" --configuration Release > "${build_log}" 2>&1
then
    cat "${build_log}" >&2
    exit 1
fi

dotnet "${baseline_root}/baseline/GoldenCorpus/bin/Release/net10.0/GoldenCorpus.dll" \
    > "${output_path}"

jq -e \
    --arg commit "${source_commit}" \
    '.SourceCommit == $commit
     and .WellKnownFormatterCount == 117
     and .GenericShapeCount == 68
     and (.Entries | length) == 203' \
    "${output_path}" \
    > /dev/null

dotnet run \
    --project "${current_project}" \
    --configuration Release \
    -- "${output_path}" \
    > "${current_payloads}"

dotnet "${baseline_root}/baseline/GoldenCorpus/bin/Release/net10.0/GoldenCorpus.dll" \
    --verify-current "${current_payloads}" \
    >&2
