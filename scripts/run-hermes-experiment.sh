#!/bin/bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
experiment_dir="$repo_root/experiments/hermes-console-app"
managed_project="$experiment_dir/managed/HermesConsoleApp/HermesConsoleApp.csproj"
hermes_root="${HERMES_PREBUILT_ROOT:-$repo_root/build/hermes/source/destroot}"
loader="${EXPO_JSI_DOTNET_LOADER:-hostfxr}"
run=1

usage() {
  cat <<'EOF'
Usage: scripts/run-hermes-experiment.sh [--no-run] [-- <args>]

Runs the Hermes console app using the current shell environment.
direnv should provide a CMake 3.24+ on PATH when the system default is older.

Environment:
  CONFIGURATION           .NET configuration. Default: Debug for HostFXR, Release for NativeAOT
  EXPO_JSI_DOTNET_LOADER  Managed loader: hostfxr or nativeaot. Default: hostfxr
  HERMES_PREBUILT_ROOT    Hermes destroot. Default: <repo>/build/hermes/source/destroot
EOF
}

parse_args() {
  if [[ "${1:-}" == "--no-run" ]]; then
    run=0
    shift
  fi

  if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
    usage
    exit 0
  fi
}

validate_loader() {
  if [[ "$loader" != "hostfxr" && "$loader" != "nativeaot" ]]; then
    echo "EXPO_JSI_DOTNET_LOADER must be hostfxr or nativeaot, got: $loader" >&2
    exit 1
  fi
}

managed_configuration() {
  if [[ -n "${CONFIGURATION:-}" ]]; then
    printf '%s\n' "$CONFIGURATION"
  elif [[ "$loader" == "nativeaot" ]]; then
    printf '%s\n' "Release"
  else
    printf '%s\n' "Debug"
  fi
}

nativeaot_rid() {
  case "$(uname -m)" in
    arm64) printf '%s\n' "osx-arm64" ;;
    x86_64) printf '%s\n' "osx-x64" ;;
    *)
      echo "Unsupported macOS architecture for NativeAOT: $(uname -m)" >&2
      exit 1
      ;;
  esac
}

check_cmake_version() {
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
}

build_generator_analyzer() {
  echo "==> Building modules generator analyzer"
  dotnet build \
    "$repo_root/managed/packages/Expo.ModulesCore.Generator/Expo.ModulesCore.Generator.csproj" \
    -c Debug
}

build_managed_hostfxr() {
  local configuration="$1"

  echo "==> Building managed dotnet HostFXR"
  dotnet build "$managed_project" \
    -c "$configuration"
}

publish_managed_nativeaot() {
  local configuration="$1"
  local rid

  rid="$(nativeaot_rid)"

  build_generator_analyzer

  echo "==> Publishing managed dotnet NativeAOT"
  dotnet publish "$managed_project" \
    -c "$configuration" \
    -r "$rid" \
    /p:PublishAot=true \
    /p:NativeLib=Shared
}

build_managed() {
  local configuration="$1"

  if [[ "$loader" == "nativeaot" ]]; then
    publish_managed_nativeaot "$configuration"
  else
    build_managed_hostfxr "$configuration"
  fi
}

configure_native_app() {
  local build_dir="$1"
  local configuration="$2"

  echo
  echo "==> Configuring cmake"
  cmake \
    -S "$experiment_dir/native" \
    -B "$build_dir" \
    -DEXPO_JSI_DOTNET_LOADER="$loader" \
    -DEXPO_JSI_MANAGED_CONFIGURATION="$configuration" \
    -DHERMES_PREBUILT_ROOT="$hermes_root"
}

build_native_app() {
  local build_dir="$1"

  echo
  echo "==> Building the app"
  cmake --build "$build_dir" --target hermes_console_app
}

run_native_app() {
  local build_dir="$1"
  shift

  if [[ "$run" == 1 ]]; then
    echo
    echo "==> Running Hermes console app ($loader)"
    "$build_dir/hermes_console_app" "$@"
  fi
}

main() {
  parse_args "$@"
  validate_loader

  local configuration
  local build_dir
  configuration="$(managed_configuration)"
  build_dir="$repo_root/build/hermes-console-app/$loader"

  check_cmake_version
  build_managed "$configuration"
  configure_native_app "$build_dir" "$configuration"
  build_native_app "$build_dir"
  run_native_app "$build_dir" "$@"
}

main "$@"
