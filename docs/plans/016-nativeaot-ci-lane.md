# Plan 016: NativeAOT publish-check lane in CI (linux-x64, compile-time proof)

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving to the
> next step. If anything in the "STOP conditions" section occurs, stop and
> report — do not improvise. When done, update the status row for this plan
> in `docs/plans/README.md` — unless a reviewer dispatched you and told you
> they maintain the index.
>
> **Drift check (run first)**:
> `git diff --stat 6db8167c..HEAD -- .github/workflows/native-tests.yml apps/hermes-console-app/managed packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator`
> If any of these files changed since this plan was written, compare the
> "Current state" excerpts against the live code before proceeding; on a
> mismatch, treat it as a STOP condition.

## Status

- **Priority**: P1
- **Effort**: S
- **Risk**: LOW (one new CI job; no source or script changes)
- **Depends on**: none
- **Category**: tests / dx
- **Planned at**: commit `6db8167c`, 2026-07-21 (re-scoped; originally
  planned at `ea07d69d`, 2026-07-20)
- **Execution status**: COMPLETE — implemented at `1f8d0414`; local macOS and
  Docker Linux NativeAOT publishes plus `actionlint` passed.

## History (why this plan was re-scoped)

The original plan added a `console-app` job on `ubuntu-24.04` running both
loader paths end-to-end. Execution was BLOCKED on 2026-07-21: the console
app cannot run on Linux at all. Verified findings (reviewer re-checked all
of them against the code on 2026-07-21):

- `apps/hermes-console-app/native/ManagedProofLoader.cpp` `nativeAotRid()`
  throws `"NativeAOT Hermes console proof currently supports macOS only."`
  on non-Apple hosts and loads `HermesConsoleApp.dylib` from an `osx-*`
  publish directory.
- `scripts/run-hermes-console-app.sh` `nativeaot_rid()` maps `uname -m`
  straight to `osx-arm64`/`osx-x64` — no Linux RIDs.
- `apps/hermes-console-app/native/CMakeLists.txt` HostFXR RID selection
  handles Windows (`win-*`) and macOS (`osx-*`) but has no Linux branch.
  (Note: the original blocked-findings text overstated this — a full
  Windows HostFXR branch exists; only Linux is missing. There is also a
  `scripts/run-hermes-console-app.ps1`, hostfxr-only.)

Decision (operator, 2026-07-21): land a cheap compile-time NativeAOT proof
on Linux now (this plan), and port the console app itself to be
cross-platform (Windows + Linux + macOS, both loaders) as a **separate
future plan**. The end-to-end loader-matrix CI lane returns as a follow-up
to that port; do not attempt it here.

## Why this matters

NativeAOT compatibility is a hard repo constraint (AGENTS.md: "keep ABI and
generated bindings NativeAOT-compatible"), but CI only exercises the managed
suite under the ordinary CoreCLR runner. The ABI has grown to v23 with no
automated NativeAOT proof of any kind. A `dotnet publish /p:PublishAot=true`
run is the compile-time half of that proof: it fails on reflection the
trimmer can't see, unsupported marshalling, and other AOT-incompatible
managed changes. It needs no Hermes prebuilt and no native host, so it runs
on every PR in a few minutes on a standard-cost runner. The runtime half
(loading the published library and running the bridge) stays gated on the
cross-platform console-app port.

## Current state

(At `6db8167c`.)

- `.github/workflows/native-tests.yml` — single `managed-tests` job,
  `os: [ubuntu-24.04, windows-latest]` matrix, 79 lines. Lines 23-24 hold a
  stale TODO:
  ```yaml
  # TODO: add loader: [hostfxr, nativeaot] dimension once NativeAOT
  # end-to-end proof lands (see docs/plans/README.md maintenance notes).
  ```
  Job steps: `actions/checkout@v4`, `actions/setup-dotnet@v4` with
  `dotnet-version: 10.0.x`, a Hermes prebuilt cache, Hermes build-on-miss,
  then the managed test suite. Match this job's step naming and style.
- `apps/hermes-console-app/managed/HermesConsoleApp/HermesConsoleApp.csproj`
  — the publish target. Key detail: when `PublishAot=true` the generator is
  consumed as a **prebuilt analyzer DLL**, not a project reference:
  ```xml
  <Analyzer
    Include="../../../../packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/bin/Debug/netstandard2.0/Expo.ModulesCore.Generator.dll"
    Condition="'$(PublishAot)' == 'true'" />
  ```
  So the generator must be built (Debug) **before** the AOT publish, or the
  publish silently runs without the source generator and fails on missing
  generated code. `scripts/run-hermes-console-app.sh`
  (`build_generator_analyzer`, line 78) does exactly this — mirror it.
- The publish itself needs no Hermes prebuilt, no CMake, and no repo native
  code. It is a pure managed → native-library compile.
- NativeAOT cannot cross-compile between OSes: `linux-*` publishes only on
  a Linux environment. On the dev machine that means Docker: a
  `mcr.microsoft.com/dotnet/sdk:10.0` container proves the Linux publish
  locally (native arch — `linux-arm64` on Apple Silicon, `linux-x64` on
  x64 hosts). The CI job publishes `linux-x64`; either RID is a valid
  Linux AOT proof of the same managed code.

## Commands you will need

| Purpose | Command (repo root) | Expected on success |
|---|---|---|
| Build generator analyzer | `dotnet build packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/Expo.ModulesCore.Generator.csproj -c Debug` | exit 0 |
| AOT publish (local proxy, Apple Silicon) | `dotnet publish apps/hermes-console-app/managed/HermesConsoleApp/HermesConsoleApp.csproj -c Release -r osx-arm64 /p:PublishAot=true /p:NativeLib=Shared` | exit 0 |
| AOT publish (CI, the real check) | same with `-r linux-x64` | exit 0 |
| Workflow lint (if installed) | `actionlint .github/workflows/native-tests.yml` | exit 0; if actionlint absent, use the YAML-parse fallback in step 2 and note it |

On an Intel Mac use `-r osx-x64` for the proxy; on Windows use `-r win-x64`.

Linux proof in Docker (step 2; pick the RID matching `uname -m` —
`linux-arm64` for arm64, `linux-x64` for x86_64):

```bash
docker run --rm -v "$(pwd)":/repo -w /repo mcr.microsoft.com/dotnet/sdk:10.0 bash -c '
  apt-get update -q && apt-get install -y --no-install-recommends clang zlib1g-dev &&
  dotnet build packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/Expo.ModulesCore.Generator.csproj -c Debug &&
  dotnet publish apps/hermes-console-app/managed/HermesConsoleApp/HermesConsoleApp.csproj -c Release -r linux-arm64 /p:PublishAot=true /p:NativeLib=Shared'
```

## Scope

**In scope** (the only files you should modify):

- `.github/workflows/native-tests.yml`
- `docs/plans/README.md` (status row only)

**Out of scope** (do NOT touch, even though they look related):

- `apps/hermes-console-app/` source (managed and native) — the
  cross-platform port is a separate future plan. If the linux-x64 publish
  fails in CI, that is a finding to report, not something to patch here.
- `scripts/run-hermes-console-app.sh` / `.ps1` — nothing in this plan runs
  them in CI.
- `scripts/build-hermes-*.{sh,ps1}` and `scripts/hermes-ref.txt` — this job
  does not use the Hermes cache at all.
- `checks.yml` — fast lane unaffected.
- Any end-to-end loader lane (`EXPO_JSI_DOTNET_LOADER` matrix) — gated on
  the console-app port plan.
- Promoting AOT/trim warnings to errors — deferred (see Maintenance notes).

## Git workflow

- Branch: `advisor/016-nativeaot-publish-check` off `development`.
- Commit style: `ci: add nativeaot publish check (linux-x64)`.
- Do NOT push or open a PR unless the operator instructed it. CI proof
  requires a push; ask the operator to push the branch when local
  verification is done.

## Steps

### Step 1: Local proxy baseline

From repo root, run the generator build, then the AOT publish with the
**local** RID (see command table). This is the baseline: it proves the
managed code publishes under NativeAOT on the host OS before you add the
CI job for Linux.

**Verify**: both commands → exit 0, and
`ls apps/hermes-console-app/managed/HermesConsoleApp/bin/Release/net10.0/<rid>/publish/`
contains a `HermesConsoleApp.dylib` (macOS) / `HermesConsoleApp.dll`
(Windows).

### Step 2: Prove the Linux publish locally in Docker

Run the Docker command from the command table (RID matching the container
arch). First run downloads the SDK image and NuGet packages — a few minutes
is normal. The container shares the repo's `obj/`/`bin/`; the next host
build re-restores automatically, no cleanup needed.

If Docker is not available on the machine, skip this step, say so in the
report, and treat the CI job in step 4 as the only Linux proof.

**Verify**: Docker command → exit 0, and
`apps/hermes-console-app/managed/HermesConsoleApp/bin/Release/net10.0/linux-<arch>/publish/`
contains `HermesConsoleApp.so`.

### Step 3: Add the `nativeaot-publish` job to native-tests.yml

Add a second job alongside `managed-tests` (no matrix, no Hermes cache):

```yaml
nativeaot-publish:
  name: nativeaot-publish (linux-x64)
  runs-on: ubuntu-24.04

  steps:
    - uses: actions/checkout@v4

    - uses: actions/setup-dotnet@v4
      with:
        dotnet-version: 10.0.x

    - name: Install NativeAOT link prerequisites
      run: |
        sudo apt-get update -q
        sudo apt-get install -y --no-install-recommends clang zlib1g-dev

    - name: Build generator analyzer
      run: dotnet build packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/Expo.ModulesCore.Generator.csproj -c Debug

    - name: Publish HermesConsoleApp (NativeAOT, linux-x64)
      run: dotnet publish apps/hermes-console-app/managed/HermesConsoleApp/HermesConsoleApp.csproj -c Release -r linux-x64 /p:PublishAot=true /p:NativeLib=Shared
```

Replace the stale TODO comment at lines 23-24 of the current file with:

```yaml
# NativeAOT compile-time proof: the nativeaot-publish job below. The
# end-to-end loader matrix (hostfxr + nativeaot, run, all OSes) is gated
# on the cross-platform console-app port (see docs/plans/016-*.md).
```

**Verify**: `actionlint .github/workflows/native-tests.yml` → exit 0. If
actionlint is absent:
`python3 -c "import yaml; yaml.safe_load(open('.github/workflows/native-tests.yml'))"`
→ exit 0 (note in your report which check you used).

### Step 4: Hand off for CI confirmation

Report to the operator: branch name, request to push, and that the expected
result is a green `nativeaot-publish (linux-x64)` job on the pushed branch.
Do not push yourself. If the operator reports the CI job failed, treat the
failure log as a finding (linux-x64 AOT incompatibility in managed code)
and report it — do not modify managed sources.

**Verify**: report delivered (CI green is confirmed by the operator/reviewer
after push).

## Test plan

No new test code — the publish is the test. Its failure output (ILC errors,
trim analysis failures, missing generated bindings) is the signal.

## Done criteria

Machine-checkable. ALL must hold:

- [ ] Local generator build + local-RID AOT publish exit 0 (step 1 evidence
      in report)
- [ ] Docker Linux AOT publish exits 0 and produces `HermesConsoleApp.so`
      (step 2; or the report states Docker was unavailable)
- [ ] `native-tests.yml` contains a `nativeaot-publish` job publishing
      `linux-x64` with `/p:PublishAot=true /p:NativeLib=Shared`, preceded by
      the generator Debug build step
- [ ] The stale TODO comment (old lines 23-24) is replaced with the new
      comment from step 2
- [ ] Workflow file passes actionlint or the YAML-parse fallback
- [ ] No files outside the in-scope list modified (`git status`)
- [ ] `docs/plans/README.md` status row updated

## STOP conditions

Stop and report back (do not improvise) if:

- The local proxy publish in step 1 or the Docker Linux publish in step 2
  fails — the managed code has an AOT (or Linux-specific AOT) regression;
  that is a product bug worth its own plan.
- The publish appears to need changes to any `.csproj`, managed source, or
  script — all out of scope.
- The `PublishAot` analyzer-DLL conditional shown in "Current state" is no
  longer in `HermesConsoleApp.csproj` (drift — the generator wiring changed).
- A step's verification fails twice after a reasonable fix attempt.

## Maintenance notes

- **Cross-platform console-app port** — split by machine (operator
  decision 2026-07-21):
  - *Linux port*: planned as `docs/plans/020-console-app-linux-port.md`
    (Linux RIDs, `.so` loading, nethost linking, end-to-end CI lane;
    Docker-provable on the dev machine). Depends on this plan landing.
  - *Windows NativeAOT port (not yet written)*: `run-hermes-console-app.ps1`
    currently throws `'Windows runner supports hostfxr in this slice.'`;
    needs the separate Windows machine.
  When those land, the end-to-end loader-matrix lane supersedes this job's
  role as the only AOT signal; keep this job anyway — it is the fast
  PR-time signal.
- This lane only proves compilation. An AOT-published library that compiles
  but misbehaves at runtime (e.g. a missing `UnmanagedCallersOnly` export)
  is not caught until the end-to-end lane exists.
- AOT/trim warnings are not promoted to errors here; doing that (e.g.
  `/p:TreatWarningsAsErrors=true` or `TrimmerSingleWarn=false` tightening)
  is a deliberate follow-up once the baseline is warning-clean.
- The linux-x64 publish exercises the same managed code as the osx-arm64
  local path; RID-conditional managed code does not exist today. If someone
  adds any, this job's coverage claim must be revisited.
