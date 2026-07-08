#!/bin/bash
# Build a local Hermes prebuilt for Linux.
#
# Library choice: shared libhermesvm.so (Hermes' default on Linux).
# Rationale: building hermesvm.a as a static archive and then linking it into
# libexpo_jsi_testhost.so would require -DCMAKE_POSITION_INDEPENDENT_CODE=ON
# across the entire Hermes build, adding significant complexity and build time.
# Using the shared library keeps the build identical to the upstream Linux
# default and is simpler to validate. jsi is linked into libhermesvm.so; on some
# toolchains it is embedded statically (self-contained libhermesvm.so), on others
# it is a separate libjsi.so staged beside it and resolved through the rpath
# configured by cmake/ExpoHermesPrebuilt.cmake.
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
build_type="${BUILD_TYPE:-Release}"

usage() {
  cat <<'EOF'
Usage: scripts/build-hermes-linux.sh [--clean]

Downloads facebook/hermes from the official repository and builds a local
Linux libhermesvm.so shared library (plus libjsi.so when jsi builds shared)
and headers for the Hermes console proof.

Environment:
  HERMES_REPO_URL   Git URL. Default: https://github.com/facebook/hermes.git
  HERMES_REF        Branch, tag, or SHA. Default: scripts/hermes-ref.txt
  HERMES_WORK_DIR   Cache/build dir. Default: <repo>/build/hermes
  BUILD_TYPE        CMake build type. Default: Release
  CMAKE_BIN         CMake executable. Default: cmake

Output:
  <repo>/build/hermes/source/destroot/include
  <repo>/build/hermes/source/destroot/lib/libhermesvm.so
  <repo>/build/hermes/source/destroot/lib/libjsi.so   (only when jsi is shared)
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
if ! command -v "$cmake_bin" >/dev/null 2>&1; then
  echo "cmake is required. Set CMAKE_BIN to a CMake 3.24+ executable." >&2
  exit 1
fi
if ! command -v ninja >/dev/null 2>&1 && ! command -v make >/dev/null 2>&1; then
  echo "ninja or make is required." >&2
  exit 1
fi
if [[ -z "${CC:-}" && -z "${CXX:-}" ]] && command -v clang >/dev/null 2>&1 && command -v clang++ >/dev/null 2>&1; then
  export CC=clang
  export CXX=clang++
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

cmake_generator_flag=""
if command -v ninja >/dev/null 2>&1; then
  cmake_generator_flag="-G Ninja"
fi

hermes_build_dir="$work_dir/build-linux"
destroot="$source_dir/destroot"

echo "Hermes source: $source_dir"
echo "Hermes ref: $(git -C "$source_dir" rev-parse --short=12 HEAD)"
echo "CMake: $cmake_bin ($cmake_version)"
echo "Build type: $build_type"
echo "Build dir: $hermes_build_dir"
echo "Destroot: $destroot"

rm -rf "$hermes_build_dir"
mkdir -p "$hermes_build_dir"

# shellcheck disable=SC2086
"$cmake_bin" \
  -S "$source_dir" \
  -B "$hermes_build_dir" \
  $cmake_generator_flag \
  -DCMAKE_BUILD_TYPE="$build_type" \
  -DCMAKE_INSTALL_PREFIX="$destroot" \
  -DHERMES_BUILD_APPLE_FRAMEWORK=OFF \
  -DHERMES_ENABLE_TEST_SUITE=OFF \
  -DHERMES_BUILD_TOOLS=OFF \
  -DHERMES_ENABLE_DEBUGGER=OFF \
  -DHERMES_BUILD_SHARED_JSI=OFF \
  -DBUILD_SHARED_LIBS=ON

"$cmake_bin" --build "$hermes_build_dir" --target hermesvm --parallel

rm -rf "$destroot"
mkdir -p "$destroot/lib" "$destroot/include"

# Copy the shared libraries required at runtime. Depending on the toolchain,
# Hermes either embeds jsi statically into libhermesvm.so (self-contained) or
# builds a separate libjsi.so that libhermesvm.so depends on at runtime. Stage
# libjsi.so only when it was actually produced.
cp "$hermes_build_dir/lib/libhermesvm.so" "$destroot/lib/"
if [[ -f "$hermes_build_dir/jsi/libjsi.so" ]]; then
  cp "$hermes_build_dir/jsi/libjsi.so" "$destroot/lib/"
fi

# Copy headers: hermes API and jsi.
# Hermes cmake install rule: install(DIRECTORY "${SOURCE}/API/hermes" DESTINATION include)
# → produces include/hermes/*.h
# JSI cmake install rule: install(DIRECTORY "${SOURCE}/API/jsi/" DESTINATION include)
# (trailing slash) → produces include/jsi/*.h (not include/jsi/jsi/*.h)
cp -r "$source_dir/API/hermes" "$destroot/include/"
# hermes.h includes hermes/Public/*.h from the top-level public header tree.
cp -r "$source_dir/public/hermes/Public" "$destroot/include/hermes/"
cp -r "$source_dir/API/jsi/jsi" "$destroot/include/"

lib="$destroot/lib/libhermesvm.so"
jsi_lib="$destroot/lib/libjsi.so"
header="$destroot/include/hermes/hermes.h"
public_header="$destroot/include/hermes/Public/HermesExport.h"
jsi_header="$destroot/include/jsi/jsi.h"

if [[ ! -f "$lib" || ! -f "$header" || ! -f "$public_header" || ! -f "$jsi_header" ]]; then
  echo "Hermes build finished, but expected library or headers were not produced." >&2
  echo "  Expected lib:        $lib" >&2
  echo "  Expected jsi lib:    $jsi_lib" >&2
  echo "  Expected header:     $header" >&2
  echo "  Expected public hdr: $public_header" >&2
  echo "  Expected jsi header: $jsi_header" >&2
  exit 1
fi

cat <<EOF
Hermes Linux prebuilt ready:
  HERMES_PREBUILT_ROOT=$destroot

Configure the proof with:
  $cmake_bin -S apps/hermes-console-app/native -B build/hermes-console-app \\
    -DHERMES_PREBUILT_ROOT="$destroot"
EOF
