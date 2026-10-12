# Runtime Lifetime Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make native runtime ownership explicit, fix Windows early teardown, and confine test-only bridge controls without changing ArrayBuffer semantics.

**Architecture:** Split runtime state and the long-lived collection from the ABI implementation file while preserving existing host build integration. The Windows installer moves its connector and runtime handle into locals before invoking the common teardown sequence. Private test hooks retain Hermes testhost capability without expanding production-facing headers.

**Tech Stack:** C++20, React Native JSI/RNW, CMake, CocoaPods forward sources, MSBuild, Hermes testhost, Markdown living specs.

---

### Task 1: Record the lifetime contract and future build-composition work

**Files:**

- Modify: `docs/roadmap.md`
- Modify: `docs/specs/runtime-and-abi.md`
- Modify: `docs/specs/hermes-testhost.md`
- Modify: `.agents/skills/expo-jsi-managed-handle-lifetime/SKILL.md`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/include/JsiRuntimeConnector.h`

- [ ] **Step 1: Add the future build-composition item**

Add a P3 roadmap item for a shared native source-set mechanism spanning Android CMake, Apple forward sources, testhost CMake, and Windows MSBuild. State that it is future work and does not change current ABI or lifetime behavior.

- [ ] **Step 2: Add the native ownership map**

Document this relationship in the living spec and lifetime skill:

```text
Host owns Connector; Connector owns or borrows jsi::Runtime by host type.
RuntimeHandle owns shared RuntimeState.
RuntimeState borrows Connector only while Active or Closing.
RuntimeState owns LongLivedObjectCollection.
Collection owns entries; entries retain RuntimeState until collection erase.
```

Document the teardown order:

```text
prepare runtime handle -> invalidate connector -> tear down managed context
-> release runtime handle -> destroy connector
```

- [ ] **Step 3: Add the connector class comment**

Document that the host owns `JsiRuntimeConnector`, bridge state only borrows it, and `invalidate()` must make future runtime/executor access fail before connector destruction.

- [ ] **Step 4: Check docs**

Run:

```sh
git diff --check
rg "TODO|TBD" docs/changes/2026-07-13-runtime-lifetime-cleanup docs/specs/runtime-and-abi.md docs/specs/hermes-testhost.md .agents/skills/expo-jsi-managed-handle-lifetime/SKILL.md
```

Expected: no diff errors or new placeholders.

### Task 2: Split native lifetime implementation into matching files

**Files:**

- Create: `packages/expo-modules-dotnet/native/packages/jsi/src/RuntimeState.h`
- Create: `packages/expo-modules-dotnet/native/packages/jsi/src/RuntimeState.cpp`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/src/LongLivedObjectCollection.h`
- Create: `packages/expo-modules-dotnet/native/packages/jsi/src/LongLivedObjectCollection.cpp`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/src/ArrayBufferHandles.h`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp`
- Modify: `packages/expo-modules-dotnet/android/src/main/cpp/CMakeLists.txt`
- Modify: `packages/expo-modules-dotnet/native/testhost/CMakeLists.txt`
- Modify: `packages/expo-modules-dotnet/ios/ExpoJsiBridgeForward.cpp`
- Modify: `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ExpoModulesDotnet.vcxproj`
- Modify: `apps/hermes-console-app/native/CMakeLists.txt`

- [ ] **Step 1: Write source-ownership comments**

Use comments that establish:

```cpp
// RuntimeHandle is the opaque ABI owner of shared RuntimeState.
// RuntimeState borrows its connector until invalidation clears the pointer.
// LongLivedObjectCollection owns entries and erases them to break entry/state cycles.
```

Keep `RuntimeState::create` as the factory because its constructor is private. Do not change it merely to use `std::make_shared`.

- [ ] **Step 2: Move RuntimeState declarations and definitions**

Move `RuntimeState` into `RuntimeState.h`. Move `runtime`, `executor`, `prepareForInvalidation`, `invalidateWithoutRuntime`, and `tryInvalidateWithoutRuntime` into `RuntimeState.cpp`. Preserve Active → Closing → Invalid and the existing early-sweep fallback.

- [ ] **Step 3: Move collection and token definitions**

Keep collection declarations and `ScheduledReleaseToken` in `LongLivedObjectCollection.h`. Move deferred-release, sweep, abandon, and token definitions into `LongLivedObjectCollection.cpp`.

- [ ] **Step 4: Update platform source lists**

Add both `.cpp` files before `ExpoJsiBridge.cpp` in Android CMake, testhost CMake, the Hermes console app CMake target, iOS forward source, and Windows MSBuild. The iOS forward source must include all three implementation units:

```cpp
#include "../native/packages/jsi/src/LongLivedObjectCollection.cpp"
#include "../native/packages/jsi/src/RuntimeState.cpp"
#include "../native/packages/jsi/src/ExpoJsiBridge.cpp"
```

- [ ] **Step 5: Run focused proof**

Run:

```sh
scripts/test-managed.sh --filter FullyQualifiedName~JavaScriptArrayBufferTests
scripts/format.sh --check --all
git diff --check
```

Expected: focused lifetime tests and formatting pass.

### Task 3: Make test-only native controls private

**Files:**

- Create: `packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridgeTestHooks.h`
- Create: `packages/expo-modules-dotnet/native/packages/jsi/src/HermesConsoleRuntimeTestControl.h`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/include/ExpoJsiBridge.h`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/include/HermesConsoleRuntimeConnector.h`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/src/HermesConsoleRuntimeConnector.cpp`
- Modify: `packages/expo-modules-dotnet/native/testhost/CMakeLists.txt`
- Modify: `packages/expo-modules-dotnet/native/testhost/src/ExpoJsiTestHost.cpp`
- Modify: `docs/specs/hermes-testhost.md`

- [ ] **Step 1: Hide bridge test hooks**

Keep `prepareRuntimeHandleForInvalidation` in public `ExpoJsiBridge.h`. Move counter, snapshot-validation, and bridge-handle test-release declarations into `ExpoJsiBridgeTestHooks.h` so only testhost includes them.

- [ ] **Step 2: Add the Hermes test-control companion**

Make queue-control operations private on `HermesConsoleRuntimeConnector`. Forward-declare and friend `HermesConsoleRuntimeTestControl`; expose static companion methods only in its private header.

- [ ] **Step 3: Update testhost calls and include paths**

Use the private hook and companion headers from `ExpoJsiTestHost.cpp`. Add the JSI `src` directory as a testhost-private include directory only. Keep fixture release as the explicit abrupt-shutdown test mode; use the existing prepare-for-invalidation control for JSI-safe teardown tests.

- [ ] **Step 4: Verify testhost behavior**

Run:

```sh
scripts/test-managed.sh
scripts/format.sh --check --all
git diff --check
```

Expected: all managed tests pass and testhost queue/teardown ordering controls remain available.

### Task 4: Fix Windows teardown order

**Files:**

- Modify: `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ExpoModulesDotnetInstaller.cpp`
- Modify: `docs/specs/runtime-and-abi.md`

- [ ] **Step 1: Move owners under the mutex**

Under `InstalledRuntime::mutex`, mark teardown complete and move the connector, runtime handle, managed context, and callback into locals. Do not call connector or JSI code under the lock.

- [ ] **Step 2: Apply the common lifecycle order outside the mutex**

Use:

```cpp
if (runtimeHandleToRelease != nullptr) {
  expo::dotnet::prepareRuntimeHandleForInvalidation(runtimeHandleToRelease);
}
if (connectorToRelease != nullptr) {
  connectorToRelease->invalidate();
}
if (teardownRuntimeContextFn != nullptr && managedRuntimeContextToTeardown != nullptr) {
  teardownRuntimeContextFn(managedRuntimeContextToTeardown);
}
if (runtimeHandleToRelease != nullptr) {
  expo::dotnet::releaseReactNativeRuntimeHandle(runtimeHandleToRelease);
}
```

- [ ] **Step 3: Verify and record platform evidence**

Run:

```sh
scripts/test-managed.sh
scripts/format.sh --check --all
git diff --check
```

Expected: portable testhost and managed suite pass. Record that Windows compilation remains required from CI or the designated Windows machine; do not claim a local Windows build passed.
