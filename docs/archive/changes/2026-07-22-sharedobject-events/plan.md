# Shared-object typed events implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> `superpowers:test-driven-development` task-by-task and
> `superpowers:verification-before-completion` before every commit. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship typed `[Event]` members on generated shared objects with
per-instance JavaScript-owned listeners and weak, race-safe managed dispatch.

**Architecture:** The generator reuses module-event shape and codec analysis,
but emits shared-object-specific partial initialization. Each class prototype
uses a private JS Array stored on each instance; managed callbacks retain only
scalar metadata or weak handles. Dispatch schedules onto the owning runtime and
uses registry snapshot/weak-lock/revalidation before reading that Array.

**Tech stack:** C# 13/.NET 10, Roslyn incremental generators, Expo.JSI managed
wrappers, Hermes-backed xUnit tests, TypeScript, Vitest, pnpm.

**Execution status:** COMPLETE (2026-07-22).

## Global constraints

- Do not modify `Expo.JSI`, native/C++, or the ABI. Consume plan 021's five-
  parameter `CreateHostFunction` overload.
- Never retain a JavaScript listener or ordinary shared-object wrapper in
  managed state outside a runtime callback.
- Preserve module-level `EventEmitterPrototype`, `EventEmitterRuntimeState`,
  and `ModuleEventEmitter` behavior.
- Event storage is one non-enumerable, non-configurable own JS Array per
  instance under a runtime-unique key.
- Release wins if the registry entry is terminal before dispatch's second
  gate validation. Dispatch wins at that validation and uses only its owned
  target wrapper afterward.
- `remove()` is idempotent. Its weak handle is disposed exactly once either
  explicitly or by plan 021's callback-state disposer.
- Use no runtime hot-path reflection, dynamic invocation, JSON conversion, or
  `object?[]` binding path.

---

### Task 1: Generator model, diagnostics, and emitted event partials

**Files:**

- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoSharedObjectModel.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`

**Interfaces:**

- Produces `ExpoSharedObjectModel.Events` using the existing `ExpoEventModel`.
- Produces `EXPOJSI026` for placement/shape, `EXPOJSI027` for payload, and
  `EXPOJSI028` for duplicate effective names.
- Generated providers call
  `GeneratedSharedObjectEvents.EmitAsync(...)`, pass declared event names and
  an `Action<DotnetRuntimeContext, SharedObject>` initializer to
  `GeneratedSharedObjectClass.Install`, and emit one cached partial-property
  delegate per instance.

- [ ] **Step 1: Add failing generator tests**

Add tests covering implicit and explicit names, payload-less and typed payload
delegates, emitted cached fields and initialization, provider codec calls,
shared-record codec emission, all invalid property shapes, unsupported
payloads, duplicate effective names, a `[JS]` member colliding with each of the
six reserved emitter methods, and `[Event]` on a class that is neither a module
nor a valid shared object. Assert exact ids, source locations, and key message
arguments.

- [ ] **Step 2: Verify RED**

Run:

```sh
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter FullyQualifiedName~SharedObjectEvent
```

Expected: FAIL because shared-object events are rejected by `EXPOJSI023` and
no shared-object event partial/provider code is emitted.

- [ ] **Step 3: Implement generator support**

Reuse `GetTypedEvents` through diagnostic-id parameters so module diagnostics
remain unchanged. Add a compilation-wide `[Event]` property input for invalid
container placement, avoiding duplicate diagnostics for valid modules and
shared objects. Emit inert matching implementations for safely reproducible
rejected partial properties. Extend shared-object record-codec emission and
reserve `addListener`, `removeListener`, `removeAllListeners`, `emit`,
`listenerCount`, and `removeSubscription` against `[JS]` prototype members.

- [ ] **Step 4: Verify GREEN**

Run the Step 2 command. Expected: PASS with no compiler diagnostics in accepted
cases.

- [ ] **Step 5: Commit**

Stage only the four task files, scan the staged diff for local paths, and
commit:

```sh
git commit -m "feat(generator): support shared object events"
```

### Task 2: JavaScript-owned listener prototype and subscription lifetime

**Files:**

- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedObjectEventPrototype.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/GeneratedSharedObjectClass.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedObjectClassRegistration.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/SharedObjectEventPrototypeTests.cs`

**Interfaces:**

- `SharedObjectEventPrototype.Install(DotnetRuntimeContext, SharedObjectClassRegistration, IReadOnlyList<string>)`
  installs the six prototype methods through context-owned generated callback
  registrations.
- `SharedObjectClassRegistration` owns the runtime-unique storage key, declared
  event names, and optional generated instance initializer, but no JS listener
  or ordinary instance wrapper.
- `GeneratedSharedObjectClass.Install` accepts declared event names and an
  optional `Action<DotnetRuntimeContext, SharedObject>` initializer.
- A subscription callback state owns one `JavaScriptWeakObject`; its disposer
  is passed to the five-parameter `CreateHostFunction` overload.

- [ ] **Step 1: Add failing Hermes prototype tests**

Cover all six method names, argument validation, listener order,
`removeListener` strict-equality matching, `removeAllListeners`,
`listenerCount`, `removeSubscription`, repeated `remove()`, separate storage
for two instances, retained methods after context disposal, and native wrapper
counters after teardown. Add an observable test-only subscription disposer
hook that proves dropping the subscription and forcing fixture GC disposes its
weak state once.

- [ ] **Step 2: Verify RED**

Run:

```sh
scripts/test-managed.sh --filter FullyQualifiedName~SharedObjectEventPrototypeTests
```

Expected: FAIL because event-capable shared-object prototypes and the extended
installation contract do not exist.

- [ ] **Step 3: Implement JS listener storage**

Use `Object.defineProperty` to install the private Array property with default
non-enumerable/non-configurable attributes. Store JS entry objects containing
`id`, `eventName`, and `listener`. Compact into a new JS Array on removal. Keep
all owned wrappers inside callback-scoped `using` blocks. The managed prototype
state may retain only the registry, exact class type, declared names,
runtime-unique property key, and next numeric id.

For `remove()`, create a target weak handle, transfer it into one idempotent
subscription state, and call:

```csharp
runtime.CreateHostFunction(
    "remove",
    0,
    RemoveSubscriptionByState,
    state,
    static value => ((SubscriptionState)value).Dispose()
)
```

The first explicit remove atomically takes the weak handle, removes one entry
if the target remains live, and disposes the handle. Later calls and the final
owned-state disposer are no-ops.

- [ ] **Step 4: Verify GREEN**

Run the Step 2 command. Expected: PASS and wrapper counters return to zero.

- [ ] **Step 5: Commit**

Stage only the four task files, scan the staged diff for local paths, and
commit:

```sh
git commit -m "feat(modules-core): add shared object listener prototype"
```

### Task 3: Weak registry dispatch and generated instance binding

**Files:**

- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedObjectEventEmitter.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedObjectRegistry.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedObjectClassRegistration.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/GeneratedSharedObjectClass.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedSharedObjectTests.cs`

**Interfaces:**

- `SharedObjectRegistry.GetLiveJavaScriptObject(SharedObject)` performs the
  gate snapshot, out-of-gate weak lock, and same-entry gate revalidation.
- Public generated-code helper `GeneratedSharedObjectEvents` exposes no-
  payload, codec payload, `JavaScriptValue`, and `ArrayBuffer` `EmitAsync`
  overloads.
- Registration initializes event delegates after reserving the exact instance
  and before JS pairing work. Initialization failure follows borrowed versus
  constructor-owned rollback rules.

- [ ] **Step 1: Add failing generated/Hermes dispatch tests**

Declare payload-less, string, record, and unsupported test events. Add tests
for two-instance disjoint listener dispatch, payload decoding, listener-throw
isolation, zero listeners, encode failure, off-runtime awaitable dispatch,
release-before-dispatch, teardown-before-dispatch, and same-context cached
delegate initialization. Add controlled registry race hooks proving release
wins before second validation and dispatch wins after it without touching the
registry again.

Add the load-bearing collection test: register a listener that captures its
own shared object, drop all JS references, call
`CollectGarbageForTesting()`, and assert the instance's registry release hook
fires. Assert no managed callback state contains the listener.

- [ ] **Step 2: Verify RED**

Run:

```sh
scripts/test-managed.sh --filter FullyQualifiedName~GeneratedSharedObjectEvent
```

Expected: FAIL because generated shared-object dispatch APIs and live-target
reacquisition do not exist.

- [ ] **Step 3: Implement weak dispatch**

Make every public `EmitAsync` overload catch immediate validation/context
errors and return a non-null faulted task. Reuse module-event scheduling and
payload ownership rules without calling `ModuleEventEmitter`. In the runtime
callback, acquire the target only through
`SharedObjectRegistry.GetLiveJavaScriptObject`, then call a listener-iteration
helper that reads the target's private Array. Catch each listener exception and
continue. Dispose payload and target wrappers within the callback.

In the registry, snapshot the active entry under `gate`, call
`entry.WeakObject.Lock()` outside `gate`, then re-enter `gate` and require the
same unreleased entry in both maps. Dispose and fail if the weak referent is
dead or release/teardown won. Once the method returns its owned target, do not
read entry or weak state again.

- [ ] **Step 4: Verify GREEN**

Run the Step 2 command, then:

```sh
scripts/test-managed.sh
```

Expected: both exit 0, including all unchanged module-event tests.

- [ ] **Step 5: Commit**

Stage only the six task files, scan the staged diff for local paths, and
commit:

```sh
git commit -m "feat(modules-core): dispatch shared object events"
```

### Task 4: TypeScript facade and authored example

**Files:**

- Modify: `packages/expo-modules-dotnet/src/ts-declarations/DotnetSharedObject.ts`
- Modify: `packages/expo-modules-dotnet/src/__type_tests__/dotnet-shared-object.ts`
- Modify: `packages/expo-modules-dotnet/src/__tests__/index.test.ts`
- Modify: `packages/example-module/dotnet/ExampleModule/ExampleCounter.cs`
- Modify: `packages/example-module/src/index.ts`

**Interfaces:**

- `DotnetSharedObject<TEventsMap extends EventsMap = Record<never, never>>`
  extends `DotnetEventEmitter<TEventsMap>`.
- `ExampleCounter` declares `[Event] Func<double, Task> OnChange` and emits it
  after incrementing.
- `ExampleCounterEvents` types `onChange(value: number): void`.

- [ ] **Step 1: Add failing TS tests and example usage**

Add accepted `onChange` listener typing and `@ts-expect-error` cases for a bad
event name and bad payload. Extend the runtime facade test to assert inherited
event methods remain throwing facade placeholders. Add the C# declaration and
awaited emission to the example.

- [ ] **Step 2: Verify RED**

Run:

```sh
pnpm --filter expo-modules-dotnet test
pnpm --filter mobile-app typecheck
```

Expected: FAIL because `DotnetSharedObject` is not generic and does not inherit
the typed emitter API.

- [ ] **Step 3: Implement the TS facade and example**

Extend `DotnetEventEmitter<TEventsMap>` without adding a second implementation
of listener methods. Preserve the current constructor error. Update the example
facade class and expose a small `addCounterChangeListener(counter, listener)`
helper returning `EventSubscription`.

- [ ] **Step 4: Verify GREEN**

Run the Step 2 commands and `scripts/test-managed.sh`. Expected: all exit 0.

- [ ] **Step 5: Commit**

Stage only the five task files, scan the staged diff for local paths, and
commit:

```sh
git commit -m "feat(example): expose shared object event facade"
```

### Task 5: Merge living docs and close transient artifacts

**Files:**

- Modify: `docs/specs/modules-core-boundary.md`
- Modify: `docs/module-authoring-guide.md`
- Move: `docs/changes/2026-07-22-sharedobject-events/spec.md` to
  `docs/archive/changes/2026-07-22-sharedobject-events/spec.md`
- Move: `docs/changes/2026-07-22-sharedobject-events/plan.md` to
  `docs/archive/changes/2026-07-22-sharedobject-events/plan.md`

The root reviewer owns the `docs/plans/README.md` DONE row.

- [ ] **Step 1: Merge the accepted delta**

Add the current-state shared-object event requirements and scenarios to
`modules-core-boundary.md`, replacing the earlier statement that events are a
future capability. Add a compact authoring recipe showing the C# partial
property, awaited emission, TypeScript event map, subscription cleanup, and
explicit shared-object `release()`.

- [ ] **Step 2: Archive the change package**

Move both transient artifacts under `docs/archive/changes/` and update their
status to completed without changing accepted requirements.

- [ ] **Step 3: Run final verification**

Run, in order:

```sh
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj
scripts/test-managed.sh
pnpm --filter mobile-app typecheck
scripts/format.sh --check --all
git diff --check
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" packages/expo-modules-dotnet/managed/packages
```

Expected: all required commands exit 0; the reflection scan has no new match in
the generated shared-object event path.

- [ ] **Step 4: Scope and privacy audit**

Use `git status --short` and `git diff --name-only` to confirm only Plan 019
files changed. Scan staged content for local absolute paths, usernames, machine
names, private hostnames, and machine-specific install paths.

- [ ] **Step 5: Commit**

Commit the merged guide/spec and archived artifacts:

```sh
git commit -m "docs(modules-core): document shared object events"
```
