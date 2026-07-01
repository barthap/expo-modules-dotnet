#!/bin/bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"

hermes_repo_url="${HERMES_REPO_URL:-https://github.com/facebook/hermes.git}"
if [[ -n "${HERMES_REF:-}" ]]; then
  hermes_ref="$HERMES_REF"
else
  hermes_ref="$(tr -d '[:space:]' < "$repo_root/scripts/hermes-ref.txt")"
fi
work_dir="${HERMES_WORK_DIR:-$repo_root/build/hermes}"
source_dir="$work_dir/source"
cmake_bin="${CMAKE_BIN:-}"
mac_deployment_target="${MAC_DEPLOYMENT_TARGET:-13.0}"
build_type="${BUILD_TYPE:-Release}"

usage() {
  cat <<'EOF'
Usage: scripts/build-hermes-macos.sh [--clean]

Downloads facebook/hermes from the official repository and builds a local
macOS hermesvm.framework plus headers for the Hermes console proof.

Environment:
  HERMES_REPO_URL        Git URL. Default: https://github.com/facebook/hermes.git
  HERMES_REF             Branch, tag, or SHA. Default: scripts/hermes-ref.txt
  HERMES_WORK_DIR        Cache/build dir. Default: <repo>/build/hermes
  MAC_DEPLOYMENT_TARGET  macOS deployment target. Default: 13.0
  BUILD_TYPE             Hermes framework build type. Default: Release
  CMAKE_BIN              CMake executable. Default: cmake

Output:
  <repo>/build/hermes/source/destroot/include
  <repo>/build/hermes/source/destroot/Library/Frameworks/macosx/hermesvm.framework
EOF
}

clean=0
for arg in "$@"; do
  case "$arg" in
    --clean)
      clean=1
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $arg" >&2
      usage >&2
      exit 2
      ;;
  esac
done

if [[ -z "$cmake_bin" ]]; then
  cmake_bin=cmake
fi

if ! command -v git >/dev/null 2>&1; then
  echo "git is required." >&2
  exit 1
fi
if ! command -v node >/dev/null 2>&1; then
  echo "node is required by Hermes' Apple framework build scripts." >&2
  exit 1
fi
if ! command -v xcodebuild >/dev/null 2>&1; then
  echo "xcodebuild is required by Hermes' Apple framework build scripts." >&2
  exit 1
fi
if ! command -v "$cmake_bin" >/dev/null 2>&1; then
  echo "cmake is required. Set CMAKE_BIN to a CMake 3.24+ executable." >&2
  exit 1
fi

cmake_version="$("$cmake_bin" --version | awk 'NR == 1 { print $3 }')"
if ! "$cmake_bin" -E capabilities >/dev/null 2>&1; then
  echo "Failed to execute CMake at $cmake_bin." >&2
  exit 1
fi

if [[ "$clean" == 1 ]]; then
  rm -rf "$work_dir"
fi

mkdir -p "$work_dir"

if [[ ! -d "$source_dir/.git" ]]; then
  git clone "$hermes_repo_url" "$source_dir"
fi

git -C "$source_dir" fetch --tags origin "$hermes_ref"
git -C "$source_dir" checkout --detach FETCH_HEAD

export MAC_DEPLOYMENT_TARGET="$mac_deployment_target"
export BUILD_TYPE="$build_type"
export PATH="$(cd "$(dirname "$cmake_bin")" && pwd -P):$PATH"

echo "Hermes source: $source_dir"
echo "Hermes ref: $(git -C "$source_dir" rev-parse --short=12 HEAD)"
echo "CMake: $cmake_bin ($cmake_version)"
echo "MAC_DEPLOYMENT_TARGET=$MAC_DEPLOYMENT_TARGET"
echo "BUILD_TYPE=$BUILD_TYPE"

(
  cd "$source_dir"
  rm -rf destroot
  bash utils/build-mac-framework.sh
)

framework="$source_dir/destroot/Library/Frameworks/macosx/hermesvm.framework/hermesvm"
header="$source_dir/destroot/include/hermes/hermes.h"
jsi_header="$source_dir/destroot/include/jsi/jsi.h"

if [[ ! -f "$framework" || ! -f "$header" || ! -f "$jsi_header" ]]; then
  echo "Hermes build finished, but expected framework or headers were not produced." >&2
  exit 1
fi

cat <<EOF
Hermes macOS prebuilt ready:
  HERMES_PREBUILT_ROOT=$source_dir/destroot

Configure the proof with:
  $cmake_bin -S apps/hermes-console-app/native -B build/hermes-console-app \\
    -DHERMES_PREBUILT_ROOT="$source_dir/destroot"
EOF
