# Nullable JavaScript Callback Parameters

## Goal

`JavaScriptCallback<TResult>?` and `JavaScriptCallback<TArgs, TResult>?`
work as parameters of generated module methods and of supported
shared-object constructors and methods. JavaScript `null` and `undefined`
decode to C# `null` without touching the function or the runtime context. A
non-null function decodes through the existing callback codec and keeps its
context-retained lifetime. This is roadmap item 032.

Callbacks are decode-only and owned by `DotnetRuntimeContext`. A callback is
never a leaf holder, so generated glue never releases or disposes it. Every
position other than a parameter stays rejected, for the nullable form exactly
as for the non-null form. The argument and result types follow the non-null
rule, so they can hold `JavaScriptValue` and `ArrayBuffer` leaves.

## Process note

The brief `docs/plans/032-nullable-javascript-callback-parameters.md`
predates 028–031. Its drift check diffs by design. This spec is reconciled
against HEAD `9f69a3d` (the 031 close) and wins over the brief where they
differ.

## Design criteria

1. **Clean code.** One null test in one place. Same call shape as the
   non-null codec, so emission does not change.
2. **Performance.** Null allocates nothing and registers nothing.
3. **Correctness.** One owner for callbacks: `DotnetRuntimeContext`. Null
   never reaches `AsFunction` or the context.

## Scope

### In scope

- `Expo.ModulesCore/Codecs/NullableJavaScriptCallbackCodec.cs` (new).
- `Expo.ModulesCore/DotnetRuntimeContext.cs`: one `internal` read-only count
  of retained callbacks, for tests.
- `Expo.ModulesCore.Generator/ExpoModulesGenerator.Codecs.cs`.
- `Expo.ModulesCore.Generator.Tests` and `Expo.ModulesCore.Tests`.
- `docs/specs/modules-core-boundary.md`,
  `docs/specs/ownership-and-scoped-refs.md`, `docs/module-authoring-guide.md`,
  `docs/roadmap.md`.

### Out of scope

- Callback returns, `Task<JavaScriptCallback<...>?>`, properties, record
  fields, lists, dictionaries, tuples, typed event payloads. They stay
  rejected as today.
- Nullable callback argument or result types inside another callback.
- Callback encode. Any change to `JavaScriptCallback.cs` beyond none.
- The callback argument consumption backlog item (P2).

## Accepted design

### Codec

One new file with two static classes that mirror `JavaScriptCallbackCodec`:

```csharp
public static class NullableJavaScriptCallbackCodec<TResult, TResultCodec>
    where TResultCodec : IJavaScriptCodec<TResult>
{
  public static JavaScriptCallback<TResult>? Decode(
      JavaScriptValueRef value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
      value.IsNullish ? null : JavaScriptCallbackCodec<TResult, TResultCodec>.Decode(value, runtime, context);
}

public static class NullableJavaScriptCallbackCodec<TArgs, TArgsCodec, TResult, TResultCodec>
    where TArgs : struct
    where TArgsCodec : IJavaScriptArgsCodec<TArgs>
    where TResultCodec : IJavaScriptCodec<TResult>
{
  public static JavaScriptCallback<TArgs, TResult>? Decode(
      JavaScriptValueRef value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
      value.IsNullish ? null : JavaScriptCallbackCodec<TArgs, TArgsCodec, TResult, TResultCodec>.Decode(value, runtime, context);
}
```

Same `Decode(ref, runtime, context)` shape, so `GetDecodeExpression` emits
the same call text with a different class name. No `Encode`.

### Descriptor and resolution

`IsExcludedFromNullableReference` no longer names the callback types; it
keeps `JavaScriptValue` only. In `TryGetNullableReferenceCodec`, after the
exclusion test, an annotated callback type resolves through
`TryGetJavaScriptCallbackCodec` with the nullable class name. The helper
takes the codec class name as a parameter (`JavaScriptCallbackCodec` or
`NullableJavaScriptCallbackCodec`); its argument and result resolution does
not change. The descriptor is the existing `CallbackCodec` row:
`StaticHelper`, `DecodeOnly`, `DotnetRuntimeContext`, `ContextRetained`.

That row is what keeps every other position rejected with no new code:

- return and async result: `DecodeOnly` → `EXPOJSI002`;
- property: `ContainsJavaScriptCallback` on the property type → `EXPOJSI015`;
- record field: the record codec requires `DecodeAndEncode` and no context
  requirement, which the callback descriptor fails → `EXPOJSI007`;
- list element, dictionary value, `Nullable<T>` inner, callback type
  argument: `AsCompositeChildCodec` rejects `StaticHelper` → the member's
  diagnostic;
- typed event payload: `GetUnsupportedEventPayload` → `EXPOJSI019` /
  `EXPOJSI027`;
- shared-object class: the same results reported as `EXPOJSI023`.

An annotated callback whose argument or result codec is invalid reports
`EXPOJSI008`, because the parameter diagnostic selector uses
`IsJavaScriptCallbackType`, which reads `OriginalDefinition`.

### Semantics

- JS `null` or `undefined` → C# `null`. No `AsFunction`, no
  `RegisterRetainedCallback`, no `Release`.
- A non-null, non-function value throws from `AsFunction` as today.
- Non-null function → the existing callback codec, unchanged. The callback
  stays registered with the context until explicit `Dispose` or context
  teardown; generated glue never disposes it.
- Optional `= null`: omission and `undefined` pass the authored default
  (`null`); explicit `null` passes `null`.

### Test seam

`DotnetRuntimeContext` gets `internal int RetainedCallbackCount` (reads
`retainedCallbacks.Count` under `gate`). The test assembly already has
`InternalsVisibleTo`. It is the only deterministic proof that the null
branch registers nothing; it is internal, not public API.

## Decisions made by the controller

1. **Two static classes, not a generic wrapper.** The callback helper is a
   static class with a context parameter and does not implement
   `IJavaScriptCodec<T>`, so `NullableReferenceCodec<T, TCodec>` cannot wrap
   it. Cost if wrong: none; the brief asks for this shape.
2. **Class-name parameter on `TryGetJavaScriptCallbackCodec`** instead of a
   second resolver. Cost if wrong: one string parameter.
3. **Internal count on the context** for the zero-registration proof. The
   brief forbids public API for a counter; `internal` with the existing
   `InternalsVisibleTo` is not public. Cost if wrong: one internal property
   to remove.
4. **No position widening.** Nullable callbacks are parameters only, like
   non-null callbacks.
5. **Leaf-bearing type arguments are accepted.** The non-null callback
   accepts `JavaScriptValue` and `ArrayBuffer` at any depth in its argument
   and result types, and the nullable codec reuses its resolver, so
   `JavaScriptCallback<ValueTuple<ArrayBuffer>, ArrayBuffer>?` works with no
   special case. A generator theory row and a Hermes test pin it. Cost if
   wrong: one rule to add back, with no reason the non-null form lacks.

## Delta against `docs/specs/modules-core-boundary.md`

### MODIFIED Requirement: Ownership-Bearing Nullable References Are Build Diagnostics

Remove `JavaScriptCallback<TResult>` and `JavaScriptCallback<TArgs, TResult>`
from the rejected list. Add: a nullable callback is supported as a method or
constructor parameter (see "Nullish Callback Parameters Decode To Null
Without Retention") and keeps the non-null rejection on every other position.
Replace scenario "Nullable callback reports the callback code" with:

#### Scenario: Nullable callback outside a parameter keeps its diagnostic

- **GIVEN** a generated return, async result, `[JS]` property, record
  field, list element, dictionary value, or typed event payload uses an
  annotated `JavaScriptCallback<...>?`
- **WHEN** the generator analyzes the member
- **THEN** it SHALL report the same diagnostic as the non-null form
- **AND** it SHALL NOT emit the member

### ADDED Requirement: Nullish Callback Parameters Decode To Null Without Retention

`JavaScriptCallback<TResult>?` and `JavaScriptCallback<TArgs, TResult>?`
SHALL be accepted as parameters of generated module methods, sync and async,
and of supported shared-object constructors and methods, including with an
authored `= null` default. They SHALL resolve to
`NullableJavaScriptCallbackCodec<...>` with the same type arguments as the
non-null codec, `DecodeOnly`, requiring `DotnetRuntimeContext`, and
`ContextRetained`. JavaScript `null` and `undefined` SHALL decode to C#
`null` without calling `AsFunction` and without registering a callback with
the context. A non-null function SHALL decode through the non-null codec
unchanged. A nullable callback is never a leaf holder: generated glue SHALL
NOT release or dispose it at invocation end. Its argument and result types
follow the non-null rule, so they can hold leaves at any depth.

#### Scenario: Nullish callback reaches authored code as null

- **GIVEN** a method with a required `JavaScriptCallback<int>?` parameter
  and a method with an optional
  `JavaScriptCallback<ValueTuple<string>, string>? cb = null` parameter
- **WHEN** JavaScript passes `null`, passes `undefined`, or omits the
  optional argument
- **THEN** authored code SHALL receive C# `null`
- **AND** the context's retained callback count SHALL NOT change

#### Scenario: Non-function non-null value is rejected

- **GIVEN** the required nullable callback parameter
- **WHEN** JavaScript passes a number
- **THEN** the call SHALL throw a JavaScript error from the function check
- **AND** the retained callback count SHALL NOT change

#### Scenario: Non-null callback keeps context-retained lifetime

- **GIVEN** the same parameters
- **WHEN** JavaScript passes a function
- **THEN** authored code SHALL invoke it synchronously and after the method
  returns, including after an `await` on an async method
- **AND** the retained callback count SHALL increase by one per decode
- **AND** explicit `Dispose` SHALL release once, and context teardown SHALL
  release an undisposed callback once

#### Scenario: Leaf-bearing nullable callback follows the leaf rules

- **GIVEN** a `JavaScriptCallback<ValueTuple<ArrayBuffer>, ArrayBuffer>?`
  parameter
- **WHEN** JavaScript passes `null`, or a function that returns a new buffer
- **THEN** authored code SHALL receive C# `null` for `null`
- **AND** for a function, the buffer the author passes SHALL stay the
  author's, and the returned buffer SHALL be the author's to dispose

#### Scenario: Shared-object parameters accept nullable callbacks

- **GIVEN** a shared-object `[JS]` constructor and method with a nullable
  callback parameter
- **WHEN** JavaScript passes `null` and a function
- **THEN** they SHALL behave as the module positions

## Delta against `docs/specs/ownership-and-scoped-refs.md`

The file has no unsupported-nullable list, so nothing is removed. Add one
sentence: a nullable callback parameter that decodes to `null` holds
nothing; a non-null one is context-retained, never a leaf holder.

## Module authoring guide

- Callbacks bullet: `JavaScriptCallback<...>?` is accepted as a parameter
  and decodes `null`/`undefined` to `null`.
- Unsupported annotated references list: drop `JavaScriptCallback<...>?`.

## Roadmap

Row 032 `IN PROGRESS` with this spec, `DONE` at close with the test count
in the 028–033 note. The brief and the change folder leave the repo at close;
git history keeps them.

## Verification

```sh
scripts/test-managed.sh
scripts/format.sh --check --all
git diff --check
rg -n 'CodecRelease[^\n]*JavaScriptCallback' \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator
git diff --unified=0 9f69a3d..HEAD -- docs packages | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\'
```

Both scans print nothing.
