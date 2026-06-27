#!/bin/bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
mode="fix"
format_cxx=0
format_csharp=0
format_cmake=0

usage() {
  cat <<'EOF'
Usage: scripts/format.sh [--check] [--all] [--cxx] [--csharp] [--cmake]

Formats repo source files with the project formatter for each language.
With no language flags, all supported languages are formatted.

Options:
  --check     Verify formatting without writing changes.
  --all       Format all supported languages. This is the default.
  --cxx       Format C, C++, and header files with clang-format.
  --csharp    Format C# projects with dotnet format whitespace.
  --cmake     Format CMake files with cmake-format.
  -h, --help  Show this help.

Environment:
  CLANG_FORMAT_BIN  clang-format executable. Default: clang-format, or xcrun on macOS.
  CMAKE_FORMAT_BIN  cmake-format executable. Default: cmake-format, or nvim Mason fallback.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --check)
      mode="check"
      ;;
    --all)
      format_cxx=1
      format_csharp=1
      format_cmake=1
      ;;
    --cxx)
      format_cxx=1
      ;;
    --csharp)
      format_csharp=1
      ;;
    --cmake)
      format_cmake=1
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
  shift
done

if [[ "$format_cxx" == 0 && "$format_csharp" == 0 && "$format_cmake" == 0 ]]; then
  format_cxx=1
  format_csharp=1
  format_cmake=1
fi

require_command() {
  local command_name="$1"
  local install_hint="$2"

  if ! command -v "$command_name" >/dev/null 2>&1; then
    echo "$command_name is required. $install_hint" >&2
    exit 1
  fi
}

find_clang_format() {
  if [[ -n "${CLANG_FORMAT_BIN:-}" ]]; then
    printf '%s\n' "$CLANG_FORMAT_BIN"
    return
  fi
  if command -v clang-format >/dev/null 2>&1; then
    printf '%s\n' "clang-format"
    return
  fi
  if command -v xcrun >/dev/null 2>&1 && xcrun --find clang-format >/dev/null 2>&1; then
    xcrun --find clang-format
    return
  fi

  echo "clang-format is required. Install it or set CLANG_FORMAT_BIN." >&2
  exit 1
}

find_cmake_format() {
  local mason_cmake_format="${XDG_DATA_HOME:-$HOME/.local/share}/nvim/mason/bin/cmake-format"

  if [[ -n "${CMAKE_FORMAT_BIN:-}" ]]; then
    printf '%s\n' "$CMAKE_FORMAT_BIN"
    return
  fi
  if command -v cmake-format >/dev/null 2>&1; then
    printf '%s\n' "cmake-format"
    return
  fi
  if [[ -x "$mason_cmake_format" ]]; then
    printf '%s\n' "$mason_cmake_format"
    return
  fi

  echo "cmake-format is required. Install it or set CMAKE_FORMAT_BIN." >&2
  exit 1
}

run_cxx_format() {
  local clang_format_bin
  local files=()
  local file
  clang_format_bin="$(find_clang_format)"

  require_command "rg" "Install ripgrep."

  while IFS= read -r -d '' file; do
    files+=("$file")
  done < <(
    cd "$repo_root"
    rg --files -0 \
      -g '*.c' -g '*.cc' -g '*.cpp' -g '*.cxx' \
      -g '*.h' -g '*.hh' -g '*.hpp' -g '*.hxx' \
      native experiments
  )

  if [[ "${#files[@]}" -eq 0 ]]; then
    return
  fi

  echo "==> Formatting C/C++ (${#files[@]} files)"
  if [[ "$mode" == "check" ]]; then
    (cd "$repo_root" && "$clang_format_bin" --dry-run --Werror "${files[@]}")
  else
    (cd "$repo_root" && "$clang_format_bin" -i "${files[@]}")
  fi
}

run_csharp_format() {
  local projects=()
  local project
  require_command "dotnet" "Install the .NET SDK."
  require_command "rg" "Install ripgrep."

  while IFS= read -r project; do
    projects+=("$project")
  done < <(
    cd "$repo_root"
    rg --files -g '*.csproj' managed experiments
  )

  if [[ "${#projects[@]}" -eq 0 ]]; then
    return
  fi

  echo "==> Formatting C# (${#projects[@]} projects)"
  for project in "${projects[@]}"; do
    if [[ "$mode" == "check" ]]; then
      (cd "$repo_root" && dotnet format "$project" whitespace --no-restore --verify-no-changes)
    else
      (cd "$repo_root" && dotnet format "$project" whitespace --no-restore)
    fi
  done
}

run_cmake_format() {
  local cmake_format_bin
  local files=()
  local file
  cmake_format_bin="$(find_cmake_format)"

  require_command "rg" "Install ripgrep."

  while IFS= read -r -d '' file; do
    files+=("$file")
  done < <(
    cd "$repo_root"
    rg --files -0 -g 'CMakeLists.txt' -g '*.cmake'
  )

  if [[ "${#files[@]}" -eq 0 ]]; then
    return
  fi

  echo "==> Formatting CMake (${#files[@]} files)"
  if [[ "$mode" == "check" ]]; then
    (cd "$repo_root" && "$cmake_format_bin" --check "${files[@]}")
  else
    (cd "$repo_root" && "$cmake_format_bin" -i "${files[@]}")
  fi
}

if [[ "$format_cxx" == 1 ]]; then
  run_cxx_format
fi
if [[ "$format_csharp" == 1 ]]; then
  run_csharp_format
fi
if [[ "$format_cmake" == 1 ]]; then
  run_cmake_format
fi

if [[ "$mode" == "check" ]]; then
  echo "Formatting check passed."
else
  echo "Formatting complete."
fi
