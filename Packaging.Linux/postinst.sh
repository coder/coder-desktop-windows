#!/bin/bash
set -e

# Reload systemd
systemctl daemon-reload

# Enable and start the service
systemctl enable coder-desktop.service
systemctl start coder-desktop.service || true

# Register URI handler
if command -v xdg-mime &>/dev/null; then
    xdg-mime default coder-desktop.desktop x-scheme-handler/coder
fi

# Update desktop database
if command -v update-desktop-database &>/dev/null; then
    update-desktop-database /usr/share/applications || true
fi
