# Linux Packaging

## Build release artifacts locally

From repo root:

```bash
# Build .deb, .rpm, and .tar.gz for amd64
VERSION=1.0.0 ./Packaging.Linux/build-release-packages.sh amd64

# Build .deb, .rpm, and .tar.gz for arm64
VERSION=1.0.0 ./Packaging.Linux/build-release-packages.sh arm64

# Build only one format
VERSION=1.0.0 FORMATS=deb ./Packaging.Linux/build-release-packages.sh amd64
```

Artifacts are written to `./dist/` by default.

Generated formats:

- `coder-desktop_<version>_amd64.deb`
- `coder-desktop_<version>_arm64.deb`
- `coder-desktop-<version>-1.x86_64.rpm`
- `coder-desktop-<version>-1.aarch64.rpm`
- `coder-desktop_<version>_amd64.tar.gz`
- `coder-desktop_<version>_arm64.tar.gz`

## Build AUR source bundle

```bash
VERSION=1.0.0 ./Packaging.Linux/build-aur-source.sh
```

Produces:

- `coder-desktop_<version>_aur.tar.gz`

This archive contains `PKGBUILD`, `coder-desktop.install`, unit/desktop files,
and can be used as a source bundle for AUR packaging.

## Debian helper

`build-deb.sh` is retained as a convenience wrapper:

```bash
VERSION=1.0.0 ./Packaging.Linux/build-deb.sh amd64
```

## Service behavior (root VPN service)

Packages install a systemd unit (`coder-desktop.service`) that runs:

- `User=root`
- `ExecStart=/usr/bin/coder-vpn-service`

Post-install scripts run `systemctl daemon-reload`, `enable`, and `start/restart`
so the VPN service is active as root after installation (when systemd is
available).

## Package contents

- `/usr/lib/coder-desktop/app/` — Avalonia desktop app binaries
- `/usr/lib/coder-desktop/service/` — VPN service binaries
- `/usr/bin/coder-desktop` — Desktop app launcher wrapper
- `/usr/bin/coder-vpn-service` — Symlink to VPN service binary
- `/usr/lib/systemd/system/coder-desktop.service` — systemd unit
- `/etc/coder-desktop/config.json` — Default configuration
- `/usr/share/applications/coder-desktop.desktop` — Desktop entry
- `/usr/share/pixmaps/coder-desktop.ico` — App icon
