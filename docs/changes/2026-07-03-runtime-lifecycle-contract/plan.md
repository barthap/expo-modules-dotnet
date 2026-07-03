# Runtime Lifecycle Contract Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement a production runtime lifecycle contract where each JavaScript runtime owns a managed module session that can be torn down deterministically by host adapters.

**Architecture:** Add an `Expo.ModulesCore` runtime session that owns generated module instances and invalidatable host-function registration state. Native adapters keep owning borrowed runtime holders and invoke a managed teardown callback during invalidation; headless Hermes tests model both early teardown and late no-JSI invalidation. Generated sync functions stay direct JSI host functions and do not depend on sync scheduler support.

**Tech Stack:** C#/.NET 10, Roslyn source generator, xUnit, C ABI, C++17 JSI bridge, Hermes testhost, React Native TurboModule/native module adapters for Android/iOS/macOS/Windows.

---

## File Structure

- Create `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/RuntimeSession.cs`
  - Runtime-scoped owner for generated module instances, host-function registration contexts, teardown state, and session invalidation.
- Create `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/GeneratedHostFunctionRegistration.cs`
  - Small invalidatable callback context pinned by `Expo.JSI.HostFunctionContext`; holds module/callback state while live and clears module references during teardown.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/GeneratedFunction.cs`
  - Add session-aware `DefineSync` overload and route generated host functions through invalidatable registrations.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/ModuleRegistry.cs`
  - Add session-oriented module object helpers without removing existing static convenience APIs.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
  - Emit generated providers that receive a runtime session and register module instances through it.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`
  - Update generated source expectations for the new provider shape.
- Create `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/RuntimeSessionTests.cs`
  - Prove teardown releases module references and rejects calls after teardown.
- Modify existing generated module tests under `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/`
  - Register through `RuntimeSession` and keep behavior coverage unchanged.
- Modify entry points:
  - `packages/example-module/dotnet/ExampleModule/EntryPoints.cs`
  - `apps/hermes-console-app/managed/HermesConsoleApp/EntryPoints.cs`
  - Any hand-authored provider proof still calling generated registration directly.
- Modify native ABI/adapter files only after managed behavior is covered:
  - `packages/expo-modules-dotnet/native/include/expo_jsi.h`
  - `packages/expo-modules-dotnet/native/packages/jsi/include/JsiRuntimeConnector.h`
  - `packages/expo-modules-dotnet/native/packages/jsi/include/ReactNativeRuntimeConnector.h`
  - `packages/expo-modules-dotnet/native/packages/jsi/src/ReactNativeRuntimeConnector.cpp`
  - `packages/expo-modules-dotnet/native/packages/jsi/include/HermesConsoleRuntimeConnector.h`
  - `packages/expo-modules-dotnet/native/packages/jsi/src/HermesConsoleRuntimeConnector.cpp`
  - `packages/expo-modules-dotnet/native/testhost/include/expo_jsi_testhost.h`
  - `packages/expo-modules-dotnet/native/testhost/src/ExpoJsiTestHost.cpp`
  - `packages/expo-modules-dotnet/android/src/main/cpp/ExpoModulesDotnetBindingsInstaller.cpp`
  - `packages/expo-modules-dotnet/ios/ExpoModulesDotnetInstaller.mm`
  - `packages/expo-modules-dotnet/macos/ExpoModulesDotnetInstaller.mm`
- Modify docs after implementation:
  - `docs/specs/runtime-and-abi.md`
  - `docs/specs/runtime-scheduling.md`
  - `docs/specs/modules-core-boundary.md`
  - `docs/roadmap.md`

Do not add artifact staging, loader configuration, package registration, prebuild, or autolinking work to this plan. Those belong to a later autolinking/prebuild milestone.

---

## Task 1: Managed Runtime Session

**Files:**
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/RuntimeSession.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/GeneratedHostFunctionRegistration.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/GeneratedFunction.cs`
- Test: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/RuntimeSessionTests.cs`

- [ ] **Step 1: Write failing teardown tests**

  Add tests that:

  - create a runtime session;
  - register a generated-style sync function through the session;
  - verify the function works before teardown;
  - call session teardown;
  - verify a later JS call throws a clear disposed-session error;
  - verify the module instance is no longer strongly retained after teardown.

  Use `WeakReference` for the module-retention assertion. Avoid asserting GC timing unless the test explicitly forces collection after clearing all strong references.

- [ ] **Step 2: Run the focused tests and confirm failure**

  Run:

  ```sh
  EXPO_JSI_TESTHOST_LIBRARY="$(pwd)/packages/expo-modules-dotnet/native/testhost/build/libexpo_jsi_testhost.dylib" \
    dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj \
    --filter RuntimeSessionTests
  ```

  Expected: fail because `RuntimeSession` does not exist.

- [ ] **Step 3: Implement `RuntimeSession`**

  Minimum API:

  - constructor accepts `JavaScriptRuntime`;
  - exposes `JavaScriptRuntime Runtime`;
  - exposes `JavaScriptObject GetOrCreateDotnetModulesObject()`;
  - tracks generated host-function registrations;
  - exposes `T GetOrCreateModule<T>(string moduleName, Func<T> factory)`;
  - exposes idempotent `Dispose()` / `TearDown()`;
  - throws `ObjectDisposedException` after teardown.

  Keep it in `Expo.ModulesCore`, not `Expo.JSI`.

- [ ] **Step 4: Implement invalidatable generated host-function registration**

  `GeneratedHostFunctionRegistration` should be the object pinned by the low-level host function context. It should:

  - hold module instance and callback delegate while live;
  - expose `Invoke(...)`;
  - clear module/callback references on session teardown;
  - throw a clear `ObjectDisposedException` if invoked after teardown;
  - tolerate native release callback arriving later.

  Do not try to free the native `HostFunctionContext` GCHandle directly from the session. The native host-function value owns that pin until JSI releases the function. Session teardown only clears the heavy managed references behind the small pinned shell.

- [ ] **Step 5: Add session-aware `GeneratedFunction.DefineSync`**

  Add an overload that receives `RuntimeSession`, `JavaScriptObject module`, function metadata, callback, and module instance. Keep existing overloads only as compatibility wrappers if needed for existing tests.

- [ ] **Step 6: Run focused tests**

  Run the same `dotnet test --filter RuntimeSessionTests` command.

  Expected: pass.

- [ ] **Step 7: Commit Task 1**

  ```sh
  git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests
  git commit -m "feat: add runtime-scoped module session"
  ```

---

## Task 2: Generated Provider Session Shape

**Files:**
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/*.cs`

- [ ] **Step 1: Update generator tests first**

  Change expectations so generated providers expose:

  ```csharp
  public static void Register(global::Expo.ModulesCore.RuntimeSession session)
  ```

  or an equivalent session-first shape. The generated provider should not instantiate modules directly inside `GeneratedFunction.DefineSync`; module instances should be created through the session.

- [ ] **Step 2: Run generator tests and confirm failure**

  ```sh
  dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj
  ```

  Expected: fail on old generated provider shape.

- [ ] **Step 3: Update `ExpoModulesGenerator`**

  Emit session-oriented provider code:

  - validate `session`;
  - get modules object from session;
  - define each module object through `ModuleRegistry`;
  - get or create one module instance per module name through session;
  - call session-aware `GeneratedFunction.DefineSync`.

  Preserve direct-call, reflection-free generated function bodies.

- [ ] **Step 4: Update generated module tests**

  Replace:

  ```csharp
  using var modules = ModuleRegistry.GetOrCreateDotnetModulesObject(runtime);
  ExpoModulesProvider_...Register(runtime, modules);
  ```

  with session registration.

- [ ] **Step 5: Run generator and module tests**

  ```sh
  dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj
  EXPO_JSI_TESTHOST_LIBRARY="$(pwd)/packages/expo-modules-dotnet/native/testhost/build/libexpo_jsi_testhost.dylib" \
    dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj
  ```

  Expected: pass.

- [ ] **Step 6: Commit Task 2**

  ```sh
  git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests
  git commit -m "feat: generate session-backed module providers"
  ```

---

## Task 3: Managed Entry Point Teardown ABI Shape

**Files:**
- Modify: `packages/example-module/dotnet/ExampleModule/EntryPoints.cs`
- Modify: `apps/hermes-console-app/managed/HermesConsoleApp/EntryPoints.cs`
- Modify: `apps/hermes-console-app/managed/HermesConsoleApp/GeneratedModuleProvider.cs` if still hand-written.
- Create or modify managed tests if entry-point helpers are factored for testability.

- [ ] **Step 1: Define managed registration result shape**

  Choose the smallest ABI-compatible shape that lets native receive:

  - status code;
  - managed teardown callback;
  - managed teardown context.

  Prefer additive exported entry points or result structs only if the NativeAOT and HostFXR paths can both resolve them cleanly. Do not break the current simple registration entry point until native callers are updated in the same plan.

- [ ] **Step 2: Factor managed session creation**

  Add a helper in each managed proof entry point that:

  - creates `JavaScriptRuntime.FromNative(...)`;
  - creates `RuntimeSession`;
  - registers the generated provider through the session;
  - returns the session as teardown context.

- [ ] **Step 3: Add teardown export/callback**

  Add a managed callback that accepts the session context and calls session teardown idempotently. Use `GCHandle` only for the session context, and make ownership explicit.

- [ ] **Step 4: Update hermes console hand-written provider path**

  If `apps/hermes-console-app/managed/HermesConsoleApp/GeneratedModuleProvider.cs` still bypasses generated session helpers, either migrate it to `RuntimeSession` or document it as a proof-only compatibility path and ensure teardown still clears module references.

- [ ] **Step 5: Run managed tests**

  ```sh
  scripts/test-managed.sh
  ```

  Expected: pass.

- [ ] **Step 6: Commit Task 3**

  ```sh
  git add packages/example-module/dotnet/ExampleModule \
    apps/hermes-console-app/managed/HermesConsoleApp
  git commit -m "feat: expose managed runtime teardown"
  ```

---

## Task 4: Native Lifecycle Interfaces And Headless Testhost

**Files:**
- Modify: `packages/expo-modules-dotnet/native/include/expo_jsi.h`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/include/JsiRuntimeConnector.h`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/include/HermesConsoleRuntimeConnector.h`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/src/HermesConsoleRuntimeConnector.cpp`
- Modify: `packages/expo-modules-dotnet/native/testhost/include/expo_jsi_testhost.h`
- Modify: `packages/expo-modules-dotnet/native/testhost/src/ExpoJsiTestHost.cpp`
- Modify or create managed fixture APIs under `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Fixtures/`

- [ ] **Step 1: Add failing native lifecycle tests through managed fixtures**

  Add tests that ask the testhost to:

  - create a runtime;
  - register a session-backed provider;
  - trigger early teardown;
  - trigger late invalidation;
  - release queued work before it runs.

  These tests should fail before native testhost support exists.

- [ ] **Step 2: Decide whether to split headless connector/executor**

  If the current `HermesConsoleRuntimeConnector` is too coupled for lifecycle tests, split it into small pieces:

  - runtime holder / lifecycle state;
  - executor loop / queue;
  - connector facade.

  Keep the split private to native testhost/shared JSI code unless another host adapter needs it.

- [ ] **Step 3: Add connector lifecycle callbacks**

  Extend `JsiRuntimeConnector` or a small adjacent lifecycle interface so native code can:

  - invalidate holder state;
  - run registered managed teardown;
  - release queued work;
  - expose whether JSI cleanup is still allowed.

  Keep this interface small. Do not add a broad app-context abstraction.

- [ ] **Step 4: Implement early teardown in headless runtime**

  Early teardown should run while the Hermes runtime is still valid on its runtime thread. It may release JSI-owned resources where required.

- [ ] **Step 5: Implement late invalidation in headless runtime**

  Late invalidation should mark runtime invalid and run managed teardown without touching JSI. It should release queued work and fault/cancel managed tasks.

- [ ] **Step 6: Run native and managed focused tests**

  ```sh
  scripts/test-managed.sh
  ```

  Expected: lifecycle tests pass.

- [ ] **Step 7: Commit Task 4**

  ```sh
  git add packages/expo-modules-dotnet/native \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests
  git commit -m "feat: model runtime teardown in testhost"
  ```

---

## Task 5: React Native Adapter Wiring

**Files:**
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/include/ReactNativeRuntimeConnector.h`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/src/ReactNativeRuntimeConnector.cpp`
- Modify: `packages/expo-modules-dotnet/android/src/main/cpp/ExpoModulesDotnetBindingsInstaller.cpp`
- Modify: `packages/expo-modules-dotnet/ios/ExpoModulesDotnetInstaller.mm`
- Modify: `packages/expo-modules-dotnet/macos/ExpoModulesDotnetInstaller.mm`
- Modify Windows adapter files in the active Windows proof when synced into this checkout.

- [ ] **Step 1: Update shared React Native connector**

  Add storage for managed teardown callback/context beside the borrowed runtime holder. Invalidation must:

  - prevent new scheduled work;
  - release queued work;
  - run managed teardown exactly once;
  - release the opaque runtime handle after teardown.

- [ ] **Step 2: Android install record lifecycle**

  Replace process-lifetime install records with adapter-owned records that can be invalidated from a React Native module/TurboModule lifecycle hook. Do not modify main application entry points.

  If the current package hook cannot observe invalidation, document the exact missing hook and implement the narrowest native module lifecycle subscription available.

- [ ] **Step 3: iOS install record lifecycle**

  Wire teardown through Expo/RN module lifecycle APIs where possible. Keep installation in package/module code. Do not add broad app delegate edits.

- [ ] **Step 4: macOS New Architecture lifecycle**

  Target the `RCTHost` / `ExpoReactNativeFactory` / New Architecture runtime model. Do not make Old Architecture bridge invalidation the primary lifecycle path.

  If this requires a temporary app-local hook before Expo Desktop prebuild support exists, keep it narrow and document that it belongs to future autolinking/prebuild work.

- [ ] **Step 5: Windows lifecycle caveat**

  For Windows, distinguish:

  - early teardown hook if a valid RNW runtime-before-destroy hook exists;
  - late invalidation if only `InstanceDestroyed` is available.

  Never run JSI-touching teardown from a late hook.

- [ ] **Step 6: Run available adapter builds/tests**

  Minimum local verification:

  ```sh
  scripts/test-managed.sh
  pnpm --filter mobile-app typecheck
  pnpm --filter desktop-app typecheck
  scripts/format.sh --check --all
  ```

  Run platform builds only for the hosts touched in the implementation slice and available on the current machine.

- [ ] **Step 7: Commit Task 5**

  ```sh
  git add packages/expo-modules-dotnet/android \
    packages/expo-modules-dotnet/ios \
    packages/expo-modules-dotnet/macos \
    packages/expo-modules-dotnet/native/packages/jsi
  git commit -m "feat: wire runtime teardown into adapters"
  ```

---

## Task 6: Living Specs And Roadmap Merge

**Files:**
- Modify: `docs/specs/runtime-and-abi.md`
- Modify: `docs/specs/runtime-scheduling.md`
- Modify: `docs/specs/modules-core-boundary.md`
- Modify: `docs/roadmap.md`
- Move or archive: `docs/changes/2026-07-03-runtime-lifecycle-contract/`

- [ ] **Step 1: Update living specs**

  Merge accepted behavior from `spec.md` and implementation results into:

  - `runtime-and-abi.md` for teardown callback / opaque handle ownership;
  - `runtime-scheduling.md` for stale work and scheduler semantics;
  - `modules-core-boundary.md` for runtime session and generated provider shape.

- [ ] **Step 2: Update roadmap**

  Mark the lifecycle contract and teardown implementation progress accurately. Keep any remaining Windows caveat explicit if it is not fully implemented.

- [ ] **Step 3: Archive or remove change artifacts**

  Follow the repo convention used by earlier completed changes. If archiving, move `docs/changes/2026-07-03-runtime-lifecycle-contract/` under `docs/archive/changes/`.

- [ ] **Step 4: Run docs checks**

  ```sh
  git diff --check
  rg "self[-]contained planning package|planning[ ]artifacts,[ ]not[ ]implementation|expo[-]modules[-]windows[-]core|Phase[ ]1:[ ]clean[ ]separate[ ]research[ ]repo|create[ ]a[ ]clean[ ]local[ ]research[ ]repository" docs/README.md docs/specs docs/roadmap.md AGENTS.md .agents/skills
  ```

  Expected: no unintended matches.

- [ ] **Step 5: Commit Task 6**

  ```sh
  git add docs
  git commit -m "docs: merge runtime lifecycle contract"
  ```

---

## Final Verification

- [ ] Run managed verification:

  ```sh
  scripts/test-managed.sh
  ```

- [ ] Run JavaScript typechecks:

  ```sh
  pnpm --filter mobile-app typecheck
  pnpm --filter desktop-app typecheck
  ```

- [ ] Run formatting check:

  ```sh
  scripts/format.sh --check --all
  ```

- [ ] Inspect staged content for local machine data before any final commit:

  ```sh
  git diff --cached --name-only
  ```

  Then scan the staged diff for local absolute paths, usernames, machine names,
  private hostnames, and machine-specific install paths. Do not commit any
  matches.

- [ ] Summarize:

  - implemented lifecycle contract;
  - host adapter hooks used;
  - Windows early-vs-late teardown status;
  - verification commands and results;
  - remaining autolinking/prebuild work left out of scope.
