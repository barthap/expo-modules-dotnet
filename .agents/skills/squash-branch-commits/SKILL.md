---
name: squash-branch-commits
description: Use when asked to squash, condense, rewrite, curate, or regroup Git branch commits before merge/rebase/fast-forward, especially when history contains many small commits, fixups, review follow-ups, bad subjects, empty commits, or unrelated outlier commits that should be separated.
---

# Squash Branch Commits

## Overview

Turn noisy branch history into reviewable commits without changing the final
tree unless the user explicitly asks to drop or move changes. Preserve useful
original commit-message detail in aggregate commit bodies.

## Guardrails

- Never push, PR, or publish. Rewriting is local only unless the user explicitly
  approves a remote action.
- Require a clean worktree before rewriting. Stop if uncommitted changes exist.
- Create a local backup branch before destructive history edits.
- Treat unrelated commits as first-class findings. Do not hide them inside a
  broad squash.
- Prefer 4-8 coherent commits for large branches, but let the branch shape win.
- Do not commit local absolute paths, usernames, private hostnames, or
  machine-specific install paths.

## Workflow

1. Establish the base:

```sh
git status --short --branch
git fetch origin main
git merge-base origin/main HEAD
```

If the branch is based on something other than `origin/main`, say so and use
the correct base only after confirming.

2. Inventory the branch. Run the bundled script:

```sh
python3 .agents/skills/squash-branch-commits/scripts/analyze_branch_commits.py --base origin/main
```

Also inspect original messages with bodies:

```sh
git log --format='%h %s%n%b%x1e' --reverse origin/main..HEAD
```

3. Classify commits:

- **Mainline feature slices:** commits that share purpose, files, specs/tests,
  or implementation dependency.
- **Fixups/follow-ups:** subjects like `fix`, `address review`, `format`,
  `update specs`, vague subjects, or commits that only adjust files introduced
  by an earlier slice. Fold these into the owning slice.
- **Design/docs lifecycle:** delta spec, plan, final spec merge, and archive
  commits belong with the feature they describe unless the final branch policy
  wants docs first.
- **Outliers:** empty commits, unrelated skill/tooling/docs, commits touching
  disjoint paths with no dependency on the main feature, or commits whose
  subject/body describes a different task. Keep as separate commits, move to a
  side branch, or drop only with user approval.

For outliers, state the evidence: commit hash, subject, changed paths, and why
it does not belong to the main branch story.

4. Propose the new history before rewriting:

```text
Base: <base-short>
Backup: backup/<branch>-before-squash-<date>
Target commits:
1. <subject> - folds <hashes>; body will include <key details>
2. ...
Outliers:
- <hash> <subject>: keep separate / move / drop? evidence...
Verification:
- final tree equals backup unless approved exclusions change it
- new log count and subjects
- worktree clean
```

Ask only for decisions that affect content: dropping/moving outliers, changing
the base, or accepting a non-identical final tree.

5. Rewrite. For simple path-separated branches, use a mixed reset and staged
path commits:

```sh
git branch backup/<branch>-before-squash-<yyyymmdd> HEAD
git rev-parse HEAD^{tree}
git reset --mixed <base>
git add <paths-for-slice-1>
git commit -m '<subject>'
...
```

For complex branches where slices overlap the same files, use interactive
rebase or `git commit-tree` only after proving each intermediate tree is
intentional. Do not rely on subject order alone; inspect patches and messages.

6. Write aggregate commit messages:

- Subject names the durable outcome, not the cleanup mechanics.
- Body summarizes the original important details as bullets.
- Mention verification limitations carried from original commits, e.g.
  "Windows build proof pending on test machine".
- Omit one-off session URLs and tool footers unless the user explicitly wants
  them.
- Replace vague or inappropriate original subjects with professional subjects.

7. Verify before claiming completion:

```sh
git rev-parse <backup>^{tree} HEAD^{tree}
git diff --exit-code <backup> HEAD
git status --short --branch
git log --oneline --reverse <base>..HEAD
git log --format='%B' <base>..HEAD | rg '/Users/|~/|/private/|dev/projects|hostname|machine'
```

If outliers were intentionally dropped or moved, do not run the equality check
as proof of success. Instead, compare the kept tree against the planned kept
diff and report the exact intentional difference.

## Current-Branch Verification Pattern

On a branch with about 20 commits where two are unrelated, the inventory should
make the outliers visible before rewriting. Expected examples:

- Empty or pathless docs commits are suspicious and need message/body review.
- Standalone `docs/specs` commits near the end of an implementation branch are
  suspicious when the subject points to a different direction than the main
  feature.
- A commit adding `.agents/skills/<other-skill>/...` is likely unrelated to a
  module-events implementation branch unless the user says the skill is part of
  the deliverable.

The skill succeeds when it can explain those outliers, fold fixups into their
owning feature slices, and preserve the final tree after the rewrite.
