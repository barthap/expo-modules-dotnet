#!/bin/sh
set -eu

script_dir="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)"
: "${PYTHON:=python3}"
exec "$PYTHON" "$script_dir/format.py" "$@"
