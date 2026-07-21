# Plan 020: Port hermes-console-app to Linux and add the end-to-end loader lane in CI

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving to the
> next step. If anything in the "STOP conditions" section occurs, stop and
> report — do not improvise. When done, update the status row for this plan
> in `docs/plans/README.md` — unless a reviewer dispatched you and told you
> they maintain the index.
>
> **Drift check (run first)**:
> `git diff --stat 6db8167c..HEAD -- apps/hermes-console-app scripts/run-hermes-console-app.sh .github/workflows/native-tests.yml docs/specs/hermes-testhost.md cmake/ExpoHermesPrebuilt.cmake`
> Expected drift: `.github/workflows/native-tests.yml` gains the
> `nativeaot-publish` job from plan 016 — that is fine and required (see
> "Depends on"). Any other change: compare the "Current state" excerpts
> against the live code before proceeding; on a mismatch, treat it as a
> STOP condition.

## Status

- **Priority**: P1
- **Effort**: M
- **Risk**: MED (native loader + CMake changes on a new OS; the bridge code
  itself is already Linux-proven by the testhost)
- **Depends on**: plan 016 (`nativeaot-publish` job must be in
  `native-tests.yml`; this plan edits the same file and its comment)
- **Category**: tech-debt / tests
- **Planned at**: commit `6db8167c`, 2026-07-21

## Why this matters

The hermes-console-app is the repo's end-to-end proof that the bridge runs
under both managed loaders (HostFXR dev loader, NativeAOT production
loader). Today the NativeAOT path is macOS-only, so CI can never run the
full proof — plan 016's `nativeaot-publish` job covers only compilation. An
AOT-published library that compiles but misbehaves at runtime (missing
`UnmanagedCallersOnly` export, bad marshalling at the ABI boundary) is
invisible to CI until someone runs the app on a Mac. Porting the app's
loader plumbing to Linux lets a standard-cost `ubuntu-24.04` job run both
loader paths end-to-end on every PR. The port is small: the JSI bridge,
Hermes prebuilt CMake glue, and the managed suite already run on Linux
(plan 008); only the console app's RID selection, published-library name,
and nethost linking hardcode macOS.

## Current state

(At `6db8167c`. All excerpts verified against the live code on 2026-07-21.)

- `scripts/run-hermes-console-app.sh` — builds and runs the app.
  `nativeaot_rid()` (lines 54-63) maps architecture only, macOS assumed:
  ```bash
  nativeaot_rid() {
      case "$(uname -m)" in
      arm64) printf '%s\n' "osx-arm64" ;;
      x86_64) printf '%s\n' "osx-x64" ;;
      *)
          echo "Unsupported macOS architecture for NativeAOT: $(uname -m)" >&2
          exit 1
          ;;
      esac
  }
  ```
  (The file is indented with tabs — keep tabs.) Everything else in the
  script is already portable: `HERMES_PREBUILT_ROOT` env override (line 7),
  `EXPO_JSI_DOTNET_LOADER` validation, CMake configure/build, generator
  analyzer build before NativeAOT publish (`build_generator_analyzer`,
  line 78 — required because the csproj consumes the generator as a
  prebuilt analyzer DLL when `PublishAot=true`).
- `apps/hermes-console-app/native/ManagedProofLoader.cpp` — two macOS
  hardcodes, both in the NativeAOT section:
  - `nativeAotRid()` (lines 203-212):
    ```cpp
    #if defined(__APPLE__) && defined(__aarch64__)
      return "osx-arm64";
    #elif defined(__APPLE__) && defined(__x86_64__)
      return "osx-x64";
    #else
      throw std::runtime_error("NativeAOT Hermes console proof currently supports macOS only.");
    #endif
    ```
  - `findNativeAotLibrary()` (line 218) appends
    `publish/HermesConsoleApp.dylib` — Linux publishes
    `HermesConsoleApp.so`.
  - The rest is already portable: `loadNativeLibrary`/`resolveExport` use
    `dlopen`/`dlsym` on non-Windows (lines 60-88), and the whole HostFXR
    section uses `get_hostfxr_path` from nethost with no OS branches
    beyond `_WIN32`.
- `apps/hermes-console-app/native/CMakeLists.txt` — two macOS assumptions,
  both in the HostFXR branches:
  - `DOTNET_HOST_RID` selection (lines 44-57): `WIN32` branch, then the
    fallback assumes macOS:
    ```cmake
    elseif(CMAKE_SYSTEM_PROCESSOR MATCHES "arm64|aarch64")
      set(DOTNET_HOST_RID "osx-arm64")
    else()
      set(DOTNET_HOST_RID "osx-x64")
    endif()
    ```
  - nethost linking (lines 116-142): `WIN32` branch links `nethost.lib` /
    copies `nethost.dll`; the `else()` branch hardcodes
    `libnethost.dylib` (link + copy_if_different).
- `cmake/ExpoHermesPrebuilt.cmake` — already handles Linux
  (`UNIX AND NOT APPLE` branch, lines 141+: links `libhermesvm.so`, links
  `libjsi.so` only when staged, configures rpath, error text points at
  `scripts/build-hermes-linux.sh`). Do not modify it.
- `scripts/build-hermes-linux.sh` — builds a Linux Hermes prebuilt.
  Overrides: `HERMES_WORK_DIR` (default `<repo>/build/hermes`), output at
  `$HERMES_WORK_DIR/source/destroot` with `lib/libhermesvm.so`. Used as-is
  by the `managed-tests` CI job. Do not modify it.
- `.github/workflows/native-tests.yml` — after plan 016: `managed-tests`
  job (ubuntu + windows matrix, Hermes cache at `build/hermes` keyed
  `hermes-${{ runner.os }}-${{ hashFiles('scripts/hermes-ref.txt') }}`)
  plus the `nativeaot-publish` job, and a comment saying the end-to-end
  loader matrix is gated on this port. Match the existing jobs' step
  naming and style.
- `docs/specs/hermes-testhost.md` lines 81-95, requirement "Headless
  Hermes Console Runners":
  ```
  The headless Hermes console proof SHALL have platform-paired runners. The
  macOS runner SHALL remain `scripts/run-hermes-console-app.sh`. The Windows
  HostFXR runner SHALL be `scripts/run-hermes-console-app.ps1`.
  ```
  This plan changes that requirement (the bash runner gains Linux), so the
  repo's living-spec workflow applies: delta spec first, merge into the
  living spec at the end. The delta content is inlined in step 2 — the
  operator approved it by approving this plan.
- `apps/hermes-console-app/README.md` — documents the two loader paths and
  a Windows section; no Linux section yet.
- Local Linux verification happens in Docker (operator-confirmed setup on
  the dev machine). NativeAOT cannot cross-compile between OSes, so the
  container is the only local Linux proof. Container arch determines the
  RID (`linux-arm64` on Apple Silicon hosts, `linux-x64` on x64).

## Commands you will need

| Purpose | Command (repo root) | Expected on success |
|---|---|---|
| Console app, HostFXR (macOS baseline/regression) | `scripts/run-hermes-console-app.sh` | exit 0 |
| Console app, NativeAOT (macOS baseline/regression) | `EXPO_JSI_DOTNET_LOADER=nativeaot scripts/run-hermes-console-app.sh` | exit 0 |
| Managed suite (regression guard) | `scripts/test-managed.sh` | exit 0 |
| Format gate | `scripts/format.sh --check --all` | exit 0 (run `scripts/format.sh` then re-check if it fails) |
| Workflow lint (if installed) | `actionlint .github/workflows/native-tests.yml` | exit 0; fallback: `python3 -c "import yaml; yaml.safe_load(open('.github/workflows/native-tests.yml'))"` |

Docker commands (steps 6-8) share this prefix — define it once per shell:

```bash
DOCKER_RUN='docker run --rm -v "$(pwd)":/repo -w /repo mcr.microsoft.com/dotnet/sdk:10.0 bash -c'
APT='apt-get update -q && apt-get install -y --no-install-recommends cmake ninja-build clang git python3 libicu-dev zlib1g-dev build-essential ca-certificates'
```

A local macOS Hermes prebuilt must already exist at
`build/hermes/source/destroot` for the baseline (build via
`scripts/build-hermes-macos.sh` if missing). The Linux prebuilt goes to a
**separate** directory, `build/hermes-linux` (gitignored under `build/`),
so it never collides with the macOS destroot.

## Scope

**In scope** (the only files you should modify or create):

- `scripts/run-hermes-console-app.sh` — `nativeaot_rid()` only
- `apps/hermes-console-app/native/ManagedProofLoader.cpp` — NativeAOT RID
  and library-name selection only
- `apps/hermes-console-app/native/CMakeLists.txt` — host RID selection and
  nethost linking only
- `apps/hermes-console-app/README.md` — add a Linux section
- `.github/workflows/native-tests.yml` — add the `console-app` job, update
  the gating comment
- `docs/changes/<yyyy-mm-dd>-console-app-linux-port/{spec.md,plan.md}`
  (create; `<yyyy-mm-dd>` = execution date)
- `docs/specs/hermes-testhost.md` — merge the delta (step 10)
- `docs/plans/README.md` (status row only)

**Out of scope** (do NOT touch, even though they look related):

- `scripts/run-hermes-console-app.ps1` and any Windows NativeAOT work —
  separate future plan, needs the Windows machine.
- `scripts/build-hermes-*.{sh,ps1}`, `scripts/hermes-ref.txt`,
  `cmake/ExpoHermesPrebuilt.cmake` — Hermes prebuilt contract stays as is.
- Managed sources (`apps/hermes-console-app/managed`, `packages/`) — if a
  loader path fails on Linux because of managed code, that is a finding.
- The `managed-tests` and `nativeaot-publish` jobs — leave both untouched;
  the publish job stays as the fast PR-time signal.
- `checks.yml`.

## Git workflow

- Branch: `advisor/020-console-app-linux-port` off `development`.
- Commit per logical unit, matching repo style, e.g.:
  - `spec: extend hermes console proof runners to linux` (delta spec)
  - `feat(console-app): port hostfxr and nativeaot loaders to linux`
  - `ci: add console-app loader lane (hostfxr, nativeaot)`
  - `docs: document linux console proof and merge runner delta`
- Before each commit, check staged content for local absolute paths,
  usernames, or machine-specific paths — none may be committed.
- Do NOT push or open a PR unless the operator instructed it. CI proof
  requires a push; ask the operator to push when local verification is done.

## Steps

### Step 1: macOS baseline

Run both console-app commands from the table. Both must pass before any
change — this is the regression baseline for the shared code paths.

**Verify**: both → exit 0.

### Step 2: Commit the delta spec

Create `docs/changes/<yyyy-mm-dd>-console-app-linux-port/spec.md`:

```markdown
# Delta: Linux hosts for the headless Hermes console proof

Modifies `docs/specs/hermes-testhost.md`, requirement "Headless Hermes
Console Runners".

## Requirement (replaces the runner-pairing sentence)

The headless Hermes console proof SHALL have platform-paired runners. The
bash runner `scripts/run-hermes-console-app.sh` SHALL support macOS and
Linux hosts, selecting the host NativeAOT runtime identifier and published
library name per platform. The Windows HostFXR runner SHALL be
`scripts/run-hermes-console-app.ps1`.

#### Scenario: Linux console proof runs both loaders
- **GIVEN** a Linux host with a Linux Hermes prebuilt destroot
- **WHEN** a developer runs `scripts/run-hermes-console-app.sh` with
  `EXPO_JSI_DOTNET_LOADER` set to `hostfxr` or `nativeaot`
- **THEN** it SHALL build the managed console app for the Linux host RID
- **AND** the native host SHALL load `HermesConsoleApp.so` (NativeAOT) or
  the HostFXR runtime via nethost (HostFXR)
- **AND** the proof SHALL exercise the same registration behavior as the
  macOS console proof
```

Create `plan.md` beside it containing one line: this delta is implemented
by `docs/plans/020-console-app-linux-port.md`. Commit both.

**Verify**: `git show --stat HEAD` lists exactly the two new files.

### Step 3: Port `nativeaot_rid()` in the run script

Replace the function body (keep tab indentation):

```bash
nativeaot_rid() {
	case "$(uname -s)-$(uname -m)" in
	Darwin-arm64) printf '%s\n' "osx-arm64" ;;
	Darwin-x86_64) printf '%s\n' "osx-x64" ;;
	Linux-aarch64 | Linux-arm64) printf '%s\n' "linux-arm64" ;;
	Linux-x86_64) printf '%s\n' "linux-x64" ;;
	*)
		echo "Unsupported host for NativeAOT: $(uname -s) $(uname -m)" >&2
		exit 1
		;;
	esac
}
```

**Verify**: `EXPO_JSI_DOTNET_LOADER=nativeaot scripts/run-hermes-console-app.sh --no-run` → exit 0 (macOS path unchanged).

### Step 4: Port `ManagedProofLoader.cpp`

In `nativeAotRid()`, add Linux branches before the throw:

```cpp
#elif defined(__linux__) && defined(__aarch64__)
  return "linux-arm64";
#elif defined(__linux__) && defined(__x86_64__)
  return "linux-x64";
#else
  throw std::runtime_error("NativeAOT Hermes console proof supports macOS and Linux only.");
#endif
```

In `findNativeAotLibrary()`, replace the hardcoded
`"publish/HermesConsoleApp.dylib"` suffix with a platform-selected library
name (`HermesConsoleApp.dylib` under `__APPLE__`, `HermesConsoleApp.so`
under `__linux__`), following the file's existing `#if` style.

**Verify**: `EXPO_JSI_DOTNET_LOADER=nativeaot scripts/run-hermes-console-app.sh` → exit 0 (macOS still green after the refactor).

### Step 5: Port `CMakeLists.txt`

Two changes, HostFXR branches only:

1. RID selection — make the OS explicit instead of assuming macOS in the
   fallback:
   ```cmake
   if(WIN32)
     # ... existing win branch unchanged ...
   elseif(APPLE)
     if(CMAKE_SYSTEM_PROCESSOR MATCHES "arm64|aarch64")
       set(DOTNET_HOST_RID "osx-arm64")
     else()
       set(DOTNET_HOST_RID "osx-x64")
     endif()
   elseif(CMAKE_SYSTEM_NAME STREQUAL "Linux")
     if(CMAKE_SYSTEM_PROCESSOR MATCHES "arm64|aarch64")
       set(DOTNET_HOST_RID "linux-arm64")
     else()
       set(DOTNET_HOST_RID "linux-x64")
     endif()
   else()
     message(FATAL_ERROR "Unsupported host platform for the HostFXR console proof")
   endif()
   ```
2. nethost linking — split the non-Windows branch: APPLE keeps
   `libnethost.dylib` (link + copy, unchanged behavior); Linux links
   `${DOTNET_HOST_NATIVE_DIR}/libnethost.so`, copies it beside the binary
   with the same `copy_if_different` pattern, and appends `$ORIGIN` to the
   target's `BUILD_RPATH` so the copy resolves at runtime.

**Verify**: `scripts/run-hermes-console-app.sh` → exit 0 (macOS HostFXR
still green through the restructured CMake).

### Step 6: Build the Linux Hermes prebuilt in Docker

One-time, slow (roughly 30-90 minutes; the output persists on the host
mount, so re-runs are cheap):

```bash
eval $DOCKER_RUN '"'"$APT"' && HERMES_WORK_DIR=/repo/build/hermes-linux bash scripts/build-hermes-linux.sh"'
```

(If the eval quoting fights you, just run the container interactively:
`docker run --rm -it -v "$(pwd)":/repo -w /repo mcr.microsoft.com/dotnet/sdk:10.0 bash`,
then the apt line and the `HERMES_WORK_DIR=... bash scripts/build-hermes-linux.sh`
inside it. Same for steps 7-8.)

**Verify**: `ls build/hermes-linux/source/destroot/lib/` contains
`libhermesvm.so`.

### Step 7: Linux HostFXR proof in Docker

In the container (same image + apt packages):

```bash
HERMES_PREBUILT_ROOT=/repo/build/hermes-linux/source/destroot bash scripts/run-hermes-console-app.sh
```

**Verify**: exit 0, output includes the loaded HostFXR path and the proof
run output.

### Step 8: Linux NativeAOT proof in Docker

Same container setup:

```bash
EXPO_JSI_DOTNET_LOADER=nativeaot HERMES_PREBUILT_ROOT=/repo/build/hermes-linux/source/destroot bash scripts/run-hermes-console-app.sh
```

**Verify**: exit 0, output includes
`Loaded NativeAOT library: .../linux-<arch>/publish/HermesConsoleApp.so`.

### Step 9: Add the `console-app` job to native-tests.yml

Add a third job alongside `managed-tests` and `nativeaot-publish`:

```yaml
console-app:
  name: console-app (${{ matrix.loader }})
  strategy:
    fail-fast: false
    matrix:
      loader: [hostfxr, nativeaot]
  runs-on: ubuntu-24.04

  steps:
    - uses: actions/checkout@v4

    - uses: actions/setup-dotnet@v4
      with:
        dotnet-version: 10.0.x

    # Same cache block and key as managed-tests — the prebuilt is shared.
    - name: Cache Hermes prebuilt
      id: hermes-cache
      uses: actions/cache@v4
      with:
        path: build/hermes
        key: hermes-${{ runner.os }}-${{ hashFiles('scripts/hermes-ref.txt') }}

    - name: Build Hermes (Linux)
      if: steps.hermes-cache.outputs.cache-hit != 'true'
      run: |
        sudo apt-get update -q
        sudo apt-get install -y --no-install-recommends \
          cmake ninja-build clang python3 libicu-dev
        bash scripts/build-hermes-linux.sh

    - name: Install console app build prerequisites
      run: |
        sudo apt-get update -q
        sudo apt-get install -y --no-install-recommends \
          cmake ninja-build clang python3 libicu-dev zlib1g-dev

    - name: Run hermes-console-app (${{ matrix.loader }})
      run: EXPO_JSI_DOTNET_LOADER=${{ matrix.loader }} bash scripts/run-hermes-console-app.sh
```

(No `HERMES_PREBUILT_ROOT` override in CI — the cache restores to
`build/hermes`, the script's default.) Update the gating comment plan 016
left near the matrix: the end-to-end lane now exists; only the Windows
NativeAOT port remains deferred.

**Verify**: `actionlint .github/workflows/native-tests.yml` → exit 0 (or
the YAML-parse fallback; note which you used).

### Step 10: Docs — README section and spec merge

1. `apps/hermes-console-app/README.md`: add a Linux section next to the
   Windows one: `scripts/build-hermes-linux.sh` for the prebuilt, both
   loader commands, and a note that on non-Linux dev machines the proof
   runs in a .NET SDK Docker container with `HERMES_WORK_DIR` /
   `HERMES_PREBUILT_ROOT` pointed at a separate destroot (generic
   instructions only — no machine-specific paths).
2. Merge the delta from step 2 into `docs/specs/hermes-testhost.md`:
   replace the runner-pairing sentence in "Headless Hermes Console
   Runners" with the delta wording and add the Linux scenario. Then move
   `docs/changes/<yyyy-mm-dd>-console-app-linux-port/` to
   `docs/archive/changes/` (matching how earlier accepted deltas were
   archived).

**Verify**: `grep -n "Linux" docs/specs/hermes-testhost.md` shows the
updated requirement; `ls docs/changes/` no longer lists the change dir.

### Step 11: Regression and format gates

Run `scripts/test-managed.sh`, then `scripts/format.sh --check --all` (if
formatting fails, run `scripts/format.sh` and re-check). Re-run the two
macOS console-app commands one last time.

**Verify**: all four → exit 0.

### Step 12: Hand off for CI confirmation

Report to the operator: branch name, request to push, expected result =
green `console-app (hostfxr)`, `console-app (nativeaot)`,
`nativeaot-publish`, and `managed-tests` jobs. Do not push yourself.

**Verify**: report delivered.

## Test plan

No new test code — the console app run is the test, now executed on two
OSes and two loaders. The Linux Docker runs (steps 7-8) are the local
proof; the CI matrix keeps it enforced.

## Done criteria

Machine-checkable. ALL must hold:

- [ ] macOS: both loader commands exit 0 (before and after the port)
- [ ] Docker Linux: both loader commands exit 0 with
      `HERMES_PREBUILT_ROOT` pointing at `build/hermes-linux/source/destroot`
- [ ] `nativeAotRid()` (cpp) and `nativeaot_rid()` (sh) return `linux-*`
      RIDs on Linux; `findNativeAotLibrary()` resolves `HermesConsoleApp.so`
- [ ] `native-tests.yml` has a `console-app` job with
      `loader: [hostfxr, nativeaot]` sharing the Hermes cache key
- [ ] Delta spec created, merged into `docs/specs/hermes-testhost.md`, and
      the change dir archived
- [ ] `scripts/test-managed.sh` and `scripts/format.sh --check --all` exit 0
- [ ] No files outside the in-scope list modified (`git status`)
- [ ] No local absolute paths, usernames, or machine names in any commit
- [ ] `docs/plans/README.md` status row updated

## STOP conditions

Stop and report back (do not improvise) if:

- The macOS baseline (step 1) fails — broken proof, not a porting problem.
- Plan 016's `nativeaot-publish` job is not in `native-tests.yml` — the
  dependency has not landed; coordinate ordering with the operator.
- The Hermes Linux build (step 6) fails at the pinned
  `scripts/hermes-ref.txt` revision — that is a prebuilt-script or upstream
  finding, and the scripts are out of scope.
- A Linux loader run fails for a reason inside managed code or the shared
  bridge (`packages/`) — product bug, out of scope, worth its own plan.
- The port seems to need changes to `cmake/ExpoHermesPrebuilt.cmake` or the
  Hermes build scripts.
- Docker is unavailable or cannot run Linux containers on the machine.
- A step's verification fails twice after a reasonable fix attempt.

## Maintenance notes

- **Windows NativeAOT port remains open** (future plan, needs the Windows
  machine): `scripts/run-hermes-console-app.ps1` still throws
  `'Windows runner supports hostfxr in this slice.'`. When it lands, the
  CI lane grows an os dimension rather than a new job.
- The `console-app` job shares the `managed-tests` Hermes cache; plan 011
  (prebuilt artifacts, BACKLOG) would speed the cold start for both.
- New ABI exports or generator changes now break this lane at runtime, not
  just at publish — that is by design; fix the change, not the lane.
- `build/hermes-linux` on dev machines is a Docker-built cache; safe to
  delete, rebuilt by step 6's command.
