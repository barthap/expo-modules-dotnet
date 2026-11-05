# Compositional Codec Resource Scope

## Goal

Generated bindings release every `JavaScriptValue` and `ArrayBuffer` wrapper
leaf exactly once, at any depth inside records, lists, dictionaries, and
nullable wrappers. This is roadmap item 030.

Some of these shapes are already accepted and already in the living spec, but
the emitted code is wrong for them:

- Records with leaf fields and `JavaScriptValue` lists or dictionaries decode
  with a `Borrowed` descriptor, so their leaves leak on every call and on a
  later decode failure.
- Their encode disposes the author's `JavaScriptValue`, and one wrapper in two
  leaves throws `ObjectDisposedException`.
- A returned `ArrayBuffer` field never releases its source lease, and an
  abandoned async record result leaks its leaves.

This change fixes those shapes and adds `ArrayBuffer` as a list element and
dictionary value, at any depth. Three callback shapes that compile today
become diagnostics (see "Positions").

## Process note

The brief `docs/plans/030-compositional-codec-resource-scope.md` predates 028
and 029. This spec is reconciled against HEAD `5824b07` and wins over it.

## Design criteria

1. **Clean code.** One mechanism, one execution order per path, no special
   cases with hidden data flow. Every new type must pay for itself.
2. **Performance.** No byte copies. No allocation and no new code on bindings
   that carry no wrapper leaf. No retain cycles.
3. **Correctness.** No leaks. Ownership is explicit at every boundary row.
   Object lifecycle is boring: create, register, release once.

## Alternatives considered

**A. Static recursive `Release` (no scope object).** A `Release` walk after
authored code runs sees the author's changes to a mutable input, and every
container needs its own catch block; the fix for both is a snapshot of the
decoded leaves, and that snapshot is the scope.

**B. One explicit conversion scope (chosen).** Decode registers each leaf it
creates. `Transfer` registers each leaf of a returned value. The scope
releases its registrations once. Composite codecs never catch. Every path has
the same order: decode, call, transfer, encode, dispose scope.

Allocation, stated exactly: today a direct `JavaScriptValue` parameter costs
one managed wrapper and one native handle, and no other managed allocation.
After this change a call with one leaf also allocates the scope object. The
scope stores its first leaf inline and allocates a list only for a second
distinct leaf. An async call with a leaf-bearing result also allocates one
`GeneratedTransferredResult<T, TCodec>` state. Bindings with no leaf keep
their current code and allocate nothing new.

## Scope

### In scope

- `Expo.ModulesCore/Codecs/`: the new scope, protocol, transferred result,
  composite codecs, and the scoped members on `JavaScriptValueCodec` and
  `ArrayBufferCodec`. All new runtime types live in this directory, in
  namespace `Expo.ModulesCore.Codecs`.
- `Expo.ModulesCore.Generator`: descriptor, models, codec resolution, module,
  shared-object, and event analysis, and emission.
- `Expo.ModulesCore.Tests` and `Expo.ModulesCore.Generator.Tests`.
- `docs/specs/ownership-and-scoped-refs.md`,
  `docs/specs/modules-core-boundary.md`, `docs/module-authoring-guide.md`,
  `.agents/skills/expo-jsi-managed-handle-lifetime/SKILL.md`, and
  `docs/roadmap.md`.

### Out of scope

- `JavaScriptValue?` (unsupported by design) and `ArrayBuffer?` (031).
- Nullable callbacks (032) and nullable concrete shared objects (033).
- Callback runtime argument consumption: invoking a `JavaScriptCallback`
  disposes a direct `JavaScriptValue` argument the author still owns
  (`JavaScriptCallback.cs:167-172`, `ValueTupleCodec.cs:30-31,44-45`). The
  close task adds one backlog line (see Roadmap).
- New collection families, such as `List<T>`.
- Native code, the C ABI, the runtime scheduler, platform adapters, and wrapper
  `Dispose`/`Retain` implementations.
- `IJavaScriptCodec<T>`. It stays unchanged.

## Accepted design

### Ownership matrix

"Leaf" means a `JavaScriptValue` or an `ArrayBuffer` wrapper. Every
invocation has at most one scope, created only when a parameter or the result
holds a leaf.

| Boundary operation | Who creates each leaf, who releases it | Scope ends |
| --- | --- | --- |
| Sync function parameters | Scoped decode creates it; the scope releases it. | At the end of the `using` block, after the result is encoded or after any failure. |
| Sync function return | The author creates it and transfers it; the scope releases it. `EncodeBorrowed` creates the JS result; the host bridge releases it. | Same `using` block. |
| Async function parameters | Scoped decode creates it; the scope releases it. | Leaf-free result: in the Promise lambda's `finally`, as today. Leaf-bearing result: at settlement or abandon (async result row), or in the lambda's `catch` on fault, cancel, or `Transfer` failure. Failure before `CreatePromise` returns: in the outer `catch`. After `CreatePromise` returns, the lambda owns the scope and the host frame never disposes it; `AsValue()` runs outside the outer `catch`. |
| Async result | The author creates it and transfers it into the same scope; `GeneratedTransferredResult<T, TCodec>` holds the scope. The runtime factory creates the JS result; the Promise scheduler releases it. | In the factory's `finally` after `EncodeBorrowed`, or on abandon. |
| Property setter | Scoped decode creates it; the scope releases it. | After the setter returns or throws. |
| Property getter | The author creates it and transfers it; the scope releases it. `EncodeBorrowed` creates the JS result; the host bridge releases it. | After the encode or a failure. |
| Shared-object member | Same as the module function and property rows. | Same. |
| Shared-object constructor | Scoped decode creates argument leaves; the factory's scope releases them. The instance is not a leaf; the registry owns it. | When the generated factory returns the instance or throws. Pairing runs later and is unchanged. |
| Span host function | Scoped decode creates non-span argument leaves and the author transfers returned leaves; one scope around `WithBytes` releases both. Glue creates the span lease and releases it with its own `using`. | After the lambda result is encoded or a failure. |
| Direct typed event payload | Unchanged: the dispatcher retains its own copy or lease; the caller keeps the original. | Unchanged. |

The span lease keeps its own `using`. Glue decodes it with the ordinary
`ArrayBufferCodec.Decode`, authored code never receives it as a leaf, and it
must stay alive for the whole `WithBytes` call that the lambda runs in.

A returned value transfers every leaf it contains. Authors store and expose
only `Retain()` copies. A returned input leaf is registered once by identity
and released once, in sync and async.

### The conversion scope

`GeneratedConversionScope` is a public sealed class, public only because
generated code in authored assemblies passes it to codecs. It has the
`Generated` prefix and the XML remark "This helper is for generated bindings,
not ordinary module author code." (`GeneratedProperty.cs:9`).

- `internal void Register(IDisposable leaf)` is the only way in. Only
  `JavaScriptValueCodec` and `ArrayBufferCodec` call it. `IDisposable` is the
  storage type, not an inference. Tests reach it through the existing
  `InternalsVisibleTo("Expo.ModulesCore.Tests")`.
- It stores the first leaf in a field and allocates a list on the second
  distinct leaf. It deduplicates by a linear `ReferenceEquals` scan; leaf
  counts are small, so it uses no hash set.
- `Dispose` disposes leaves in registration order, the simplest loop; leaves
  are independent. If any disposal throws, it throws
  `AggregateException(cleanupFailures)`, flattened as
  `DotnetRuntimeContext.DisposeAndCapture` does
  (`DotnetRuntimeContext.cs:336-350`).
- A registration after disposal throws `ObjectDisposedException`. Generated
  code never registers after disposal; the ownership handoff at
  `CreatePromise` return (see "Generated execution order") gives every scope
  one owner at a time.
- It is single-owner and not thread-safe. Nothing references the scope from a
  leaf or value, so it creates no retain cycle.

Generated glue disposes the scope with a plain `using` or `finally`. A
cleanup exception during unwinding replaces the primary exception. This is
accepted because no registered leaf can throw from `Dispose`: both leaf types
exchange a handle and call a `void` native release with no managed `throw`
(`Expo.JSI/JavaScriptValue.cs:233-243`, `Expo.ModulesCore/ArrayBuffer.cs:238-244`,
`Expo.JSI/JavaScriptArrayBuffer.cs:177-184`).

### Scoped codec protocol

`IGeneratedScopedCodec<T>` declares three static abstract members:

- `Decode(JavaScriptValueRef, JavaScriptRuntime, GeneratedConversionScope)`.
  Every boundary decode site and every composite decode already holds a ref.
- `Transfer(T value, GeneratedConversionScope scope)`: registers every leaf of
  `value`. It does no runtime work, so it can run off the JS thread.
- `EncodeBorrowed(T value, JavaScriptRuntime runtime)`: returns an
  independent owned JS value and never consumes or disposes a source leaf.

The members cannot go on `IJavaScriptCodec<T>`, a non-documented extension
point: it has no self-type parameter, so a `static virtual` default cannot
forward to its `static abstract` members.

`Transfer` of a null value registers nothing and does not throw, in every
codec, including generated class-record codecs. The two nullable codecs
encode null as JavaScript `null`. The leaf, list, dictionary, and
class-record codecs throw `ArgumentNullException` from `EncodeBorrowed`, as
the ordinary codecs do. `Transfer` is total so that a null element in the
middle of a list cannot stop the leaves after it from registering.

### Types

| Type | Why it exists |
| --- | --- |
| `GeneratedConversionScope` | The one owner of decoded and transferred leaves. |
| `IGeneratedScopedCodec<T>` | Composition needs a generic constraint. |
| `GeneratedTransferredResult<T, TCodec>` | `ResolveOwned` needs a class state that holds the value and the scope; one helper replaces hand-written async return glue. |
| `GeneratedScopedListCodec<T, TCodec>` | `IReadOnlyList<T>` over a scoped element. The existing list codec's `IJavaScriptCodec<T>` constraint cannot take a scoped child. |
| `GeneratedScopedDictionaryCodec<T, TCodec>` | Both `Dictionary<string, T>` and `IReadOnlyDictionary<string, T>`, as 029 did for the ordinary dictionary codec. |
| `GeneratedScopedNullableCodec<T, TCodec>` | `T?` for a value type, for example a record struct with a leaf. |
| `GeneratedScopedNullableReferenceCodec<T, TCodec>` | `T?` for a reference type. Nullable lists and dictionaries compose through it over the scoped list and dictionary codecs. |

The leaves need no new type:

- `JavaScriptValueCodec` (already a `readonly struct`) implements both
  `IJavaScriptCodec<JavaScriptValue>` and
  `IGeneratedScopedCodec<JavaScriptValue>`. Scoped `Decode` retains and
  registers; `Transfer` registers the source; `EncodeBorrowed` returns
  `value.Retain()`. Its ordinary members do not change.
- `ArrayBufferCodec` changes from a `static class` to a `readonly struct`
  that implements `IGeneratedScopedCodec<ArrayBuffer>` only. Its existing
  static members do not change, so every caller compiles, as with 029's
  change to `JavaScriptArrayCodec`. Scoped `Decode` decodes and registers;
  `Transfer` registers the source; `EncodeBorrowed` calls `Encode`, which
  creates a new JS value with no byte copy.

A generated record codec whose record holds a leaf implements only
`IGeneratedScopedCodec<TRecord>`. It passes the scope to each leaf-bearing
field, calls the ordinary codec for other fields, and disposes each encoded
field temporary after it sets it. For a class record, `Transfer` returns on
null and `EncodeBorrowed` starts with
`ArgumentNullException.ThrowIfNull(value)`. A record struct cannot be null and
gets neither line. Leaf-free records keep their current text.

### Generated execution order

Sync functions, properties, shared-object members, the shared-object
constructor factory, and span host functions:

1. `using var` scope, before the first leaf-bearing decode.
2. Decode leaf-bearing arguments with the scoped `Decode`; decode others as
   today.
3. Call the authored member.
4. For a leaf-bearing result: `Transfer`, then `EncodeBorrowed`.
5. The `using` block disposes the scope.

Async functions run steps 1-2 in the host-function frame (the scope is a
plain local there, not `using`) and capture the scope in the Promise lambda.
The outer `catch` covers only the code that runs before `CreatePromise`
returns, and it disposes the scope on a failure there. Ownership passes to
the lambda when `CreatePromise` returns: from then on the lambda is the sole
owner, and the host frame never touches the scope again. `AsValue()` on the
Promise runs after the outer `catch`, so its failure throws from the host
function, the bridge turns it into a JavaScript error, and the lambda's own
paths still dispose the scope. No two threads ever use one scope at the same
time. Inside the lambda:

- Leaf-free result: `try { ... } finally { scope.Dispose(); }`, as today.
- Leaf-bearing result: `try { var r = await task; return
  GeneratedTransferredResult<T, TCodec>.Resolve(r, scope); } catch {
  scope.Dispose(); throw; }`.

`GeneratedTransferredResult<T, TCodec>.Resolve(value, scope)` calls
`TCodec.Transfer(value, scope)` and returns
`JavaScriptPromiseResult.ResolveOwned(state, factory, abandon)`. If `Transfer`
throws, the lambda's `catch` disposes the scope and the Promise rejects;
leaves that `Transfer` did not reach stay with the author. The factory runs
`try { return TCodec.EncodeBorrowed(value, runtime); } finally {
scope.Dispose(); }`. `OwnedResultState.CreateValue` claims the state before
it calls the factory (`JavaScriptPromiseResult.cs:115-123`), so abandon never
runs after a factory failure, and the factory's `finally` is the only cleanup
on that path. Abandon disposes the scope. An abandon failure keeps the
existing `ResolveOwned` abandon behavior.

### Off-thread cleanup

A `JavaScriptValue` release deletes the native `jsi::Value` on the calling
thread (`releaseValue`, `ExpoJsiBridge.cpp:2601-2607`). A JS-backed
`ArrayBuffer` release goes through the long-lived object collection and the
runtime executor. Today, direct async `JavaScriptValue` parameters already
delete `jsi::Value` off the runtime thread, in the Promise lambda's `finally`.
That off-thread delete is an existing assumption. This change inherits it
without new verification and applies it to more leaves: the input leaves of
a leaf-free async result, the scope on fault or cancel, and abandoned
results. With one async scope, the success path of a leaf-bearing result now
releases its input leaves in the factory, on the JS thread. Cleanup uses only
the existing wrapper `Dispose` operations; this change does not redesign
them.

### Descriptor

- `ExpoDecodedOwnership` and `ExpoEncodeOperation` are removed, with the
  `DecodedOwnership` and `EncodeOperation` fields and the `IsInvocationOwned`
  helper.
- A new flat field `ResourceOwnership` of enum `ExpoResourceOwnership`
  replaces them: `Borrowed` (no leaf), `ScopedResource` (holds at least one
  leaf at any depth), `ContextRetained` (callbacks, kept as the fact
  `ExpoDecodedOwnership` carried). Every resolver branch sets it explicitly.
- `ExpoCodecShape` gains `ScopedOnly`: the type implements only
  `IGeneratedScopedCodec<T>`. `StaticHelper` now covers only callbacks and
  shared objects.
- Every descriptor has one `Expression`. `DecodeOperation` applies to
  ordinary codecs; scoped decode always calls the scoped `Decode`.

| Branch | Shape | ResourceOwnership | Expression |
| --- | --- | --- | --- |
| Leaf-free types | as today | `Borrowed` | as today |
| `JavaScriptValue` | `JavaScriptCodec` | `ScopedResource` | `JavaScriptValueCodec` |
| `ArrayBuffer` | `ScopedOnly` | `ScopedResource` | `ArrayBufferCodec` |
| List, dictionary, or nullable over a `ScopedResource` child | `ScopedOnly` | `ScopedResource` | the scoped composite |
| Record with any `ScopedResource` field | `ScopedOnly` | `ScopedResource` | the record codec |
| `JavaScriptCallback<...>` | `StaticHelper` | `ContextRetained` | as today |
| Direct `[ExpoSharedObject]` class | `StaticHelper` | `Borrowed` | as today |

`ExpoGeneratedRecordCodecModel` stores the record's `ResourceOwnership` and
whether the record is a value type (record struct) or a reference type (class
record); emission needs the second fact for the null guards. The
record cache-hit path (`Codecs.cs:321-325`) rebuilds the descriptor from that
stored fact, not from the type name alone. Descriptors still do not nest
child descriptors.

### Positions

| Position | Accepts |
| --- | --- |
| Top-level boundary (parameter, return, async result, property, shared-object constructor or member, span-function return) | Any shape with `DecodeAndEncode`, as today. `ScopedResource` uses the scoped path. |
| List element, dictionary value | `JavaScriptCodec` or `ScopedOnly`. A `ScopedResource` child selects the scoped composite. |
| `Nullable<T>` inner, nullable-reference inner | `JavaScriptCodec` or `ScopedOnly`. `ArrayBuffer?` and `JavaScriptValue?` stay excluded by `IsExcludedFromNullableReference` before resolution. `IReadOnlyList<ArrayBuffer>?` is accepted, because the nullable-list branch resolves the element under the list-element rule. |
| Record field | `DecodeAndEncode` and no context, as today. |
| Callback result, callback-argument tuple element | `JavaScriptCodec` only, so direct `JavaScriptValue` keeps working. `ScopedOnly` and `StaticHelper` report `EXPOJSI008` (`EXPOJSI023` on a shared object). |
| Typed event payload | Unchanged. The nested-resource check reports `EXPOJSI019` (module) or `EXPOJSI027` (shared object) before codec resolution. |

### Intended generator changes

The `ArrayBuffer` rows in `ExpoModulesGeneratorTests.cs:4752-4756` and
`:4768`, the matching shared-object rows, and the
`IReadOnlyList<ArrayBuffer>` record-field row (`:4800-4816`) change from
diagnostics to supported. Callback rows with `ArrayBuffer` keep `EXPOJSI008`.

## Decisions made by the controller

User decisions (2026-11-05):

1. One protocol for every leaf-bearing return: `Transfer`, then
   `EncodeBorrowed`. A direct `JavaScriptValue` return gets one extra
   Retain/Release pair.
2. The scope replaces per-parameter cleanup on functions, properties,
   shared-object members, the shared-object constructor factory, and span host
   functions. Direct typed-event paths keep their scheduling.
3. Replaced by decision 6.
4. The callback argument consumption bug goes to the roadmap backlog.
5. The callback position rule is by shape. A `ScopedOnly` descriptor (any
   leaf-bearing composite) and a `StaticHelper` descriptor are rejected in
   callback result and callback-argument positions. Direct `JavaScriptValue`
   (`ScopedResource` with `JavaScriptCodec` shape) stays accepted there.
   Leaf-bearing records emit only the scoped protocol, and each descriptor
   has one `Expression`.
6. One scope per invocation, for async too. A leaf-bearing async result takes
   the same scope into `ResolveOwned`. Returning an input leaf is correct in
   sync and async.
7. `GeneratedScopedNullableCodec<T, TCodec>` stays;
   `Nullable<record struct with a leaf>` stays supported.

Controller rulings are stated where they apply in the design: names and
remark (conversion scope), cleanup form and aggregate rule (conversion scope),
leaf codecs (Types), allocation strategy (conversion scope), factory cleanup
(execution order), scans and counters (Verification).

## Delta against `docs/specs/ownership-and-scoped-refs.md`

### ADDED Requirement: Conversion Scope Owns Boundary Wrapper Leaves

`GeneratedConversionScope` SHALL own every `JavaScriptValue` and `ArrayBuffer`
leaf that generated decode creates or that a returned value transfers. It
SHALL deduplicate by reference identity, attempt every registration on
`Dispose`, and make `Dispose` idempotent. Only `JavaScriptValueCodec` and
`ArrayBufferCodec` SHALL register leaves. It SHALL NOT own the JS value
returned to the host. It SHALL NOT use ambient, thread-static, async-local,
reflection, or finalizer ownership, and SHALL NOT register a value because it
implements `IDisposable`.

#### Scenario: Aliased leaf releases once
- **GIVEN** a scope where the same wrapper is registered twice
- **WHEN** the scope is disposed twice
- **THEN** the wrapper SHALL release exactly once

#### Scenario: Equal but distinct leaves both release
- **GIVEN** two distinct wrappers that compare equal
- **WHEN** both are registered and the scope is disposed
- **THEN** both SHALL release

#### Scenario: Registration after disposal
- **GIVEN** a disposed scope
- **WHEN** a codec registers a leaf
- **THEN** the scope SHALL throw `ObjectDisposedException`

#### Scenario: Cleanup failure is aggregated
- **GIVEN** a scope where one registered `IDisposable` throws from `Dispose`
- **WHEN** the scope is disposed
- **THEN** every other registration SHALL still be disposed
- **AND** the scope SHALL throw `AggregateException` with the failure

### ADDED Requirement: Boundary Leaf Ownership Is Explicit

Generated bindings SHALL follow the ownership matrix in this change's design,
with at most one scope per invocation. Authored code SHALL store only
`Retain()` copies of leaves beyond an invocation. A returned value SHALL
transfer all its leaves to generated glue.

#### Scenario: Stored state is returned
- **GIVEN** a getter exposes a stored record with a `JavaScriptValue` field
- **WHEN** JavaScript reads it
- **THEN** the getter SHALL return a new record built from `Retain()` copies
- **AND** the stored record SHALL stay usable

#### Scenario: Method returns its own input leaf
- **GIVEN** a sync or async method returns one of its `JavaScriptValue`
  inputs, or a record that contains one
- **WHEN** JavaScript calls it
- **THEN** the call or the Promise SHALL produce that value
- **AND** the leaf SHALL release exactly once

#### Scenario: Async inputs live until settlement
- **GIVEN** an async method has a leaf-bearing parameter and a leaf-bearing
  result
- **WHEN** the Task completes
- **THEN** the input leaf SHALL stay alive until the factory has encoded the
  result or the result is abandoned
- **AND** it SHALL release exactly once

## Delta against `docs/specs/modules-core-boundary.md`

### ADDED Requirement: Generated Bindings Compose Leaves Through One Scope

Every generated binding with a `ScopedResource` parameter or result SHALL use
one `GeneratedConversionScope` per invocation, the
`IGeneratedScopedCodec<T>` protocol, and the execution order in this change's
design. Bindings with no leaf SHALL keep their generated text and SHALL NOT
allocate a scope. `IJavaScriptCodec<T>` SHALL stay unchanged. Callback and
shared-object descriptors SHALL never be `ScopedResource`.

#### Scenario: Later field decode fails
- **GIVEN** a record parameter whose first field is an `ArrayBuffer` and whose
  second field fails to decode
- **WHEN** JavaScript calls the method
- **THEN** the first field's lease SHALL release exactly once
- **AND** the call SHALL fail through the catchable JavaScript error path

#### Scenario: Alias in two returned leaves
- **GIVEN** a method returns a record whose two fields hold the same
  `JavaScriptValue`
- **WHEN** generated glue encodes it
- **THEN** both JavaScript properties SHALL hold the value
- **AND** the source SHALL release exactly once

#### Scenario: Encode failure cleans transferred leaves
- **GIVEN** a returned value whose encode throws after `Transfer`
- **WHEN** generated glue handles the failure
- **THEN** every transferred leaf SHALL release exactly once

#### Scenario: Abandoned async result
- **GIVEN** an async result with leaves whose settlement never reaches the
  runtime
- **WHEN** the scheduler abandons it
- **THEN** every input and result leaf SHALL release exactly once

### MODIFIED Requirement: Sync Function Generation Uses Direct Calls

Scenario "Generated sync function accepts JavaScriptValue": replace the
disposal line with "generated dispatch SHALL register the wrapper in the
invocation's conversion scope, which disposes it after the result is encoded
or after a failure".

Scenario "Generated sync function returns JavaScriptValue": replace the last
two lines with "generated glue SHALL register the returned wrapper in the
invocation's conversion scope and return an independent retained wrapper to
the host-function bridge; the scope SHALL release the source once".

Scenario "JavaScript callbacks use generated codecs": add "a callback result
or argument type that holds a leaf inside a record, list, or dictionary SHALL
be rejected; a direct `JavaScriptValue` stays supported".

Add:

#### Scenario: Record with wrapper fields crosses the boundary
- **GIVEN** a record with an `ArrayBuffer` and a `JavaScriptValue` field is a
  parameter or a return
- **WHEN** JavaScript calls the method
- **THEN** each decoded or returned leaf SHALL release exactly once
- **AND** the returned source leaves SHALL NOT be disposed before encoding

### MODIFIED Requirement: Generated Property Access Uses Direct Typed Glue

Setter and getter leaves follow the property rows of the ownership matrix.
In "Setter receives JavaScriptValue", the conversion scope disposes the
invocation-owned wrapper. In "Getter returns a retained JavaScriptValue copy",
generated glue transfers the copy and returns an independent retained wrapper.

### MODIFIED Requirement: Async Function Generation Returns Promises

Scenario "Task of JavaScriptValue resolves encoded value": replace the last
two lines with "generated glue SHALL transfer the wrapper into the
invocation's conversion scope through `GeneratedTransferredResult<T, TCodec>`;
the runtime factory SHALL encode an independent retained wrapper and then
dispose the scope; abandon SHALL dispose the scope".

### MODIFIED Requirement: Generated Constructors Create Registry-Paired Instances

Add: "Constructor arguments with leaves SHALL decode into one conversion scope
in the generated factory. The scope SHALL dispose when the factory returns the
instance or throws. Registry pairing is unchanged."

### MODIFIED Requirement: Non-Null Collections Compose Recursively

Add: "A list element or dictionary value MAY be `ArrayBuffer`, or any type
that holds a leaf. Such a collection SHALL use the scoped list or dictionary
codec. `JavaScriptValue` elements, which are accepted today, SHALL no longer
leak on decode or dispose the author's wrapper on encode. A collection that
holds a leaf SHALL NOT be a callback result or callback argument."

#### Scenario: Collections of buffers round-trip
- **GIVEN** a method accepts and returns `IReadOnlyList<ArrayBuffer>`,
  `Dictionary<string, ArrayBuffer>`, or a record containing a list containing
  a dictionary of `ArrayBuffer`
- **WHEN** JavaScript passes JS-created buffers
- **THEN** each decoded lease and each returned source SHALL release exactly
  once
- **AND** the JavaScript result SHALL stay usable after the scope ends

### MODIFIED Requirement: Nullable Collection Containers And Contents Compose Recursively

Limit the three named adapters to leaf-free contents. Add: "A nullable
container whose content holds a leaf SHALL use
`GeneratedScopedNullableReferenceCodec<T, TCodec>` over the scoped list or
dictionary codec. A null container registers nothing."

### MODIFIED Requirement: Module-facing ArrayBuffer And Binary Codecs

Change the `ArrayBuffer` row constraint to: "Direct parameters, returns,
properties, record fields, list elements, and dictionary values; not a
nullable inner type, callback type argument, or nested typed-event payload".

### MODIFIED Requirement: Invalid Typed Events Are Build Diagnostics

#### Scenario: Nested leaf in a typed event payload stays rejected
- **GIVEN** a module or shared-object `[Event]` payload is a record, list, or
  dictionary that holds a `JavaScriptValue` or `ArrayBuffer`, including
  `IReadOnlyList<ArrayBuffer>`
- **WHEN** the generator analyzes the event
- **THEN** it SHALL report `EXPOJSI019` on a module or `EXPOJSI027` on a
  shared object
- **AND** event emission SHALL NOT use a conversion scope

### MODIFIED Requirement: Unsupported Signatures Are Build Diagnostics

Scenario "Static helper codec is used as a generic codec argument": change the
GIVEN to "a `Nullable<T>` or nullable-reference inner type, callback result,
or callback-argument tuple element is `ArrayBuffer`, a `JavaScriptCallback`,
or a type that holds one". List elements and dictionary values of
`ArrayBuffer` are no longer in this scenario.

Scenario "Record field needs a runtime context": a field whose codec is
`ScopedOnly` is valid. Only fields whose type has no codec, whose codec lacks
`DecodeAndEncode`, or whose codec has a context requirement report
`EXPOJSI007`.

Add:

#### Scenario: Leaf-bearing composite is a callback type argument
- **GIVEN** a `JavaScriptCallback` result or argument tuple element is a
  record that holds a leaf, `IReadOnlyList<JavaScriptValue>`, or a
  `JavaScriptValue` dictionary, or a nullable form of these
- **WHEN** the project is compiled
- **THEN** the generator SHALL report `EXPOJSI008`, or `EXPOJSI023` on a
  shared-object class
- **AND** a direct `JavaScriptValue` callback result or argument SHALL stay
  supported

### MODIFIED Requirement: Generator Conversion Policy Is Typed

Replace "decoded ownership, encode operation" with "resource ownership".

## Module authoring guide and skill

- Update `docs/module-authoring-guide.md:143-145` (binary data), the callback
  type rule, and the ownership note to match the deltas above.
- Skill "Generated Module Ownership": `JavaScriptValueCodec.Encode` still
  returns the same wrapper, but generated glue no longer uses it for boundary
  returns. Generated glue transfers into the conversion scope and returns an
  independent retained wrapper.

## Roadmap

Row 030 is `IN PROGRESS` during this change and `DONE` at close. The close
task adds this line under "Unplanned backlog / ABI and codecs":

- **P2 — Callback argument ownership**: invoking a `JavaScriptCallback`
  disposes `JavaScriptValue` arguments that the author still owns.

## Verification

Tests SHALL NOT rely on finalizers or GC for an exact-once assertion. Counter
assertions SHALL use deltas after `ResetCounters` on `HermesTestRuntime`,
calibrated against a leaf-free baseline. `ReleasedValues` counts traversal
temporaries too. Exact-once `ArrayBuffer` assertions SHALL use JS-created
buffers and `LongLivedArrayBuffersReleased`; native-backed leases have no
counter. The one existing exact `ReleasedValues` assertion that runs
generated glue and shifts is `Modules/ModuleAttributeTests.cs:95-125` in
`Expo.ModulesCore.Tests`: `1u` becomes `3u` and `2u` becomes `4u`. The test
SHALL state the delta and its cause: each of its two getter reads transfers
the authored `Retain()` copy into the scope and returns an independent
retained wrapper, which is the extra Retain/Release pair on direct
`JavaScriptValue` returns.

Required test classes:

1. Scope unit tests: empty, one leaf, alias dedup, equal but distinct,
   idempotent dispose, registration after dispose, and one throwing
   `IDisposable` registered through internal `Register` while the others
   still dispose.
2. Codec tests with counters for `JavaScriptValueCodec`, `ArrayBufferCodec`,
   each composite codec, and `GeneratedTransferredResult<T, TCodec>`: decode
   registers once; `EncodeBorrowed` leaves the source usable; `Transfer`
   releases the source once at scope end; the JS result stays usable after
   scope disposal; one alias encoded twice; encode failure; factory failure;
   abandon.
3. Generator source tests: one scope per leaf-bearing member, no per-leaf
   cleanup, no scope on leaf-free members, both async lambda shapes, the
   descriptor table, the position table, every row of "Intended generator
   changes", the three callback shapes that become diagnostics, and a direct
   `JavaScriptCallback<JavaScriptValue>` result and a
   `JavaScriptCallback<ValueTuple<JavaScriptValue>, int>` argument that stay
   accepted. No test pins the direct callback case today.
4. Digest proof: the first implementation task splits
   `GeneratorPreservesCodecPolicyModuleOutputContract` and
   `GeneratorPreservesCodecPolicySharedObjectOutputContract` into a leaf-free
   fixture and a resource fixture, and pins both on unchanged code. The
   leaf-free hashes, and `GeneratorPreservesModuleOutputContract`,
   `GeneratorPreservesSharedObjectOutputContract`, and
   `GeneratorPreservesNullableCodecOutputContract`, SHALL stay identical
   through 030. The resource hashes are re-pinned after review of the
   generated text.
5. Hermes integration tests in `Expo.ModulesCore.Tests`: sync success and
   throw; async success, rejection, and abandon; async inputs alive until
   settlement; property get and set; shared-object member and constructor;
   record with `ArrayBuffer`; list and dictionary of buffers; record to list
   to dictionary to buffer; alias in two leaves; sync and async method that
   returns its own input leaf; later-field decode failure; async read of a
   buffer after `await`; transferred return source disposed while the JS
   result stays usable; encode failure; `Nullable<record struct with a leaf>`.

Commands, each expected to pass or print nothing:

```sh
scripts/test-managed.sh            # all pass, 0 skipped
scripts/format.sh --check --all
git diff --check
rg -n 'ThreadStatic|AsyncLocal|ThreadLocal|CurrentConversionScope|static\s+(readonly\s+)?GeneratedConversionScope' \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator -g '*.cs'
rg -n ' is IDisposable|as IDisposable|GetInterfaces|typeof\(IDisposable\)' \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator -g '*.cs'
git diff --unified=0 5824b07..HEAD -- docs packages .agents \
  | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\'
```

Also run the 029 `rg` command in "Codec text is opaque". The two `rg` scans
print nothing at `5824b07`.
