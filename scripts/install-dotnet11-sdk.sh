#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_root="$repo_root/.dotnet"
version="${DOTNET_VERSION:-11.0.100-preview.4.26230.115}"
installer="$repo_root/.tmp/dotnet-install.sh"

mkdir -p "$repo_root/.tmp" "$dotnet_root"

if [[ -x "$dotnet_root/dotnet" ]] && "$dotnet_root/dotnet" --version | grep -qx "$version"; then
  echo ".NET SDK $version already installed at $dotnet_root"
  exit 0
fi

curl -fsSL --retry 10 --retry-delay 2 --retry-all-errors \
  -o "$installer" \
  https://dot.net/v1/dotnet-install.sh

bash "$installer" \
  --version "$version" \
  --install-dir "$dotnet_root" \
  --no-path

"$dotnet_root/dotnet" --info
