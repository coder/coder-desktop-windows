#!/bin/bash
set -e

# Stop and disable the service
systemctl stop coder-desktop.service || true
systemctl disable coder-desktop.service || true
