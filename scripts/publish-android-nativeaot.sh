#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [[ "$#" -gt 0 ]]; then
  runtime_ids=("$@")
else
  runtime_ids=(android-arm64 android-x64)
fi

for runtime_id in "${runtime_ids[@]}"; do
  "$repo_root/scripts/dotnet11.sh" publish "$repo_root/Mihomo.Android/Mihomo.Android.csproj" \
    -c Release \
    -f net11.0-android \
    -r "$runtime_id" \
    -p:UseSharedCompilation=false \
    -nr:false
done
