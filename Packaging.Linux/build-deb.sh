#!/bin/bash
set -euo pipefail

# Build a .deb package for Coder Desktop
# Usage: ./build-deb.sh [amd64|arm64]

ARCH="${1:-amd64}"
VERSION="${VERSION:-0.1.0}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
BUILD_DIR="$(mktemp -d)"
PKG_DIR="$BUILD_DIR/coder-desktop_${VERSION}_${ARCH}"

echo "Building Coder Desktop .deb package v${VERSION} for ${ARCH}..."

# Map architecture names
case "$ARCH" in
    amd64) RID="linux-x64" ;;
    arm64) RID="linux-arm64" ;;
    *) echo "Unsupported architecture: $ARCH"; exit 1 ;;
esac

# Build the service
echo "Publishing Vpn.Service..."
dotnet publish "$ROOT_DIR/Vpn.Service" -r "$RID" -c Release --self-contained -o "$PKG_DIR/usr/lib/coder-desktop/service"

# Create directory structure
mkdir -p "$PKG_DIR/usr/bin"
mkdir -p "$PKG_DIR/usr/share/applications"
mkdir -p "$PKG_DIR/etc/systemd/system"
mkdir -p "$PKG_DIR/etc/coder-desktop"
mkdir -p "$PKG_DIR/DEBIAN"

# Symlinks
ln -sf "/usr/lib/coder-desktop/service/CoderVpnService" "$PKG_DIR/usr/bin/coder-vpn-service"

# Copy packaging files
cp "$SCRIPT_DIR/coder-desktop.service" "$PKG_DIR/etc/systemd/system/"
cp "$SCRIPT_DIR/coder-desktop.desktop" "$PKG_DIR/usr/share/applications/"

# Default config
cat > "$PKG_DIR/etc/coder-desktop/config.json" <<'EOF'
{
  "Manager": {
    "ServiceRpcSocketPath": "/run/coder-desktop/vpn.sock",
    "TunnelBinaryPath": "/usr/lib/coder-desktop/coder-vpn",
    "TunnelBinarySignatureSigner": "",
    "TunnelBinaryAllowVersionMismatch": false
  }
}
EOF

# Create DEBIAN control file
cat > "$PKG_DIR/DEBIAN/control" <<EOF
Package: coder-desktop
Version: ${VERSION}
Architecture: ${ARCH}
Maintainer: Coder Technologies Inc. <support@coder.com>
Description: Coder Desktop - Connect to Coder workspaces
 Provides a VPN tunnel to Coder workspaces with a system
 service for managing connections.
Depends: libnotify-bin, libsecret-tools
Recommends: freerdp2-x11 | remmina
Section: net
Priority: optional
EOF

# Install scripts
cp "$SCRIPT_DIR/postinst.sh" "$PKG_DIR/DEBIAN/postinst"
cp "$SCRIPT_DIR/prerm.sh" "$PKG_DIR/DEBIAN/prerm"
chmod 755 "$PKG_DIR/DEBIAN/postinst" "$PKG_DIR/DEBIAN/prerm"

# Build .deb
dpkg-deb --build "$PKG_DIR"
mv "$PKG_DIR.deb" "$ROOT_DIR/coder-desktop_${VERSION}_${ARCH}.deb"

echo "Package built: coder-desktop_${VERSION}_${ARCH}.deb"

# Cleanup
rm -rf "$BUILD_DIR"
