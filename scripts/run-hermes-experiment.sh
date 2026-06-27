#!/usr/bin/env bash

set -euo pipefail

SCRIPTS_DIR=$(dirname $0)
REPO_ROOT=$(dirname $SCRIPTS_DIR)

PROJ_DIR="$REPO_ROOT/experiments/hermes-console-hostfxr"
BUILD_DIR="$REPO_ROOT/build/hermes-console-hostfxr"

echo "Building dotnet"
dotnet build "$PROJ_DIR/managed/HostFxrJSIProof/HostFxrJSIProof.csproj" -c Debug

echo -e "\nDoing cmake -S whatever it does (some configuration I guess. idk if it's one-off command or what)"
cmake -S "$PROJ_DIR/native" -B "$BUILD_DIR"

echo -e "\nDoing cmake build"
cmake --build "$BUILD_DIR" --target hermes_console_hostfxr

echo -e "\nRunning the app...\n\n"
"$BUILD_DIR/hermes_console_hostfxr" ${@:1}
