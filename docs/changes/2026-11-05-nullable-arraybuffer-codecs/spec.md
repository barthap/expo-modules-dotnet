# Nullable ArrayBuffer Codecs

## Goal

`ArrayBuffer?` works on every generated boundary where `ArrayBuffer` works
today. JavaScript `null` and `undefined` decode to C# `null`, C# `null`
encodes as JavaScript `null`, and a non-null buffer follows the same
`Release` rules as `ArrayBuffer`. This is roadmap item 031.

Plan 026 rejected `ArrayBuffer?` because cleanup did not compose. Plan 030
added `Release` to the one codec protocol, and `NullableReferenceCodec<T,
TCodec>` already releases a non-null value through its child codec. This
change removes only the `ArrayBuffer` part of the plan-026 exclusion.
`JavaScriptValue?` stays rejected by design, because `JavaScriptValue`
already represents and inspects JavaScript `null` and `undefined`.

## Process note

The brief `docs/plans/031-nullable-arraybuffer-codecs.md` predates 028 and
029. This spec is reconciled against the 030 tip and wins over the brief
where they differ. Two such differences are deliberate (decisions 6 and 7):
the brief keeps nested `ArrayBuffer?` event payloads rejected and leaves the
callback positions open. After 030, both positions accept non-null
`ArrayBuffer`, so the nullable form follows the non-null rule there too.

## Design criteria

1. **Clean code.** No new codec type when an existing one fits. One
   execution order per path. No special case for one position.
2. **Performance.** Null holds no leaf and allocates nothing. Non-null keeps
   the 030 cost exactly. Leaf-free generated output stays byte-identical.
3. **Correctness.** No leaks. A decoded non-null buffer releases exactly
   once through `Release`. Null never creates a lease.

## Scope

### In scope

- `Expo.ModulesCore.Generator`: `ExpoModulesGenerator.Codecs.cs`
  (`IsExcludedFromNullableReference`) and
  `ExpoModulesGenerator.EventAnalysis.cs` (`GetUnsupportedEventPayload`).
- `Expo.ModulesCore/ModuleEventEmitter.cs` and
  `Expo.ModulesCore/SharedObjectEventEmitter.cs`: the direct `ArrayBuffer`
  overloads become `ArrayBuffer?`.
- `Expo.ModulesCore.Generator.Tests` and `Expo.ModulesCore.Tests`.
- `docs/specs/modules-core-boundary.md`,
  `docs/specs/ownership-and-scoped-refs.md`, `docs/module-authoring-guide.md`,
  and `docs/roadmap.md`.

### Out of scope

- `JavaScriptValue?` (unsupported by design).
- Nullable callbacks (032) and nullable concrete shared objects (033).
- Any change to `ArrayBuffer`, `ArrayBufferCodec`,
  `NullableReferenceCodec<T, TCodec>`, the composite codecs, the ABI, the
  scheduler, or platform code.

## Accepted design

### Codec: the existing nullable-reference codec

No new runtime type. `ArrayBuffer?` resolves to

```text
NullableReferenceCodec<global::Expo.ModulesCore.ArrayBuffer, ArrayBufferCodec>
```

through the fall-through at the end of `TryGetNullableReferenceCodec`, once
`ArrayBufferMetadataName` leaves `IsExcludedFromNullableReference`.
`ArrayBufferCodec` implements `IJavaScriptCodec<ArrayBuffer>`, so the
constraint `TCodec : IJavaScriptCodec<T>` holds. The descriptor is a
composite over a `HoldsLeaves` child, so it is `HoldsLeaves` too, and its
`ValueTypeName` is `global::Expo.ModulesCore.ArrayBuffer?`.

The codec already has the short-circuit the brief asks for:

```text
Decode(nullish)      -> null
Decode(non-nullish)  -> ArrayBufferCodec.Decode
Encode(null)         -> runtime.CreateNull()
Encode(value)        -> ArrayBufferCodec.Encode
Release(null)        -> nothing
Release(value)       -> ArrayBufferCodec.Release
```

Composition follows from 030 with no generator change:

- A record field, list element, dictionary value, and `IReadOnlyList<T>?` or
  dictionary `?` content resolve their child through `GetCodec`, and a
  `HoldsLeaves` child makes the composite `HoldsLeaves`.
- `Nullable<T>` over a record struct with an `ArrayBuffer?` field selects
  `NullableCodec`.
- Generated glue declares the `ArrayBuffer?` local before the one `try` and
  releases it in `finally` through `CodecRelease`, as for `ArrayBuffer`. An
  async result travels in the same `StrongBox` to the Promise factory.
- A callback result or callback-argument tuple element uses the codec as a
  type argument, as `ArrayBuffer` does after 030.

`IsExcludedFromNullableReference` keeps `JavaScriptValue` and the two
callback types. The combined `is JavaScriptValueMetadataName or
ArrayBufferMetadataName` test becomes a `JavaScriptValue`-only test.

### Optional parameters

`ArrayBuffer?` has no constant default, so the only authored default is
`= null`. Omission and explicit `undefined` pass the authored default under
the existing optional-argument rule. Explicit `null` and, on a required
parameter, explicit `undefined` decode to `null`. The brief's "explicit null
with a non-null authored default" scenario cannot occur and has no test.

### Typed events

`TryGetEventDelegatePayload` already assigns `ExpoEventPayloadKind.ArrayBuffer`
to `Func<ArrayBuffer?, Task>`, because `SymbolDisplayFormat.FullyQualifiedFormat`
does not print the annotation. The generator change is in
`GetUnsupportedEventPayload`: only an annotated `JavaScriptValue` keeps the
"applies a nullable annotation to a transfer-sensitive wrapper" reason, at
any depth. An annotated `ArrayBuffer` is accepted at the top level and nested.

A nested `ArrayBuffer?` inside a record, list, dictionary, or `Nullable<T>`
payload goes through the payload codec, so it encodes as JavaScript `null` or
as the buffer. The author keeps every nested leaf, as for a nested
`ArrayBuffer` after 030.

Emission is unchanged. The generated lambda for the `ArrayBuffer` payload
kind is `value => emitter.EmitAsync(module, "name", value)`; with a
`Func<ArrayBuffer?, Task>` property, `value` is `ArrayBuffer?` and binds to
the nullable overload.

Both direct emitter overloads change their parameter type to `ArrayBuffer?`:

- `ModuleEventEmitter.EmitAsync(object module, string eventName,
ArrayBuffer? payload, CancellationToken cancellationToken = default)`.
  The `ArgumentNullException.ThrowIfNull(payload)` guard goes away.
- `GeneratedSharedObjectEvents.EmitAsync(DotnetRuntimeContext context,
SharedObject instance, string eventName, ArrayBuffer? payload)`.

A null payload schedules one dispatch whose payload is an explicit
JavaScript `null` created on the runtime (`runtime.CreateNull()`), through
the same one-payload `Emit` path as every other payload. It is not the
payload-less overload, so a listener receives one argument that is `null`.
Null retains no lease and touches no `ArrayBuffer` member. A non-null payload
keeps the current path byte for byte: retain the invocation lease
synchronously before the task is returned, encode on the runtime, and release
the lease once at a terminal state. Validation, cancellation, scheduling,
and exception behavior do not change.

This is a public-signature change on two overloads. An existing caller that
passed `null` got `ArgumentNullException` (module) or
`NullReferenceException` (shared object); it now dispatches a `null` payload.
No caller in the repo does that.

### Positions

| Position                                                             | `ArrayBuffer?` | Diagnostic if rejected |
| -------------------------------------------------------------------- | -------------- | ---------------------- |
| Sync and async method parameter, required or `= null`                | accepted       | —                      |
| Sync return and `Task<ArrayBuffer?>`                                 | accepted       | —                      |
| `[JS]` property, readable and writable                               | accepted       | —                      |
| Record field, at any depth                                           | accepted       | —                      |
| List element and dictionary value, at any depth                      | accepted       | —                      |
| `IReadOnlyList<ArrayBuffer?>?`, dictionary `?`                       | accepted       | —                      |
| Shared-object constructor parameter, method, property                | accepted       | —                      |
| Direct module or shared-object typed event payload                   | accepted       | —                      |
| Nested typed event payload (record, list, dictionary, `Nullable<T>`) | accepted       | —                      |
| Callback result or callback-argument tuple element                   | accepted       | —                      |
| `JavaScriptValue?` anywhere                                          | rejected       | unchanged per position |

### Generated output contracts

`GeneratorPreservesCodecPolicyDiagnosticContract` contains an `ArrayBuffer?`
member that today reports `EXPOJSI001`. After this change the member
compiles, so that digest and diagnostic list move. The plan re-pins it with
a one-line reason in the commit. No other digest moves.

### Intended changes

- `ExpoModulesGenerator.Codecs.cs`: `IsExcludedFromNullableReference` no
  longer names `ArrayBufferMetadataName`.
- `ExpoModulesGenerator.EventAnalysis.cs`: `GetUnsupportedEventPayload`
  rejects only an annotated `JavaScriptValue`.
- `ModuleEventEmitter.cs`, `SharedObjectEventEmitter.cs`: nullable direct
  overloads with the null branch above.
- Tests: generator acceptance per position and the rejections that stay;
  Hermes-backed module, shared-object, and event tests with lease counters.
- Docs: the merge below.

## Decisions made by the controller

1. **No new codec type.** `NullableReferenceCodec<ArrayBuffer,
ArrayBufferCodec>` already has the required short-circuit and `Release`.
   The brief's `NullableArrayBufferCodec` was its fallback. Cost if wrong:
   one small struct to add later.
2. **Direct emitter overloads become `ArrayBuffer?`** instead of a new
   method name. C# cannot overload on the annotation, and a second name
   would make generated emission branch on nullability for no gain. Cost if
   wrong: a caller that relied on the null guard now dispatches `null`.
3. **Null event payload is an explicit JavaScript `null` argument**, not the
   payload-less dispatch, so `Func<ArrayBuffer?, Task>` always delivers one
   argument. Cost if wrong: a listener sees `null` where it expected no
   argument; both are falsy.
4. **The "explicit null with non-null default" scenario is dropped** as not
   expressible for a reference type without a constant default.
5. **Digest re-pins are deliberate.** One diagnostic contract test
   includes an `ArrayBuffer?` fixture whose diagnostic disappears. The plan
   re-pins it once with a reason. Cost if wrong: a masked unrelated change
   in the same digest, which the per-position acceptance tests would still
   catch.
6. **Callback positions accept `ArrayBuffer?`**, because 030 accepts
   `ArrayBuffer` there and the codec is an ordinary `IJavaScriptCodec<T>`.
   The callback ownership rules apply: an argument stays with the author, a
   result belongs to the author, and null holds no leaf. Rejecting it would
   need a callback-only special case.
7. **Nested `ArrayBuffer?` event payloads are accepted**, because 030
   accepts nested `ArrayBuffer` and the payload codec composes. Keeping them
   rejected would need a top-level-only rule in the event check.

## Delta against `docs/specs/modules-core-boundary.md`

### MODIFIED Requirement: Ownership-Bearing Nullable References Are Build Diagnostics

Remove `ArrayBuffer` from the rejected list in the first paragraph. The
list becomes `JavaScriptValue`, `JavaScriptCallback<TResult>`,
`JavaScriptCallback<TArgs, TResult>`, `SharedObject`, `SharedRef<T>`, and a
concrete `[ExpoSharedObject]` class. Add one sentence: `ArrayBuffer?` is
supported through `NullableReferenceCodec` (see "Nullable ArrayBuffer Is A
Nullable Reference").

The typed-event paragraph becomes: a `Func<JavaScriptValue?, Task>` payload
bypasses codec resolution, so only the event payload check can report
`EXPOJSI019` or `EXPOJSI027` for it. A `Func<ArrayBuffer?, Task>` payload is
the direct `ArrayBuffer` payload kind plus nullability.

Scenario "Excluded nullable parameter or return reports the method codes":
`JavaScriptValue?` only; drop `ArrayBufferCodec` from the last line.

### MODIFIED Requirement: Unsupported Signatures Are Build Diagnostics

Scenario "Static helper codec is used as a generic codec argument": "or the
annotated type is `ArrayBuffer?` or `JavaScriptValue?`" becomes "or the
annotated type is `JavaScriptValue?`".

### MODIFIED Requirement: Invalid Typed Events Are Build Diagnostics

`EXPOJSI019` covers a nullable annotation on a `JavaScriptValue` at any
depth, not on an `ArrayBuffer`. The "Typed event payload is unsupported"
scenario names a nullable `JavaScriptValue` only.

### MODIFIED Requirement: Module-facing ArrayBuffer And Binary Codecs

Table row `ArrayBuffer`, constraints column: "Every codec position, including
list elements, dictionary values, callback types, and nested typed-event
payloads, each also as `ArrayBuffer?`".

### MODIFIED Requirement: Typed Event Payload Ownership Is Explicit

After the direct `ArrayBuffer` paragraph, add: for a direct `ArrayBuffer?`,
a null payload SHALL dispatch one explicit JavaScript `null` argument through
the same payload path, SHALL retain no lease, and SHALL NOT use the
payload-less dispatch. A non-null payload SHALL follow the direct
`ArrayBuffer` rule unchanged. The nested-leaf paragraph names
`ArrayBuffer?` next to `ArrayBuffer`; a nested null encodes as JavaScript
`null`.

#### Scenario: Direct nullable ArrayBuffer event delivers null without a lease

- **GIVEN** a module or shared-object typed event declares
  `Func<ArrayBuffer?, Task>`
- **WHEN** authored code invokes it with `null`
- **THEN** the listener SHALL receive exactly one argument that is
  JavaScript `null`
- **AND** `LongLivedArrayBuffersReleased` SHALL NOT change
- **WHEN** authored code invokes it with a buffer and disposes the buffer
  after the call returns
- **THEN** dispatch SHALL still deliver the bytes through its retained lease
- **AND** the lease SHALL release exactly once

### ADDED Requirement: Nullable ArrayBuffer Is A Nullable Reference

`ArrayBuffer?` SHALL resolve to
`NullableReferenceCodec<ArrayBuffer, ArrayBufferCodec>` on every position
where `ArrayBuffer` is accepted. JavaScript `null` and `undefined` SHALL
decode to C# `null` without a lease. C# `null` SHALL encode as JavaScript
`null`. A non-null value SHALL follow the `ArrayBuffer` rules unchanged:
decode gives the leaf to generated glue, which releases it once through
`Release`, and `Encode` leaves the source usable. The generator SHALL NOT add
a nullable-specific cleanup path.

#### Scenario: Nullish parameter holds no leaf

- **GIVEN** a generated method declares an `ArrayBuffer?` parameter
- **WHEN** JavaScript passes `null` or `undefined`
- **THEN** authored code SHALL receive C# `null`
- **AND** `LongLivedArrayBuffersReleased` SHALL NOT change

#### Scenario: Non-null parameter releases once

- **GIVEN** the same method
- **WHEN** JavaScript passes a JavaScript-created buffer to a sync method,
  and to an async method that awaits before reading it
- **THEN** authored code SHALL read the buffer, after the await on the async
  path
- **AND** each call SHALL release the lease exactly once

#### Scenario: Optional nullable buffer uses the authored default

- **GIVEN** a generated method declares `ArrayBuffer? value = null`
- **WHEN** JavaScript omits the argument, passes `undefined`, or passes
  `null`
- **THEN** authored code SHALL receive C# `null` in all three cases
- **AND** no lease SHALL be retained

#### Scenario: Nullable return and property encode null or the buffer

- **GIVEN** a sync method, a `Task<ArrayBuffer?>` method, and a readable and
  writable `ArrayBuffer?` property
- **WHEN** authored code returns or stores `null`
- **THEN** JavaScript SHALL receive `null`
- **WHEN** authored code returns a buffer
- **THEN** JavaScript SHALL read its bytes
- **AND** the returned leaf SHALL release exactly once after encode
- **WHEN** JavaScript assigns a buffer to the property and authored code
  stores a `Retain()` copy
- **THEN** the setter's decoded leaf SHALL release once and the stored copy
  SHALL stay readable until the module releases it

#### Scenario: Nullable buffer inside a record and a collection

- **GIVEN** a record with an `ArrayBuffer?` field and an
  `IReadOnlyList<ArrayBuffer?>` parameter
- **WHEN** JavaScript passes `null` leaves next to buffer leaves
- **THEN** null leaves SHALL release nothing and buffer leaves SHALL
  release exactly once each
- **WHEN** a later field decode fails after a non-null buffer field decoded
- **THEN** the earlier lease SHALL release exactly once

#### Scenario: Shared-object members accept nullable buffers

- **GIVEN** a shared-object `[JS]` constructor parameter, method parameter,
  and property of type `ArrayBuffer?`
- **WHEN** JavaScript passes `null` and a buffer
- **THEN** they SHALL behave as the module positions above

#### Scenario: Nullable buffer in a callback and a nested event payload

- **GIVEN** a callback whose argument and result are `ArrayBuffer?`, and a
  typed event whose payload is a list of `ArrayBuffer?`
- **WHEN** authored code passes `null` and a buffer
- **THEN** JavaScript SHALL receive `null` and the buffer
- **AND** the author's buffer SHALL stay usable, and the author SHALL own a
  non-null callback result

#### Scenario: JavaScriptValue stays excluded

- **GIVEN** a generated member declares `JavaScriptValue?` next to an
  `ArrayBuffer?` member
- **WHEN** the generator runs
- **THEN** it SHALL report the member's existing diagnostic for
  `JavaScriptValue?` and no diagnostic for `ArrayBuffer?`

## Delta against `docs/specs/ownership-and-scoped-refs.md`

The "Boundary Leaf Ownership Is Explicit" requirement gains one sentence: a
decoded `ArrayBuffer?` that is `null` holds no leaf, and its `Release` does
nothing.

## Module authoring guide

- Binary data: `ArrayBuffer?` works wherever `ArrayBuffer` works.
- Unsupported annotated references list: drop `ArrayBuffer?`.
- Typed events section: `Func<ArrayBuffer?, Task>` delivers `null` as one
  `null` argument.

## Roadmap

Row 031 becomes `IN PROGRESS` with this spec and `DONE` at close. The
028–033 note gains a sentence for 031 with the test count. The brief and this
change folder leave git at close; git history keeps them.

## Verification

```sh
scripts/test-managed.sh
scripts/format.sh --check --all
git diff --check
rg -n 'JavaScriptValueMetadataName or ArrayBufferMetadataName|ArrayBuffer.*nullable annotation' \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator docs/module-authoring-guide.md
git diff --unified=0 e543159..HEAD -- docs packages | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\'
```

The exclusion scan and the privacy scan print nothing.
