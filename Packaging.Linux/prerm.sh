#!/usr/bin/env bash
set -e

if command -v systemctl >/dev/null 2>&1; then
    systemctl stop coder-desktop.service || true
    systemctl disable coder-desktop.service || true
fi
