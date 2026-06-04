#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
framework="net11.0-android"
configuration="Release"
package_name="com.embermoth.mihomo"
tmp_dir="$(mktemp -d)"
trap 'rm -rf "$tmp_dir"' EXIT

if [[ "$#" -gt 0 ]]; then
  runtime_ids=("$@")
else
  runtime_ids=(android-arm64 android-x64)
fi

for runtime_id in "${runtime_ids[@]}"; do
  output_root="$repo_root/Mihomo.Android/bin/$configuration/$framework/$runtime_id"
  intermediate_root="$repo_root/Mihomo.Android/obj/$configuration/$framework/$runtime_id"
  apk_path="$output_root/publish/$package_name-Signed.apk"

  rm -rf "$output_root" "$intermediate_root"

  "$repo_root/scripts/dotnet11.sh" publish "$repo_root/Mihomo.Android/Mihomo.Android.csproj" \
    -c "$configuration" \
    -f "$framework" \
    -r "$runtime_id" \
    -p:UseSharedCompilation=false \
    -nr:false

  if [[ ! -f "$apk_path" ]]; then
    echo "Missing published APK: $apk_path" >&2
    exit 1
  fi

  classes_dex="$tmp_dir/$runtime_id-classes.dex"
  unzip -p "$apk_path" classes.dex > "$classes_dex"
  if ! grep -a -q 'NativeAotRuntimeProvider' "$classes_dex"; then
    echo "Published APK is missing NativeAotRuntimeProvider in classes.dex: $apk_path" >&2
    exit 1
  fi
done
