#!/usr/bin/env bash
set -euo pipefail

echo "=== Codomon Requirements Check ==="

SDK_PACKAGE="dotnet-sdk-8.0"

confirm_install() {
  read -r -p "Would you like checkreq.sh to install the required .NET SDK now? [y/N] " answer
  case "$answer" in
    [yY] | [yY][eE][sS]) return 0 ;;
    *) return 1 ;;
  esac
}

require_sudo() {
  if ! command -v sudo >/dev/null 2>&1; then
    echo "ERROR: 'sudo' is required for automatic setup but is not available."
    return 1
  fi

  echo "Requesting sudo access..."
  if ! sudo -v; then
    echo "ERROR: Failed to acquire sudo privileges."
    return 1
  fi
}

install_microsoft_packages_repo() {
  local ubuntu_version="$1"
  local package_file="/tmp/packages-microsoft-prod.deb"

  echo "Installing Microsoft package repository..."
  wget "https://packages.microsoft.com/config/ubuntu/${ubuntu_version}/packages-microsoft-prod.deb" -O "$package_file" || return 1
  sudo dpkg -i "$package_file" || return 1
  rm -f "$package_file"
}

setup_dotnet_ubuntu_24() {
  local ubuntu_version="$1"
  install_microsoft_packages_repo "$ubuntu_version" || return 1
  echo "Installing ${SDK_PACKAGE}..."
  sudo apt-get update || return 1
  sudo apt-get install -y "$SDK_PACKAGE" || return 1
}

setup_dotnet_ubuntu_26() {
  local ubuntu_version="$1"
  install_microsoft_packages_repo "$ubuntu_version" || return 1
  echo "Installing software-properties-common..."
  sudo apt-get update || return 1
  sudo apt-get install -y software-properties-common || return 1
  echo "Adding .NET backports PPA for Ubuntu 26..."
  echo "Heads-up: this step prints a long repository description and may ask you to press ENTER. This is normal."
  sudo add-apt-repository ppa:dotnet/backports || return 1
  echo "Installing ${SDK_PACKAGE}..."
  sudo apt-get update || return 1
  sudo apt-get install -y "$SDK_PACKAGE" || return 1
}

verify_dotnet() {
  local dotnet_version major

  echo "Verifying dotnet installation..."
  if ! command -v dotnet >/dev/null 2>&1; then
    echo "ERROR: dotnet is still not available in PATH."
    return 1
  fi

  if ! dotnet_version=$(dotnet --version 2>/dev/null); then
    echo "ERROR: dotnet is installed but 'dotnet --version' failed."
    return 1
  fi
  echo "dotnet version: $dotnet_version"

  major="${dotnet_version%%.*}"
  if [[ ! "$major" =~ ^[0-9]+$ ]]; then
    echo "ERROR: Unable to parse dotnet version '$dotnet_version'."
    return 1
  fi

  if [ "$major" -lt 8 ]; then
    echo "ERROR: .NET 8 or higher is required (found $dotnet_version)."
    return 1
  fi

  return 0
}

if command -v dotnet >/dev/null 2>&1; then
  if ! verify_dotnet; then
    exit 1
  fi
  echo "OK: All requirements met."
  exit 0
fi

echo "dotnet is not installed or not in PATH."

if [ ! -f /etc/os-release ]; then
  echo "ERROR: Unsupported platform. Install .NET 8 SDK manually from https://dotnet.microsoft.com/download and re-run this script."
  exit 1
fi

# shellcheck disable=SC1091
. /etc/os-release

os_id="${ID:-}"
os_like="${ID_LIKE:-}"
ubuntu_version="${VERSION_ID:-}"

if [[ "$os_id" != "ubuntu" && "$os_like" != *ubuntu* ]]; then
  echo "ERROR: Unsupported platform '$os_id'. Install .NET 8 SDK manually from https://dotnet.microsoft.com/download and re-run this script."
  exit 1
fi

if [ -z "$ubuntu_version" ]; then
  echo "ERROR: Unable to determine Ubuntu version from /etc/os-release."
  exit 1
fi

ubuntu_major="${ubuntu_version%%.*}"
echo "Detected Ubuntu ${ubuntu_version}"

case "$ubuntu_major" in
  24 | 26) ;;
  *)
    echo "ERROR: Ubuntu ${ubuntu_version} is not currently supported by automatic setup."
    echo "Please install .NET 8 SDK manually from https://dotnet.microsoft.com/download and re-run this script."
    exit 1
    ;;
esac

if ! confirm_install; then
  echo "Installation cancelled. Install .NET 8 SDK manually and re-run this script."
  exit 1
fi

if ! require_sudo; then
  echo "Automatic setup cannot continue without sudo access."
  exit 1
fi

if [ "$ubuntu_major" = "24" ]; then
  echo "Using Ubuntu 24 setup path."
  if ! setup_dotnet_ubuntu_24 "$ubuntu_version"; then
    echo "ERROR: Automatic installation failed during Ubuntu 24 setup."
    echo "Please install .NET 8 SDK manually from https://dotnet.microsoft.com/download and re-run this script."
    exit 1
  fi
else
  echo "Using Ubuntu 26 setup path."
  if ! setup_dotnet_ubuntu_26 "$ubuntu_version"; then
    echo "ERROR: Automatic installation failed during Ubuntu 26 setup."
    echo "Please install .NET 8 SDK manually from https://dotnet.microsoft.com/download and re-run this script."
    exit 1
  fi
fi

if ! verify_dotnet; then
  echo "ERROR: .NET SDK setup appears incomplete."
  echo "Please inspect the messages above, complete the installation manually if needed, and re-run this script."
  exit 1
fi

echo "OK: All requirements met."
