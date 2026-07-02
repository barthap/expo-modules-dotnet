#!/bin/bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd -P)"
managed_project="$repo_root/packages/example-module/dotnet/ExampleModule/ExampleModule.csproj"
generator_project="$repo_root/packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/Expo.ModulesCore.Generator.csproj"
managed_dir="$repo_root/apps/desktop-app/macos/Managed"
loader="${EXPO_DOTNET_LOADER:-${EXPO_JSI_DOTNET_LOADER:-hostfxr}}"

usage() {
  cat <<'EOF'
Usage: apps/desktop-app/scripts/build-managed.sh

Environment:
  CONFIGURATION          .NET configuration. Default: Debug for HostFXR, Release for NativeAOT
  EXPO_DOTNET_LOADER     Managed loader: hostfxr or nativeaot. Xcode default: hostfxr
  EXPO_JSI_DOTNET_LOADER Compatibility alias for EXPO_DOTNET_LOADER
EOF
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

macos_rid() {
  case "$(uname -m)" in
    arm64) printf '%s\n' "osx-arm64" ;;
    x86_64) printf '%s\n' "osx-x64" ;;
    *)
      echo "Unsupported macOS architecture: $(uname -m)" >&2
      exit 1
      ;;
  esac
}

dotnet_root() {
  local base_path
  base_path="$(
    dotnet --info |
      awk -F: '/Base Path/ { gsub(/^[ \t]+|[ \t]+$/, "", $2); print $2; exit }'
  )"
  if [[ -n "$base_path" ]]; then
    cd "$base_path/../.." && pwd -P
    return
  fi

  cd "$(dirname "$(command -v dotnet)")" && pwd -P
}

latest_nethost_library() {
  local rid="$1"
  local pack_root
  local latest_version

  pack_root="$(dotnet_root)/packs/Microsoft.NETCore.App.Host.$rid"
  if [[ ! -d "$pack_root" ]]; then
    echo "Missing .NET host pack: $pack_root" >&2
    exit 1
  fi

  latest_version="$(
    find "$pack_root" -mindepth 1 -maxdepth 1 -type d -exec basename {} \; |
      awk '/^[0-9]+([.][0-9]+){2,}$/' |
      sort -t. -k1,1n -k2,2n -k3,3n |
      tail -n 1
  )"
  if [[ -z "$latest_version" ]]; then
    echo "No stable .NET host pack versions found under $pack_root" >&2
    exit 1
  fi

  printf '%s\n' "$pack_root/$latest_version/runtimes/$rid/native/libnethost.dylib"
}

reset_managed_dir() {
  mkdir -p "$managed_dir"
  find "$managed_dir" -mindepth 1 ! -name .gitignore ! -name .gitkeep -exec rm -rf {} +
}

dotnet_build_env() {
  env \
    -u ACTION \
    -u ARCHS \
    -u CURRENT_ARCH \
    -u PLATFORM_NAME \
    -u PRODUCT_NAME \
    -u PROJECT_NAME \
    -u TARGET_NAME \
    -u TARGETNAME \
    dotnet "$@"
}

build_generator_analyzer() {
  dotnet_build_env build "$generator_project" -c Debug
}

build_hostfxr() {
  local configuration="$1"
  local output_dir="$repo_root/packages/example-module/dotnet/ExampleModule/bin/$configuration/net10.0"
  local nethost

  dotnet_build_env build "$managed_project" -c "$configuration"

  reset_managed_dir
  find "$output_dir" -maxdepth 1 \( -name '*.dll' -o -name '*.deps.json' -o -name '*.runtimeconfig.json' \) \
    -exec cp {} "$managed_dir/" \;

  nethost="$(latest_nethost_library "$(macos_rid)")"
  cp "$nethost" "$managed_dir/"
}

publish_nativeaot() {
  local configuration="$1"
  local rid
  local publish_dir

  rid="$(macos_rid)"
  publish_dir="$repo_root/packages/example-module/dotnet/ExampleModule/bin/$configuration/net10.0/$rid/publish"

  build_generator_analyzer
  dotnet_build_env publish "$managed_project" \
    -c "$configuration" \
    -r "$rid" \
    /p:PublishAot=true \
    /p:NativeLib=Shared

  reset_managed_dir
  cp "$publish_dir/libExampleModule.dylib" "$managed_dir/"
}

main() {
  if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
    usage
    exit 0
  fi

  if [[ "$loader" != "hostfxr" && "$loader" != "nativeaot" ]]; then
    echo "EXPO_DOTNET_LOADER must be hostfxr or nativeaot, got: $loader" >&2
    exit 1
  fi

  local configuration
  configuration="$(managed_configuration)"

  case "$loader" in
    hostfxr) build_hostfxr "$configuration" ;;
    nativeaot) publish_nativeaot "$configuration" ;;
  esac

  echo "Staged $loader managed artifacts in apps/desktop-app/macos/Managed"
}

main "$@"
