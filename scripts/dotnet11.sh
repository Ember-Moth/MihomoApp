#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_root="$repo_root/.dotnet"

if [[ ! -x "$dotnet_root/dotnet" ]]; then
  echo ".NET 11 SDK not found at $dotnet_root/dotnet" >&2
  exit 1
fi

export DOTNET_ROOT="$dotnet_root"
export PATH="$dotnet_root:$PATH"

exec "$dotnet_root/dotnet" "$@"
