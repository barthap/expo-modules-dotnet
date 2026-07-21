# SharedObject Authoring Surface Implementation Plan

> **For Codex:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` to execute this plan task by task. Use `superpowers:test-driven-development` for each behavior change and `superpowers:verification-before-completion` before every commit and final handoff.

**Goal:** Ship generated, exact-type `SharedObject` authoring with module-owned JavaScript classes, explicit idempotent `release()`, registry-backed identity, `SharedRef<T>`, TypeScript facades, an example, and authoring documentation.

**Architecture:** Keep all JSI mechanics in the existing managed `Expo.JSI` surface and all identity/lifetime state in the context-owned `SharedObjectRegistry`. The generator discovers shared types across the compilation, links each valid type to exactly one `[ExpoModule(Classes = ...)]`, and emits direct constructor/member/codec glue into the provider. A managed-only instance binding prevents cross-context pairing; registry pairing uses reserve/build/commit so prototype creation, NativeState attachment, weak creation, callbacks, and `OnRelease` never run under the registry gate. Constructor-created instances use terminal rollback, while ordinary encoding of a caller-owned instance rolls back to an unpaired, retryable state.

**Tech Stack:** C# 13/.NET 10, Roslyn incremental source generators, Expo.JSI managed wrappers, Hermes-backed xUnit tests, TypeScript, Vitest, pnpm.

## Ground Rules

- The approved requirements are in `docs/changes/2026-07-21-sharedobject-authoring/spec.md`.
- Keep `[ExpoModule(Classes = new[] { typeof(...) })]`; the reviewed Expo Modules v2 article does not specify a conflicting ownership mechanism.
- Expose explicit `release()` only. Do not add `Symbol.dispose`, JavaScript `using`, events, reflection, dynamic invocation, JSON conversion, an Expo.JSI API, native/C++ code, or a C ABI entry.
- Do not change `SharedObjectRegistryTests.cs`; add public-path tests separately.
- Run tasks sequentially. Use a fresh implementation subagent for each task and a review subagent after each implementation. Do not use worktrees; all work stays on the current branch.
- Before each commit, inspect `git diff --cached` for absolute paths, usernames, hostnames, or machine-specific install paths.
- If Plan 016 changes appear, leave them untouched. Preserve unrelated working-tree changes.

## Task 1: Prove a constructor callback is possible with the existing ABI

**Files:**

- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedSharedObjectInfrastructureTests.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/GeneratedSharedObjectClass.cs`

**Step 1: Characterize host-function construction first**

Add a Hermes test named `HostFunctionConstructorCapabilityIsCharacterized`. Create a context-owned host function whose callback records its decoded arguments and returns an object, then call it through `JavaScriptFunction.CallAsConstructor`. Assert either the supported behavior that the callback runs and its returned object is the constructor result, or the specific existing Expo.JSI/Hermes error that proves host functions are not directly constructible. Do not weaken the assertion to “either outcome”. Record the observed behavior in the test name/comment after the first run.

Run:

```sh
scripts/test-managed.sh --filter FullyQualifiedName~HostFunctionConstructorCapability
```

Expected: the characterization passes with one unambiguous platform behavior.

**Step 2: Add the managed constructor primitive**

Implement `GeneratedSharedObjectClass` as a public generated-code helper. If host functions are constructible, use a `GeneratedHostFunctionRegistration` directly. If they are not, use only existing wrappers to create a constructable `Proxy`: `runtime.CreateClass(name)` as the target, a context-owned host function as the handler's `construct` trap, and global `Proxy` invoked through `CallAsConstructor`. The trap must decode the arguments array without reflection and return the generated callback's owned paired object. Retain no temporary ordinary wrapper after installation.

Add tests named `GeneratedConstructorInvokesManagedCallbackWithNew`, `GeneratedConstructorReturnsCallbackObjectWithExactPrototype`, `GeneratedConstructorRejectsCallWithoutNew`, and `GeneratedConstructorRegistrationIsReleasedWithContext`. Verify `new Constructor(...)`, `Object.getPrototypeOf(value) === Constructor.prototype`, and `value instanceof Constructor`.

Run:

```sh
scripts/test-managed.sh --filter FullyQualifiedName~GeneratedSharedObjectInfrastructureTests
```

Expected: all tests pass and long-lived wrapper counters return to zero after context disposal.

**STOP:** If neither a direct host function nor an existing-wrapper `Proxy` can provide a callback-backed `new` with the exact prototype and context-owned teardown, stop. Do not add an Expo.JSI/native/ABI API.

**Step 3: Commit**

```sh
git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/GeneratedSharedObjectClass.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedSharedObjectInfrastructureTests.cs
git commit -m "feat(modules-core): add generated shared object class primitive"
```

## Task 2: Add the public declarations and compilation-wide generator model

**Files:**

- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedObject.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedRef.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/ExpoSharedObjectAttribute.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/ExpoModuleAttribute.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/JSAttribute.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoSharedObjectModel.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`

**Step 1: Write failing API and declaration tests**

Add generator tests for valid implicit/explicit names and valid indirect `SharedRef<T>` inheritance. Add one independently asserted source-location/message case for each `EXPOJSI021` declaration failure: nested, generic, non-sealed, non-partial, missing `SharedObject` base, and null/empty/blank explicit name. Add compile-time API assertions for `ExpoModuleAttribute.Classes`, `[JS]` on constructors, protected virtual `OnRelease`, and non-owning `SharedRef<T>.Ref`.

Run:

```sh
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter FullyQualifiedName~SharedObject
```

Expected: FAIL because the APIs/models/diagnostic do not exist.

**Step 2: Implement public declarations and aggregate discovery**

Add:

- `public abstract SharedObject` with only the protected virtual `OnRelease()` author hook and internal lifetime/pairing operations;
- `public class SharedRef<T> : SharedObject` with constructor injection and read-only `Ref`, with no automatic `IDisposable`/`IAsyncDisposable` handling;
- class-only `[ExpoSharedObject]` with optional validated name;
- settable `Type[] Classes { get; set; } = Array.Empty<Type>()` on `ExpoModuleAttribute`;
- `AttributeTargets.Constructor` on `JSAttribute`.

In `Initialize`, add an `[ExpoSharedObject]` syntax provider and combine its collected declaration models with the complete collected module set before validation or codec selection. Model ownership and exact codec eligibility in stable, equatable records; do not retain Roslyn symbols in emitted models. Only after aggregate linking should the generator produce each module's class/member/codec model.

Implement `EXPOJSI021` and register it in `ToDiagnostic`. Invalid types emit no binding support and cause no secondary generated-C# errors.

Run the command from Step 1. Expected: PASS.

**Step 3: Commit**

```sh
git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedObject.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedRef.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/ExpoSharedObjectAttribute.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/ExpoModuleAttribute.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/JSAttribute.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoSharedObjectModel.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs
git commit -m "feat(modules-core): model shared object declarations"
```

## Task 3: Validate ownership, constructors, members, codecs, and namespaces

**Files:**

- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoSharedObjectModel.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`

**Step 1: Write failing diagnostic tests**

Add table-driven tests with exact IDs, locations, and message arguments for:

- `EXPOJSI022`: multiple `[JS]` constructors, inaccessible constructor, named `[JS("...")]` constructor, unsupported/static constructor shape;
- `EXPOJSI023`: constructor/member parameter or result without a codec, invalid method/property shape, `[Event]`, direct `SharedObject`/`SharedRef<T>`/unattributed base boundary use;
- `EXPOJSI024`: invalid `Classes` entry, duplicate entry, missing owner, multiple owners, duplicate native-only names, and a constructible class colliding separately with a module function, property, class, observing hook, and reserved event-runtime member;
- `EXPOJSI025`: duplicate prototype member names and each reserved `release`, `constructor`, and `__proto__` name.

Also add positive model tests for one constructible and one native-created-only class owned by the same module, exact sealed shared-object parameter/return/property types, sync and `Task`/`Task<T>` members, and public/internal setters. Add `EXPOJSI023` cases for a shared-object type nested inside a record, list, dictionary, nullable wrapper, callback, or other composed codec. The approved spec requires exact shared-object codecs only when the authored type is used directly at a generated boundary; nested shared-object composition is not part of this change.

Run the generator test command. Expected: FAIL with missing validation.

**Step 2: Implement validation before emission**

Build the owning module's full JavaScript namespace before emitting any module: methods, properties, exposed class names, observing hooks, and inherited/reserved event runtime members. Keep effective class names unique even for native-created-only types. Discover one accessible attributed constructor at most; reuse existing codec analysis for its parameters. Reuse method/property shape, lower-camel/default-name, async, argument, and property rules, but reject shared-object events and reserved prototype names.

Extend codec modeling with explicit `RequiresRuntimeContext` flags for every direct decode and encode location, including sync return, async result, constructor argument, and property getter/setter. A valid shared codec exists only for an exact, attributed, owned, sealed concrete type used directly at that boundary. Reject nested shared-object composition with `EXPOJSI023`; do not change the runtime-only `IJavaScriptCodec<T>` composition interfaces in this change. Do not fall through to “type implements `IJavaScriptCodec<T>`” for a shared base. For async results, the model SHALL make the generated host callback capture its exact `DotnetRuntimeContext` while `GeneratedFunction.CurrentRuntimeContext` is valid; settlement code SHALL receive that captured context explicitly and SHALL NOT consult the thread-static accessor after the host-function frame exits.

Run the generator test command. Expected: PASS with no compiler diagnostics in accepted cases.

**Step 3: Commit**

```sh
git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoSharedObjectModel.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs
git commit -m "feat(generator): validate shared object bindings"
```

## Task 4: Make pairing cross-context safe and transactional

**Files:**

- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedObject.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedObjectRegistry.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedObjectPrototype.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedObjectClassRegistration.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/PublicSharedObjectRegistryTests.cs`

**Step 1: Write failing ownership/rollback tests**

Use a concrete test `SharedObject` and injected pairing failure points. Cover:

- exact runtime type required before lookup/allocation;
- same-context repeat encode returns strict equality;
- another context/runtime cannot pair the same managed instance;
- a reservation blocks/rejects re-entrant duplicate pairing without duplicate state;
- all JSI/prototype/weak/NativeState operations and `OnRelease` observe `Monitor.IsEntered(registry gate) == false`;
- ordinary first-encode failure clears every partial wrapper/state, leaves the instance unpaired and unreleased, and a retry succeeds;
- constructor-owned failure marks terminal, calls `OnRelease` once, rejects retry, and converges with NativeState rollback re-entry;
- explicit release surfaces `OnRelease` failure; NativeState cleanup swallows it; registry/context teardown aggregates it and continues;
- context disposal during an in-flight reservation cancels commit and leaves no live pair.

Run:

```sh
scripts/test-managed.sh --filter FullyQualifiedName~PublicSharedObjectRegistryTests
```

Expected: FAIL against the current lock-held entry creation.

**Step 2: Implement managed-only binding and reserve/build/commit**

Give `SharedObject` an internal lock-protected binding state: unpaired, reserved by one `SharedObjectRegistry`, paired to that registry, and terminal. It must reject a second context before JSI work, roll ordinary failed reservation back to unpaired, and convert constructor-owned failed reservation to terminal. The terminal transition owns the exactly-once `OnRelease` call and occurs outside registry and weak-wrapper locks.

Preserve the existing internal identity-proof route used by the unchanged
`SharedObjectRegistryTests`: its `ISharedObjectLifetime` overload and generic
release prototype MAY remain internal and keep their current semantics. Add a
separate registered-class API for public/generated `SharedObject` values that
requires an exact owned class registration and custom prototype. Public codecs
and generated bindings SHALL use only that strict API and SHALL never fall
back to the internal generic route for an unowned or unregistered authored
class. If these paths cannot be separated without changing the existing test
contract, stop and report a spec conflict.

Refactor `SharedObjectRegistry` into these phases:

1. under its gate, validate active/no-repairing/exact type and install a managed reservation with a unique id;
2. outside the gate, create the object with its registered class prototype, attach `SharedObjectNativeState`, and create the weak wrapper;
3. under the gate, commit only if the reservation and context remain active;
4. outside the gate, dispose or transfer every wrapper and apply borrowed or constructor-owned rollback.

Snapshot an existing entry under the gate, call `WeakObject.Lock()` outside it, then revalidate the same entry under the gate. Release/teardown must be able to cancel reservations and converge without deadlock. Registry entries still retain only lifetime state, NativeState state, and `JavaScriptWeakObject`, never ordinary object/function/prototype wrappers. Keep `SharedObjectPrototype.release` idempotent and callback state weak.

Run the new test filter, then:

```sh
scripts/test-managed.sh --filter FullyQualifiedName~SharedObjectRegistryTests
```

Expected: both pass, with the existing registry test file unchanged.

**Step 3: Commit**

```sh
git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedObject.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedObjectRegistry.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedObjectPrototype.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedObjectClassRegistration.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/PublicSharedObjectRegistryTests.cs
git commit -m "feat(modules-core): pair public shared objects transactionally"
```

## Task 5: Emit lazy class installation, prototype members, constructors, and codecs

**Files:**

- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/GeneratedSharedObjectClass.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/GeneratedProperty.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/SharedObjectCodec.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedSharedObjectTests.cs`

**Step 1: Write failing emitted-source and Hermes tests**

Assert generated source directly calls the authored constructor/method/property, resolves `thisValue` through `SharedObjectCodec<T>`/the context registry, passes `GeneratedFunction.CurrentRuntimeContext` on shared decode and encode, and contains no reflection, `dynamic`, JSON, or `object?[]`. Assert class installation appears inside `EmitModuleRegistrationFunction`, after module materialization, and not in provider metadata registration.

For a direct asynchronous shared-object result, assert generated code captures
the exact `DotnetRuntimeContext` before the host-function callback returns and
supplies that captured context explicitly to the shared-object codec during
Promise settlement. It SHALL not read
`GeneratedFunction.CurrentRuntimeContext` from the later continuation. Nested
shared-object codec composition SHALL remain rejected by Task 3.
Add a Hermes case whose `Task<SharedType>` remains incomplete until after the
host-function frame exits, then completes on another managed thread; resolution
must preserve the original managed/JavaScript identity without an absent or
wrong thread-static context.

In the generated fixture add constructible, native-created-only, concrete `SharedRef<T>`, and sibling shared classes. Hermes tests must cover lazy installation/reuse, constructor argument decoding, prototype identity, implicit/explicit names, sync/async methods, read-only/read-write properties, native-created return, managed-to-JS strict identity, JS-to-managed original identity, exact-type and foreign/released receiver rejection before authored code, explicit repeated `release()`, deterministic collection, teardown, use after release, and `SharedRef<T>` non-ownership.

Run generator and generated shared-object test filters. Expected: FAIL because no glue is emitted.

**Step 2: Emit the bindings**

Have `EmitModuleRegistrationFunction` install every owned class exactly once when the lazy module is created. `GeneratedSharedObjectClass` owns each context/type registration and prototype, installs `release`, generated methods, and generated accessors on that shared prototype, and exposes the constructor as the module's class-name property only when `[JS]` marks a valid constructor. Native-created-only classes still register their internal prototype.

Emit one typed callback per constructor/member/accessor. Constructor glue decodes arguments first, directly invokes `new AuthoredType(...)`, then transfers that returned instance to the registry's constructor-owned pairing path. Method/property glue resolves the receiver as the exact type before calling authored code. Route sync/async parameters/results and property values through `SharedObjectCodec<T>` using the current runtime context. Preserve existing owned-wrapper disposal rules for `JavaScriptValue`, `ArrayBuffer`, callbacks, and async results.
Capture the context used by any incomplete async operation before leaving the
generated host callback, and pass it into result encoding after `await`; never
rely on `GeneratedFunction.CurrentRuntimeContext` during settlement.

Run:

```sh
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj
scripts/test-managed.sh --filter FullyQualifiedName~GeneratedSharedObjectTests
scripts/test-managed.sh
```

Expected: PASS; existing `SharedObjectRegistryTests` remain byte-for-byte unchanged.

**Step 3: Commit**

```sh
git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/GeneratedSharedObjectClass.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/GeneratedProperty.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/SharedObjectCodec.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedSharedObjectTests.cs
git commit -m "feat(generator): emit shared object bindings"
```

## Task 6: Export the TypeScript facade and add the authored example

**Files:**

- Create: `packages/expo-modules-dotnet/src/ts-declarations/DotnetSharedObject.ts`
- Modify: `packages/expo-modules-dotnet/src/index.ts`
- Modify: `packages/expo-modules-dotnet/src/__tests__/index.test.ts`
- Create: `packages/expo-modules-dotnet/src/__type_tests__/dotnet-shared-object.ts`

**Step 1: Write failing runtime and type tests**

Assert `DotnetSharedObject` is an exported class value, direct construction throws guidance that instances come from a generated class or module return, its prototype has only `constructor` and `release`, and subclasses inherit `release(): void`. Do not assert native instances satisfy `instanceof DotnetSharedObject`.

Run:

```sh
pnpm --filter expo-modules-dotnet test
pnpm --filter expo-modules-dotnet typecheck
```

Expected: FAIL before the export exists.

**Step 2: Implement and verify**

Add the real facade class and export it from `index.ts`. Its placeholder `release()` and constructor both throw the same unavailable-facade guidance when called on an actual facade object; generated native values supply their own prototype method.

Run the commands from Step 1. Expected: PASS.

**Files:**

- Create: `packages/example-module/dotnet/ExampleModule/ExampleCounter.cs`
- Modify: `packages/example-module/dotnet/ExampleModule/ExampleMathModule.cs`
- Modify: `packages/example-module/src/index.ts`
- Modify: `apps/mobile-app/App.tsx`
- Modify: `apps/desktop-app/App.tsx`

**Step 3: Add a failing generated integration assertion**

Before app changes, extend `GeneratedSharedObjectTests` or the existing example-module integration test to require an owned `ExampleCounter` constructor and a native-created return path, then verify increment/read and exact identity. Expected: FAIL until the example is authored.

**Step 4: Implement the example**

Add a sealed partial `[ExpoSharedObject]` handle with `[JS]` constructor, method, property, and idempotent resource cleanup in `OnRelease`. Retain `[ExpoModule(Classes = new[] { typeof(ExampleCounter) })]` on `ExampleMathModule`; add a module method that returns a pre-existing/native-created counter. In TypeScript, declare `ExampleCounter extends DotnetSharedObject`, type the module class property as `typeof ExampleCounter`, and expose constructor and return helpers.

Add a “Shared object” capability to both apps. Its handler must use the class/returned handle and call `release()` in `finally`; after release, display the expected catchable use-after-release result rather than relying on `Symbol.dispose` or `using`.

Run:

```sh
scripts/test-managed.sh --filter FullyQualifiedName~SharedObject
pnpm --filter mobile-app typecheck
pnpm --filter desktop-app typecheck
```

Expected: PASS.

**Step 5: Commit**

```sh
git add packages/expo-modules-dotnet/src packages/example-module apps/mobile-app/App.tsx apps/desktop-app/App.tsx packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests
git commit -m "feat: expose shared object authoring example"
```

## Task 7: Merge the living spec, document authoring, and verify the branch

**Files:**

- Modify: `docs/module-authoring-guide.md`
- Modify: `docs/specs/modules-core-boundary.md`
- Modify: `docs/plans/README.md`
- Archive/remove after merge, per workflow: `docs/changes/2026-07-21-sharedobject-authoring/spec.md`
- Archive/remove after merge, per workflow: `docs/changes/2026-07-21-sharedobject-authoring/plan.md`

**Step 1: Update authoritative documentation**

Merge every approved requirement and scenario into `modules-core-boundary.md`, replacing the “internal identity proof only” limitation. Update the guide with declaration constraints, retained `Classes` ownership, constructible versus native-created-only classes, exact-type parameters/returns/properties, naming, `SharedRef<T>` non-ownership, synchronous thread-agnostic `OnRelease` restrictions, idempotent `release()`, use-after-release errors, and a `try/finally` example. State that `Symbol.dispose` is deferred pending TypeScript/runtime compatibility review.

Update the Plan 017 row only after all verification succeeds. Archive or remove the transient change folder as directed by the living-spec workflow; do not leave accepted requirements solely in transient files.

**Step 2: Run full verification**

```sh
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj
scripts/test-managed.sh
pnpm --filter expo-modules-dotnet test
pnpm --filter expo-modules-dotnet typecheck
pnpm --filter mobile-app typecheck
pnpm --filter desktop-app typecheck
scripts/format.sh --check --all
git diff --check
git status --short
```

Expected: every command exits 0; no tests are skipped; the status contains only Plan 017 work plus previously identified unrelated user changes. If formatting fails, run `scripts/format.sh`, re-review the diff, and repeat every affected verification command.

Also run:

```sh
rg -n "internal identity proof only|Symbol\.dispose|object\?\[\]|dynamic|System\.Reflection" docs/specs/modules-core-boundary.md packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator
git diff -- packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/SharedObjectRegistryTests.cs
```

Expected: no stale internal-only statement; `Symbol.dispose` appears only as an explicit non-goal/deferred note; no generated runtime fallback; the existing registry test diff is empty.

**Step 3: Commit**

Stage only the intended documentation/index changes after confirming no local paths are present, then commit:

```sh
git commit -m "docs: document shared object authoring"
```

## Final owner review

Before handoff, dispatch a read-only review against the approved spec. It must check exact-type identity, cross-context ownership, reserve/build/commit races, constructor-owned versus borrowed rollback, teardown aggregation, NativeState exception containment, wrapper ownership, lazy class installation, every `EXPOJSI021`–`EXPOJSI025` category, and the absence of new Expo.JSI/native/ABI work. Resolve every actionable finding and rerun the affected task plus the full verification block.
