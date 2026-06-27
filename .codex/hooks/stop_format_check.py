#!/usr/bin/env python3
import json
import subprocess
import sys
from pathlib import Path


def emit(payload: dict) -> None:
    print(json.dumps(payload, separators=(",", ":")))


def read_input() -> dict:
    try:
        raw = sys.stdin.read()
        if not raw.strip():
            return {}
        data = json.loads(raw)
        return data if isinstance(data, dict) else {}
    except json.JSONDecodeError:
        return {}


def git_root(cwd: str) -> Path | None:
    try:
        result = subprocess.run(
            ["git", "rev-parse", "--show-toplevel"],
            cwd=cwd,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            check=True,
        )
    except (OSError, subprocess.CalledProcessError):
        return None

    root = result.stdout.strip()
    return Path(root) if root else None


def main() -> int:
    hook_input = read_input()
    cwd = hook_input.get("cwd") or "."
    root = git_root(cwd)
    if root is None:
        return 0

    format_script = root / "scripts" / "format.sh"
    if not format_script.is_file():
        return 0

    result = subprocess.run(
        [str(format_script), "--check", "--all"],
        cwd=root,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
    )
    if result.returncode == 0:
        return 0

    output = result.stdout.strip()
    reason = (
        "Formatting check failed. Run `scripts/format.sh`, inspect the diff, "
        "then rerun `scripts/format.sh --check --all` before finalizing."
    )
    if output:
        reason = f"{reason}\n\nFormatter output:\n{output[-4000:]}"

    if hook_input.get("stop_hook_active") is True:
        emit({"systemMessage": reason})
        return 0

    emit({"decision": "block", "reason": reason})
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
