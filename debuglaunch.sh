#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "=== Launching Codomon with startup diagnostics ==="
dotnet run --project Codomon.Desktop/Codomon.Desktop.csproj -- --debug-launch "$@"
