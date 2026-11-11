#!/usr/bin/env python3
from __future__ import annotations

import argparse
import re
import sys
from dataclasses import dataclass
from pathlib import Path


CHECKED_LANGUAGES = {"csharp", "ts", "json", "xml"}
EXEMPLAR_DIRECTORIES = (
    "packages/example-module/dotnet/ExampleModule/",
    "packages/expo-constants-dotnet/",
    "apps/hermes-console-app/managed/HermesConsoleApp/",
)
DEFAULT_GUIDE = "docs/module-authoring-guide.md"

OPEN_FENCE = re.compile(r"^(`{3,})([^`]*)$")
CLOSE_FENCE = re.compile(r"^(`{3,})\s*$")
PATH_PATTERN = r"[A-Za-z0-9_.@-]+(?:/[A-Za-z0-9_.@-]+)+"
PATH_COMMENTS = (
    re.compile(r"^//\s*(" + PATH_PATTERN + r")\s*$"),
    re.compile(r"^#\s*(" + PATH_PATTERN + r")\s*$"),
    re.compile(r"^<!--\s*(" + PATH_PATTERN + r")\s*-->\s*$"),
)
BACKED_BY = re.compile(r"^<!--.*\bbacked-by:.*-->\s*$")


@dataclass
class Block:
    fence_line: int
    language: str
    body: list[str] | None


def repo_root() -> Path:
    return Path(__file__).resolve().parents[1]


def parse_blocks(lines: list[str]) -> list[Block]:
    blocks: list[Block] = []
    index = 0
    while index < len(lines):
        opening = OPEN_FENCE.match(lines[index])
        if opening is None:
            index += 1
            continue
        fence_length = len(opening.group(1))
        info = opening.group(2).split()
        language = info[0].lower() if info else ""
        body: list[str] | None = None
        cursor = index + 1
        while cursor < len(lines):
            closing = CLOSE_FENCE.match(lines[cursor])
            if closing is not None and len(closing.group(1)) >= fence_length:
                body = lines[index + 1 : cursor]
                break
            cursor += 1
        blocks.append(Block(index + 1, language, body))
        index = cursor + 1 if body is not None else len(lines)
    return blocks


def path_comment(line: str) -> str | None:
    for pattern in PATH_COMMENTS:
        match = pattern.match(line.strip())
        if match is not None:
            return match.group(1)
    return None


def split_runs(language: str, body: list[str]) -> list[tuple[int, list[str]]]:
    """Split body at elision markers. Returns (first body index, run lines)."""
    marker = "<!-- ... -->" if language == "xml" else "// ..."
    runs: list[tuple[int, list[str]]] = []
    start = 0
    for index, line in enumerate(body):
        if line.strip() == marker:
            runs.append((start, body[start:index]))
            start = index + 1
    runs.append((start, body[start:]))
    return runs


def check_excerpt(root: Path, language: str, rel_path: str, body: list[str]) -> str | None:
    runs = split_runs(language, body)
    if any(not run for _, run in runs):
        return "an elision marker must sit between two excerpt runs"
    try:
        source = (root / rel_path).read_text(encoding="utf-8").splitlines()
    except (OSError, UnicodeDecodeError):
        return f"{rel_path} does not exist or cannot be read"
    position = 0
    for offset, run in runs:
        found = -1
        last_start = len(source) - len(run)
        for candidate in range(position, last_start + 1):
            if source[candidate : candidate + len(run)] == run:
                found = candidate
                break
        if found < 0:
            # Block lines count the path comment as line 1.
            first = offset + 2
            last = offset + len(run) + 1
            return (
                f"block lines {first}-{last} are not a verbatim run of "
                f"{rel_path} after the previous run"
            )
        position = found + len(run)
    return None


def check_block(root: Path, lines: list[str], block: Block) -> str | None:
    if block.body is None:
        return "code fence is not closed"
    if block.language not in CHECKED_LANGUAGES:
        return None
    declares_module = block.language == "csharp" and any(
        "[ExpoModule" in line or "[ExpoSharedObject" in line for line in block.body
    )
    rel_path = path_comment(block.body[0]) if block.body else None
    if rel_path is not None:
        parts = rel_path.split("/")
        if rel_path.startswith("/") or ".." in parts:
            return f"{rel_path} is not a repo-relative path"
        if declares_module and not rel_path.startswith(EXEMPLAR_DIRECTORIES):
            return f"{rel_path} is not under an exemplar directory"
        return check_excerpt(root, block.language, rel_path, block.body[1:])
    if declares_module:
        return "a csharp sample with [ExpoModule or [ExpoSharedObject needs a path comment"
    if block.language == "csharp":
        above = lines[block.fence_line - 2] if block.fence_line >= 2 else ""
        if BACKED_BY.match(above) is None:
            return (
                "a csharp sample without a path comment needs a backed-by "
                "comment directly above it"
            )
    return None


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Check that guide code samples are verbatim excerpts of repo files."
    )
    parser.add_argument("guide", nargs="?", help="guide file to check")
    args = parser.parse_args()

    root = repo_root()
    guide_label = args.guide if args.guide is not None else DEFAULT_GUIDE
    guide_path = Path(args.guide) if args.guide is not None else root / DEFAULT_GUIDE
    try:
        lines = guide_path.read_text(encoding="utf-8").splitlines()
    except (OSError, UnicodeDecodeError) as error:
        print(f"cannot read {guide_label}: {error}", file=sys.stderr)
        return 2

    failures = 0
    for block in parse_blocks(lines):
        reason = check_block(root, lines, block)
        if reason is not None:
            print(f"{guide_label}:{block.fence_line}: {reason}")
            failures += 1
    if failures:
        return 1
    print("done")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
