# Host-Function Owned Callback State Implementation Plan

**Goal:** Add opt-in, exactly-once disposal of one host function's managed
callback state across creation failure, JavaScript collection, and runtime
teardown.

**Architecture:** `HostFunctionContext` stores the configured disposer and owns
an interlocked terminal guard. `JavaScriptRuntime.CreateHostFunction` preserves
its exact four-parameter overload and adds a separate five-parameter owned-state
overload. Both managed creation failure and the existing native release callback
use the same contained release operation.

**Tech stack:** C#, .NET, xUnit, Hermes-backed native testhost.

## Constraints

- Modify only the two managed production files, host-function tests, this
  change folder, and `docs/specs/host-functions-and-errors.md`.
- Do not change native code, the C ABI, host objects, `Expo.ModulesCore`, or
  shared-object events.
- Keep every existing `CreateHostFunction` call source- and binary-compatible
  by preserving the exact four-parameter overload.
- Do not let a managed exception cross `ReleaseHostFunctionContext`.
- Write and run each lifecycle test before the production change required to
  pass it.

## Task 1: Owned callback-state release

**Files:**

- Modify `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptRuntime.cs`.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.JSI/Interop/HostFunctionContext.cs`.
- Add lifecycle tests under
  `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/HostFunctions/`.

1. Add tests for exactly-once disposal after forced JavaScript GC and at
   runtime teardown. Run the host-function tests and confirm RED because the
   owned-state overload does not exist.
2. Add the five-parameter overload, interlocked terminal guard, and contained
   common release path. Run the host-function tests and confirm GREEN.
3. Add a creation-failure test by invalidating the runtime from the test thread
   before calling the creation path directly. Invalidating inside an active
   executor frame self-joins the testhost executor. Confirm the disposer uses
   the existing creation-failure release and the test passes.
4. Add tests for omitted disposal and a throwing disposer. Confirm each test
   can fail for the intended behavior, then confirm cleanup and the remaining
   host-function tests pass.
5. Add forced-GC and teardown tests whose owned callback state contains a
   `JavaScriptWeakObject`. Confirm the tests fail before wiring the disposer,
   then confirm safe disposal and native weak-handle cleanup.
6. Run `scripts/test-managed.sh` and commit the verified production and test
   changes as `feat(jsi): dispose owned host-function callback state`.

## Task 2: Merge the accepted contract

**Files:**

- Modify `docs/specs/host-functions-and-errors.md`.
- Move this change folder to
  `docs/archive/changes/2026-07-22-hostfunction-owned-state/`.

1. Merge the overload ownership, terminal-path, thread-safety, weak-object,
   shared-state, and exception-containment requirements into the living spec.
2. Archive `spec.md` and `plan.md` after the living spec becomes authoritative.
3. Run `scripts/format.sh --check --all` and `git diff --check`.
4. Commit the living-spec merge and archive as
   `docs(jsi): merge owned host-function state contract`.
