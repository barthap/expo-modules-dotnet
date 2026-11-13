#!/bin/bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
# Not the directory scripts/test-managed.sh deletes on every run.
# @ref docs/specs/hermes-testhost.md "Canonical Benchmark Runners"
build_dir="$repo_root/build/jsi-testhost-bench"
hermes_root="${HERMES_PREBUILT_ROOT:-$repo_root/build/hermes/source/destroot}"
benchmark_project="$repo_root/packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Benchmarks/Expo.ModulesCore.Benchmarks.csproj"
benchmark_args=()

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

# Runs a build step. A failure exits with 70, so it cannot be mistaken for a
# benchmark result.
build_step() {
	"$@" || {
		echo "Build step failed: $*" >&2
		exit 70
	}
}

if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
	cat <<'EOF'
Usage: scripts/bench-managed.sh [-- <benchmark args...>]

Builds everything in Release, then runs the call-path benchmark against the
Hermes-backed native JSI testhost. The table goes to stdout, progress to stderr.

Benchmark arguments (after --):
  --filter <regex>   Run only the cases whose "<name> (<direction>)" matches
                     (cases without a direction match on "<name>")
  --help             Print the benchmark usage

Environment:
  HERMES_PREBUILT_ROOT    Hermes destroot. Default: <repo>/build/hermes/source/destroot

The native testhost is built incrementally in build/jsi-testhost-bench. The
directory is not deleted between runs and is separate from the one used by
scripts/test-managed.sh.

Exit codes (the benchmark's own code is passed through):
  0    Success
  1    Benchmark: at least one row is noisy (the table is still printed).
       Also this script: the Hermes prebuilt was not found (a hint is printed).
  2    Benchmark: --filter matches no case
  3    Benchmark: a case failed verification or threw
  64   Benchmark: unknown argument or invalid regex
  70   This script: a build step failed
EOF
	exit 0
fi

if (($#)); then
	if [[ "$1" != "--" ]]; then
		echo "Unknown argument: $1 (benchmark arguments go after --, see --help)." >&2
		exit 64
	fi
	shift
	benchmark_args=("$@")
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

echo "==> Building Expo.JSI" >&2
build_step dotnet build "$repo_root/packages/expo-modules-dotnet/managed/packages/Expo.JSI/Expo.JSI.csproj" -c Release >&2

echo >&2
echo "==> Building Expo.ModulesCore" >&2
build_step dotnet build "$repo_root/packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Expo.ModulesCore.csproj" -c Release >&2

echo >&2
echo "==> Building Expo.ModulesCore.Generator" >&2
build_step dotnet build "$repo_root/packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/Expo.ModulesCore.Generator.csproj" -c Release >&2

echo >&2
echo "==> Building Expo.ModulesCore.Benchmarks" >&2
build_step dotnet build "$benchmark_project" -c Release >&2

echo >&2
echo "==> Configuring native testhost" >&2
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
# Release on purpose: a Debug testhost gives numbers that mislead.
# @ref docs/specs/hermes-testhost.md "Canonical Benchmark Runners"
build_step run_in_repo_env cmake \
	-S "$repo_root/packages/expo-modules-dotnet/native/testhost" \
	-B "$build_dir" \
	-DCMAKE_BUILD_TYPE=Release \
	-DHERMES_PREBUILT_ROOT="$hermes_root" \
	${_cmake_extra_args[@]+"${_cmake_extra_args[@]}"} >&2

echo >&2
echo "==> Building native testhost" >&2
build_step run_in_repo_env cmake --build "$build_dir" --target expo_jsi_testhost >&2

if [[ ! -f "$testhost_library" ]]; then
	echo "Native testhost library was not produced at $testhost_library" >&2
	exit 70
fi

echo >&2
echo "==> Running Expo.ModulesCore.Benchmarks" >&2
exit_code=0
EXPO_JSI_TESTHOST_LIBRARY="$testhost_library" \
	dotnet run --no-build -c Release --project "$benchmark_project" -- ${benchmark_args[@]+"${benchmark_args[@]}"} || exit_code=$?
exit "$exit_code"
