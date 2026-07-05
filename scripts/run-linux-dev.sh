#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

print_help() {
  cat <<'HELP'
Usage: scripts/run-linux-dev.sh [--no-build] [--sudo-service] [-- <app-args...>]

Builds and runs the Linux VPN service + Avalonia app together for local dev.

Options:
  --no-build      Skip dotnet build before launch
  --sudo-service  Run Vpn.Service via sudo (recommended for real tunnel setup)
  --help          Show this help

Any remaining arguments are passed through to the app executable.
Examples:
  scripts/run-linux-dev.sh
  scripts/run-linux-dev.sh --no-build -- --minimized
  scripts/run-linux-dev.sh --sudo-service
HELP
}

build=1
sudo_service=0
app_args=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-build)
      build=0
      shift
      ;;
    --sudo-service)
      sudo_service=1
      shift
      ;;
    --help|-h)
      print_help
      exit 0
      ;;
    --)
      shift
      while [[ $# -gt 0 ]]; do
        app_args+=("$1")
        shift
      done
      ;;
    *)
      app_args+=("$1")
      shift
      ;;
  esac
done

if [[ $sudo_service -eq 1 ]] && [[ ${EUID:-$(id -u)} -ne 0 ]]; then
  if ! command -v sudo >/dev/null 2>&1; then
    echo "[run-linux-dev] --sudo-service requested, but sudo is not installed"
    exit 1
  fi

  echo "[run-linux-dev] Validating sudo access..."
  sudo -v
fi

if [[ $build -eq 1 ]]; then
  echo "[run-linux-dev] Building Vpn.Service..."
  dotnet build "$ROOT_DIR/Vpn.Service" -c Release >/dev/null
  echo "[run-linux-dev] Building App.Avalonia..."
  dotnet build "$ROOT_DIR/App.Avalonia" -c Release >/dev/null
fi

runtime_base="${XDG_RUNTIME_DIR:-/tmp}"
run_dir="$runtime_base/coder-desktop-dev"
log_dir="$run_dir/logs"
socket_path="$run_dir/vpn.sock"

if [[ $sudo_service -eq 1 ]]; then
  cache_dir="/tmp/coder-desktop-dev-root"
else
  cache_base="${XDG_CACHE_HOME:-$HOME/.cache}"
  cache_dir="$cache_base/coder-desktop-dev"
fi
tunnel_binary_path="$cache_dir/coder-vpn"

mkdir -p "$log_dir" "$cache_dir"
rm -f "$socket_path"

service_stdout_log="$log_dir/vpn-service.stdout.log"
service_file_log="$log_dir/vpn-service.file.log"
app_stdout_log="$log_dir/app.stdout.log"

# For manual runs, show the tray window unless the caller explicitly requests
# minimized/hidden startup behavior.
start_mode_args=" ${app_args[*]} "
if [[ "$start_mode_args" != *" --minimized "* ]] &&
   [[ "$start_mode_args" != *" --start-hidden "* ]] &&
   [[ "$start_mode_args" != *" --show "* ]]; then
  app_args=(--show "${app_args[@]}")
fi

service_pid=""
app_pid=""

cleanup() {
  if [[ -n "$app_pid" ]] && kill -0 "$app_pid" 2>/dev/null; then
    kill "$app_pid" 2>/dev/null || true
    wait "$app_pid" 2>/dev/null || true
  fi

  if [[ -n "$service_pid" ]] && kill -0 "$service_pid" 2>/dev/null; then
    kill "$service_pid" 2>/dev/null || true
    wait "$service_pid" 2>/dev/null || true
  fi

  if [[ $sudo_service -eq 1 ]] && command -v sudo >/dev/null 2>&1; then
    # Best effort cleanup in case the sudo wrapper exited but the root service
    # process remained alive.
    sudo -n pkill -f "CoderVpnService.*Manager:ServiceRpcSocketPath=$socket_path" 2>/dev/null || true
  fi
}
trap cleanup EXIT INT TERM

if [[ $sudo_service -eq 1 ]]; then
  echo "[run-linux-dev] Starting VPN service with sudo..."
else
  echo "[run-linux-dev] Starting VPN service..."
fi

if [[ $sudo_service -eq 1 ]] && [[ ${EUID:-$(id -u)} -ne 0 ]]; then
  sudo --preserve-env=XDG_RUNTIME_DIR,XDG_CACHE_HOME,HOME \
    "$ROOT_DIR/Vpn.Service/bin/Release/net8.0/CoderVpnService" \
    "Manager:ServiceRpcSocketPath=$socket_path" \
    "Manager:TunnelBinaryPath=$tunnel_binary_path" \
    "Manager:TunnelBinaryAllowVersionMismatch=true" \
    "Serilog:WriteTo:0:Args:path=$service_file_log" \
    >"$service_stdout_log" 2>&1 &
else
  "$ROOT_DIR/Vpn.Service/bin/Release/net8.0/CoderVpnService" \
    "Manager:ServiceRpcSocketPath=$socket_path" \
    "Manager:TunnelBinaryPath=$tunnel_binary_path" \
    "Manager:TunnelBinaryAllowVersionMismatch=true" \
    "Serilog:WriteTo:0:Args:path=$service_file_log" \
    >"$service_stdout_log" 2>&1 &
fi
service_pid="$!"

for _ in {1..40}; do
  if [[ -S "$socket_path" ]]; then
    break
  fi

  if ! kill -0 "$service_pid" 2>/dev/null; then
    echo "[run-linux-dev] VPN service exited before creating socket: $socket_path"
    echo "[run-linux-dev] Last service output:"
    tail -n 40 "$service_stdout_log" || true
    exit 1
  fi

  sleep 0.25
done

if [[ ! -S "$socket_path" ]]; then
  echo "[run-linux-dev] Timed out waiting for VPN socket: $socket_path"
  echo "[run-linux-dev] Last service output:"
  tail -n 40 "$service_stdout_log" || true
  exit 1
fi

echo "[run-linux-dev] Starting Avalonia app..."
CODER_DESKTOP_RPC_SOCKET_PATH="$socket_path" \
"$ROOT_DIR/App.Avalonia/bin/Release/net8.0/Coder Desktop" "${app_args[@]}" \
  >"$app_stdout_log" 2>&1 &
app_pid="$!"

echo "[run-linux-dev] Service PID: $service_pid"
echo "[run-linux-dev] App PID:     $app_pid"
echo "[run-linux-dev] Socket:      $socket_path"
echo "[run-linux-dev] Tunnel bin:  $tunnel_binary_path"
if [[ $sudo_service -eq 1 ]]; then
  echo "[run-linux-dev] Service mode: sudo"
fi
echo "[run-linux-dev] Logs:"
echo "  - $service_stdout_log"
echo "  - $service_file_log"
echo "  - $app_stdout_log"
echo "[run-linux-dev] App writes bootstrap logs to ~/.local/state/coder-desktop/app-avalonia.log"

echo "[run-linux-dev] Waiting for app to exit (Ctrl+C to stop both)..."
wait "$app_pid"
