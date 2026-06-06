#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
unsigned_ipa="${1:-$repo_root/artifacts/ios/Aureline-ios-arm64-unsigned.ipa}"
signed_ipa="${2:-$repo_root/artifacts/ios/Aureline-ios-arm64-signed.ipa}"

p12="${P12:-}"
p12_password="${P12_PASSWORD:-}"
app_provision="${APP_PROVISION:-}"
extension_provision="${EXTENSION_PROVISION:-}"
app_entitlements="${APP_ENTITLEMENTS:-}"
extension_entitlements="${EXTENSION_ENTITLEMENTS:-}"

if ! command -v zsign >/dev/null 2>&1; then
  echo "zsign is required" >&2
  exit 1
fi

if [[ -z "$p12" || -z "$app_provision" || -z "$extension_provision" ]]; then
  cat >&2 <<'EOF'
Required environment variables:
  P12=/path/to/development_or_distribution.p12
  P12_PASSWORD=optional-password
  APP_PROVISION=/path/to/com.embermoth.aureline.mobileprovision
  EXTENSION_PROVISION=/path/to/com.embermoth.aureline.PacketTunnel.mobileprovision

Optional:
  APP_ENTITLEMENTS=/path/to/app-entitlements.plist
  EXTENSION_ENTITLEMENTS=/path/to/packet-tunnel-entitlements.plist
EOF
  exit 1
fi

for path in "$unsigned_ipa" "$p12" "$app_provision" "$extension_provision"; do
  if [[ ! -f "$path" ]]; then
    echo "File not found: $path" >&2
    exit 1
  fi
done

tmp_dir="$(mktemp -d)"
trap 'rm -rf "$tmp_dir"' EXIT

unzip -q "$unsigned_ipa" -d "$tmp_dir"

app_dir="$(find "$tmp_dir/Payload" -maxdepth 1 -type d -name '*.app' | head -n 1)"
if [[ -z "$app_dir" ]]; then
  echo "No .app found in unsigned IPA: $unsigned_ipa" >&2
  exit 1
fi

extension_dir="$(find "$app_dir" -type d -name '*.appex' | head -n 1)"
if [[ -z "$extension_dir" ]]; then
  echo "No PacketTunnel .appex found in app bundle: $app_dir" >&2
  exit 1
fi

zsign_common=(-f -k "$p12")
if [[ -n "$p12_password" ]]; then
  zsign_common+=(-p "$p12_password")
fi

extension_args=("${zsign_common[@]}" -m "$extension_provision")
if [[ -n "$extension_entitlements" ]]; then
  extension_args+=(-e "$extension_entitlements")
fi
extension_args+=("$extension_dir")

app_args=("${zsign_common[@]}" -m "$app_provision")
if [[ -n "$app_entitlements" ]]; then
  app_args+=(-e "$app_entitlements")
fi
app_args+=(-o "$signed_ipa" "$app_dir")

mkdir -p "$(dirname "$signed_ipa")"

zsign "${extension_args[@]}"
zsign "${app_args[@]}"

echo "Signed IPA: $signed_ipa"
