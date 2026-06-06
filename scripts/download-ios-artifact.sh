#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
workflow="${WORKFLOW:-ios.yml}"
artifact_name="${ARTIFACT_NAME:-aureline-ios}"
output_dir="${2:-$repo_root/artifacts/ios}"
run_id="${1:-${GH_RUN_ID:-}}"

if ! command -v gh >/dev/null 2>&1; then
  echo "gh CLI is required" >&2
  exit 1
fi

if [[ -z "$run_id" ]]; then
  run_id="$(gh run list \
    --workflow "$workflow" \
    --status success \
    --limit 1 \
    --json databaseId \
    --jq '.[0].databaseId')"
fi

if [[ -z "$run_id" || "$run_id" == "null" ]]; then
  echo "No successful iOS workflow run found" >&2
  exit 1
fi

rm -rf "$output_dir"
mkdir -p "$output_dir"

gh run download "$run_id" \
  --name "$artifact_name" \
  --dir "$output_dir"

echo "Downloaded $artifact_name from run $run_id to $output_dir"
