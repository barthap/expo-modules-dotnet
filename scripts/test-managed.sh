#!/bin/bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
build_dir="$repo_root/build/jsi-testhost"
configuration="${CONFIGURATION:-Debug}"
hermes_root="${HERMES_PREBUILT_ROOT:-$repo_root/build/hermes/source/destroot}"

if [[ "$(uname)" == "Darwin" ]]; then
  testhost_library="$build_dir/libexpo_jsi_testhost.dylib"
  _build_hermes_hint="scripts/build-hermes-macos.sh"
else
  testhost_library="$build_dir/libexpo_jsi_testhost.so"
  _build_hermes_hint="scripts/build-hermes-linux.sh"
fi

run_in_repo_env() {
	if command -v direnv >/dev/null 2>&1; then
		direnv exec "$repo_root" "$@"
	else
		"$@"
	fi
}

if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
	cat <<'EOF'
Usage: scripts/test-managed.sh [dotnet test args...]

Builds the Hermes-backed native JSI testhost and runs managed test projects.

Environment:
  CONFIGURATION           .NET configuration. Default: Debug
  HERMES_PREBUILT_ROOT    Hermes destroot. Default: <repo>/build/hermes/source/destroot
EOF
	exit 0
fi

if [[ ! -d "$hermes_root/include" ]]; then
	cat >&2 <<EOF
Hermes prebuilt was not found at:
  $hermes_root

Run:
  $_build_hermes_hint
EOF
	exit 1
fi

echo "==> Building Expo.JSI"
dotnet build "$repo_root/packages/expo-modules-dotnet/managed/packages/Expo.JSI/Expo.JSI.csproj" -c "$configuration"

echo
echo "==> Building Expo.ModulesCore"
dotnet build "$repo_root/packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Expo.ModulesCore.csproj" -c "$configuration"

echo
echo "==> Building Expo.ModulesCore.Generator"
dotnet build "$repo_root/packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/Expo.ModulesCore.Generator.csproj" -c "$configuration"

echo
echo "==> Running Expo.ModulesCore.Generator.Tests"
dotnet test "$repo_root/packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj" \
	-c "$configuration" \
	"$@"

echo
echo "==> Configuring native testhost"
rm -rf "$build_dir"
_cmake_extra_args=()
if [[ "$(uname)" != "Darwin" ]]; then
  # On Linux pick Ninja if available and prefer clang when the default C++
  # compiler is not set, to match the CI toolchain.
  if command -v ninja >/dev/null 2>&1; then
    _cmake_extra_args+=("-G" "Ninja")
  fi
  if [[ -z "${CXX:-}" ]] && command -v clang++ >/dev/null 2>&1; then
    export CC="${CC:-clang}"
    export CXX="${CXX:-clang++}"
  fi
fi
run_in_repo_env cmake \
	-S "$repo_root/packages/expo-modules-dotnet/native/testhost" \
	-B "$build_dir" \
	-DHERMES_PREBUILT_ROOT="$hermes_root" \
	${_cmake_extra_args[@]+"${_cmake_extra_args[@]}"}

echo
echo "==> Building native testhost"
run_in_repo_env cmake --build "$build_dir" --target expo_jsi_testhost

echo
echo "==> Running Expo.JSI.Tests"
EXPO_JSI_TESTHOST_LIBRARY="$testhost_library" \
	dotnet test "$repo_root/packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Expo.JSI.Tests.csproj" \
	-c "$configuration" \
	"$@"

echo
echo "==> Running Expo.ModulesCore.Tests"
EXPO_JSI_TESTHOST_LIBRARY="$testhost_library" \
	dotnet test "$repo_root/packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj" \
	-c "$configuration" \
	"$@"
