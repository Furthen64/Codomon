#!/usr/bin/env bash
set -euo pipefail

echo "=== Codomon Requirements Check ==="

if ! command -v dotnet &>/dev/null; then
  echo "ERROR: dotnet is not installed or not in PATH"
  exit 1
fi

DOTNET_VERSION=$(dotnet --version)
echo "dotnet version: $DOTNET_VERSION"

MAJOR=$(echo "$DOTNET_VERSION" | cut -d. -f1)
if [ "$MAJOR" -lt 8 ]; then
  echo "ERROR: .NET 8 or higher is required (found $DOTNET_VERSION)"
  exit 1
fi

echo "OK: All requirements met."
