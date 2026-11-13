#!/usr/bin/env python3
"""Check references to the living specs in docs/specs.

The reference forms and the rules are described in docs/specs/README.md,
section "References To Requirements".
"""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
from pathlib import Path


SPECS_DIR = "docs/specs/"
MAX_BYTES = 2_000_000

# Built by concatenation so that this file is not a reference itself.
REF_MARK = "@" + "ref"
REF_LINE = re.compile(re.escape(REF_MARK) + r"[ \t]+(.*)$")
REF_TARGET = re.compile(r'^(docs/specs/[A-Za-z0-9_./-]+\.md)(?: "([^"]+)")?$')
COMMENT_CLOSERS = ("-->", "*/")

HEADING = re.compile(r"^### Requirement: (.+?)\s*$")
FENCE = re.compile(r"^\s*(`{3,}|~{3,})")
# A citation in prose can wrap over lines, so it is matched on the whole text.
CITATION = re.compile(
    r"`### Requirement: ([^`]+)`" r"(?:[\s(]*(?:in\s+)?`([A-Za-z0-9_./-]+\.md)`)?"
)
SPEC_PATH = re.compile(r"docs/specs/[A-Za-z0-9_./-]*\.md")
BARE_MARKDOWN_NAME = re.compile(r"`([a-z0-9-]+(?:/[a-z0-9-]+)*\.md)`")


def repo_root() -> Path:
    return Path(__file__).resolve().parents[1]


def tracked_files(root: Path) -> list[str]:
    output = subprocess.run(
        ["git", "ls-files", "-z"], cwd=root, check=True, capture_output=True
    ).stdout
    return [name for name in output.decode("utf-8").split("\0") if name]


def read_text(root: Path, rel_path: str) -> str | None:
    path = root / rel_path
    try:
        if not path.is_file() or path.stat().st_size > MAX_BYTES:
            return None
        return path.read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError):
        return None


def requirement_headings(text: str) -> list[tuple[int, str]]:
    """Requirement headings outside code fences, as (line number, name)."""
    headings: list[tuple[int, str]] = []
    fence: str | None = None
    for number, line in enumerate(text.splitlines(), 1):
        marker = FENCE.match(line)
        if marker is not None:
            kind = marker.group(1)[0]
            if fence is None:
                fence = kind
            elif fence == kind:
                fence = None
            continue
        if fence is None:
            heading = HEADING.match(line)
            if heading is not None:
                headings.append((number, heading.group(1)))
    return headings


def line_of(text: str, offset: int) -> int:
    return text.count("\n", 0, offset) + 1


def resolve_spec(name: str, rel_path: str, specs: dict[str, set[str]]) -> str | None:
    """Resolve a file name from prose to a spec path, or None if it is not one."""
    own_dir = rel_path.rsplit("/", 1)[0] + "/" if "/" in rel_path else ""
    for candidate in (name, SPECS_DIR + name, own_dir + name):
        if candidate in specs:
            return candidate
    return None


def check(root: Path, files: list[str]) -> list[str]:
    """Return one message per broken reference in the given repo-relative files."""
    failures: list[str] = []
    texts: dict[str, str] = {}
    for rel_path in files:
        text = read_text(root, rel_path)
        if text is not None:
            texts[rel_path] = text

    # Requirement names per spec file. A name is unique across all specs, so a
    # citation by name alone has one target.
    specs: dict[str, set[str]] = {}
    owner: dict[str, str] = {}
    for rel_path in sorted(texts):
        if not (rel_path.startswith(SPECS_DIR) and rel_path.endswith(".md")):
            continue
        specs[rel_path] = set()
        for number, name in requirement_headings(texts[rel_path]):
            if name in owner:
                failures.append(
                    f'{rel_path}:{number}: requirement "{name}" is also in {owner[name]}'
                )
            else:
                owner[name] = rel_path
            specs[rel_path].add(name)

    for rel_path in sorted(texts):
        text = texts[rel_path]

        for number, line in enumerate(text.splitlines(), 1):
            for match in REF_LINE.finditer(line):
                target = match.group(1).strip()
                for closer in COMMENT_CLOSERS:
                    if target.endswith(closer):
                        target = target[: -len(closer)].strip()
                parsed = REF_TARGET.match(target)
                if parsed is None:
                    failures.append(
                        f"{rel_path}:{number}: {REF_MARK} must be "
                        f'{REF_MARK} docs/specs/<file>.md "Requirement Name" on one line'
                    )
                    continue
                spec, name = parsed.group(1), parsed.group(2)
                if spec not in specs:
                    failures.append(f"{rel_path}:{number}: {spec} is not a spec file")
                elif name is not None and name not in specs[spec]:
                    hint = f" (it is in {owner[name]})" if name in owner else ""
                    failures.append(
                        f'{rel_path}:{number}: {spec} has no requirement "{name}"{hint}'
                    )

        for match in SPEC_PATH.finditer(text):
            if match.group(0) not in specs:
                failures.append(
                    f"{rel_path}:{line_of(text, match.start())}: "
                    f"{match.group(0)} is not a spec file"
                )

        if not rel_path.endswith(".md"):
            continue

        for match in CITATION.finditer(text):
            number = line_of(text, match.start())
            name = " ".join(match.group(1).split())
            if name not in owner:
                failures.append(f'{rel_path}:{number}: no spec has requirement "{name}"')
                continue
            if match.group(2) is None:
                continue
            spec = resolve_spec(match.group(2), rel_path, specs)
            if spec is not None and name not in specs[spec]:
                failures.append(
                    f'{rel_path}:{number}: requirement "{name}" is in {owner[name]}, '
                    f"not in {spec}"
                )

        if rel_path.startswith(SPECS_DIR):
            for match in BARE_MARKDOWN_NAME.finditer(text):
                name = match.group(1)
                if resolve_spec(name, rel_path, specs) is None and name not in texts:
                    failures.append(
                        f"{rel_path}:{line_of(text, match.start())}: "
                        f"{name} is not a spec file or a repo file"
                    )

    return sorted(set(failures))


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Check that references to docs/specs requirements resolve."
    )
    parser.parse_args()

    root = repo_root()
    failures = check(root, tracked_files(root))
    for failure in failures:
        print(failure)
    if failures:
        return 1
    print("done")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
