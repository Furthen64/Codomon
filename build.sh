#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

APP_VERSION="0.1.0"
BUILD_DATE="$(date -u +%Y-%m-%d)"

echo "=== Building Codomon ==="
echo "    Version:    ${APP_VERSION}"
echo "    Build date: ${BUILD_DATE}"

dotnet build Codomon.Desktop/Codomon.Desktop.csproj -c Release \
    -p:AppVersion="${APP_VERSION}" \
    -p:BuildDate="${BUILD_DATE}"

echo "=== Build complete ==="
