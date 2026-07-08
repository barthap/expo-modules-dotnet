# Plan 008: Port the Hermes testhost to Linux and add native-tests.yml (Linux + Windows heavy CI lane)

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving to the
> next step. If anything in the "STOP conditions" section occurs, stop and
> report — do not improvise. When done, update the status row for this plan
> in `docs/plans/README.md` — unless a reviewer dispatched you and told you they
> maintain the index.
>
> **Drift check (run first)**: `git diff --stat 0f6fc760..HEAD -- scripts/ cmake/ packages/expo-modules-dotnet/native/testhost/ .github/workflows/`
> If any in-scope file changed since this plan was written, compare the
> "Current state" excerpts against the live code before proceeding; on a
> mismatch, treat it as a STOP condition. (Exception: `.github/workflows/checks.yml`
> existing is EXPECTED — plan 001 creates it.)

## Status

- **Priority**: P1
- **Effort**: M–L — the Linux port has real unknowns
- **Risk**: MED — new platform for the native testhost
- **Depends on**: docs/plans/001-ci-checks-workflow.md (workflow conventions
  established there; this plan adds the second workflow of the agreed
  two-file architecture)
- **Category**: dx
- **Planned at**: commit `0f6fc760`, 2026-07-08 (design agreed with operator same day)

## Operator-decided CI architecture (do not deviate)

- `checks.yml` (plan 001) = fast lane. THIS plan adds **`native-tests.yml`** =
  heavy lane: Hermes cache + native testhost + full managed suite, on an
  **`os: [ubuntu-latest, windows-latest]` matrix** (operator merged the
  Windows leg into scope 2026-07-08 — the Windows scripts already exist and
  are proven on the operator's Windows machine). NO macOS runners.
- Future lanes grow in THIS workflow as matrix rows / new jobs (see
  Maintenance notes): `loader: [hostfxr, nativeaot]` dimension,
  `apps/hermes-console-app` E2E job. RNW tests will be a separate workflow
  later.
- `experiments/*` are hello-world; CI ignores them. The app that matters for
  future E2E lanes is `apps/hermes-console-app` (native CMake host +
  `ManagedProofLoader` + managed app).

## Why this matters

The managed test suite — the repo's primary verification gate — currently
runs only on macOS because the build chain is macOS-hardcoded, even though the
bridge C++ is portable (no Apple APIs; verified). Porting the testhost to
Linux puts the full suite on cheap 1×-multiplier runners and proves the
"portable, headless" claim of the managed core. Until this lands, CI (plan
001) enforces everything EXCEPT the tests that matter most.

## Current state

- Platform state AFTER commit `e380065e` ("Port build/test scripts to
  Windows", 2026-07-08 rebase): macOS AND Windows are supported; Linux is the
  only missing leg.
  - `cmake/ExpoHermesPrebuilt.cmake` (NEW, shared) — locates and links the
    Hermes prebuilt: `if(APPLE)` links `hermesvm.framework` (lines ~42–62),
    `elseif(WIN32)` handles `hermesvm.dll`/`hermes.dll` + ICU dll staging
    (lines ~63–126). **The Linux port is now a branch in THIS file** — the
    testhost `CMakeLists.txt` already delegates here.
  - Build scripts: `scripts/build-hermes-macos.sh` and
    `scripts/build-hermes-windows.ps1` (NEW) — both driven by
    `scripts/hermes-ref.txt` (override `HERMES_REF`), output under
    `build/hermes/.../destroot`. Read both before writing the Linux one;
    mirror the shared contract.
  - Test runners: `scripts/test-managed.sh` (bash/macOS; line 8 hardcodes
    `libexpo_jsi_testhost.dylib` — make OS-aware for Linux `.so`) and
    `scripts/test-managed.ps1` (NEW, Windows). Env unchanged:
    `CONFIGURATION`, `HERMES_PREBUILT_ROOT`.
  - `docs/specs/hermes-testhost.md` — already updated for Windows; add Linux
    alongside.
  - Delta spec precedent: `docs/changes/2026-07-07-windows-hermes-build.md` —
    read it; the Linux work follows the same shape.
- Bridge sources (`native/packages/jsi/`) are portable C++ — if the port
  needs to change them, STOP.
- Test projects needing the testhost: `Expo.JSI.Tests`,
  `Expo.ModulesCore.Tests`. (`Expo.ModulesCore.Generator.Tests` is pure
  managed and already covered by `checks.yml`.)
- Hermes on Linux builds via CMake into a static or shared `libhermesvm` (no
  framework bundle). Exact target/output names depend on the pinned ref —
  read that ref's CMake before writing the script.
- Workflow conventions from plan 001 (match them): pinned action versions,
  `--frozen-lockfile`, concurrency group with `cancel-in-progress`, no
  macOS, `experiments/*` ignored.
- Repo commit convention: `build: ...`, `ci: ...`.

## Commands you will need

| Purpose | Command | Expected on success |
|---|---|---|
| Managed tests | `scripts/test-managed.sh` | all pass |
| Hermes (macOS) | `scripts/build-hermes-macos.sh` | destroot populated |
| Hermes (Linux, new) | `scripts/build-hermes-linux.sh` | destroot populated |
| Bash syntax | `bash -n scripts/build-hermes-linux.sh` | exit 0 |
| Format | `scripts/format.sh --check --all` | exit 0 |
| YAML sanity | `python3 -c "import yaml; yaml.safe_load(open('.github/workflows/native-tests.yml'))"` | exit 0 |

## Scope

**In scope** (create/modify only):
- `scripts/build-hermes-linux.sh` (create; mirror the macOS/Windows scripts'
  contract — neither existing script may change behavior)
- `cmake/ExpoHermesPrebuilt.cmake` (add the Linux branch; APPLE and WIN32
  branches untouched)
- `packages/expo-modules-dotnet/native/testhost/CMakeLists.txt` (only if the
  delegation to `ExpoHermesPrebuilt.cmake` needs a touch — prefer not)
- `scripts/test-managed.sh` (OS-aware testhost library name + build-script
  hint; macOS behavior unchanged)
- `.github/workflows/native-tests.yml` (create)
- `docs/specs/hermes-testhost.md` (record Linux support once proven)
- `docs/plans/README.md` (status row)

**Out of scope** (do NOT touch):
- Bridge sources under `native/packages/jsi/` — portability bugs there are
  findings, not fixes in this plan.
- `checks.yml` — plan 001's file.
- NativeAOT loader lane, `hermes-console-app` E2E, RNW workflow,
  Android/Maestro, macOS/iOS CI — future rows/jobs (see Maintenance notes).
- The Windows scripts themselves (`build-hermes-windows.ps1`,
  `test-managed.ps1`) — CI calls them as-is; if a script needs changes to run
  on `windows-latest`, report first (they are proven on the operator's
  machine; a runner-specific failure is information).
- `experiments/*`.

## Git workflow

- Branch: `advisor/008-linux-testhost`
- Commits: `build: port hermes testhost to linux`, then
  `ci: add native-tests workflow (linux managed suite)`
- Do NOT push or open a PR unless the operator instructed it. The Linux leg
  is ultimately verified by a pushed Actions run if no local Linux
  environment exists.

## Steps

### Step 1: Linux Hermes build script

Create `scripts/build-hermes-linux.sh` with the SAME contract as the macOS
script: same env vars (`HERMES_REF`/`hermes-ref.txt`, `HERMES_WORK_DIR`),
same output layout (`build/hermes/source/destroot` with `include/` + the
library). Prefer a static library (avoids `LD_LIBRARY_PATH` management in
`test-managed.sh`); fall back to shared `libhermesvm.so` if the pinned ref
makes static impractical — record the choice and why in the script header.

**Verify**: `bash -n scripts/build-hermes-linux.sh` → exit 0. Full
verification on Linux happens in Step 4 (Docker locally if available,
otherwise the Actions run).

### Step 2: CMake + test-managed.sh portability

In `cmake/ExpoHermesPrebuilt.cmake`, add a Linux branch after the WIN32 one:
locate the Hermes library + headers from the destroot and link (add system
libs — pthread, dl — only if the linker asks). Match the existing branches'
error-message style. In `scripts/test-managed.sh`: derive the testhost
library filename from `uname` (`.dylib` on Darwin, `.so` on Linux) and point
the missing-Hermes hint at the matching build script.

**Verify**: on macOS, `scripts/test-managed.sh` → all pass (zero regression —
this is the gate for the whole step).

### Step 3: Write `native-tests.yml` (ubuntu + windows matrix)

`on: [push, pull_request]`, concurrency group per plan-001 conventions.
Job `managed-tests`, `runs-on: ${{ matrix.os }}` with
`matrix.os: [ubuntu-latest, windows-latest]`:

- checkout → `actions/setup-dotnet` (same SDK pinning as `checks.yml`) →
  `actions/cache` on `build/hermes` keyed
  `hermes-${{ runner.os }}-${{ hashFiles('scripts/hermes-ref.txt') }}`.
- On cache miss, per-OS build step (use `if: runner.os == ...` conditions):
  ubuntu → `scripts/build-hermes-linux.sh`; windows →
  `pwsh scripts/build-hermes-windows.ps1`.
- Test step, per-OS: ubuntu → `scripts/test-managed.sh`; windows →
  `pwsh scripts/test-managed.ps1`.

Leave a comment marking where the future `loader: [hostfxr, nativeaot]`
dimension goes. Read both ps1 scripts for required parameters/env before
wiring them — do not guess flags.

**Verify**: YAML sanity command → exit 0.

### Step 4: Prove both legs

Linux: if Docker is available locally, run an `ubuntu` container over the
repo, execute Step 1's script + `scripts/test-managed.sh`, iterate until
green. Windows: the scripts are already proven on the operator's Windows
machine — the CI leg mainly verifies the workflow wiring; a pushed Actions
run is the check. If no local Linux environment exists, report the branch
ready to push and treat the Actions run as verification for both legs.
First-run build/link failures are normal iteration — EXCEPT failures
implicating bridge sources (STOP) or test-behavior differences between OSes
(STOP, report verbatim: that's a portability finding, the whole point of
this lane).

**Verify**: full managed suite green on Linux AND Windows (container or
Actions) AND still green on macOS locally.

### Step 5: Record Linux support in the living spec

Add to `docs/specs/hermes-testhost.md`: supported platforms (macOS, Linux),
per-platform build script and library naming, and the CI cache contract
(`scripts/hermes-ref.txt` as the cache key). Run `scripts/format.sh` if the
check asks.

**Verify**: `grep -ni linux docs/specs/hermes-testhost.md` → section present;
`scripts/format.sh --check --all` → exit 0.

## Test plan

No new test code — the existing managed suite passing on a second OS is the
payload and the proof.

## Done criteria

- [ ] `scripts/build-hermes-linux.sh` exists; macOS script behavior unchanged.
- [ ] `cmake/ExpoHermesPrebuilt.cmake` has a Linux branch; APPLE and WIN32
      branches untouched (`git diff` on those line ranges shows no changes).
- [ ] `scripts/test-managed.sh` passes on macOS (regression) and on Linux
      (container or Actions run).
- [ ] `.github/workflows/native-tests.yml` exists, valid YAML, with the
      `[ubuntu-latest, windows-latest]` matrix and per-OS build/test steps,
      Hermes cache keyed per-OS on `hermes-ref.txt`;
      `grep -i macos .github/workflows/native-tests.yml` → no matches.
- [ ] Managed suite green on the Windows leg (Actions run).
- [ ] `docs/specs/hermes-testhost.md` records Linux support.
- [ ] No files outside in-scope list modified (`git status`).
- [ ] `docs/plans/README.md` status row updated.

## STOP conditions

Stop and report back (do not improvise) if:

- The port requires changes under `native/packages/jsi/` (bridge portability
  bug — operator decides).
- Hermes at the pinned ref does not build on Linux, or needs source patches —
  do not fork/patch Hermes.
- Tests pass on macOS but fail on Linux for behavioral (not build/link)
  reasons — report failing tests verbatim.
- `scripts/test-managed.sh` on macOS is red before any changes.

## Maintenance notes

Future lanes grow HERE (each its own plan when the time comes), in the
operator-agreed order:

1. **NativeAOT loader lane** — gate on the end-to-end NativeAOT proof (see
   roadmap assessment in `docs/plans/README.md`); the natural vehicle is
   `apps/hermes-console-app` (native host + `ManagedProofLoader`): a job that
   builds it with the NativeAOT-published managed side and runs it headless.
   Then `loader: [hostfxr, nativeaot]` becomes a matrix dimension.
2. **RNW tests** — separate workflow file (different toolchain and cost
   profile), not rows here. Windows minutes bill at 2× on private repos —
   keep Windows jobs lean.
3. **Android (later Maestro e2e), then iOS/macOS** — only when repo growth
   justifies it (macOS runners are 10× minutes).

- When `scripts/hermes-ref.txt` changes, caches roll; the first run per OS
  after a bump is slow. If bumps become frequent, publish Hermes destroots as
  workflow artifacts or a release asset.
- Reviewer: confirm the APPLE and WIN32 branches of
  `ExpoHermesPrebuilt.cmake` are byte-identical to before, and that the
  static-vs-shared choice is documented in the Linux script.
