# Plan 001: Add checks.yml — fast CI lane on ubuntu + windows (no native code)

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving to the
> next step. If anything in the "STOP conditions" section occurs, stop and
> report — do not improvise. When done, update the status row for this plan
> in `docs/plans/README.md` — unless a reviewer dispatched you and told you they
> maintain the index.
>
> **Drift check (run first)**: `git diff --stat 0f6fc760..HEAD -- scripts/format.sh scripts/format.py package.json pnpm-workspace.yaml packages/expo-modules-dotnet-autolinking/package.json packages/expo-modules-dotnet/package.json`
> If any in-scope file changed since this plan was written, compare the
> "Current state" excerpts against the live code before proceeding; on a
> mismatch, treat it as a STOP condition.

## Status

- **Priority**: P1
- **Effort**: S
- **Risk**: LOW — workflow YAML only, zero repo code changes
- **Depends on**: none
- **Category**: dx
- **Planned at**: commit `0f6fc760`, 2026-07-08 (design agreed with operator same day)

## Operator-decided CI architecture (do not deviate)

Two workflows split by cost profile:

1. **`checks.yml`** (THIS PLAN) — fast lane, cheap runners, OS matrix
   `[ubuntu-latest, windows-latest]` where it adds signal. NO macOS runners.
   NO Hermes, NO native builds.
2. **`native-tests.yml`** (plan 008) — heavy lane: Hermes cache, native
   testhost, full managed suite. Linux first; the future
   `os × loader (hostfxr/nativeaot)` matrix and a `hermes-console-app`
   E2E job grow THERE, not here.

`experiments/*` are hello-world smoke apps — CI must ignore them entirely.

## Why this matters

No CI exists (`.github/` absent); all verification is manual. This lane
enforces everything that needs no native toolchain, immediately: formatting,
mobile typecheck, autolinking CLI tests, adapter TS tests, and the Roslyn
generator test suite. Windows coverage here is not decoration: the autolinking
CLI is path-manipulation code (classic `path.sep` bug territory) and the
generator (`ExpoModulesGenerator.cs`) is the most-churned file in the repo —
both get Windows verification for the cost of a 2× minutes multiplier on
short jobs.

## Current state

- `.github/` — does not exist.
- Fast checks available (all verified locally on macOS at plan time):
  - `pnpm --filter mobile-app typecheck`
  - `pnpm --filter expo-modules-dotnet-autolinking test` (vitest)
  - `pnpm --filter expo-modules-dotnet test` (vitest — `"test": "vitest run"`
    at `packages/expo-modules-dotnet/package.json:9`)
  - `scripts/format.sh --check --all` → `scripts/format.py`; needs
    clang-format, cmake-format (env overrides `CLANG_FORMAT_BIN`,
    `CMAKE_FORMAT_BIN`), dotnet SDK, prettier.
  - Generator tests are PURE MANAGED (no native testhost): the "TestHost" in
    `Expo.ModulesCore.Generator.Tests/GeneratorTestHost.cs` is a Roslyn
    in-memory harness. Runnable via
    `dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj`.
    (`Expo.JSI.Tests` and `Expo.ModulesCore.Tests` DO need the native
    testhost — they belong to plan 008, NOT here.)
- Package manager: pnpm workspace (`pnpm-workspace.yaml`, `pnpm-lock.yaml`).
  Check root `package.json` for a `packageManager` field / `engines` to pin
  Node+pnpm versions in the workflow; if absent use Node 22 + latest pnpm.
- dotnet SDK version: check `global.json` at repo root; if absent, infer from
  csproj TargetFramework (`net10.0` per Expo.ModulesCore build outputs).
- Repo commit convention: conventional-commit-ish (`ci: ...`).

## Commands you will need

| Purpose | Command | Expected on success |
|---|---|---|
| Install | `pnpm install --frozen-lockfile` | exit 0 |
| Typecheck | `pnpm --filter mobile-app typecheck` | exit 0 |
| CLI tests | `pnpm --filter expo-modules-dotnet-autolinking test` | all pass |
| TS tests | `pnpm --filter expo-modules-dotnet test` | all pass |
| Generator tests | `dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj` | all pass |
| Format check | `scripts/format.sh --check --all` | exit 0 |
| YAML sanity | `python3 -c "import yaml; yaml.safe_load(open('.github/workflows/checks.yml'))"` | exit 0 |

## Scope

**In scope** (create/modify only):
- `.github/workflows/checks.yml` (create)
- `docs/plans/README.md` (status row)

**Out of scope** (do NOT touch):
- ANY repo code or script — this plan changes zero source files. If a check
  fails on Windows (e.g. a path bug in CLI tests), that is a FINDING to
  report, not something to fix in this plan.
- `native-tests.yml`, Hermes, testhost — plan 008.
- macOS runners anywhere.
- `experiments/*` — ignored by CI.
- Pre-commit hooks, release/publish workflows.

## Git workflow

- Branch: `advisor/001-ci-checks`
- Commit: `ci: add checks workflow (ubuntu + windows fast lane)`
- Do NOT push or open a PR unless the operator instructed it. Note: Windows
  jobs can only be truly verified by a pushed run — tell the operator when
  the branch is ready to push.

## Steps

### Step 1: Write `checks.yml`

`on: [push, pull_request]` (dedupe with a `concurrency` group keyed on ref,
`cancel-in-progress: true`). Jobs:

1. **node** — `strategy.matrix.os: [ubuntu-latest, windows-latest]`.
   Steps: checkout → pnpm/action-setup + setup-node (versions per Current
   state) → `pnpm install --frozen-lockfile` →
   `pnpm --filter mobile-app typecheck` →
   `pnpm --filter expo-modules-dotnet-autolinking test` →
   `pnpm --filter expo-modules-dotnet test`.
2. **generator-tests** — same OS matrix. Steps: checkout →
   `actions/setup-dotnet` (SDK per Current state) → the `dotnet test`
   command from the table.
3. **format** — `ubuntu-latest` only. Steps: checkout → setup-dotnet →
   setup-node/pnpm + `pnpm install --frozen-lockfile` (prettier) → install
   clang-format (`sudo apt-get install -y clang-format`) and cmake-format
   (`pipx install cmakelang` or `pip install cmakelang`; export
   `CMAKE_FORMAT_BIN` if the binary name differs) →
   `scripts/format.sh --check --all`.

Windows shell note: repo scripts are bash; the Windows jobs above deliberately
use only pnpm/dotnet commands (no `scripts/*.sh`), so default shells are fine.

**Verify**: YAML sanity command → exit 0.

### Step 2: Local dry-run of every command

Run all six commands from the table locally, in a clean state
(`pnpm install --frozen-lockfile` first).

**Verify**: every command exits 0. If format check flags pre-existing files:
STOP condition.

### Step 3: Hand off for the push run

Report to the operator: branch ready; Windows matrix legs need a pushed run to
verify. If the operator pushes and a Windows leg fails, triage: workflow bug →
fix here; repo code path bug on Windows → report as a finding, leave the job
in place (red is information), do not patch source.

**Verify**: operator informed; on a pushed run, both matrix legs of `node` and
`generator-tests` plus `format` are green (or Windows failures are reported
as findings).

## Test plan

No new test code. The workflow IS the deliverable; verification is Step 2's
local run plus the pushed run.

## Done criteria

- [ ] `.github/workflows/checks.yml` exists, valid YAML; jobs `node` +
      `generator-tests` (both `[ubuntu-latest, windows-latest]`) and `format`
      (ubuntu only); concurrency group set.
- [ ] `grep -i macos .github/workflows/checks.yml` → no matches.
- [ ] `grep -i "experiments" .github/workflows/checks.yml` → no matches.
- [ ] All six table commands exit 0 locally.
- [ ] No files outside in-scope list modified (`git status`).
- [ ] `docs/plans/README.md` status row updated.

## STOP conditions

Stop and report back (do not improvise) if:

- Any table command fails locally BEFORE workflow work (baseline broken).
- `scripts/format.sh --check --all` flags pre-existing files.
- The generator test project turns out to require the native testhost after
  all (contradicts Current state).
- You are tempted to edit any source file to make Windows pass.

## Maintenance notes

- Plan 008 adds `native-tests.yml`; keep this file free of native concerns
  forever — the split by cost profile is the architecture.
- If CLI-test Windows legs reveal path bugs, that validates the Windows
  matrix decision — file the finding, fix in a dedicated change with a test.
- Reviewer: pin action versions (`@v4` etc.), check `--frozen-lockfile`
  everywhere, confirm concurrency cancellation is on.
