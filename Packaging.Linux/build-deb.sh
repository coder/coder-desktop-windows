#!/usr/bin/env bash
set -euo pipefail

# Backwards-compatible wrapper around the multi-format release packaging script.
# Usage: ./build-deb.sh [amd64|arm64]

ARCH="${1:-amd64}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"

FORMATS=deb OUT_DIR="$ROOT_DIR" "$SCRIPT_DIR/build-release-packages.sh" "$ARCH"
