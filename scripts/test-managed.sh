#!/bin/bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
build_dir="$repo_root/build/jsi-testhost"
configuration="${CONFIGURATION:-Debug}"
hermes_root="${HERMES_PREBUILT_ROOT:-$repo_root/build/hermes/source/destroot}"
selected_projects=()
dotnet_test_args=()

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
Usage: scripts/test-managed.sh [--project <repo-relative-test-csproj>] [dotnet test args...]

Builds the Hermes-backed native JSI testhost and runs managed test projects.

Environment:
  CONFIGURATION           .NET configuration. Default: Debug
  HERMES_PREBUILT_ROOT    Hermes destroot. Default: <repo>/build/hermes/source/destroot
EOF
	exit 0
fi

while (($#)); do
	case "$1" in
		--project)
			if (($# < 2)); then
				echo "Missing project path after --project." >&2
				exit 1
			fi
			selected_projects+=("$2")
			shift 2
			;;
		*)
			dotnet_test_args+=("$1")
			shift
			;;
	esac
done

test_projects=()
if ((${#selected_projects[@]})); then
	for selected_project in "${selected_projects[@]}"; do
		if [[ "$selected_project" == /* ]]; then
			echo "Invalid test project path: $selected_project (paths must be repo-relative)." >&2
			exit 1
		fi

		candidate_project="$repo_root/$selected_project"
		if [[ ! -f "$candidate_project" || -L "$candidate_project" ]]; then
			echo "Invalid test project path: $selected_project (must be an existing regular file)." >&2
			exit 1
		fi
		if [[ "$(basename "$candidate_project")" != *.Tests.csproj ]]; then
			echo "Invalid test project path: $selected_project (must name a *.Tests.csproj file)." >&2
			exit 1
		fi

		resolved_parent="$(cd "$(dirname "$candidate_project")" && pwd -P)"
		resolved_project="$resolved_parent/$(basename "$candidate_project")"
		if [[ "$resolved_project" != "$repo_root/"* ]]; then
			echo "Invalid test project path: $selected_project (must resolve inside the repository)." >&2
			exit 1
		fi

		if ((${#test_projects[@]})); then
			for existing_project in "${test_projects[@]}"; do
				if [[ "$resolved_project" == "$existing_project" ]]; then
					echo "Invalid test project path: $selected_project (duplicate selection)." >&2
					exit 1
				fi
			done
		fi
		test_projects+=("$resolved_project")
	done
else
	test_projects=(
		"$repo_root/packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj"
		"$repo_root/packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Expo.JSI.Tests.csproj"
		"$repo_root/packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj"
	)

	while IFS= read -r discovered_project; do
		if ((${#test_projects[@]})); then
			for existing_project in "${test_projects[@]}"; do
				if [[ "$discovered_project" == "$existing_project" ]]; then
					echo "Duplicate managed test project: $discovered_project" >&2
					exit 1
				fi
			done
		fi
		test_projects+=("$discovered_project")
	done < <(
		find "$repo_root/packages" -mindepth 4 -maxdepth 4 -type f \
			-path '*/dotnet/*.Tests/*.Tests.csproj' -print | LC_ALL=C sort
	)
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

for test_project in "${test_projects[@]}"; do
	echo
	echo "==> Running $(basename "$test_project" .csproj)"
	EXPO_JSI_TESTHOST_LIBRARY="$testhost_library" \
		dotnet test "$test_project" -c "$configuration" "${dotnet_test_args[@]+"${dotnet_test_args[@]}"}"
done
