#!/usr/bin/env bash
set -euo pipefail

# Build an AUR source archive containing PKGBUILD + install/unit/desktop assets.
#
# Usage:
#   VERSION=1.2.3 ./build-aur-source.sh

VERSION="${VERSION:-0.1.0}"
OUT_DIR="${OUT_DIR:-$(pwd)/dist}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORK_DIR="$(mktemp -d)"
AUR_DIR="$WORK_DIR/coder-desktop"

cleanup() {
  rm -rf "$WORK_DIR"
}
trap cleanup EXIT

mkdir -p "$OUT_DIR"
mkdir -p "$AUR_DIR"

sed -E "s/^pkgver=.*/pkgver=${VERSION}/" "$SCRIPT_DIR/PKGBUILD" > "$AUR_DIR/PKGBUILD"
cp "$SCRIPT_DIR/coder-desktop.install" "$AUR_DIR/"
cp "$SCRIPT_DIR/coder-desktop.service" "$AUR_DIR/"
cp "$SCRIPT_DIR/coder-desktop.desktop" "$AUR_DIR/"
cp "$SCRIPT_DIR/README.md" "$AUR_DIR/README.md"

ARCHIVE_NAME="coder-desktop_${VERSION}_aur.tar.gz"
tar -C "$WORK_DIR" -czf "$OUT_DIR/$ARCHIVE_NAME" "coder-desktop"

echo "AUR source archive created: $OUT_DIR/$ARCHIVE_NAME"
