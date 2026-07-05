#!/usr/bin/env bash
set -e

# Service runs as root (see coder-desktop.service User=root).
if command -v systemctl >/dev/null 2>&1; then
    systemctl daemon-reload || true
    systemctl enable coder-desktop.service || true
    systemctl restart coder-desktop.service || systemctl start coder-desktop.service || true
fi

# Refresh desktop entry cache where available.
if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database /usr/share/applications || true
fi
