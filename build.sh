#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "=== Building Codomon ==="
dotnet build Codomon.Desktop/Codomon.Desktop.csproj -c Release
echo "=== Build complete ==="
