#!/usr/bin/env python3
from __future__ import annotations

import argparse
import os
import platform
import shutil
import subprocess
import sys
from pathlib import Path


CXX_EXTENSIONS = {".c", ".cc", ".cpp", ".cxx", ".h", ".hh", ".hpp", ".hxx"}
CXX_GENERATED_EXCLUDES = {"AutolinkedNativeModules.g.cpp", "AutolinkedNativeModules.g.h"}
CSHARP_EXTENSIONS = {".csproj"}
CMAKE_NAMES = {"CMakeLists.txt"}
CMAKE_EXTENSIONS = {".cmake"}
SOURCE_ROOTS = ("packages", "apps", "experiments")


def repo_root() -> Path:
    return Path(__file__).resolve().parents[1]


def run(command: list[str], cwd: Path) -> None:
    result = subprocess.run(command, cwd=cwd)
    if result.returncode != 0:
        raise SystemExit(result.returncode)


def require_command(command_name: str, install_hint: str) -> str:
    resolved = shutil.which(command_name)
    if resolved is None:
        raise SystemExit(f"{command_name} is required. {install_hint}")
    return resolved


def git_files(root: Path) -> list[str]:
    result = subprocess.run(
        ["git", "ls-files"],
        cwd=root,
        check=True,
        text=True,
        stdout=subprocess.PIPE,
    )
    return [line for line in result.stdout.splitlines() if line]


def under_source_root(path: str) -> bool:
    normalized = path.replace("\\", "/")
    return any(
        normalized == root or normalized.startswith(f"{root}/") for root in SOURCE_ROOTS
    )


def find_clang_format() -> str:
    override = os.environ.get("CLANG_FORMAT_BIN")
    if override:
        return override

    resolved = shutil.which("clang-format")
    if resolved is not None:
        return resolved

    if platform.system() == "Darwin" and shutil.which("xcrun") is not None:
        result = subprocess.run(
            ["xcrun", "--find", "clang-format"],
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
        )
        if result.returncode == 0 and result.stdout.strip():
            return result.stdout.strip()

    if platform.system() == "Windows":
        vs_clang_format = find_visual_studio_clang_format()
        if vs_clang_format is not None:
            return str(vs_clang_format)

    raise SystemExit("clang-format is required. Install it or set CLANG_FORMAT_BIN.")


def find_visual_studio_clang_format() -> Path | None:
    program_files_x86 = os.environ.get("ProgramFiles(x86)")
    if not program_files_x86:
        return None

    vswhere = (
        Path(program_files_x86)
        / "Microsoft Visual Studio"
        / "Installer"
        / "vswhere.exe"
    )
    if not vswhere.is_file():
        return None

    result = subprocess.run(
        [
            str(vswhere),
            "-latest",
            "-products",
            "*",
            "-property",
            "installationPath",
        ],
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.DEVNULL,
    )
    install_path = result.stdout.strip()
    if result.returncode != 0 or not install_path:
        return None

    for architecture in ("x64", "ARM64"):
        candidate = (
            Path(install_path)
            / "VC"
            / "Tools"
            / "Llvm"
            / architecture
            / "bin"
            / "clang-format.exe"
        )
        if candidate.is_file():
            return candidate

    return None


def find_cmake_format() -> str:
    override = os.environ.get("CMAKE_FORMAT_BIN")
    if override:
        return override

    resolved = shutil.which("cmake-format")
    if resolved is not None:
        return resolved

    mason_cmake_format = (
        Path(os.environ.get("XDG_DATA_HOME", str(Path.home() / ".local" / "share")))
        / "nvim"
        / "mason"
        / "bin"
        / "cmake-format"
    )
    if mason_cmake_format.is_file():
        return str(mason_cmake_format)

    raise SystemExit("cmake-format is required. Install it or set CMAKE_FORMAT_BIN.")


def run_cxx_format(root: Path, files: list[str], check: bool) -> None:
    cxx_files = [
        path
        for path in files
        if under_source_root(path) and Path(path).suffix.lower() in CXX_EXTENSIONS
        and Path(path).name not in CXX_GENERATED_EXCLUDES
    ]
    if not cxx_files:
        return

    clang_format = find_clang_format()
    print(f"==> Formatting C/C++ ({len(cxx_files)} files)", flush=True)
    if check:
        run([clang_format, "--dry-run", "--Werror", *cxx_files], root)
    else:
        run([clang_format, "-i", *cxx_files], root)


def run_csharp_format(root: Path, files: list[str], check: bool) -> None:
    projects = [
        path
        for path in files
        if under_source_root(path) and Path(path).suffix.lower() in CSHARP_EXTENSIONS
    ]
    if not projects:
        return

    dotnet = require_command("dotnet", "Install the .NET SDK.")
    print(f"==> Formatting C# ({len(projects)} projects)", flush=True)
    for project in projects:
        command = [dotnet, "format", project, "whitespace", "--no-restore"]
        if check:
            command.append("--verify-no-changes")
        run(command, root)


def run_cmake_format(root: Path, files: list[str], check: bool) -> None:
    cmake_files = [
        path
        for path in files
        if Path(path).name in CMAKE_NAMES
        or Path(path).suffix.lower() in CMAKE_EXTENSIONS
    ]
    if not cmake_files:
        return

    cmake_format = find_cmake_format()
    print(f"==> Formatting CMake ({len(cmake_files)} files)", flush=True)
    if check:
        run([cmake_format, "--check", *cmake_files], root)
    else:
        run([cmake_format, "-i", *cmake_files], root)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Formats repo source files with the project formatter for each language."
    )
    parser.add_argument("--check", action="store_true", help="Verify without writing changes.")
    parser.add_argument("--all", action="store_true", help="Format all supported languages.")
    parser.add_argument("--cxx", action="store_true", help="Format C, C++, and header files.")
    parser.add_argument("--csharp", action="store_true", help="Format C# projects.")
    parser.add_argument("--cmake", action="store_true", help="Format CMake files.")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if not (args.all or args.cxx or args.csharp or args.cmake):
        args.all = True

    root = repo_root()
    files = git_files(root)

    if args.all or args.cxx:
        run_cxx_format(root, files, args.check)
    if args.all or args.csharp:
        run_csharp_format(root, files, args.check)
    if args.all or args.cmake:
        run_cmake_format(root, files, args.check)

    if args.check:
        print("Formatting check passed.")
    else:
        print("Formatting complete.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
