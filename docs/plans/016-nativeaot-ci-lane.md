# Plan 016: NativeAOT loader lane in CI (hermes-console-app, hostfxr + nativeaot)

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving to the
> next step. If anything in the "STOP conditions" section occurs, stop and
> report — do not improvise. When done, update the status row for this plan
> in `docs/plans/README.md` — unless a reviewer dispatched you and told you
> they maintain the index.
>
> **Drift check (run first)**:
> `git diff --stat ea07d69d..HEAD -- .github/workflows/native-tests.yml scripts/run-hermes-console-app.sh apps/hermes-console-app`
> If any in-scope file changed since this plan was written, compare the
> "Current state" excerpts against the live code before proceeding; on a
> mismatch, treat it as a STOP condition.

## Status

- **Priority**: P1
- **Effort**: M
- **Risk**: LOW-MED (CI plumbing; the loader paths themselves already exist)
- **Depends on**: none
- **Category**: tests / dx
- **Planned at**: commit `ea07d69d`, 2026-07-20

## Why this matters

NativeAOT compatibility is a hard repo constraint (AGENTS.md: "keep ABI and
generated bindings NativeAOT-compatible"), but CI only exercises the managed
suite under the ordinary CoreCLR test runner. The advisor roadmap assessment
flagged this as the biggest strategic gap: the ABI has grown to v23 (weak
objects, ArrayBuffer, typed events) with no automated NativeAOT proof. The
proof vehicle already exists — `apps/hermes-console-app` runs the bridge
end-to-end through either HostFXR or NativeAOT — it just never runs in CI.
After this plan, every push proves both loader paths on Linux, so an
AOT-incompatible change (reflection, unsupported marshalling, trimmed export)
fails a PR instead of surfacing weeks later on a device.

## Current state

(At `ea07d69d`.)

- `.github/workflows/native-tests.yml` — single `managed-tests` job,
  `os: [ubuntu-24.04, windows-latest]` matrix, 79 lines. Lines 23-24:
  ```yaml
  # TODO: add loader: [hostfxr, nativeaot] dimension once NativeAOT
  # end-to-end proof lands (see docs/plans/README.md maintenance notes).
  ```
  The job caches a Hermes prebuilt at `build/hermes` keyed on
  `hermes-${{ runner.os }}-${{ hashFiles('scripts/hermes-ref.txt') }}`,
  builds Hermes on cache miss (`scripts/build-hermes-linux.sh` with
  `cmake ninja-build clang python3 libicu-dev` installed via apt), then runs
  `scripts/test-managed.sh` (Linux) / `scripts/test-managed.ps1` (Windows).
- `scripts/run-hermes-console-app.sh` — builds and runs the console app.
  Key behavior (read the script fully before editing anything):
  - `EXPO_JSI_DOTNET_LOADER` env var: `hostfxr` (default) or `nativeaot`;
    validated at line 38-39.
  - Hermes prebuilt root: `HERMES_PREBUILT_ROOT`, default
    `<repo>/build/hermes/source/destroot` (line 7) — same location the CI
    cache restores.
  - `nativeaot` path: `publish_managed_nativeaot()` (line 93+) runs
    `dotnet publish -r <rid> /p:PublishAot=true /p:NativeLib=Shared` and the
    native host loads exported entry points from the published shared
    library; `nativeaot_rid()` (line 54) picks the RID.
  - Supports `--no-run` and `-- <args>`.
- `apps/hermes-console-app/README.md` documents both loader paths (lines
  40-60) and states the native CMake flag `EXPO_JSI_DOTNET_LOADER` mirrors
  the env var.
- `apps/hermes-console-app/{managed,native}` — the app itself. Do not modify
  it; if it fails, that is a finding, not something to patch around.
- Convention: workflow style, cache keys, and step naming should match the
  existing `managed-tests` job in the same file.

## Commands you will need

| Purpose | Command (repo root) | Expected on success |
|---|---|---|
| Console app, HostFXR (local) | `scripts/run-hermes-console-app.sh` | exit 0 |
| Console app, NativeAOT (local) | `EXPO_JSI_DOTNET_LOADER=nativeaot scripts/run-hermes-console-app.sh` | exit 0 |
| Workflow lint (if installed) | `actionlint .github/workflows/native-tests.yml` | exit 0; skip if actionlint absent, note it |
| Managed suite (regression guard) | `scripts/test-managed.sh` | exit 0 |

A local Hermes prebuilt must exist at `build/hermes/source/destroot` (build
via `scripts/build-hermes.sh` for the host platform if missing — check
`scripts/` for the exact host build script before running anything).

## Scope

**In scope** (the only files you should modify or create):

- `.github/workflows/native-tests.yml`
- `scripts/run-hermes-console-app.sh` — only if a small flag is needed for
  CI friendliness (e.g. non-interactive output); behavior for local use must
  not change
- `docs/plans/README.md` (status row only)

**Out of scope** (do NOT touch, even though they look related):

- `apps/hermes-console-app/` source — if either loader path fails, STOP.
- `scripts/build-hermes-linux.sh` / `scripts/build-hermes-windows.ps1` and
  `scripts/hermes-ref.txt` — the cache contract stays as is.
- `checks.yml` — fast lane is unaffected.
- A Windows console-app lane — the run script is bash-only today; Windows is
  a deferred follow-up (see Maintenance notes).
- Hermes prebuilt artifact publishing — that is plan 011 (BACKLOG).

## Git workflow

- Branch: `advisor/016-nativeaot-ci-lane` off `development`.
- Commit style: `ci: add console-app loader lane (hostfxr, nativeaot)`.
- Do NOT push or open a PR unless the operator instructed it. Note: CI proof
  requires a push; ask the operator to push the branch when local
  verification is done.

## Steps

### Step 1: Verify both loader paths locally

Run the two console-app commands from the table above on the local machine.
This is the baseline: if either fails before any change, STOP and report the
failure output — the lane cannot land on a broken proof.

**Verify**: both commands → exit 0.

### Step 2: Add the `console-app` job to native-tests.yml

Add a second job alongside `managed-tests`:

```yaml
console-app:
  name: console-app (${{ matrix.loader }})
  strategy:
    fail-fast: false
    matrix:
      loader: [hostfxr, nativeaot]
  runs-on: ubuntu-24.04
```

Steps mirror the existing job: checkout, setup-dotnet 10.0.x, the same
Hermes cache block (same key — the cache is shared with `managed-tests`),
the same Linux Hermes build-on-miss step, then:

```yaml
- name: Run hermes-console-app (${{ matrix.loader }})
  run: EXPO_JSI_DOTNET_LOADER=${{ matrix.loader }} bash scripts/run-hermes-console-app.sh
```

Install the same apt packages as the Hermes build step if the console app's
native CMake build needs them even on cache hit (cmake/ninja/clang are NOT
present by default for the app's own native build — check what the script
compiles and add an explicit install step for the app build's needs).
Remove the now-resolved TODO comment at lines 23-24 and leave a short
comment pointing at this plan.

**Verify**: `actionlint .github/workflows/native-tests.yml` → exit 0 (or
YAML-parse the file with `python3 -c "import yaml,sys; yaml.safe_load(open('.github/workflows/native-tests.yml'))"`
→ exit 0 if actionlint is absent).

### Step 3: Local regression guard

Run `scripts/test-managed.sh` to confirm nothing else was disturbed.

**Verify**: exit 0.

### Step 4: Hand off for CI proof

Report to the operator: branch name, request to push, and that the expected
result is a green `console-app (hostfxr)` and `console-app (nativeaot)` pair
on the pushed branch. Do not push yourself.

**Verify**: report delivered (CI green is confirmed by the operator/reviewer
after push).

## Test plan

No new test code — the console app is the test. The lane's value is the
NativeAOT publish + load succeeding end-to-end; its failure output (publish
errors, missing exports, trim warnings promoted to errors) is the signal.

## Done criteria

Machine-checkable. ALL must hold:

- [ ] Both local console-app commands exit 0 (step 1 evidence in report)
- [ ] `native-tests.yml` contains a `console-app` job with
      `loader: [hostfxr, nativeaot]` matrix and shared Hermes cache key
- [ ] The stale TODO comment (lines 23-24) is gone
- [ ] Workflow file passes actionlint or YAML parse
- [ ] `scripts/test-managed.sh` exits 0
- [ ] No files outside the in-scope list modified (`git status`)
- [ ] `docs/plans/README.md` status row updated

## STOP conditions

Stop and report back (do not improvise) if:

- Either loader path fails locally in step 1 — that is a product bug worth
  its own plan, not a CI-plumbing detour.
- Making the lane work seems to require changing `apps/hermes-console-app`
  source or the Hermes build scripts.
- The NativeAOT publish needs OS packages or SDK workloads that the runner
  image cannot install non-interactively.
- A step's verification fails twice after a reasonable fix attempt.

## Maintenance notes

- Windows console-app lane: deferred until a `run-hermes-console-app.ps1`
  equivalent exists; when written, it should join this job's matrix as an
  os dimension.
- Future ABI or generator changes that add reflection or new exports now
  break this lane — that is by design; fix the change, not the lane.
- Plan 011 (Hermes prebuilt artifacts) would speed this lane's cold start;
  the cache key contract here is compatible with it.
- The advisor roadmap note "NativeAOT lane (vehicle: apps/hermes-console-app,
  gated on the E2E proof)" is satisfied by this plan; the mobile-app NativeAOT
  device proof remains separate P3 work.
