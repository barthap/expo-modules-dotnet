# Nullable ArrayBuffer Codecs

## Goal

`ArrayBuffer?` works on every generated boundary where `ArrayBuffer` works
today. JavaScript `null` and `undefined` decode to C# `null`, C# `null`
encodes as JavaScript `null`, and a non-null buffer follows the same
conversion scope as `ArrayBuffer`. This is roadmap item 031.

Plan 026 rejected `ArrayBuffer?` because cleanup did not compose. Plan 030
added the conversion scope and the scoped nullable-reference codec. This
change removes only the `ArrayBuffer` part of the plan-026 exclusion.
`JavaScriptValue?` stays rejected by design, because `JavaScriptValue`
already represents and inspects JavaScript `null` and `undefined`.

## Process note

The brief `docs/plans/031-nullable-arraybuffer-codecs.md` predates 028, 029,
and 030. Its drift check diffs by design. This spec is reconciled against
HEAD `e5fdcfc` and wins over the brief where they differ.

## Design criteria

1. **Clean code.** No new codec type when an existing one fits. One
   execution order per path. No special case with hidden data flow.
2. **Performance.** Null registers nothing and allocates nothing. Non-null
   keeps the 030 cost exactly.
3. **Correctness.** No leaks. The conversion scope stays the only owner of a
   decoded buffer. Null never reaches the registry, the scope, or a lease.

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
- `ArrayBuffer?` as a callback result or callback-argument tuple element. A
  non-null `ArrayBuffer` is rejected there today (030 decision 5), and the
  nullable form follows the non-null rule.
- Nested leaves in typed event payloads. They stay rejected (030).
- Any change to `ArrayBuffer`, `GeneratedConversionScope`, the scoped
  composite codecs, the ABI, the scheduler, or platform code.

## Accepted design

### Codec: the existing scoped nullable-reference codec

No new runtime type. `ArrayBuffer?` resolves to

```text
GeneratedScopedNullableReferenceCodec<global::Expo.ModulesCore.ArrayBuffer, ArrayBufferCodec>
```

through the fall-through at the end of `TryGetNullableReferenceCodec`, once
`ArrayBufferMetadataName` leaves `IsExcludedFromNullableReference`.
`ArrayBufferCodec` already implements `IGeneratedScopedCodec<ArrayBuffer>`,
so the constraint `TCodec : IGeneratedScopedCodec<T>` holds. The descriptor
is the `ScopedCodec` row: `ScopedOnly` shape, `ScopedResource` ownership.

The codec already has the exact short-circuit the brief asks for:

```text
Decode(nullish)       -> null, no registration
Decode(non-nullish)   -> ArrayBufferCodec.Decode(ref, runtime, scope)
Transfer(null)        -> nothing
Transfer(non-null)    -> ArrayBufferCodec.Transfer(value, scope)
EncodeBorrowed(null)  -> runtime.CreateNull()
EncodeBorrowed(value) -> ArrayBufferCodec.EncodeBorrowed(value, runtime)
```

Composition follows from 030 with no generator change:

- A record field, list element, dictionary value, and `IReadOnlyList<T>?` or
  dictionary `?` content resolve their child through `GetCodec`, and a
  `ScopedOnly` child selects the scoped composite codec.
- `Nullable<T>` over a record struct with an `ArrayBuffer?` field selects
  `GeneratedScopedNullableCodec`.
- A callback result or callback-argument element goes through
  `AsJavaScriptCodecArgument`, which rejects a `ScopedOnly` descriptor, so
  `ArrayBuffer?` stays rejected there with `EXPOJSI008`, like `ArrayBuffer`.

`IsExcludedFromNullableReference` keeps `JavaScriptValue` and the two
callback types. The combined `is JavaScriptValueMetadataName or
ArrayBufferMetadataName` test becomes a `JavaScriptValue`-only test.

### Optional parameters

`ArrayBuffer?` has no constant default, so the only authored default is
`= null`. Omission and explicit `undefined` pass the authored default under
the existing optional-argument rule. Explicit `null` and, on a required
parameter, explicit `undefined` decode to `null`. The brief's "explicit null
with a non-null authored default" scenario cannot occur and has no test.

### Direct typed events

`TryGetEventDelegatePayload` already assigns `ExpoEventPayloadKind.ArrayBuffer` to
`Func<ArrayBuffer?, Task>`, because `SymbolDisplayFormat.FullyQualifiedFormat`
does not print the annotation. The only generator change is in
`GetUnsupportedEventPayload`: at the top level, an annotated `ArrayBuffer`
returns `null` (accepted), and an annotated `JavaScriptValue` keeps the
"applies a nullable annotation to a transfer-sensitive wrapper" reason. The
nested branch is unchanged: a nested `JavaScriptValue`, `ArrayBuffer`, or
`ArrayBuffer?` inside a record, list, dictionary, or `Nullable<T>` payload
stays rejected with `EXPOJSI019` or `EXPOJSI027`.

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

| Position                                                             | `ArrayBuffer?` | Diagnostic if rejected      |
| -------------------------------------------------------------------- | -------------- | --------------------------- |
| Sync and async method parameter, required or `= null`                | accepted       | —                           |
| Sync return and `Task<ArrayBuffer?>`                                 | accepted       | —                           |
| `[JS]` property, readable and writable                               | accepted       | —                           |
| Record field, at any depth                                           | accepted       | —                           |
| List element and dictionary value, at any depth                      | accepted       | —                           |
| `IReadOnlyList<ArrayBuffer?>?`, dictionary `?`                       | accepted       | —                           |
| Shared-object constructor parameter, method, property                | accepted       | —                           |
| Direct module or shared-object typed event payload                   | accepted       | —                           |
| Nested typed event payload (record, list, dictionary, `Nullable<T>`) | rejected       | `EXPOJSI019` / `EXPOJSI027` |
| Callback result or callback-argument tuple element                   | rejected       | `EXPOJSI008`                |
| `JavaScriptValue?` anywhere                                          | rejected       | unchanged per position      |

### Generated output contracts

`GeneratorPreservesCodecPolicyDiagnosticContract` contains an `ArrayBuffer?`
member that today reports `EXPOJSI001`. After this change the member
compiles, so that digest and diagnostic list move. The plan re-pins it with
a one-line reason in the commit. No other digest moves.

### Intended changes

- `ExpoModulesGenerator.Codecs.cs`: `IsExcludedFromNullableReference` no
  longer names `ArrayBufferMetadataName`.
- `ExpoModulesGenerator.EventAnalysis.cs`: `GetUnsupportedEventPayload`
  accepts a top-level annotated `ArrayBuffer`.
- `ModuleEventEmitter.cs`, `SharedObjectEventEmitter.cs`: nullable direct
  overloads with the null branch above.
- Tests: generator acceptance per position and the rejections that stay;
  Hermes-backed module, shared-object, and event tests with lease counters.
- Docs: the merge below.

## Decisions made by the controller

1. **No new codec type.** `GeneratedScopedNullableReferenceCodec<ArrayBuffer,
ArrayBufferCodec>` already has the required short-circuit, and 030 built
   it for this. The brief's `NullableArrayBufferScopedCodec` was its
   fallback. Cost if wrong: one small struct to add later.
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
6. **Callback positions stay rejected** for `ArrayBuffer?`, because the
   non-null form is rejected there (030 decision 5). Cost if wrong: none;
   032 does not need them either.

## Delta against `docs/specs/modules-core-boundary.md`

### MODIFIED Requirement: Ownership-Bearing Nullable References Are Build Diagnostics

Remove `ArrayBuffer` from the rejected list in the first paragraph. The
list becomes `JavaScriptValue`, `JavaScriptCallback<TResult>`,
`JavaScriptCallback<TArgs, TResult>`, `SharedObject`, `SharedRef<T>`, and a
concrete `[ExpoSharedObject]` class. Add one sentence: `ArrayBuffer?` is
supported through the scoped nullable-reference codec (see "Nullable
ArrayBuffer Is A Scoped Nullable Reference").

The typed-event paragraph becomes: a `Func<JavaScriptValue?, Task>` payload
bypasses codec resolution, so only the event payload check can report
`EXPOJSI019` or `EXPOJSI027` for it. A `Func<ArrayBuffer?, Task>` payload is
the direct `ArrayBuffer` payload kind plus nullability.

Scenario "Excluded nullable parameter or return reports the method codes":
`JavaScriptValue?` only; drop `ArrayBufferCodec` from the last line.

### MODIFIED Requirement: Unsupported Signatures Are Build Diagnostics

Scenario "Static helper codec is used as a generic codec argument": remove
"or the annotated type is `ArrayBuffer?` or `JavaScriptValue?`" and add "or
the annotated type is `JavaScriptValue?`". A callback result or
callback-argument tuple element that is `ArrayBuffer?` is covered by the
existing `ArrayBuffer` wording, because the nullable form follows the
non-null rule.

### MODIFIED Requirement: Generated Bindings Compose Leaves Through One Scope

Composition table, row "`Nullable<T>` inner, nullable-reference inner":
replace "`ArrayBuffer?` and `JavaScriptValue?` stay excluded before
resolution" with "`JavaScriptValue?` stays excluded before resolution.
`ArrayBuffer?` resolves to `GeneratedScopedNullableReferenceCodec<ArrayBuffer,
ArrayBufferCodec>`."

### MODIFIED Requirement: Module-facing ArrayBuffer And Binary Codecs

Table row `ArrayBuffer`, constraints column: "Direct parameters, returns,
properties, record fields, list elements, dictionary values, and direct
typed-event payloads, each also as `ArrayBuffer?`; not a callback type
argument or a nested typed-event payload".

### MODIFIED Requirement: Typed Event Payload Ownership Is Explicit

After the direct `ArrayBuffer` paragraph, add: for a direct `ArrayBuffer?`,
a null payload SHALL dispatch one explicit JavaScript `null` argument through
the same payload path, SHALL retain no lease, and SHALL NOT use the
payload-less dispatch. A non-null payload SHALL follow the direct
`ArrayBuffer` rule unchanged.

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

### ADDED Requirement: Nullable ArrayBuffer Is A Scoped Nullable Reference

`ArrayBuffer?` SHALL resolve to
`GeneratedScopedNullableReferenceCodec<ArrayBuffer, ArrayBufferCodec>` on
every position where `ArrayBuffer` is accepted, and SHALL be rejected with
the same diagnostic as `ArrayBuffer` on every position where `ArrayBuffer`
is rejected. JavaScript `null` and `undefined` SHALL decode to C# `null`
without a lease and without a scope registration. C# `null` SHALL encode as
JavaScript `null`. A non-null value SHALL follow the `ArrayBuffer` scope
rules unchanged: decode registers the lease in the invocation scope,
`Transfer` registers a returned lease, `EncodeBorrowed` leaves the source
usable. The generator SHALL NOT add a nullable-specific cleanup path.

#### Scenario: Nullish parameter registers nothing

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

#### Scenario: Nullable return and property encode null or transfer

- **GIVEN** a sync method, a `Task<ArrayBuffer?>` method, and a readable and
  writable `ArrayBuffer?` property
- **WHEN** authored code returns or stores `null`
- **THEN** JavaScript SHALL receive `null`
- **WHEN** authored code returns a buffer
- **THEN** JavaScript SHALL read its bytes
- **AND** the returned lease SHALL release exactly once after encode
- **WHEN** JavaScript assigns a buffer to the property and authored code
  stores a `Retain()` copy
- **THEN** the setter's decoded lease SHALL release once and the stored copy
  SHALL stay readable until the module releases it

#### Scenario: Nullable buffer inside a record and a collection

- **GIVEN** a record with an `ArrayBuffer?` field and an
  `IReadOnlyList<ArrayBuffer?>` parameter
- **WHEN** JavaScript passes `null` leaves next to buffer leaves
- **THEN** null leaves SHALL register nothing and buffer leaves SHALL
  release exactly once each
- **WHEN** a later field decode fails after a non-null buffer field decoded
- **THEN** the earlier lease SHALL release exactly once

#### Scenario: Shared-object members accept nullable buffers

- **GIVEN** a shared-object `[JS]` constructor parameter, method parameter,
  and property of type `ArrayBuffer?`
- **WHEN** JavaScript passes `null` and a buffer
- **THEN** they SHALL behave as the module positions above

#### Scenario: JavaScriptValue stays excluded

- **GIVEN** a generated member declares `JavaScriptValue?` next to an
  `ArrayBuffer?` member
- **WHEN** the generator runs
- **THEN** it SHALL report the member's existing diagnostic for
  `JavaScriptValue?` and no diagnostic for `ArrayBuffer?`

#### Scenario: Nested nullable buffer in a typed event stays rejected

- **GIVEN** a typed event declares `Func<Record, Task>` where the record has
  an `ArrayBuffer?` field, or `Func<IReadOnlyList<ArrayBuffer?>, Task>`
- **WHEN** the generator runs
- **THEN** it SHALL report `EXPOJSI019` (module) or `EXPOJSI027`
  (shared object) with the nested transfer-sensitive wrapper reason

## Delta against `docs/specs/ownership-and-scoped-refs.md`

The "Boundary Leaf Ownership Is Explicit" requirement gains one sentence: a
decoded `ArrayBuffer?` that is `null` holds no leaf and registers nothing.

## Module authoring guide

- Binary data bullet: `ArrayBuffer` and `ArrayBuffer?` work directly, as a
  record field, a list element, a dictionary value, and a direct typed-event
  payload; neither works as a callback type argument or a nested typed-event
  payload.
- Unsupported annotated references list: drop `ArrayBuffer?`.
- Typed events section, if it lists payload kinds: `Func<ArrayBuffer?, Task>`
  delivers `null` as one `null` argument.

## Roadmap

Row 031 becomes `IN PROGRESS` with this spec and `DONE` at close. The
028–033 note gains a sentence for 031 with the test count. The brief moves to
`docs/archive/plans/`. The change folder moves to `docs/archive/changes/`.

## Verification

```sh
scripts/test-managed.sh
scripts/format.sh --check --all
git diff --check
rg -n 'ArrayBuffer.*(unsupported nullable|nullable annotation)|IsNullable.*ArrayBuffer|JavaScriptValueMetadataName or ArrayBufferMetadataName' \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator docs/module-authoring-guide.md
git diff --unified=0 e5fdcfc..HEAD -- docs packages | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\'
```

The exclusion scan prints exactly one line, the
`JavaScriptValueMetadataName or ArrayBufferMetadataName` test in
`ExpoModulesGenerator.EventAnalysis.cs` that the nested-wrapper branch still
needs, and nothing from `ExpoModulesGenerator.Codecs.cs` or the guide. The
privacy scan prints nothing.
