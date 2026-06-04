#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
dotnet_root="$repo_root/.dotnet"
nuget_cache="$repo_root/.tmp/nuget"
sdk_feature_band="11.0.100-preview.4"

if [[ ! -x "$dotnet_root/dotnet" ]]; then
  echo "Missing project-local .NET SDK: $dotnet_root/dotnet" >&2
  exit 1
fi

mkdir -p "$nuget_cache"

if [[ "${MIHOMO_ANDROID_WORKLOAD_MODE:-official}" == "official" ]]; then
  echo "Installing Android workload through dotnet workload install."
  if timeout 30m "$dotnet_root/dotnet" workload install android --skip-manifest-update --verbosity minimal; then
    "$dotnet_root/dotnet" workload list
    exit 0
  fi

  echo "Official Android workload install failed; falling back to minimal pack installation." >&2
fi

download_package() {
  local package_id="$1"
  local version="$2"
  local lower_id
  lower_id="$(printf '%s' "$package_id" | tr '[:upper:]' '[:lower:]')"
  local package_file="$nuget_cache/$lower_id.$version.nupkg"

  if [[ -s "$package_file" ]]; then
    return
  fi

  local url="https://api.nuget.org/v3-flatcontainer/$lower_id/$version/$lower_id.$version.nupkg"
  echo "Downloading $package_id $version"
  curl -fL --retry 10 --retry-delay 2 --retry-all-errors -o "$package_file" "$url"
}

install_pack() {
  local package_id="$1"
  local version="$2"
  local lower_id
  lower_id="$(printf '%s' "$package_id" | tr '[:upper:]' '[:lower:]')"
  local package_file="$nuget_cache/$lower_id.$version.nupkg"
  local pack_dir="$dotnet_root/packs/$package_id/$version"

  download_package "$package_id" "$version"

  if [[ ! -d "$pack_dir" || ! -f "$pack_dir/$package_id.nuspec" ]]; then
    echo "Installing $package_id $version"
    rm -rf "$pack_dir"
    mkdir -p "$pack_dir"
    unzip -q -o "$package_file" -d "$pack_dir"
  fi

  mkdir -p "$dotnet_root/metadata/workloads/InstalledPacks/v1/$package_id/$version"
  : > "$dotnet_root/metadata/workloads/InstalledPacks/v1/$package_id/$version/$sdk_feature_band"
}

# Minimal net11 Android workload subset for linux-x64 hosts.
# This intentionally skips net10 compatibility packs from the official android workload.
packages=(
  "Microsoft.Android.Sdk.Linux|36.99.0-preview.4.137"
  "Microsoft.Android.Ref.36.1|36.99.0-preview.4.137"
  "Microsoft.Android.Runtime.36.1.android|36.99.0-preview.4.137"
  "Microsoft.Android.Runtime.Mono.36.1.android-arm|36.99.0-preview.4.137"
  "Microsoft.Android.Runtime.Mono.36.1.android-arm64|36.99.0-preview.4.137"
  "Microsoft.Android.Runtime.Mono.36.1.android-x64|36.99.0-preview.4.137"
  "Microsoft.Android.Runtime.Mono.36.1.android-x86|36.99.0-preview.4.137"
  "Microsoft.Android.Runtime.CoreCLR.36.1.android-arm64|36.99.0-preview.4.137"
  "Microsoft.Android.Runtime.CoreCLR.36.1.android-x64|36.99.0-preview.4.137"
  "Microsoft.Android.Runtime.NativeAOT.36.1.android-arm64|36.99.0-preview.4.137"
  "Microsoft.Android.Runtime.NativeAOT.36.1.android-x64|36.99.0-preview.4.137"
  "Microsoft.Android.Templates|36.99.0-preview.4.137"
  "Microsoft.NET.Runtime.MonoAOTCompiler.Task|11.0.0-preview.4.26230.115"
  "Microsoft.NET.Runtime.MonoTargets.Sdk|11.0.0-preview.4.26230.115"
  "Microsoft.NETCore.App.Runtime.Mono.android-arm|11.0.0-preview.4.26230.115"
  "Microsoft.NETCore.App.Runtime.Mono.android-arm64|11.0.0-preview.4.26230.115"
  "Microsoft.NETCore.App.Runtime.Mono.android-x64|11.0.0-preview.4.26230.115"
  "Microsoft.NETCore.App.Runtime.Mono.android-x86|11.0.0-preview.4.26230.115"
  "Microsoft.NETCore.App.Runtime.AOT.linux-x64.Cross.android-arm|11.0.0-preview.4.26230.115"
  "Microsoft.NETCore.App.Runtime.AOT.linux-x64.Cross.android-arm64|11.0.0-preview.4.26230.115"
  "Microsoft.NETCore.App.Runtime.AOT.linux-x64.Cross.android-x64|11.0.0-preview.4.26230.115"
  "Microsoft.NETCore.App.Runtime.AOT.linux-x64.Cross.android-x86|11.0.0-preview.4.26230.115"
)

for entry in "${packages[@]}"; do
  IFS='|' read -r package_id version <<< "$entry"
  install_pack "$package_id" "$version"
done

android_sdk_dir="$dotnet_root/packs/Microsoft.Android.Sdk.Linux/36.99.0-preview.4.137"
if [[ -d "$android_sdk_dir/tools" ]]; then
  find "$android_sdk_dir/tools" -type f \( -path '*/bin/*' -o -name '*.sh' \) -exec chmod +x {} +
fi
if [[ -d "$android_sdk_dir/tools/Linux" ]]; then
  find "$android_sdk_dir/tools/Linux" -type f -exec chmod +x {} +
fi

mkdir -p "$dotnet_root/metadata/workloads/$sdk_feature_band/InstalledWorkloads"
: > "$dotnet_root/metadata/workloads/$sdk_feature_band/InstalledWorkloads/android"

echo "Installed minimal net11 Android workload marker."
