# Plan 010: Make Darwin installer lifecycle state thread-safe

> **Executor instructions**: This is a deferred follow-up for the structured
> startup errors branch. Follow the steps in order. If anything in "STOP
> conditions" occurs, stop and report instead of widening scope. When done,
> update the status row in `docs/plans/README.md`.
>
> **Drift check (run first)**:
>
> ```sh
> git diff --stat e861b7c9..HEAD -- packages/expo-modules-dotnet/ios/ExpoModulesDotnetInstaller.mm packages/expo-modules-dotnet/macos/ExpoModulesDotnetInstaller.mm packages/expo-modules-dotnet/android/src/main/cpp/ExpoModulesDotnetBindingsInstaller.cpp packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ExpoModulesDotnetInstaller.cpp
> git diff --stat -- packages/expo-modules-dotnet/ios/ExpoModulesDotnetInstaller.mm packages/expo-modules-dotnet/macos/ExpoModulesDotnetInstaller.mm packages/expo-modules-dotnet/android/src/main/cpp/ExpoModulesDotnetBindingsInstaller.cpp packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ExpoModulesDotnetInstaller.cpp
> ```
>
> This plan was written while the structured startup error implementation files
> had uncommitted changes. If either command reports in-scope file changes,
> compare the "Current state" excerpts below against live code before editing.
> On mismatch, treat it as a STOP condition.

## Status

- **Priority**: P1
- **Effort**: M
- **Risk**: MED — native lifecycle locking around teardown can deadlock if it
  holds a mutex while calling managed or React Native code
- **Depends on**: none; should land after the structured startup error slice so
  it does not collide with active implementation edits
- **Category**: bug / tech-debt
- **Planned at**: commit `e861b7c9`, 2026-07-09

## Why this matters

The structured startup error slice intentionally fixes teardown exception
containment and Android loader diagnostics inline, but defers Darwin locking.
The remaining Darwin adapter state is mutable across registration, error
reporting, runtime replacement, and invalidation without synchronization.
Android and Windows already protect equivalent installer/runtime state with
mutexes; iOS and macOS should either match that locking model or document and
enforce a proven single-thread affinity. Prefer locking unless React Native
evidence proves every call path is same-thread.

## Current state

- `packages/expo-modules-dotnet/ios/ExpoModulesDotnetInstaller.mm` owns the iOS
  TurboModule installer and NativeAOT runtime registration. `InstalledRuntime`
  mutates `registered_`, `lastError_`, `managedRuntimeContext_`, and
  `teardownRuntimeContext_` during registration and destruction without an
  internal mutex. Relevant lines at plan time:
  - `95-105`: `~InstalledRuntime()` invalidates the connector, calls
    `teardownRuntimeContext_(managedRuntimeContext_)`, nulls
    `managedRuntimeContext_`, then releases the runtime handle.
  - `109-140`: `registerModules()` reads `registered_`, resolves entry points,
    writes `teardownRuntimeContext_`, `lastError_`, `managedRuntimeContext_`,
    and `registered_`.
  - `144-146`: `lastError()` returns `lastError_` with no lock.
  - `221-230`: `installJSIBindingsWithRuntime` assigns `_installedRuntime`.
  - `233-255`: `installModules`, `getLastError`, and `invalidate` read or reset
    `_installedRuntime` directly.
- `packages/expo-modules-dotnet/macos/ExpoModulesDotnetInstaller.mm` follows the
  same pattern for the desktop adapter. Relevant lines at plan time:
  - `44-54`: `~InstalledRuntime()` invalidates the connector, calls managed
    teardown, then releases the runtime handle.
  - `58-95`: `registerModules()` reads/writes `registered_`, `lastError_`,
    `managedRuntimeContext_`, and `teardownRuntimeContext_` with no mutex.
  - `99-101`: `lastError()` reads `lastError_` with no mutex.
  - `181-191`: `installJSIBindingsWithRuntime` replaces `_installedRuntime`.
  - `194-231`: `installModulesWithRuntime`, `installModules`, `getLastError`,
    and `invalidate` read or reset `_installedRuntime` directly.
- `packages/expo-modules-dotnet/android/src/main/cpp/ExpoModulesDotnetBindingsInstaller.cpp`
  is the nearest existing locking precedent, not an edit target for this plan:
  - `6`: includes `<mutex>`.
  - `62-64`: global `installedRuntimeMutex`, `installedRuntime`, and
    `lastError`.
  - `168-182`: prepare/install paths acquire `installedRuntimeMutex` before
    replacing or reading `installedRuntime`.
- `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ExpoModulesDotnetInstaller.cpp`
  is a stronger teardown precedent, not an edit target for this plan:
  - `112-116`: `InstalledRuntime::registerModules` stores managed runtime
    context under `mutex`.
  - `150-181`: `teardown()` moves connector/runtime state into locals under
    lock, then calls managed teardown after the lock is released.
  - `201-205`: `InstallerState` owns a mutex-protected installer record.
  - `217-269`, `284-315`: initialization, `installModules`, and
    `getLastError` guard shared state with `state->mutex`.
- `docs/specs/runtime-scheduling.md` says React Native connectors hold a
  borrowed `facebook::jsi::Runtime` and `CallInvoker` inside an explicit
  runtime-state holder; the raw runtime pointer is non-owning, and holder
  invalidation is the lifetime primitive. The same spec allows React Native
  macOS to capture the current JSI runtime from the installer TurboModule host
  function.

## Commands you will need

| Purpose | Command | Expected on success |
|---|---|---|
| Managed suite | `scripts/test-managed.sh` | all pass |
| Mobile iOS build/smoke | `pnpm --filter mobile-app ios` | build succeeds and app launches on the selected simulator |
| Desktop macOS build/smoke | `pnpm --filter desktop-app macos` | build succeeds and app launches |
| Desktop typecheck | `pnpm --filter desktop-app typecheck` | exit 0 |
| Format check | `scripts/format.sh --check --all` | exit 0 |
| Whitespace | `git diff --check` | exit 0 |

If the iOS or macOS native build is impractical in the executor environment,
record the exact blocker and perform targeted ObjC++ reasoning against the
files above; do not claim platform verification passed.

## Scope

**In scope**:
- `packages/expo-modules-dotnet/ios/ExpoModulesDotnetInstaller.mm`
- `packages/expo-modules-dotnet/macos/ExpoModulesDotnetInstaller.mm`
- Darwin-only tests or smoke notes if the repo has a suitable harness
- `docs/plans/README.md` status row

**Out of scope**:
- Android and Windows implementation files. Use them only as examples.
- Managed packages, source generators, autolinking CLI, and living specs unless
  the implementation changes a documented runtime contract.
- The structured startup error behavior itself. This plan must not rewrite the
  error messages or teardown exception containment from that slice.

## Git workflow

- Branch: `codex/darwin-installer-lifecycle-locking`
- Commit style: match recent history, for example
  `fix(ios): guard installer runtime lifecycle state`.
- Do not push or open a PR unless the operator explicitly asks.
- Before staging, check the diff does not contain local absolute paths,
  usernames, machine names, private hostnames, or machine-specific install
  paths.

## Steps

### Step 1: Decide locking vs single-thread affinity

Read the live iOS/macOS installer files and the React Native module call paths
they rely on. Try to prove whether `installModules`, `getLastError`,
`invalidate`, and macOS `installModulesWithRuntime` are guaranteed to run on
one thread for the lifetime of the module instance.

Preferred decision: add explicit locking. Only choose documented/enforced
single-thread affinity if there is concrete React Native evidence in this repo
or upstream headers that all relevant calls are same-thread.

**Verify**: record the decision in the implementation PR/commit notes. If
choosing single-thread affinity, include the exact evidence path and line
references; otherwise proceed with locking.

### Step 2: Add installer-level synchronization without re-entrant teardown

For both Darwin files, protect `_installedRuntime` access with an Objective-C++
member mutex. The installer methods should take a local `std::shared_ptr` copy
under lock, then release the lock before calling `InstalledRuntime` methods or
destroying the old runtime when possible.

Target shape:

- `installJSIBindingsWithRuntime` / macOS replacement path: create the new
  runtime before taking the installer mutex; swap `_installedRuntime` under
  lock; let the old `shared_ptr` destruct after the lock is released.
- `installModules`: copy `_installedRuntime` under lock; if null, report the
  existing not-ready error; call `registerModules()` outside the installer
  mutex.
- `getLastError`: copy `_installedRuntime` under lock; call `lastError()`
  outside the installer mutex; preserve current fallback behavior.
- `invalidate`: move/reset `_installedRuntime` under lock; let teardown run
  outside the installer mutex.

**Verify**: `git diff --check` exits 0. Read the diff and confirm no managed
teardown or React Native call happens while holding the installer mutex.

### Step 3: Add runtime-level synchronization without holding locks through managed teardown

For both `InstalledRuntime` classes, protect `registered_`, `lastError_`,
`managedRuntimeContext_`, and `teardownRuntimeContext_`.

Target shape:

- Add `mutable std::mutex mutex_;`.
- `lastError()` returns a copy under `mutex_`.
- `registerModules()` prevents double registration safely. Do not hold
  `mutex_` while resolving symbols or calling `createRuntimeContext`; after the
  call returns, lock to publish `managedRuntimeContext_`,
  `teardownRuntimeContext_`, `registered_`, and `lastError_`.
- Destructor/teardown moves `connector_`, `runtimeHandle_`,
  `managedRuntimeContext_`, and `teardownRuntimeContext_` into locals under
  `mutex_`, clears the stored fields, then releases the lock before calling
  connector invalidation, managed teardown, or runtime-handle release. Use the
  Windows `teardown()` shape as the conceptual model.

Be careful about duplicate concurrent `registerModules()` calls: either serialize
registration so only one caller enters the create path, or introduce a clear
in-progress state. Do not let two callers create two managed runtime contexts
for the same borrowed JSI runtime.

**Verify**: `git diff --check` exits 0. Inspect both files and confirm no lock
is held while calling `teardownRuntimeContext`, `connector->invalidate()`, or
React Native/CallInvoker code.

### Step 4: Platform verification

Run the common managed gate and the Darwin smoke/build checks that are
available in the executor environment.

**Verify**:
- `scripts/test-managed.sh` -> all pass.
- `pnpm --filter desktop-app typecheck` -> exit 0.
- `pnpm --filter desktop-app macos` -> build succeeds and launches, or record
  the precise unavailable-tooling blocker.
- `pnpm --filter mobile-app ios` -> build succeeds and launches, or record the
  precise unavailable-tooling blocker.
- `scripts/format.sh --check --all` -> exit 0.
- `git diff --check` -> exit 0.

## Test plan

- There is likely no unit harness for ObjC++ installer lifecycle races. If one
  exists when this plan is executed, add a targeted regression test that calls
  install/getLastError/invalidate/reinstall in an order that would have raced.
- If no harness exists, the required verification is code review plus the iOS
  and macOS app builds/smokes above. The reviewer should inspect the locking
  boundaries manually.
- Do not add synthetic tests that only assert current error strings; this plan
  is about state ownership and teardown safety, not diagnostics text.

## Done criteria

- [ ] iOS installer protects `_installedRuntime` and `InstalledRuntime` mutable
  fields, with no managed teardown under lock.
- [ ] macOS installer protects `_installedRuntime` and `InstalledRuntime`
  mutable fields, including `installModulesWithRuntime` replacement, with no
  managed teardown under lock.
- [ ] Android and Windows files are unchanged except for incidental reads.
- [ ] `scripts/test-managed.sh` passes, or an environment blocker is recorded.
- [ ] macOS build/smoke passes, or an environment blocker is recorded.
- [ ] iOS build/smoke passes, or an environment blocker is recorded.
- [ ] `scripts/format.sh --check --all` and `git diff --check` pass.
- [ ] `docs/plans/README.md` status row updated.

## STOP conditions

Stop and report back if:

- Live iOS/macOS code no longer matches the current-state excerpts and the safe
  locking point is unclear.
- A correct fix requires touching Android, Windows, managed runtime code, or
  structured startup error behavior outside this plan.
- You find concrete evidence that React Native guarantees same-thread access
  but the repo has no way to enforce or document that guarantee locally.
- Avoiding locks around managed teardown would require a larger lifecycle
  redesign.
- Platform builds fail for reasons unrelated to this plan after one reasonable
  retry; record the exact command and error instead of masking it.

## Maintenance notes

Reviewers should focus on lock boundaries, not just presence of mutexes. The
important invariant is: shared installer/runtime state is protected, but calls
into managed teardown, connector invalidation, symbol resolution, and React
Native scheduling are not made while holding locks. Future Darwin adapter
changes that add runtime replacement, lazy registration, or new installer
methods must use the same pattern.
