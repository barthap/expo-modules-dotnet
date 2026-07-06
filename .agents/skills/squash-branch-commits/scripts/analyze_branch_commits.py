#!/usr/bin/env python3
"""Inventory branch commits for manual squash planning.

This script is intentionally read-only. It does not decide the final history;
it surfaces path overlap, empty commits, and likely follow-ups/outliers so the
agent can make an evidence-backed plan.
"""

from __future__ import annotations

import argparse
import collections
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path


FOLLOWUP_WORDS = (
    "fix",
    "follow",
    "review",
    "format",
    "cleanup",
    "address",
    "update",
    "final",
    "green",
    "wip",
)
BAD_SUBJECT_WORDS = ("commit ", "kurwa", "stuff", "misc", "changes")


@dataclass
class Commit:
    sha: str
    subject: str
    body: str
    paths: list[str]
    tokens: set[str]


def git(args: list[str]) -> str:
    result = subprocess.run(
        ["git", *args],
        check=False,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    if result.returncode != 0:
        raise SystemExit(result.stderr.strip() or f"git {' '.join(args)} failed")
    return result.stdout


def path_token(path: str) -> str:
    parts = Path(path).parts
    if not parts:
        return "<empty>"
    if parts[0] == ".agents" and len(parts) >= 3:
        return "/".join(parts[:3])
    if parts[0] == "docs" and len(parts) >= 3 and parts[1] in {"changes", "archive"}:
        return "/".join(parts[:3])
    if parts[0] == "docs" and len(parts) >= 2:
        return "/".join(parts[:2])
    if parts[0] == "packages" and len(parts) >= 3:
        return "/".join(parts[:3])
    if parts[0] == "apps" and len(parts) >= 2:
        return "/".join(parts[:2])
    return parts[0]


def load_commits(base: str, head: str) -> list[Commit]:
    shas = [line for line in git(["rev-list", "--reverse", f"{base}..{head}"]).splitlines() if line]
    commits: list[Commit] = []
    for sha in shas:
        subject = git(["show", "-s", "--format=%s", sha]).strip()
        body = git(["show", "-s", "--format=%b", sha]).strip()
        paths = [
            line
            for line in git(["diff-tree", "--no-commit-id", "--name-only", "-r", sha]).splitlines()
            if line
        ]
        tokens = {path_token(path) for path in paths}
        commits.append(Commit(sha=sha, subject=subject, body=body, paths=paths, tokens=tokens))
    return commits


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", default="origin/main", help="base revision, default: origin/main")
    parser.add_argument("--head", default="HEAD", help="head revision, default: HEAD")
    args = parser.parse_args()

    commits = load_commits(args.base, args.head)
    token_counts = collections.Counter(token for commit in commits for token in commit.tokens)
    dominant = {token for token, count in token_counts.items() if count >= 2}

    print(f"Base: {args.base}")
    print(f"Head: {args.head}")
    print(f"Commit count: {len(commits)}")
    print()

    print("Dominant path tokens:")
    for token, count in token_counts.most_common():
        marker = "*" if token in dominant else "-"
        print(f"  {marker} {token}: {count}")
    print()

    print("Commit inventory:")
    for index, commit in enumerate(commits, start=1):
        short = commit.sha[:8]
        tokens = ", ".join(sorted(commit.tokens)) if commit.tokens else "<no changed paths>"
        flags: list[str] = []
        lower_subject = commit.subject.lower()
        if not commit.paths:
            flags.append("empty/no-path commit")
        if any(word in lower_subject for word in FOLLOWUP_WORDS):
            flags.append("follow-up subject")
        if any(word in lower_subject for word in BAD_SUBJECT_WORDS):
            flags.append("bad/vague subject")
        if commit.tokens and not (commit.tokens & dominant):
            flags.append("path-isolated")
        if any(token.startswith(".agents/skills/") for token in commit.tokens):
            flags.append("skill/tooling path")
        if commit.tokens == {"docs/specs"}:
            flags.append("standalone stable-docs commit")

        print(f"{index:02d}. {short} {commit.subject}")
        print(f"    tokens: {tokens}")
        if flags:
            print(f"    flags: {', '.join(flags)}")
        if commit.body:
            first_body_line = commit.body.splitlines()[0]
            print(f"    body: {first_body_line[:120]}")
        for path in commit.paths[:8]:
            print(f"    - {path}")
        if len(commit.paths) > 8:
            print(f"    ... {len(commit.paths) - 8} more paths")
    print()

    print("Likely outlier candidates:")
    candidates = [
        commit
        for commit in commits
        if not commit.paths
        or (commit.tokens and not (commit.tokens & dominant))
        or any(token.startswith(".agents/skills/") for token in commit.tokens)
        or commit.tokens == {"docs/specs"}
    ]
    if not candidates:
        print("  none flagged by path/empty heuristics")
    for commit in candidates:
        print(f"  - {commit.sha[:8]} {commit.subject}")

    print()
    print("Likely follow-up candidates:")
    followups = [
        commit
        for commit in commits
        if any(word in commit.subject.lower() for word in FOLLOWUP_WORDS + BAD_SUBJECT_WORDS)
    ]
    if not followups:
        print("  none flagged by subject heuristics")
    for commit in followups:
        print(f"  - {commit.sha[:8]} {commit.subject}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
