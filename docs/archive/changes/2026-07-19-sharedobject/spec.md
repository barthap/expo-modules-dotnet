# Plan 007: SharedObject identity and weak-object spike

## Goal

Define and prove the minimum runtime mechanism for a future C# `SharedObject`
authoring surface: one managed instance and one live JavaScript class instance
have stable, same-runtime identity, and every release source reaches one safe,
idempotent terminal path.

This is a design and identity spike. It implements and proves the opaque
weak-object primitive plus the internal per-runtime registry required for that
identity and release behavior. It does not deliver the complete generator or
public SharedObject feature.

## Scope

| Included in this spike | Deferred slices |
| --- | --- |
| Opaque weak-object ABI and `Expo.JSI` wrapper | Public `SharedObject` base, protected `OnRelease`, `[ExpoSharedObject]` attribute, and generator diagnostics |
| Internal `SharedObjectRegistry` identity and release prototype | Generated constructor, method, property, and event bindings |
| NativeState-backed JS-to-managed lookup | Public `SharedRef<T>` API |
| Deterministic identity, explicit release, and teardown tests | TypeScript constructors, facade APIs, and usage guide |
| Maintainer ownership and teardown specification | Cross-runtime sharing |

The spike remains portable and headless. It SHALL NOT introduce platform
adapter dependencies, runtime hot-path reflection, or a HostObject-first
SharedObject representation.

## Prior art and compatibility direction

The Expo Modules 2.0 authoring direction uses an annotated class that derives
from `SharedObject`; an owning module lists the class, and annotated members
are bound on a JavaScript class prototype. The upstream implementation uses a
registry-backed native identity and a native-state releaser, while the Android
registry keeps a weak JavaScript counterpart so native-to-JS conversion can
return the existing JS object. This delta adopts those identity properties,
not platform implementation details. Inspiration includes
`<expo-repo>/packages/expo-modules-core/common/cpp/SharedObject.cpp`,
`<expo-repo>/packages/expo-modules-core/ios/Core/SharedObjects/SharedObjectRegistry.swift`,
and
`<expo-repo>/packages/expo-modules-core/android/src/main/java/expo/modules/kotlin/sharedobjects/SharedObjectRegistry.kt`.

This repository already supplies the required roles in portable form:
`ExpoClassInstaller` and `JavaScriptObjectFactory` provide class/prototype
installation under `_expoDotnet`; NativeState provides hidden JS-object tokens
and a managed release callback; `DotnetRuntimeContext` provides runtime-scoped
module ownership; and native `RuntimeState` with
`LongLivedObjectCollection` provides the release-versus-abandon lifetime
model. The new registry composes these primitives. It does not expose raw JSI
layouts to C# or make `Expo.JSI` aware of module names, generated classes, or
SharedObject policy.

## Accepted target design

This section describes the author-facing feature accepted for subsequent
public-feature slices. Requirements implemented by this spike appear in the
next section.

### Authoring surface

The target authoring shape is an `[ExpoSharedObject]`-annotated C# class that
derives from `SharedObject`. The attribute MAY provide an explicit JavaScript
class-name override; without one, the generated class name follows the module
authoring naming convention. An annotated class may have one `[JS]`
constructor and `[JS]` methods or properties. A future `[Event]` member shape
may add typed events on the same class hierarchy.

An owning module SHALL opt in explicitly with
`[ExpoModule(Classes = new[] { typeof(Cache) })]`. The generator will install
that class constructor on the generated module object under
`_expoDotnet.modules`; a TypeScript facade MAY re-export the constructor. A
class without a `[JS]` constructor is native-created only. Its instances can
still be returned to JavaScript once a generated binding supports the type,
but JavaScript cannot construct one directly.

`JavaScriptValue` remains an existing optional advanced module API convertible
with a codec. `JavaScriptObject` is an approved optional advanced
module-convertible/API direction, but adding its codec is a separate
module-convertibles slice unrelated to SharedObject. Ordinary SharedObject
authors SHALL NOT need either type, bridge handles, NativeState tokens,
registry IDs, weak wrappers, or explicit runtime scheduling to declare a
SharedObject class.

### User-facing lifetime principle

Shared objects are ordinary C# objects. While a paired JavaScript object is
live, converting the same managed instance to JavaScript SHALL return that
same JavaScript object. Releasing from JavaScript, JavaScript collection, and
runtime teardown SHALL converge on a single terminal release operation.

Terminal release SHALL be idempotent, remove the pairing, and invoke exactly
one protected virtual `OnRelease` hook. `OnRelease` SHALL run at most once,
SHALL NOT access JSI or bridge wrappers, and SHALL NOT block on JavaScript
runtime work. A released instance SHALL NOT be paired again in that runtime.
Subclasses MAY clean up their own managed or native resources in `OnRelease`.

The future `SharedRef<T>` is a thin `SharedObject` carrying `T`. It SHALL NOT
infer whether it owns `T` and SHALL NOT automatically call `Dispose` on `T`.
A subclass that owns its resource performs that cleanup in `OnRelease`.

## Requirements implemented by this spike

### Requirement: Opaque weak-object ABI

The native bridge SHALL add a production-quality opaque weak-object handle to
the C ABI. C++ SHALL own all JSI weak-reference mechanics. Managed code SHALL
receive only opaque weak-object and value handles, structured ABI results, and
explicit release functions.

The ABI SHALL create a weak handle from an owned or borrowed JavaScript object
value without making the weak handle an owner of the JavaScript object. It
SHALL lock a live weak handle into a newly owned JavaScript object value handle
or report that the referent is unavailable. It SHALL release weak bridge
handles exactly once.

#### Scenario: A live weak object is locked

- **GIVEN** a live JavaScript object and an owned weak bridge handle created
  for it on runtime `R`
- **WHEN** code locks the weak handle while `R` is valid
- **THEN** the ABI SHALL return a new owned object value handle for the same
  JavaScript object
- **AND** disposing that owned wrapper SHALL NOT release the JavaScript object
  while JavaScript still references it

#### Scenario: A collected weak object is locked

- **GIVEN** the referent has been collected and no strong JavaScript or bridge
  handle keeps it live
- **WHEN** code locks its weak handle on the owning runtime
- **THEN** the ABI SHALL report no object without fabricating an object handle

#### Scenario: A weak handle is released during teardown

- **GIVEN** a weak bridge handle remains when the runtime is preparing for
  invalidation or has already been invalidated
- **WHEN** the handle reaches its release path
- **THEN** native release or abandonment SHALL erase its long-lived entry
- **AND** it SHALL never dereference invalid JSI state

### Requirement: Managed weak-object wrapper

`Expo.JSI` SHALL expose a public `JavaScriptWeakObject` owned wrapper and a
`JavaScriptObject.CreateWeak()` method. `CreateWeak()` SHALL return an owned
weak wrapper. `JavaScriptWeakObject.Lock()` SHALL return either `null` or a
new owned `JavaScriptObject` wrapper. These wrappers own native bridge
handles, not JavaScript objects.

Weak wrapper operations SHALL have the same runtime affinity as the originating
object. `CreateWeak` and `Lock` SHALL run only in a valid access frame for the
owning runtime. A disposed weak wrapper, a disposed object wrapper, or an
invalidated runtime SHALL fail loudly before attempting invalid JSI access.
`JavaScriptWeakObject.Dispose` SHALL atomically detach and release its opaque
bridge handle, return without blocking, and never enter JSI, require an access
frame, or synchronously schedule runtime work. `Dispose` SHALL be idempotent.
Each successful `Lock` produces an independent owned object wrapper; its
caller SHALL dispose it or transfer ownership.

The public low-level APIs added by this slice SHALL have XML documentation
that states runtime affinity, nullable lock semantics, ownership, disposal,
and use-after-dispose behavior.

#### Scenario: Multiple locks are independent

- **GIVEN** one live `JavaScriptWeakObject`
- **WHEN** it is locked twice
- **THEN** each successful lock SHALL return a separate owned
  `JavaScriptObject` wrapper for the same JavaScript object
- **AND** disposing either wrapper SHALL NOT invalidate the other

#### Scenario: A weak wrapper is used after disposal

- **GIVEN** an owned `JavaScriptWeakObject` whose `Dispose` has completed
- **WHEN** code calls `Lock`
- **THEN** the wrapper SHALL throw `ObjectDisposedException` before invoking
  the ABI

### Requirement: Native weak-handle lifetime accounting

Native weak handles SHALL use the established `RuntimeState` and
`LongLivedObjectCollection` model for work that must remain associated with a
runtime. An active release SHALL run only while JSI is valid. A queued release
that cannot safely run SHALL abandon the JSI payload. Both paths SHALL erase
the collection entry, so no entry retains `RuntimeState` after terminal
release or abandonment.

#### Scenario: Runtime teardown wins a weak-handle race

- **GIVEN** weak-handle release work is queued and the runtime becomes invalid
  before that work runs
- **WHEN** the queued token is invoked or destroyed
- **THEN** the collection SHALL abandon the payload, erase the entry, and
  complete without JSI access

### Requirement: Per-runtime internal identity registry

Each `DotnetRuntimeContext` SHALL own one internal `SharedObjectRegistry`. The
registry SHALL maintain an incrementing entry identifier and two
reference-equality mappings: identifier to `SharedObjectEntry`, and managed
internal lifetime-contract instance to that entry. An entry SHALL strongly
retain that instance, retain its terminal-release state, and own one
`JavaScriptWeakObject` bridge wrapper for its JavaScript counterpart.

This spike SHALL NOT publish a `SharedObject` base class, a protected
`OnRelease` API, `[ExpoSharedObject]`, or any generated SharedObject binding.
The registry proof SHALL use an internal lifetime contract and an internal or
test-only prototype object with an exactly-once release action. The later
public-feature slice maps that release action to the accepted public
`SharedObject.OnRelease` contract.

The internal proof SHALL create its tiny per-pair prototype and release
function only inside the active GetOrCreate conversion frame. It SHALL attach
their JavaScript references to the returned instance, then dispose every
construction-time managed object, function, and value wrapper before returning
the one owned instance wrapper to its caller. `SharedObjectRegistry` SHALL NOT
retain an ordinary `JavaScriptObject` or `JavaScriptFunction` wrapper, or a
prototype owner, after conversion. For JSI resources, each live entry retains
only its opaque teardown-safe `JavaScriptWeakObject`; its other retained state
is NativeState and managed lifetime state. The later public class-prototype
installation design remains a deferred public-feature slice.

The JavaScript instance SHALL carry an internal NativeState token whose
managed state resolves the entry identifier. JavaScript-to-managed conversion
SHALL validate the token and registry liveness, then return the retained
managed instance. Managed-to-JavaScript conversion SHALL lock the entry's weak
wrapper; when it succeeds, it returns the locked owned `JavaScriptObject`
wrapper to its caller. When it reports no object, the entry has no live
JavaScript counterpart and its terminal release path runs instead of creating
a replacement pairing. This enforces the no-repairing rule after release.

The internal proof SHALL keep all NativeState tokens, weak wrappers, owned
object wrappers, scoped refs, retains, and detaches out of its lifetime
contract. Its prototype glue may transfer an owned value wrapper at a codec
boundary; it SHALL dispose or detach every other owned wrapper it creates.

#### Scenario: Managed-to-JS identity is stable

- **GIVEN** one active registry entry for managed object `M`
- **WHEN** generated or prototype conversion returns `M` to JavaScript twice
- **THEN** both conversions SHALL expose the same live JavaScript object by
  strict equality

#### Scenario: JS-to-managed conversion is stable

- **GIVEN** JavaScript holds the paired object for managed object `M`
- **WHEN** prototype conversion receives that object as the internal lifetime
  contract
- **THEN** NativeState lookup SHALL return the same managed instance `M`

#### Scenario: A foreign or stale object is decoded

- **GIVEN** a JavaScript object has no matching internal-registry NativeState token
  or its registry entry is no longer live
- **WHEN** prototype conversion attempts to decode it as the internal lifetime
  contract
- **THEN** conversion SHALL fail loudly without returning a different managed
  instance or allocating a new pairing

### Requirement: Shared-object terminal release

The registry SHALL provide one internal, idempotent terminal-release method
for explicit JavaScript release, NativeState release during JavaScript object
collection, and `DotnetRuntimeContext` teardown. The first caller removes both
maps, marks the entry released, disposes the entry's owned weak wrapper only
through the specified opaque-handle release path, and invokes the internal
release action once. Later calls SHALL be no-ops. The later public-feature
slice maps this action to `OnRelease`.

The NativeState callback MAY invoke that managed terminal transition and
dispose the entry's owned `JavaScriptWeakObject` because its `Dispose` contract
only atomically detaches and releases an opaque bridge handle without entering
JSI, requiring an access frame, blocking, or synchronously scheduling runtime
work. The callback SHALL NOT call `CreateWeak` or `Lock`, access any other JSI
wrapper or scoped ref, block, or run arbitrary runtime work.

Production platform adapters SHALL preserve the authoritative teardown order:

```text
prepare runtime handle -> invalidate connector -> tear down managed context
```

Preparing the runtime handle SHALL enter Closing and perform the JSI-safe
long-lived-object sweep while the connector and runtime remain usable. Only
after that preparation may the adapter invalidate the connector and tear down
the managed context. Registry terminal release after connector invalidation
SHALL use only `JavaScriptWeakObject.Dispose` and managed actions. At that
point, `Dispose` releases an already swept or otherwise teardown-safe opaque
handle; it SHALL NOT require an ordinary JSI wrapper, a prototype owner, JSI
access, or an access frame. This contract SHALL preserve leak-free terminal
release without requiring a platform-adapter change.

`DotnetRuntimeContext.Dispose` SHALL atomically transition the context from
Active to Disposing before cleanup begins. New or concurrent access SHALL
reject while the context is Disposing, and no accessor SHALL use a registry or
other context owner while its cleanup runs. Disposal SHALL drain the
`SharedObjectRegistry` and every later context owner with aggregate-and-
continue semantics. Every registry entry SHALL reach a terminal state and have
its internal release action attempted before `Dispose` completes or throws.
Only after that cleanup SHALL the context transition to terminal Disposed; it
SHALL do so before returning normally or throwing the final
`AggregateException`.

#### Scenario: Explicit JavaScript release wins collection

- **GIVEN** JavaScript explicitly releases a paired object
- **WHEN** its NativeState is later cleared or collected
- **THEN** the registry SHALL keep only the first terminal release
- **AND** the internal release action SHALL have run exactly once

#### Scenario: NativeState callback releases the weak bridge handle

- **GIVEN** collection invokes the NativeState release callback for an active
  entry whose owned `JavaScriptWeakObject` has not been disposed
- **WHEN** the callback performs the registry terminal transition
- **THEN** it SHALL remove the entry, invoke the internal release action once,
  and dispose the weak wrapper through its opaque-handle-only `Dispose` path
- **AND** it SHALL not enter JSI, require an access frame, block, synchronously
  schedule runtime work, or access another JSI wrapper or scoped ref

#### Scenario: Runtime teardown releases a live pair

- **GIVEN** a context has a live shared-object entry
- **WHEN** the context begins deterministic disposal
- **THEN** the registry SHALL terminally release the entry exactly once
- **AND** the entry SHALL no longer resolve from its NativeState token
- **AND** the native bridge SHALL not access JSI after invalidation

#### Scenario: Prepared connector invalidation precedes managed-context teardown

- **GIVEN** a live shared-object entry and a production platform adapter that
  still has valid JSI access
- **WHEN** the adapter prepares the runtime handle, invalidates the connector,
  and then calls `DotnetRuntimeContext.Dispose`
- **THEN** preparation SHALL enter Closing and sweep the long-lived collection
  before connector shutdown
- **AND** registry cleanup SHALL use only `JavaScriptWeakObject.Dispose` and
  managed actions
- **AND** it SHALL not retain or access an ordinary JSI wrapper or prototype
  owner, enter JSI, or require an access frame
- **AND** the weak wrapper SHALL release only its already swept or otherwise
  teardown-safe opaque handle
- **AND** cleanup SHALL release the entry without a platform-adapter change

#### Scenario: A release action throws during context teardown

- **GIVEN** a Disposing context has multiple live shared-object entries or
  later context owners and one internal release action throws
- **WHEN** `DotnetRuntimeContext.Dispose` drains its owners
- **THEN** accessors SHALL reject and SHALL NOT use an owner while cleanup runs
- **AND** later entries and owners SHALL still reach their cleanup paths
- **AND** every registry entry SHALL be terminal and have its release action
  attempted before the context transitions to Disposed
- **AND** `Dispose` SHALL throw one final `AggregateException` only after that
  terminal transition

#### Scenario: Managed code uses an object after release

- **GIVEN** a shared object has reached terminal release
- **WHEN** conversion attempts to expose it to JavaScript or resolve it from a
  stale JavaScript instance
- **THEN** the operation SHALL fail loudly
- **AND** it SHALL NOT create another entry or invoke the release action again

## Prototype test requirements

The spike SHALL add hermes-backed tests covering all of the following:

- A weak lock succeeds while its referent is live and reports no object once
  the referent is dead.
- Multiple successful locks are independent owned wrappers.
- Weak-wrapper disposal is idempotent and use after disposal fails loudly.
- Weak-handle release and abandonment during runtime teardown neither leak
  collection entries nor touch invalid JSI.
- Connector invalidation before managed-context teardown releases registry
  entries through weak-handle disposal and managed actions only after the
  required prepare step has performed the JSI-safe sweep.
- A managed instance converts to the same JavaScript object twice.
- A paired JavaScript object round-trips back to the same managed instance.
- Explicit JavaScript release invokes the internal release action exactly once
  and removes the registry entry.
- Managed-to-JS and JS-to-managed conversion after release fail loudly.
- Deterministic context/runtime teardown terminally releases a live entry.

The implementation SHALL first determine whether the selected Hermes API
exposes deterministic JavaScript garbage collection. If it does, the testhost
SHALL expose a test-only hook and the spike SHALL prove collection-triggered
release through that hook. If the selected Hermes API supports adding such a
hook within this plan's approved testhost scope, the implementation MAY add it
and run that proof. If neither path is available, the executor SHALL run the
other deterministic proof paths, record the missing control and commands
attempted, and STOP with a NO-GO decision. It SHALL NOT add a timing-based
collection test.

The testhost's bare `InvalidateRuntime` control models abrupt shutdown without
a JSI-safe sweep; it is not the production-adapter teardown scenario. A test
that proves production-order or otherwise requires a JSI-safe long-lived sweep
SHALL call the explicit prepare-for-invalidation control before
`InvalidateRuntime`, as required by the Hermes testhost specification.

## Documentation requirements

This delta and its merged maintainer specification SHALL document the registry
ownership map, runtime affinity, release-versus-abandon paths, and the
`OnRelease` restrictions. The public normal-usage guide, resource-cleanup
recipe, and TypeScript authoring documentation belong to the deferred public
feature slice; they SHALL NOT be represented as implemented by this spike.

## Hard constraints

- The boundary remains a C ABI with opaque handles. C++ owns JSI mechanics,
  and C# owns module logic.
- C# SHALL NOT observe raw `jsi::Runtime`, `jsi::Value`, or `jsi::Object`
  layouts.
- The registry and generated binding path SHALL use direct dictionaries,
  type ids, and generated calls, not runtime hot-path reflection.
- The ABI and managed design SHALL remain NativeAOT-compatible.
- The reusable bridge and managed core SHALL remain portable and headless.

## Spike record and stop/go gate

The completion record for this spike SHALL contain the following evidence and
distinguish it from approved design intent:

| Field | Required record |
| --- | --- |
| Hypothesis | An opaque weak bridge handle plus a per-context registry can preserve same-runtime JS↔managed identity and reach one release path without raw JSI in C#. |
| Commands | `scripts/test-managed.sh --filter FullyQualifiedName~SharedObject`, the applicable weak-wrapper test filter, `scripts/test-managed.sh`, and `scripts/format.sh --check --all`. |
| Expected result | The weak and registry tests, including deterministic collection release, pass; full managed tests and formatting pass; the registry releases or abandons every entry without stale JSI access. |
| Actual result | GO. Focused filters passed: Hermes collection 2/2, weak wrapper 11/11, ABI 2/2, registry 11/11, and runtime context 11/11, with no skipped tests. The full managed runner passed generator 46/46, low-level JSI 189/189, and ModulesCore 106/106. Formatting and `git diff --check` passed. Diff-added ownership and reflection scans printed no matches. |
| Artifacts | The ABI and `Expo.JSI` weak wrapper changes, internal lifetime-contract registry prototype, focused tests, and merged maintainer ownership/teardown specification. |
| Ownership and lifetime findings | Record every owned wrapper, retain, detach, NativeState callback, release-or-abandon path, and evidence that no terminal entry remains retained. |
| Scheduler findings | Record the NativeState callback context, any queue or invalidation race exercised, and confirmation that the internal release action performed no JSI work or blocking runtime operation. |
| Stop/go decision | GO only when the weak primitive, registry identity, deterministic collection release, and every other deterministic proof path pass. Otherwise record STOP/NO-GO with the condition and evidence. |

A STOP is required if the weak ABI cannot preserve opaque-handle and teardown
rules, if the NativeState callback cannot safely enter the terminal path
without JSI work, if generated binding work becomes necessary to prove the
registry, or if deterministic JavaScript GC is unavailable and cannot be
added within the approved testhost scope. The final condition is a
verification gap and a NO-GO, not permission to add a timing-based test.

## Completion record: GO

### Commands and results

- `scripts/test-managed.sh --filter FullyQualifiedName~HermesGarbageCollectionTests`:
  2 passed, 0 failed, 0 skipped.
- `scripts/test-managed.sh --filter FullyQualifiedName~JavaScriptWeakObjectTests`:
  11 passed, 0 failed, 0 skipped.
- `scripts/test-managed.sh --filter FullyQualifiedName~ExpoJsiApiTests`:
  2 passed, 0 failed, 0 skipped.
- `scripts/test-managed.sh --filter FullyQualifiedName~SharedObjectRegistryTests`:
  11 passed, 0 failed, 0 skipped.
- `scripts/test-managed.sh --filter FullyQualifiedName~DotnetRuntimeContextTests`:
  11 passed, 0 failed, 0 skipped.
- `scripts/test-managed.sh`: generator 46/46, `Expo.JSI.Tests` 189/189, and
  `Expo.ModulesCore.Tests` 106/106, all with zero skips.
- `scripts/format.sh --check --all` and `git diff --check`: passed.
- Diff-added owned-conversion and reflection scans from the plan baseline:
  no matches.

### Artifacts and ownership findings

The v23 ABI owns JSI weak mechanics in C++. `JavaScriptWeakObject` owns only
its opaque bridge handle; `Lock()` creates a separate owned object wrapper.
`CreateWeak()` and `Lock()` require the originating access frame. Wrapper
disposal detaches the handle under its own gate and then performs only opaque,
nonblocking release work.

The registry owns reference-identity maps, a managed lifetime-contract
instance, NativeState token state, and one weak wrapper per entry. Per-pair
prototype construction uses named `using` ownership for the prototype,
release function, release value, and callback `this` retain. It retains no
ordinary JSI wrapper after conversion returns. The release callback and
NativeState state both retain only a weak registry reference. Terminal release
removes maps before disposing the weak wrapper and running the managed action;
the latter two operations happen outside the registry and weak-wrapper gates.

NativeState callback release is reentrancy-safe: a callback that arrives while
the registry gate is held records terminal work for a post-gate drain. It does
not create or lock a weak wrapper, use a scoped ref, access JSI, block, or
synchronously schedule runtime work. The prepared teardown proof uses the
supported order `prepare -> invalidate -> managed teardown`. Bare invalidation
models abrupt shutdown only; it was a plan-shape deadlock risk, not a supported
production-order proof.

### Scheduler and counter findings

The testhost calls Hermes deterministic collection on its runtime executor;
the collection tests prove both executor synchronization and dead weak-lock
results without timing. Queued weak release before bridge-handle invalidation
increments the weak-abandon counter and leaves zero remaining entries. Queued
weak release during preparation increments the weak-release counter and also
leaves zero remaining entries. The registry collection, explicit release, and
context-teardown tests each prove one managed terminal action and zero registry
entries.

### Decision

GO. The opaque weak primitive, deterministic collection, release/abandon
accounting, same-runtime internal identity registry, exactly-once terminal
path, NativeState callback constraints, and context teardown proof all passed.
The public authoring surface remains deferred as listed below.

## Deferred slices

1. Add `[ExpoSharedObject]`, `[ExpoModule(Classes = ...)]` validation, and
   generated constructor, method, and property bindings.
2. Add SharedObject-typed generated codecs and public `SharedRef<T>` with its
   explicit ownership contract.
3. Add typed `[Event]` support for shared objects after the event-member
   surface is available.
4. Add TypeScript constructors, facades, normal authoring guidance, and the
   resource-cleanup recipe.
5. Assess cross-runtime sharing as a separate ownership design; this registry
   is strictly per `DotnetRuntimeContext`.
6. Add a `JavaScriptObject` codec only through a separate module-convertibles
   slice. `JavaScriptValue` already has its advanced-module codec; neither
   codec is required for normal SharedObject authoring.
