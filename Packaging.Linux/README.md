# Linux Packaging

## Building

```bash
# Build .deb for amd64
./build-deb.sh amd64

# Build .deb for arm64
VERSION=1.0.0 ./build-deb.sh arm64
```

## Package contents

- `/usr/lib/coder-desktop/` — Service binaries
- `/usr/bin/coder-vpn-service` — Symlink to VPN service
- `/etc/systemd/system/coder-desktop.service` — Systemd unit
- `/etc/coder-desktop/config.json` — Default configuration
- `/usr/share/applications/coder-desktop.desktop` — Desktop entry

## Dependencies

- `libnotify-bin` — Desktop notifications
- `libsecret-tools` — Credential storage
- `freerdp2-x11` or `remmina` — RDP client (optional)
