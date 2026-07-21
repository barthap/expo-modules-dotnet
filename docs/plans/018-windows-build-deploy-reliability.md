# Plan 018: Windows RNW build/deploy reliability — CLI launch, PDB locking, ReactNativeDir resolver

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving to the
> next step. If anything in the "STOP conditions" section occurs, stop and
> report — do not improvise. When done, update the status row for this plan
> in `docs/plans/README.md` — unless a reviewer dispatched you and told you
> they maintain the index.
>
> **Prerequisite**: This plan requires a Windows machine with VS 2026 and the
> RNW 0.81 toolchain (referred to as `<windows-test-machine>`). If you are
> not running on one, STOP immediately and report — nothing here can be
> verified from macOS/Linux.
>
> **Drift check (run first)**:
> `git diff --stat ea07d69d..HEAD -- apps/desktop-app/windows packages/expo-modules-dotnet/windows packages/expo-modules-dotnet-autolinking`
> If any in-scope file changed since this plan was written, compare the
> "Current state" excerpts against the live code before proceeding; on a
> mismatch, treat it as a STOP condition.

## Status

- **Priority**: P1 (reliability) / P2 (resolver)
- **Effort**: L (investigation-heavy; fix size unknown until reproduced;
  includes resolver implementation)
- **Risk**: MED (Windows-only, tooling-dependent, partly unreproduced)
- **Depends on**: none
- **Category**: bug / dx / direction
- **Planned at**: commit `ea07d69d`, 2026-07-20
- **Execution status**: BLOCKED on 2026-07-22 because the current executor is
  on macOS and the plan's Windows + VS 2026 + RNW 0.81 prerequisite is absent.
  No implementation was attempted.

## Why this matters

The repo exists as the `expo-modules-windows` successor, and Windows is the
flagship adapter — but the RNW CLI build/deploy path is unreliable in ways
that are known only anecdotally: "RNW CLI launch reliability and VS/PDB
locking issues" (roadmap, Backlog: Platform Adapters). Direct MSBuild works
and is what CI-less validation uses today; the CLI path a real app developer
would use does not reliably launch or rebuild. Separately, the Windows
adapter leans on RNW property sheets for include paths, and the roadmap
requires that `ReactNativeDir` resolve the consuming app's actual
`react-native` package instead of the sibling-path workaround that commit
`3c64fb4d` explicitly must not become. This plan turns the reliability
issues from folklore into recorded reproductions with fixes, and ships the
proper resolver: designed, operator-approved, implemented, and tested — per
the AGENTS.md "Maturity" rule, no design-only deliverables and no
temporary shortcuts.

## Current state

(At `ea07d69d`. This area is less pinned-down than other plans — the first
deliverable is precisely to record what is actually broken.)

- Roadmap statements this plan executes against (`docs/roadmap.md`):
  - "the RNW CLI build/deploy path still has VS 2026/PDB locking follow-up
    work" (Current Baseline).
  - "P1 — Windows build/deploy reliability: ... remaining production work
    includes RNW CLI launch reliability and VS/PDB locking issues."
  - "P2 — Windows `ReactNativeDir` resolution: The adapter currently relies
    on RNW property sheets for the JSI and CallInvoker include paths. Future
    Windows adapter work that needs `ReactNativeDir` must resolve the
    consuming app's selected `react-native` package, not walk from the
    physical package directory or assume it is a sibling of
    `react-native-windows`. Design an app-scoped Node resolver/config-plugin
    or an RNW target-provided property that works for pnpm monorepos,
    independently versioned desktop/mobile apps, and direct MSBuild
    invocations. Context: commit `3c64fb4d...` used the sibling-path
    workaround that must not become the general resolver."
- Commit `3c64fb4d` ("fix(windows): resolve React Native headers from RNW
  targets") touched `docs/specs/runtime-and-abi.md`, an autolinking test
  (`windowsMsbuildTarget.test.ts`), and
  `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ExpoModulesDotnet.vcxproj`
  — headers now come through RNW build targets. Read it
  (`git show 3c64fb4d`) before the resolver design; it is the current-state
  baseline the design must not regress.
- `apps/desktop-app/windows` — the RNW 0.81 example app (direct MSBuild is
  the validated path).
- `packages/expo-modules-dotnet/windows` — the Windows adapter
  (`ExpoModulesDotnet.vcxproj` and friends).
- `packages/expo-modules-dotnet-autolinking` — the CLI that stages HostFXR
  artifacts; `src/__tests__/windowsMsbuildTarget.test.ts` covers MSBuild
  target selection.
- The repo-local skill `rnw-windows-setup` (if available in your
  environment) is the canonical guide for RNW build/deploy/autolinking
  failures on this app — use it throughout.

## Commands you will need

| Purpose | Command | Expected on success |
|---|---|---|
| Install | `pnpm install --frozen-lockfile` | exit 0 |
| Autolinking tests | `pnpm --filter expo-modules-dotnet-autolinking test` (verify the exact filter name from the package.json before running) | exit 0 |
| Direct MSBuild build | per `rnw-windows-setup` skill / `apps/desktop-app` README | app builds and launches |
| RNW CLI path | `pnpm --filter desktop-app windows` or the repo's documented equivalent (check `apps/desktop-app/package.json` scripts) | today: unreliable — that's the subject |
| Formatting | `scripts/format.sh --check --all` | exit 0 |

## Suggested executor toolkit

- Skill `rnw-windows-setup` — read before any build attempt.
- `git show 3c64fb4d` — resolver baseline.
- `docs/changes/2026-07-18-rnw-081-header-resolution/` (if still present)
  and `docs/specs/runtime-and-abi.md` Windows sections.

## Scope

**In scope** (the only files you should modify or create):

- `apps/desktop-app/windows/` — build/launch configuration fixes only
- `packages/expo-modules-dotnet/windows/` — adapter project/props fixes only
- `packages/expo-modules-dotnet-autolinking/` — only if a CLI fix falls out
  of the reproduction (plus its tests)
- `docs/changes/2026-<mm-dd>-windows-reliability/` (create) — reproduction
  notes, resolver design doc, and the spec delta
- `docs/specs/runtime-and-abi.md` — Windows resolution contract (step 4)
- `docs/roadmap.md` — Windows bullets (step 5)
- `docs/plans/README.md` (status row only)

**Out of scope** (do NOT touch, even though they look related):

- RNW version bumps (0.81 is the pinned lane), VS toolset changes, or
  Windows CI lanes.
- `Expo.JSI` / managed core / ABI — nothing here may leak into portable code.
- macOS/iOS/Android adapter code.

## Git workflow

- Branch: `advisor/018-windows-reliability` off `development`.
- Commit style: `fix(windows): ...` / `docs(windows): ...`.
- Do NOT push or open a PR unless the operator instructed it.

## Steps

### Step 1: Record the reproductions

On `<windows-test-machine>`, run (a) the direct MSBuild path and (b) the RNW
CLI path, each from clean and from incremental state, plus a
rebuild-while-app-running attempt to provoke the PDB locking failure. Record
in `docs/changes/2026-<mm-dd>-windows-reliability/repro.md`: exact command,
environment (VS version, RNW version), expected result, actual result, and
the verbatim error for every failure. Quote errors exactly. No machine
names, usernames, or absolute local paths in the committed notes — use
`<windows-test-machine>` and `<repo>` placeholders.

**Verify**: `repro.md` committed with at least the CLI-launch and PDB-lock
attempts recorded (whether they failed or not — "could not reproduce" is a
valid, valuable result).

### Step 2: Classify and fix the bounded failures

For each recorded failure, classify: (A) fixable in-repo (project/props/CLI
staging bug), (B) upstream RNW/VS tooling issue to document and work around,
(C) not reproducible. Fix class-A items with the smallest change, one commit
each, re-running the failing scenario after each fix. For class-B items,
document the workaround in the repro notes (and in the `rnw-windows-setup`
skill's territory — note it for the operator rather than editing the skill).

**Verify**: each class-A fix's failing scenario now passes; autolinking
tests → exit 0; direct MSBuild path still builds and launches.

### Step 3: ReactNativeDir resolver design + operator decision

Write `docs/changes/<same-folder>/resolver-design.md`: 2-3 candidate designs
(app-scoped Node resolver script feeding an MSBuild property; an RNW
target-provided property if RNW 0.81 exposes one — verify against the
installed RNW targets, name the exact target/property; config-plugin-owned
props file), each evaluated against the three required environments (pnpm
monorepo, independently versioned desktop/mobile apps, direct MSBuild
invocation), with a recommendation. The sibling-path walk from `3c64fb4d`
must be listed as explicitly rejected. Present to the operator and get the
option chosen before implementing.

**Verify**: design doc committed; operator's chosen option recorded in the
doc.

### Step 4: Implement the chosen resolver

Implement the approved design so the Windows adapter resolves the consuming
app's selected `react-native` package in all three required environments.
Expected shape (adjust to the chosen option): resolution logic in
`packages/expo-modules-dotnet-autolinking` (with unit tests next to
`windowsMsbuildTarget.test.ts` covering the three environments) feeding an
MSBuild property consumed by
`packages/expo-modules-dotnet/windows/ExpoModulesDotnet.vcxproj`/props.
Remove any remaining sibling-path assumption the implementation supersedes.
Update the Windows sections of `docs/specs/runtime-and-abi.md` through the
living-spec workflow (delta in the same `docs/changes/` folder, merged on
completion) since this changes the documented resolution contract.

**Verify**: autolinking tests → exit 0 including new resolver tests; direct
MSBuild build of `apps/desktop-app/windows` succeeds; RNW CLI path succeeds
(or matches its step-2 recorded state if a class-B blocker remains — say
which).

### Step 5: Close out

Update `docs/roadmap.md` Windows bullets: reliability items fixed/documented
and the P2 `ReactNativeDir` entry marked complete. Archive the
`docs/changes/` folder per the living-spec skill. Formatting check.

**Verify**: `scripts/format.sh --check --all` → exit 0.

## Test plan

- Class-A fixes: the recorded failing scenario re-run is the test; where a
  fix lands in the autolinking CLI, add a unit test next to
  `windowsMsbuildTarget.test.ts` following its structure.
- Resolver: unit tests for the resolution logic covering pnpm monorepo,
  independently versioned apps, and direct MSBuild property injection —
  modeled after `windowsMsbuildTarget.test.ts`.
- No new managed/native tests expected — this plan is tooling reliability.

## Done criteria

Machine-checkable / evidence-backed. ALL must hold:

- [ ] `repro.md` exists with recorded attempts for CLI launch and PDB
      locking (verbatim errors or "not reproducible")
- [ ] Every class-A failure has a commit + re-run evidence; autolinking
      tests exit 0
- [ ] Direct MSBuild path still builds and launches (evidence in report)
- [ ] `resolver-design.md` exists with per-environment evaluation and the
      operator's chosen option recorded
- [ ] The chosen resolver is implemented with passing unit tests for all
      three environments; no sibling-path assumption remains
      (`git grep -n 'react-native-windows/..' packages/expo-modules-dotnet/windows` → no resolver-related matches)
- [ ] `docs/specs/runtime-and-abi.md` Windows resolution contract updated
      and the change folder archived
- [ ] `scripts/format.sh --check --all` exits 0
- [ ] No committed file contains local absolute paths, usernames, or
      machine names (`git grep -n 'Users/\|C:\\\\Users' -- docs/changes` → no matches)
- [ ] No files outside the in-scope list modified (`git status`)
- [ ] `docs/plans/README.md` status row updated

## STOP conditions

Stop and report back (do not improvise) if:

- You are not on a Windows machine with VS 2026 + RNW 0.81 (check first).
- A fix appears to require changing portable core, ABI, or RNW version.
- A failure's root cause is inside RNW/VS itself and the only in-repo
  "fix" would be a fragile workaround — document it (class B) instead of
  patching, and flag it.
- The RNW CLI path is broken in a way that predates this repo's integration
  (pure upstream breakage) — record and stop; do not fork the CLI.
- A step's verification fails twice after a reasonable fix attempt.

## Maintenance notes

- Reviewer should scrutinize: any vcxproj/props change against direct
  MSBuild AND CLI paths (both must keep working), and that repro notes are
  scrubbed of machine-specific paths.
- A future Windows CI lane (roadmap: "RNW as a separate workflow") should
  encode whatever scenarios step 1 recorded as its smoke checks.
