#!/bin/bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
experiment_dir="$repo_root/experiments/hermes-console-hostfxr"
build_dir="$repo_root/build/hermes-console-hostfxr"
configuration="${CONFIGURATION:-Debug}"
hermes_root="${HERMES_PREBUILT_ROOT:-$repo_root/build/hermes/source/destroot}"

run=1
if [[ "${1:-}" == "--no-run" ]]; then
  run=0
  shift
fi

if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
  cat <<'EOF'
Usage: scripts/run-hermes-experiment.sh [--no-run] [-- <args>]

Runs the Hermes console HostFXR proof using the current shell environment.
direnv should provide a CMake 3.24+ on PATH when the system default is older.

Environment:
  CONFIGURATION           .NET configuration. Default: Debug
  HERMES_PREBUILT_ROOT    Hermes destroot. Default: <repo>/build/hermes/source/destroot
EOF
  exit 0
fi

cmake_version="$(cmake --version | awk 'NR == 1 { print $3 }')"
cmake_major="$(printf '%s' "$cmake_version" | cut -d. -f1)"
cmake_minor="$(printf '%s' "$cmake_version" | cut -d. -f2)"
if [[ "$cmake_major" -lt 3 || "$cmake_major" -eq 3 && "$cmake_minor" -lt 24 ]]; then
  cat >&2 <<EOF
CMake 3.24+ is required, but PATH resolves cmake $cmake_version.
Let direnv put a newer cmake first on PATH, then rerun this script.
EOF
  exit 1
fi

echo "==> Building managed dotnet"
dotnet build \
  "$experiment_dir/managed/HostFxrJSIProof/HostFxrJSIProof.csproj" \
  -c "$configuration"

echo
echo "==> Configuring cmake"
cmake \
  -S "$experiment_dir/native" \
  -B "$build_dir" \
  -DHERMES_PREBUILT_ROOT="$hermes_root"

echo
echo "==> Building the app"
cmake --build "$build_dir" --target hermes_console_hostfxr

if [[ "$run" == 1 ]]; then
  echo
  echo "==> Running Hermes console HostFXR proof"
  "$build_dir/hermes_console_hostfxr" "$@"
fi
