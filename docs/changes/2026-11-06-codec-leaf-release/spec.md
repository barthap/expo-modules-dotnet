# Codec Leaf Release

Amends roadmap item 030. It replaces the conversion scope and the scoped codec
family that 030 shipped on this branch with one codec protocol.

## Goal

Codecs have two jobs: decode arguments from JavaScript and encode results back.
This change adds a third member, `Release`, to the one codec protocol, so a
codec of any shape also knows how to free the wrapper leaves it holds. Every
combination then composes in every position: a dictionary of `ArrayBuffer`, a
list of records that hold a `JavaScriptValue`, or a callback whose argument is
a record with an `ArrayBuffer`.

A leaf is a `JavaScriptValue` or an `ArrayBuffer` wrapper.

## Design criteria

These criteria bind every decision below.

- **Clean code**: no overengineering, no odd special cases, obvious data flow.
- **Performance**: no needless copies, allocations, or retain cycles.
  Leaf-free bindings keep their generated text byte for byte.
- **Boring ownership**: no leaks, one obvious owner, exactly-once release.

## Scope

### In scope

- `IJavaScriptCodec<T>` gains `static virtual void Release(T value) { }`.
- `JavaScriptValueCodec.Encode` returns `value.Retain()`.
- `ArrayBufferCodec` implements `IJavaScriptCodec<ArrayBuffer>`.
- `Release` overrides in the leaf codecs, the list and dictionary codecs, the
  five nullable codecs, and generated record codecs that hold leaves.
- Composite decode releases the children it already decoded when a later
  child fails.
- One public helper, `CodecRelease`, that releases several values and
  aggregates failures.
- Generated glue calls `Release` in a `finally` for leaf-bearing arguments and
  results, in sync functions, properties, shared-object members, the
  shared-object constructor factory, span functions, and async functions.
- Leaf-bearing types become valid as callback arguments, callback results, and
  nested typed event payloads (module and shared object).
- Delete `GeneratedConversionScope`, `IGeneratedScopedCodec<T>`,
  `GeneratedTransferredResult<T, TCodec>`, `GeneratedScopedListCodec<T,
  TCodec>`, `GeneratedScopedDictionaryCodec<T, TCodec>`,
  `GeneratedScopedNullableCodec<T, TCodec>`,
  `GeneratedScopedNullableReferenceCodec<T, TCodec>`, and their tests.
- Descriptor: remove `ExpoCodecShape.ScopedOnly`, rename
  `ExpoResourceOwnership.ScopedResource` to `HoldsLeaves`.

### Out of scope

- Callback lifetime. The runtime context keeps one retained entry per decoded
  callback until teardown. This is a separate backlog item.
- Finalizers, GC-driven release, and ambient, thread-static, or async-local
  ownership. These stay forbidden.
- `ArrayBuffer?`, `JavaScriptValue?`, nullable callbacks, nullable shared
  objects, unions, and `Uint8Array` (031–036).
- Async start order. The authored async method starts before `CreatePromise`
  returns. If something fails in between, the outer `catch` releases arguments
  that the running `Task` still uses. 030 has the same hazard. It gets its own
  backlog item.

## Accepted design

### Codec contract

```csharp
public interface IJavaScriptCodec<T>
{
  static abstract T Decode(JavaScriptValueRef value, JavaScriptRuntime runtime);
  static abstract T Decode(JavaScriptValue value, JavaScriptRuntime runtime);
  static abstract JavaScriptValue Encode(T value, JavaScriptRuntime runtime);
  static virtual void Release(T value) { }
}
```

- `Decode` gives the caller every leaf in the result.
- `Encode` returns a new JavaScript value. It never disposes `value` or a leaf
  in it.
- `Release` disposes every leaf in `value`. Null or default releases nothing.

Leaf `Dispose` is idempotent, so releasing one wrapper twice frees its native
handle once. That covers a method that returns its own input and a record that
holds one leaf in two fields.

Plain codecs keep the empty default. `JavaScriptValueCodec` and
`ArrayBufferCodec` dispose the leaf. Composites release each child through the
child codec. A composite that releases more than one child attempts every child
and throws one `AggregateException` with every failure. A composite whose
decode fails at child N releases children 0 to N−1 and rethrows. If that
cleanup also fails, its `AggregateException` propagates instead, as a C#
`finally` would.

Every generated record codec uses one `Encode` text, with no null guard.
Encoding a null class record, for example a null element in a returned list,
fails with a catchable JavaScript error. `Release` skips the null record and
still releases the other elements.

`JavaScriptCallbackCodec` and `SharedObjectCodec<T>` are static classes, not
`IJavaScriptCodec<T>` implementations. They get no `Release`: callbacks stay
context-retained and shared objects stay registry-owned.

### Rules for authors

1. A leaf you receive as a parameter is valid until the call ends (async:
   until the Promise settles). To keep it, call `Retain()`.
2. A leaf you return is no longer yours.
3. A leaf you pass to a callback or an event stays yours. Keep it alive until
   the call or the returned `Task` completes.
4. A leaf you get back from a callback is yours. Dispose it.

### Generated glue

Leaf-free bindings keep their generated text. For a leaf-bearing binding the
generator declares each leaf-bearing argument, and a leaf-bearing result,
before one `try`. The `finally` releases the result first, then the arguments
in declaration order. An argument that never decoded is still default and
releases nothing.

```csharp
Payload __expoArg0 = default!;
Reply __expoResult = default!;
try
{
  __expoArg0 = PayloadCodec.Decode(arguments.GetValue(0), runtime);
  var __expoArg1 = NumberCodec<int>.Decode(arguments.GetValue(1), runtime);
  __expoResult = module.Describe(__expoArg0, __expoArg1);
  return ReplyCodec.Encode(__expoResult, runtime);
}
finally
{
  List<Exception>? __expoReleaseFailures = null;
  CodecRelease.Release<Reply, ReplyCodec>(__expoResult, ref __expoReleaseFailures);
  CodecRelease.Release<Payload, PayloadCodec>(__expoArg0, ref __expoReleaseFailures);
  CodecRelease.ThrowIfFailed(__expoReleaseFailures);
}
```

The generator emits a `Release` call only for a value whose descriptor is
`HoldsLeaves`. The span lease keeps its own `using`.

Async functions decode in the host-function frame. Until `CreatePromise`
returns, the outer `catch` releases the arguments. After that the Promise
lambda owns them:

- Leaf-free result: the lambda releases the arguments in its `finally`.
- Leaf-bearing result: the lambda passes a
  `StrongBox<(TResult, TArg0, ...)>` (or `StrongBox<TResult>` when no argument
  holds leaves) to `JavaScriptPromiseResult.ResolveOwned`.
  The static factory encodes the result and then releases the result and the
  arguments. The static abandon callback releases them without encoding. A
  fault or cancel releases them in the lambda's `catch`.

### Descriptor

`ExpoResourceOwnership` is `Borrowed`, `HoldsLeaves`, or `ContextRetained`.
`ExpoCodecShape` is `JavaScriptCodec` or `StaticHelper`. `ExpoCodecDescriptor`
gains `ValueTypeName`, the codec's `T` with its nullable annotation, which the
emitter uses to declare locals and helper type arguments.

| Branch | Shape | ResourceOwnership |
| --- | --- | --- |
| Leaf-free types | as before | `Borrowed` |
| `JavaScriptValue`, `ArrayBuffer` | `JavaScriptCodec` | `HoldsLeaves` |
| List, dictionary, or nullable over a `HoldsLeaves` child | `JavaScriptCodec` | `HoldsLeaves` |
| Record with any `HoldsLeaves` field | `JavaScriptCodec` | `HoldsLeaves` |
| `JavaScriptCallback<...>` | `StaticHelper` | `ContextRetained` |
| Direct `[ExpoSharedObject]` class | `StaticHelper` | `Borrowed` |

A list, dictionary, or nullable over a leaf-bearing child uses the same codec
type as over a leaf-free child: `JavaScriptArrayCodec<T, TCodec>`,
`JavaScriptDictionaryCodec<T, TCodec>`, `NullableCodec<T, TCodec>`,
`NullableReferenceCodec<T, TCodec>`, `NullableReadOnlyListCodec<T, TCodec>`,
`NullableDictionaryCodec<T, TCodec>`, or
`NullableReadOnlyDictionaryCodec<T, TCodec>`.

## Callers of the ordinary `Encode`

Every caller below disposes the value that `Encode` returns, or relied on
`JavaScriptValueCodec.Encode` returning its input. With `Encode` returning a
new value, each one disposes only its own copy.

| Caller | Today | After |
| --- | --- | --- |
| `JavaScriptCallback<TArgs, TResult>.InvokeCore` disposes every encoded argument in `finally` | Disposes an author's `JavaScriptValue` argument (roadmap backlog "P2 — Callback argument ownership") | Disposes only the retained copy. No code change. |
| `ValueTupleCodec<...>.Encode` calls `JavaScriptArgsCodec.DisposeEncoded` when a later element fails | Disposes the author's earlier `JavaScriptValue` argument | Disposes only the copies. No code change. |
| `JavaScriptArrayCodec.Encode`, `JavaScriptDictionaryCodec.Encode` dispose each child with `using var` | Disposes an author's `JavaScriptValue` element (030 routed generated glue around this through the scoped codecs) | Disposes only the copy. No code change. |
| Generated record `Encode` disposes each field with `using var` | Same as above for a `JavaScriptValue` field | Disposes only the copy. One emitter for every record. |
| `ModuleEventEmitter.EmitAsync<TCodec, T>`, `GeneratedSharedObjectEvents.EmitAsync<TCodec, T>`, `Module.SendEventAsync<TCodec, T>` use `using var` on the encoded payload | No leaf could reach them through generated code | Nested leaves reach them. The emitter disposes only the encoded value. No code change. |
| `NullableCodec`, `NullableReferenceCodec`, `NullableReadOnlyListCodec`, `NullableDictionaryCodec`, `NullableReadOnlyDictionaryCodec` | Return the inner encoded value | Unchanged. |
| Generated sync, property getter, span return, and async result glue | 030 transfers into the scope and calls `EncodeBorrowed` | Calls `Encode`, then `Release` on the source in `finally`. |
| `ArrayBufferCodec.EncodeCopy`, `ByteArrayCodec`, `MemoryByteCodec`, `ReadOnlyMemoryByteCodec`, span-return glue | Dispose a temporary buffer after `ArrayBufferCodec.Encode` | Unchanged. `EncodeCopy` stays. |

`JavaScriptCallback.InvokeCore` decodes the result with the owned `Decode`
overload inside `using var result`, so the author owns every leaf of a callback
result. That already holds today for a direct `JavaScriptValue` result.

## Deviations from the design notes

1. **`CodecRelease` is public, not internal.** Generated record codecs and
   generated glue live in the author's assembly and call it. It carries the
   "for generated bindings" remark that other generated helpers use.
2. **The async state is `StrongBox<(TResult, TArg0, ...)>`, not a bare value
   tuple.** `JavaScriptPromiseResult.ResolveOwned<TState>` requires
   `TState : class`. `StrongBox<T>` is the smallest class that holds the tuple
   and keeps the factory and abandon callbacks `static`. With no leaf-bearing
   argument the state is `StrongBox<TResult>`, and the callbacks read
   `__expoState.Value!`, because `StrongBox<T>.Value` is maybe-null and
   generated code compiles with `#nullable enable` and no warnings.
3. **Generated glue uses one `finally` and `CodecRelease`.** The design sketch
   nests a `try` for the result. One `finally` that releases the result first
   and then the arguments has the same order. Going through `CodecRelease`
   also means a failing release does not skip the ones after it.
4. **`ExpoCodecDescriptor` gains `ValueTypeName`.** The model's existing type
   names drop the nullable annotation, which makes generated code warn with
   CS8631 or CS8600 under `#nullable enable`. `GetCodec` stamps the annotated
   name once, so the emitter never re-derives a type from codec text.

## Delta against `docs/specs/ownership-and-scoped-refs.md`

### REMOVED Requirement: Conversion Scope Owns Boundary Wrapper Leaves

The conversion scope no longer exists.

### MODIFIED Requirement: Boundary Leaf Ownership Is Explicit

A leaf is a `JavaScriptValue` or an `ArrayBuffer` wrapper. Every
`IJavaScriptCodec<T>` SHALL follow this contract: `Decode` SHALL give the
caller every leaf in the result. `Encode` SHALL return a new owned JavaScript
value and SHALL NOT dispose `value` or any leaf in it. `Release` SHALL dispose
every leaf in `value`, and SHALL do nothing for null or default.

Authored code SHALL follow four rules:

1. A leaf received as a parameter is valid until the call ends, or until the
   Promise settles for an async method. Authored code SHALL call `Retain()` to
   keep it.
2. A returned leaf belongs to generated glue.
3. A leaf passed to a callback or an event stays with the author, who SHALL
   keep it alive until the call or the returned `Task` completes.
4. A leaf returned by a callback belongs to the author, who SHALL dispose it.

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

#### Scenario: Callback argument stays with the author
- **GIVEN** authored code invokes a callback with a `JavaScriptValue` or a
  record holding an `ArrayBuffer`
- **WHEN** the invocation returns
- **THEN** the author's leaves SHALL stay usable
- **AND** each encoded copy SHALL release exactly once

#### Scenario: Callback result belongs to the author
- **GIVEN** a callback result type holds an `ArrayBuffer`
- **WHEN** the author disposes the decoded leaves
- **THEN** each SHALL release exactly once

## Delta against `docs/specs/modules-core-boundary.md`

### REMOVED Requirement: Generated Bindings Compose Leaves Through One Scope

Replaced by the two ADDED requirements below. Its ownership matrix, execution
order, scoped protocol, and position table no longer apply.

### ADDED Requirement: Codecs Release Leaves Through One Protocol

`IJavaScriptCodec<T>` SHALL declare `static virtual void Release(T value)`
with an empty default body. There SHALL be no second codec protocol.
`JavaScriptValueCodec` and `ArrayBufferCodec` (a `readonly struct`
implementing `IJavaScriptCodec<ArrayBuffer>`) SHALL dispose the leaf.
`JavaScriptArrayCodec<T, TCodec>`, `JavaScriptDictionaryCodec<T, TCodec>`
(through both dictionary interfaces), `NullableCodec<T, TCodec>`,
`NullableReferenceCodec<T, TCodec>`, `NullableReadOnlyListCodec<T, TCodec>`,
`NullableDictionaryCodec<T, TCodec>`, `NullableReadOnlyDictionaryCodec<T,
TCodec>`, and every generated record codec whose record holds a leaf SHALL
release each child through the child codec. A composite with more than one
child SHALL attempt every child and SHALL throw one `AggregateException` with
every failure, through the public `CodecRelease` helper. A list, dictionary,
or record decode that fails at a child SHALL release the children it already
decoded and rethrow. `JavaScriptValueCodec.Encode` SHALL return
`value.Retain()`. No codec SHALL use finalizers, reflection, or ambient state
to release.

#### Scenario: A missing override leaks
- **GIVEN** a composite codec over `ArrayBufferCodec`, holding JS-created
  buffers
- **WHEN** a test calls `Release` through the generic `TCodec.Release`
- **THEN** each buffer entry SHALL release exactly once
- **AND** the test SHALL fail if that composite used the empty default

#### Scenario: Release failure is aggregated
- **GIVEN** a list whose second element's release throws
- **WHEN** the list codec releases it
- **THEN** the first and third elements SHALL still release
- **AND** the codec SHALL throw `AggregateException` with that failure

#### Scenario: Later element decode fails
- **GIVEN** a JavaScript array whose first element is an `ArrayBuffer` and
  whose second element is not
- **WHEN** `JavaScriptArrayCodec<ArrayBuffer, ArrayBufferCodec>` decodes it
- **THEN** the first lease SHALL release exactly once
- **AND** the decode error SHALL propagate

#### Scenario: Encode leaves the source usable
- **GIVEN** an author-owned `JavaScriptValue` inside a list
- **WHEN** `JavaScriptArrayCodec<JavaScriptValue, JavaScriptValueCodec>`
  encodes the list
- **THEN** the author's wrapper SHALL stay usable
- **AND** the encoder SHALL release only its own copy

### ADDED Requirement: Generated Bindings Release Leaves After Each Call

Generated bindings with no `HoldsLeaves` argument or result SHALL keep their
generated text. A leaf-bearing sync function, property getter or setter,
shared-object member, shared-object constructor factory, or span function SHALL
declare its leaf-bearing arguments and result before one `try`, and its
`finally` SHALL release the result first and then the arguments in declaration
order. An async function SHALL release leaf-bearing arguments in the outer
`catch` until `CreatePromise` returns, and only in the Promise lambda after
that. A leaf-bearing async result SHALL travel to the factory in a
`StrongBox` with the leaf-bearing arguments. The factory SHALL release them
after it encodes, and abandon SHALL release them without encoding. The
generator SHALL emit `Release` only for a `HoldsLeaves` descriptor. The span
lease SHALL keep its own `using`.

| Branch | Shape | ResourceOwnership |
| --- | --- | --- |
| Leaf-free types | as before | `Borrowed` |
| `JavaScriptValue`, `ArrayBuffer` | `JavaScriptCodec` | `HoldsLeaves` |
| List, dictionary, or nullable over a `HoldsLeaves` child | `JavaScriptCodec` | `HoldsLeaves` |
| Record with any `HoldsLeaves` field | `JavaScriptCodec` | `HoldsLeaves` |
| `JavaScriptCallback<...>` | `StaticHelper` | `ContextRetained` |
| Direct `[ExpoSharedObject]` class | `StaticHelper` | `Borrowed` |

A record codec model SHALL store whether the record holds leaves and whether it
is a value type, so a cache hit rebuilds the same descriptor.

#### Scenario: Leaf-free member text is unchanged
- **GIVEN** the leaf-free output contract sources
- **WHEN** the generator runs
- **THEN** the output hashes SHALL equal the hashes from before this change

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
- **AND** the source's native handle SHALL release exactly once

#### Scenario: Encode failure releases returned leaves
- **GIVEN** a returned value whose encode throws
- **WHEN** generated glue handles the failure
- **THEN** every leaf of the returned value SHALL release exactly once

#### Scenario: Abandoned async result
- **GIVEN** an async result with leaves whose settlement never reaches the
  runtime
- **WHEN** the scheduler abandons it
- **THEN** every input and result leaf SHALL release exactly once

### MODIFIED Requirement: Sync Function Generation Uses Direct Calls

Replace these scenarios. The others stay.

#### Scenario: Generated sync function accepts JavaScriptValue
- **GIVEN** a generated sync function accepts `JavaScriptValue`
- **WHEN** JavaScript calls that function
- **THEN** generated dispatch SHALL retain the scoped argument into an owned
  wrapper for the invocation
- **AND** generated dispatch SHALL release it in `finally`, after the result is
  encoded or after a failure
- **AND** authored module code SHALL NOT dispose or store the argument wrapper

#### Scenario: Generated sync function returns JavaScriptValue
- **GIVEN** a generated sync function returns `JavaScriptValue`
- **WHEN** JavaScript calls that function
- **THEN** ownership of the returned wrapper SHALL pass to generated glue
- **AND** generated glue SHALL return a retained copy to the host-function
  bridge and release the returned wrapper once

#### Scenario: Record with wrapper fields crosses the boundary
- **GIVEN** a record with an `ArrayBuffer` and a `JavaScriptValue` field is a
  parameter or a return
- **WHEN** JavaScript calls the method
- **THEN** each decoded or returned leaf SHALL release exactly once
- **AND** no returned leaf SHALL be disposed before it is encoded

#### Scenario: JavaScript callbacks use generated codecs
- **GIVEN** a generated sync or async function accepts
  `JavaScriptCallback<TResult>` or `JavaScriptCallback<TArgs, TResult>`
- **WHEN** JavaScript passes a function value
- **THEN** generated dispatch SHALL decode it as a retained callback owned by
  the current `DotnetRuntimeContext`
- **AND** `JavaScriptCallback<TResult>` SHALL represent zero callback arguments
- **AND** `JavaScriptCallback<TArgs, TResult>` SHALL use `ValueTuple` argument
  codecs for one through eight callback arguments
- **AND** callback argument and result values SHALL use generated
  `Expo.ModulesCore` codecs without runtime reflection or dynamic invocation
- **AND** a callback argument or result type MAY hold leaves at any depth

### MODIFIED Requirement: Generated Property Access Uses Direct Typed Glue

Replace these scenarios. The requirement text stays.

#### Scenario: Getter returns a module-convertible value
- **GIVEN** a generated property getter returns a type with a supported codec
- **WHEN** JavaScript reads the accessor
- **THEN** generated glue SHALL encode the result through that codec
- **AND** a returned value that holds a leaf SHALL follow the same rule as a
  synchronous `[JS]` method return

#### Scenario: Setter receives JavaScriptValue
- **GIVEN** a generated property setter accepts `JavaScriptValue`
- **WHEN** JavaScript assigns the property
- **THEN** generated glue SHALL retain and own the decoded wrapper for the
  synchronous setter invocation
- **AND** it SHALL release that wrapper after the authored setter returns or
  throws
- **AND** authored code SHALL NOT dispose or store the invocation-owned wrapper
- **AND** authored code that needs the value later SHALL store and eventually
  dispose an explicit retained copy

#### Scenario: Getter returns a retained JavaScriptValue copy
- **GIVEN** authored module state owns a stored `JavaScriptValue`
- **WHEN** a generated property getter exposes that value
- **THEN** the getter SHALL return an explicit retained copy whose ownership
  passes to generated glue
- **AND** generated glue SHALL return its own retained copy and release the
  getter's copy once
- **AND** the module SHALL keep ownership of its original stored wrapper

### MODIFIED Requirement: Typed Event Payload Ownership Is Explicit

Replace the paragraph that rejects nested wrappers with:

Records, lists, and dictionaries that hold a `JavaScriptValue` or an
`ArrayBuffer` SHALL be valid typed event payloads. Generated glue SHALL encode
them on the owning runtime and SHALL dispose only the encoded value. The
author keeps every nested leaf and SHALL keep it alive until the returned task
completes. A payload containing `JavaScriptCallback` SHALL stay rejected.
Event-safety classification SHALL run before general codec resolution can
mutate generated-record-codec state.

Add this scenario:

#### Scenario: Nested leaf payload stays with the author
- **GIVEN** a module or shared-object event payload is a record holding a
  `JavaScriptValue` and an `ArrayBuffer`
- **WHEN** authored code awaits the event
- **THEN** listeners SHALL receive both values
- **AND** the author's leaves SHALL stay usable after the task completes
- **AND** each encoded copy SHALL release exactly once

### MODIFIED Requirement: Invalid Typed Events Are Build Diagnostics

In the requirement text, `EXPOJSI019` covers payloads with no encode-capable
event codec, a callback codec, or a nullable annotation on a `JavaScriptValue`
or `ArrayBuffer` at any depth. It no longer covers nested non-null leaves.

Replace this scenario:

#### Scenario: Typed event payload is unsupported
- **GIVEN** a valid-shaped typed event has an unsupported payload, a callback,
  or a nullable `JavaScriptValue` or `ArrayBuffer` at any depth
- **WHEN** the generator analyzes the event
- **THEN** it SHALL report `EXPOJSI019`
- **AND** it SHALL not emit reflection, dynamic conversion, callback `Encode`
  source, or a secondary compiler error

Remove the scenario "Nested leaf in a typed event payload stays rejected".

### MODIFIED Requirement: Invalid Shared-Object Events Are Build Diagnostics

Replace this scenario:

#### Scenario: Unsupported payload is diagnosed
- **GIVEN** a valid-shaped shared-object event uses a callback payload or a
  nullable `JavaScriptValue` or `ArrayBuffer` at any depth
- **WHEN** the generator analyzes it
- **THEN** it SHALL report `EXPOJSI027`
- **AND** it SHALL not emit reflection, dynamic conversion, or callback encode
  source

### MODIFIED Requirement: Async Function Generation Returns Promises

Replace this scenario:

#### Scenario: Task of JavaScriptValue resolves encoded value
- **GIVEN** a generated provider registers a module with an authored `[JS]`
  method returning `Task<JavaScriptValue>`
- **WHEN** the task completes with a JavaScript value wrapper
- **THEN** ownership of the returned wrapper SHALL pass to generated glue
- **AND** the runtime factory SHALL encode a retained copy and then release
  the returned wrapper and the leaf-bearing arguments
- **AND** abandoning the result SHALL release them without encoding

### MODIFIED Requirement: Unsupported Signatures Are Build Diagnostics

Replace this scenario:

#### Scenario: Static helper codec is used as a generic codec argument
- **GIVEN** a list element, dictionary value, `Nullable<T>` inner type,
  nullable-reference inner type, callback result, or callback-argument tuple
  element is a `JavaScriptCallback` or a type that holds one, or the annotated
  type is `ArrayBuffer?` or `JavaScriptValue?`
- **WHEN** the project is compiled
- **THEN** the generator SHALL report the using member's unsupported-type
  diagnostic: `EXPOJSI001`, `EXPOJSI002`, `EXPOJSI008`, `EXPOJSI015`, or
  `EXPOJSI023`
- **AND** generated source SHALL NOT pass a static codec class as a type
  argument
- **AND** a type that holds a leaf is not in this scenario in any of these
  positions

In "Record field needs a runtime context", remove the last **AND** (the one
about the scoped protocol).

Remove the scenario "Leaf-bearing composite is a callback type argument".

### MODIFIED Requirement: Module-facing ArrayBuffer And Binary Codecs

Replace the `ArrayBuffer` table row with:

| CLR type | Boundary behavior | Generated-binding constraints |
| --- | --- | --- |
| `ArrayBuffer` | Preserves wrapper ownership semantics | Every codec position, including list elements, dictionary values, callback types, and nested typed-event payloads; not a nullable inner type |

### MODIFIED Requirement: Non-Null Collections Compose Recursively

Replace the last four sentences of the requirement text with:

A list element or dictionary value MAY be `ArrayBuffer`, `JavaScriptValue`, or
any type that holds a leaf. The same `JavaScriptArrayCodec<T, TCodec>` and
`JavaScriptDictionaryCodec<T, TCodec>` SHALL serve it.

In "Collections of buffers round-trip", the last **AND** reads: the
JavaScript result SHALL stay usable after the call releases its leaves.

### MODIFIED Requirement: Nullable Collection Containers And Contents Compose Recursively

Replace the last three sentences of the requirement text with:

These three adapters SHALL serve both leaf-free and leaf-bearing contents. A
null container releases nothing.

### MODIFIED Requirement: Generated Constructors Create Registry-Paired Instances

Replace the conversion-scope paragraph with:

The generated factory SHALL release leaf-bearing constructor arguments in a
`finally`, after the factory returns the instance or throws. Registry pairing
is unchanged.

## Docs, skill, roadmap

- `docs/module-authoring-guide.md`: the four rules for authors, once, in
  section 3. Remove the list, dictionary, and callback restrictions on leaves.
  Section 5 states rule 3 for nested event payloads. Section 7 states rules 3
  and 4.
- `.agents/skills/expo-jsi-managed-handle-lifetime/SKILL.md`: "Generated
  Module Ownership" lists the codec contract and the four rules.
- `docs/roadmap.md`: row 030 keeps its ID and is retitled "Composable codec
  leaf release". One or two note lines record the redesign. The backlog item
  "P2 — Callback argument ownership" is fixed by this change and leaves the
  backlog. A new backlog line records callback lifetime (one retained entry per
  decoded callback until runtime teardown). Another backlog line records the
  async start order hazard from "Out of scope".
- Briefs 031–033 replace mentions of the conversion scope, `Transfer`, and
  `EncodeBorrowed` with `Release`. Their own designs are rebased in their own
  items.

## Verification

- `scripts/test-managed.sh` passes with no failures and no skips. Baseline on
  this branch before the change: 1062 passed, 0 failed, 0 skipped.
- `scripts/format.sh --check --all` and `git diff --check` pass.
- `git diff <base>..HEAD | grep -n '/[U]sers/'` prints nothing.
- The v2 hot-path check prints nothing new:
  `rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\?\[\]|JsonSerializer" packages/expo-modules-dotnet/managed/packages`.
- `rg -n 'GeneratedConversionScope|IGeneratedScopedCodec|GeneratedTransferredResult|GeneratedScoped|EncodeBorrowed|ScopedOnly|ScopedResource' packages docs/specs docs/module-authoring-guide.md .agents`
  prints nothing.
