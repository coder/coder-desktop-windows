#!/usr/bin/env bash
set -euo pipefail

# Build Linux release packages from a single staged filesystem layout.
#
# Usage:
#   VERSION=1.2.3 ./build-release-packages.sh amd64
#   VERSION=1.2.3 FORMATS=deb,rpm,tar ./build-release-packages.sh arm64
#
# Supported architectures:
#   - amd64
#   - arm64
#
# Supported formats (FORMATS comma-separated):
#   - deb
#   - rpm
#   - tar

ARCH="${1:-amd64}"
VERSION="${VERSION:-0.1.0}"
FORMATS="${FORMATS:-deb,rpm,tar}"
OUT_DIR="${OUT_DIR:-$(pwd)/dist}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
BUILD_DIR="$(mktemp -d)"
PKGROOT="$BUILD_DIR/pkgroot"
STAGE_DIR="$PKGROOT/coder-desktop"

cleanup() {
  rm -rf "$BUILD_DIR"
}
trap cleanup EXIT

has_format() {
  local needle="$1"
  [[ ",${FORMATS}," == *",${needle},"* ]]
}

case "$ARCH" in
  amd64)
    RID="linux-x64"
    DEB_ARCH="amd64"
    RPM_ARCH="x86_64"
    ;;
  arm64)
    RID="linux-arm64"
    DEB_ARCH="arm64"
    RPM_ARCH="aarch64"
    ;;
  *)
    echo "Unsupported architecture: $ARCH" >&2
    exit 1
    ;;
esac

mkdir -p "$OUT_DIR"

echo "[release-packaging] Building Coder Desktop v${VERSION} (${ARCH})"

echo "[release-packaging] Publishing App.Avalonia (${RID})"
dotnet publish "$ROOT_DIR/App.Avalonia" \
  -r "$RID" \
  -c Release \
  -o "$STAGE_DIR/usr/lib/coder-desktop/app"

echo "[release-packaging] Publishing Vpn.Service (${RID})"
dotnet publish "$ROOT_DIR/Vpn.Service" \
  -r "$RID" \
  -c Release \
  -o "$STAGE_DIR/usr/lib/coder-desktop/service"

mkdir -p "$STAGE_DIR/usr/bin"
mkdir -p "$STAGE_DIR/usr/share/applications"
mkdir -p "$STAGE_DIR/usr/share/pixmaps"
mkdir -p "$STAGE_DIR/usr/lib/systemd/system"
mkdir -p "$STAGE_DIR/etc/coder-desktop"

cat > "$STAGE_DIR/usr/bin/coder-desktop" <<'WRAPPER'
#!/usr/bin/env bash
exec "/usr/lib/coder-desktop/app/Coder Desktop" "$@"
WRAPPER
chmod 755 "$STAGE_DIR/usr/bin/coder-desktop"
ln -sf "/usr/lib/coder-desktop/service/CoderVpnService" "$STAGE_DIR/usr/bin/coder-vpn-service"

cp "$SCRIPT_DIR/coder-desktop.service" "$STAGE_DIR/usr/lib/systemd/system/"
cp "$SCRIPT_DIR/coder-desktop.desktop" "$STAGE_DIR/usr/share/applications/"
cp "$ROOT_DIR/App.Avalonia/Assets/coder.ico" "$STAGE_DIR/usr/share/pixmaps/coder-desktop.ico"

cat > "$STAGE_DIR/etc/coder-desktop/config.json" <<'CONFIG'
{
  "Manager": {
    "ServiceRpcSocketPath": "/run/coder-desktop/vpn.sock",
    "TunnelBinaryPath": "/usr/lib/coder-desktop/coder-vpn",
    "TunnelBinarySignatureSigner": "",
    "TunnelBinaryAllowVersionMismatch": false
  }
}
CONFIG

if has_format deb; then
  echo "[release-packaging] Building .deb"
  mkdir -p "$STAGE_DIR/DEBIAN"

  cat > "$STAGE_DIR/DEBIAN/control" <<CONTROL
Package: coder-desktop
Version: ${VERSION}
Architecture: ${DEB_ARCH}
Maintainer: Coder Technologies Inc. <support@coder.com>
Description: Coder Desktop - Connect to Coder workspaces
 Provides a Linux tray app and root VPN service for connecting
 to Coder workspaces.
Depends: dotnet-runtime-8.0, libnotify-bin, libsecret-tools
Recommends: freerdp2-x11 | remmina
Section: net
Priority: optional
CONTROL

  cp "$SCRIPT_DIR/postinst.sh" "$STAGE_DIR/DEBIAN/postinst"
  cp "$SCRIPT_DIR/prerm.sh" "$STAGE_DIR/DEBIAN/prerm"
  chmod 755 "$STAGE_DIR/DEBIAN/postinst" "$STAGE_DIR/DEBIAN/prerm"

  dpkg-deb --build "$STAGE_DIR" "$OUT_DIR/coder-desktop_${VERSION}_${DEB_ARCH}.deb"
fi

if has_format rpm; then
  echo "[release-packaging] Building .rpm"
  if ! command -v fpm >/dev/null 2>&1; then
    echo "fpm is required for rpm builds (gem install fpm)" >&2
    exit 1
  fi

  fpm -s dir -t rpm \
    -n coder-desktop \
    -v "$VERSION" \
    --iteration 1 \
    --architecture "$RPM_ARCH" \
    --license "AGPL-3.0-only" \
    --url "https://github.com/coder/coder-desktop-linux" \
    --maintainer "Coder Technologies Inc. <support@coder.com>" \
    --description "Coder Desktop Linux tray app and root VPN service" \
    --depends "dotnet-runtime-8.0" \
    --depends "libnotify" \
    --depends "libsecret" \
    --after-install "$SCRIPT_DIR/postinst.sh" \
    --before-remove "$SCRIPT_DIR/prerm.sh" \
    --package "$OUT_DIR/coder-desktop-${VERSION}-1.${RPM_ARCH}.rpm" \
    -C "$STAGE_DIR" \
    .
fi

if has_format tar; then
  echo "[release-packaging] Building .tar.gz"
  TAR_ROOT="$BUILD_DIR/tar/coder-desktop-${VERSION}-${ARCH}"
  mkdir -p "$TAR_ROOT"
  cp -a "$STAGE_DIR/." "$TAR_ROOT/"
  tar -C "$BUILD_DIR/tar" -czf "$OUT_DIR/coder-desktop_${VERSION}_${ARCH}.tar.gz" "coder-desktop-${VERSION}-${ARCH}"
fi

echo "[release-packaging] Output directory: $OUT_DIR"
ls -lh "$OUT_DIR"
