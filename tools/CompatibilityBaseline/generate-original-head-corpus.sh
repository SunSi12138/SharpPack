#!/usr/bin/env bash
set -euo pipefail

readonly source_commit="85ab9ad76c380aca48c09ff3a0ad955ee5a2902b"
readonly script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly repository_root="$(cd "${script_dir}/../.." && pwd)"
readonly tracked_corpus="${repository_root}/tests/MemoryPack.Tests/Compatibility/original-head-golden.json"
readonly baseline_root="$(mktemp -d /tmp/memorypack-golden-XXXXXXXX)"
update_corpus=false

if [[ $# -gt 1 ]] || [[ $# -eq 1 && "$1" != "--update" ]]
then
    echo "usage: $0 [--update]" >&2
    exit 2
fi
if [[ $# -eq 1 ]]
then
    update_corpus=true
fi

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
readonly generated_corpus="${baseline_root}/generated-original-head-golden.json"

if ! dotnet build "${fixture_project}" --configuration Release > "${build_log}" 2>&1
then
    cat "${build_log}" >&2
    exit 1
fi

dotnet "${baseline_root}/baseline/GoldenCorpus/bin/Release/net10.0/GoldenCorpus.dll" \
    > "${generated_corpus}"

jq -e \
    --arg commit "${source_commit}" \
    '.SourceCommit == $commit
     and .WellKnownFormatterCount == 117
     and .GenericShapeCount == 68
     and (.Entries | length) == 203' \
    "${generated_corpus}" \
    > /dev/null

if [[ "${update_corpus}" == true ]]
then
    cp "${generated_corpus}" "${tracked_corpus}"
else
    readonly generated_deterministic="${baseline_root}/generated-deterministic.json"
    readonly tracked_deterministic="${baseline_root}/tracked-deterministic.json"
    jq -S \
        '{SourceCommit, WellKnownFormatterCount, GenericShapeCount,
          Entries: [.Entries[] | select(.Deterministic)]}' \
        "${generated_corpus}" > "${generated_deterministic}"
    jq -S \
        '{SourceCommit, WellKnownFormatterCount, GenericShapeCount,
          Entries: [.Entries[] | select(.Deterministic)]}' \
        "${tracked_corpus}" > "${tracked_deterministic}"
    diff -u "${tracked_deterministic}" "${generated_deterministic}"
fi

dotnet run \
    --project "${current_project}" \
    --configuration Release \
    -- "${tracked_corpus}" \
    > "${current_payloads}"

dotnet "${baseline_root}/baseline/GoldenCorpus/bin/Release/net10.0/GoldenCorpus.dll" \
    --verify-current "${current_payloads}" \
    >&2
