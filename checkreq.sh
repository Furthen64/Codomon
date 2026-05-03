#!/usr/bin/env bash
set -euo pipefail

echo "=== Codomon Requirements Check ==="

if ! command -v dotnet &>/dev/null; then
  echo "ERROR: dotnet is not installed or not in PATH"
  echo ""
  # Detect Ubuntu and provide install guidance
  if [ -f /etc/os-release ]; then
    . /etc/os-release
    if [ "$ID" = "ubuntu" ] || [ "$ID_LIKE" = "ubuntu" ]; then
      UBUNTU_VERSION="$VERSION_ID"
      echo "Ubuntu $UBUNTU_VERSION detected. To install the .NET 8 SDK, run:"
      echo ""
      echo "  # Add the Microsoft package repository"
      echo "  wget https://packages.microsoft.com/config/ubuntu/${UBUNTU_VERSION}/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb"
      echo "  sudo dpkg -i /tmp/packages-microsoft-prod.deb"
      echo "  rm /tmp/packages-microsoft-prod.deb"
      echo ""
      echo "  # Install the .NET 8 SDK"
      echo "  sudo apt-get update"
      echo "  sudo apt-get install -y dotnet-sdk-8.0"
      echo ""
      echo "After installation, open a new terminal and re-run this script."
    else
      echo "Visit https://dotnet.microsoft.com/download to install .NET 8 SDK for your platform."
    fi
  else
    echo "Visit https://dotnet.microsoft.com/download to install .NET 8 SDK for your platform."
  fi
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
