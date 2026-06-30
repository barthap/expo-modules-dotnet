#!/bin/bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
experiment_dir="$repo_root/experiments/hermes-console-hostfxr"
build_dir="$repo_root/build/hermes-console-hostfxr"
loader="${EXPO_JSI_DOTNET_LOADER:-hostfxr}"
if [[ "$loader" != "hostfxr" && "$loader" != "nativeaot" ]]; then
  echo "EXPO_JSI_DOTNET_LOADER must be hostfxr or nativeaot, got: $loader" >&2
  exit 1
fi

if [[ -n "${CONFIGURATION:-}" ]]; then
  configuration="$CONFIGURATION"
elif [[ "$loader" == "nativeaot" ]]; then
  configuration="Release"
else
  configuration="Debug"
fi
build_dir="$build_dir/$loader"
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
  CONFIGURATION           .NET configuration. Default: Debug for HostFXR, Release for NativeAOT
  EXPO_JSI_DOTNET_LOADER  Managed loader: hostfxr or nativeaot. Default: hostfxr
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

managed_project="$experiment_dir/managed/HostFxrJSIProof/HostFxrJSIProof.csproj"

if [[ "$loader" == "nativeaot" ]]; then
  case "$(uname -m)" in
    arm64) rid="osx-arm64" ;;
    x86_64) rid="osx-x64" ;;
    *)
      echo "Unsupported macOS architecture for NativeAOT: $(uname -m)" >&2
      exit 1
      ;;
  esac

  echo "==> Building modules generator analyzer"
  dotnet build \
    "$repo_root/managed/packages/Expo.ModulesCore.Generator/Expo.ModulesCore.Generator.csproj" \
    -c Debug

  echo "==> Publishing managed dotnet NativeAOT"
  dotnet publish "$managed_project" \
    -c "$configuration" \
    -r "$rid" \
    /p:PublishAot=true \
    /p:NativeLib=Shared
else
  echo "==> Building managed dotnet HostFXR"
  dotnet build "$managed_project" \
    -c "$configuration"
fi

echo
echo "==> Configuring cmake"
cmake \
  -S "$experiment_dir/native" \
  -B "$build_dir" \
  -DEXPO_JSI_DOTNET_LOADER="$loader" \
  -DEXPO_JSI_MANAGED_CONFIGURATION="$configuration" \
  -DHERMES_PREBUILT_ROOT="$hermes_root"

echo
echo "==> Building the app"
cmake --build "$build_dir" --target hermes_console_hostfxr

if [[ "$run" == 1 ]]; then
  echo
  echo "==> Running Hermes console $loader proof"
  "$build_dir/hermes_console_hostfxr" "$@"
fi
