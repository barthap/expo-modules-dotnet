# Base-Class Shared-Object Arguments

## Goal

An authored abstract class that derives from `SharedObject` works as a
parameter type of generated module methods and of supported shared-object
constructors and methods. JavaScript passes an instance of any generated
sealed `[ExpoSharedObject]` subclass; authored code receives the original
managed instance typed as the base. Every other position, the nullable form,
and the `SharedObject` and `SharedRef<T>` bases stay rejected. This is
roadmap item 034. It unblocks `copy(to: FileSystemPath)` and
`move(to: FileSystemPath)` in roadmap item 038.

## Process note

The brief `docs/plans/034-base-class-shared-object-arguments.md` was written
at the 033 close (`d9af9c6`) and this spec follows it, with one change: the
brief asked the generator to require at least one generated subclass in the
compilation. Boundary analysis runs per module and per shared-object class
before the incremental pipeline collects the shared-object models, so that
check has no cheap home. The spec drops it (decision 1).

## Design criteria

1. **Clean code.** One new decode-only static helper, one explicit flag on
   the boundary validator that says "this position is a parameter", and no
   assignability anywhere near the exact codec or prototype selection.
2. **Performance.** Decode is the existing registry resolution plus one
   managed `is` test. No reflection, no scan.
3. **Correctness.** The registry only ever holds instances of generated
   sealed classes, so `managed is TBase` is a complete check. Foreign
   context, released, plain object, and wrong-class failures are the
   registry's and the `is` test's, unchanged.

## Scope

### In scope

- `Expo.ModulesCore/Codecs/SharedObjectBaseCodec.cs` (new).
- `Expo.ModulesCore.Generator/ExpoModulesGenerator.SharedObjectValidation.cs`
  and the five call sites of `TryAnalyzeSharedObjectBoundaryType` in
  `ExpoModulesGenerator.ModuleAnalysis.cs` and
  `ExpoModulesGenerator.SharedObjectModel.cs`.
- `Expo.ModulesCore.Generator.Tests` and `Expo.ModulesCore.Tests`.
- `docs/specs/modules-core-boundary.md`, `docs/module-authoring-guide.md`,
  `docs/roadmap.md`.

### Out of scope

- `packages/expo-file-system-dotnet` members (`copy`, `move`): item 038.
- A base as a return, `Task<T>` result, or property type: encode needs an
  exact prototype.
- A nullable base (`PathBase?`): 038 does not need it. If a module asks for
  it, it is one more helper; it goes to the unplanned backlog.
- Nested positions (lists, dictionaries, records, callbacks, typed events).
- Any change to `SharedObject`, `SharedRef<T>`, `SharedObjectRegistry`,
  `GeneratedSharedObjectClass`, `SharedObjectCodec<T>`, or
  `NullableSharedObjectCodec<T>`.

## Accepted design

### Codec

```csharp
public static class SharedObjectBaseCodec<TBase> where TBase : SharedObject
{
  public static TBase Decode(JavaScriptValueRef value, JavaScriptRuntime runtime, DotnetRuntimeContext runtimeContext);
  public static TBase Decode(JavaScriptValue value, JavaScriptRuntime runtime, DotnetRuntimeContext runtimeContext);
}
```

Both overloads mirror `SharedObjectCodec<T>.Decode` (argument checks,
`Retain` for the ref overload, `AsObject`, `ResolveManaged`) and then
require `managed is TBase instance`; otherwise they throw
`InvalidOperationException` with the message
`The shared object receiver is not a '{typeof(TBase)}' instance.`, the
exact codec's wording. No `Encode` and no `Release`. The managed instance
stays caller-owned and registry-owned, never a leaf holder.

### Generator

`TryAnalyzeSharedObjectBoundaryType` gains a required `bool isParameter`
argument. The two parameter call sites (module method parameters in
`ModuleAnalysis.cs` and `[JS]` constructor parameters in
`SharedObjectModel.cs`) pass `true`; the return, async result, and property
call sites pass `false`. Shared-object methods and properties reach the
module call sites, so they inherit the same flag.

`GetDirectSharedObjectBoundaryIssue` keeps its first two rules
(`SharedObject`, `SharedRef<T>`). Its attribute rule splits:

- marked `[ExpoSharedObject]`: the existing rules (top-level, non-generic,
  sealed) and, since 033, the annotation selects the nullable codec;
- not marked and `IsAbstract`: the type is an **authored base**. It SHALL
  be accepted only when `isParameter` is true, the annotation is not
  `Annotated`, and it is top-level and non-generic. The codec expression is
  `SharedObjectBaseCodec<Exact>` with the descriptor row `StaticHelper`,
  `DecodeOnly`, `Decode`, `DotnetRuntimeContext`, `Borrowed`. Otherwise it
  reports `EXPOJSI023` with one of these reasons:
  - `which is an abstract shared-object base and is supported only as a parameter`
  - `which is an abstract shared-object base and must be used without a nullable annotation`
  - `which is an abstract shared-object base and must be a top-level, non-generic class`
- not marked and not abstract: the existing reason
  `which is not marked [ExpoSharedObject]`.

No descriptor field is added. The `DecodeOnly` row also keeps every nested
position rejected through the existing `StaticHelper` filters, and the
nested-shape path (`TryFindNestedSharedObjectType`) does not change: a base
inside a list, dictionary, record, callback, or typed event payload reports
the existing composed-codec reason.

Emission does not change: the parameter decode text is
`SharedObjectBaseCodec<global::...Base>.Decode(arguments.GetValue(i), runtime, GeneratedFunction.CurrentRuntimeContext)`,
and the optional-argument form follows the existing default-branch text.

### Semantics

- JavaScript passes an instance of any generated sealed subclass of the
  base, including a JavaScript subclass of that generated class: authored
  code receives the original managed instance. Repeated calls receive the
  same instance.
- A plain object, a primitive, `null`, `undefined`, a released instance, an
  instance owned by another context, or an instance of a shared class that
  does not derive from the base fails before authored code runs, with the
  registry's or the base codec's existing message.
- Omitting a required base argument fails the argument-count check.
- An optional base parameter can only default to `null`, and `null` is
  rejected by the codec, so an optional base parameter is not a supported
  shape in practice; the generator does not special-case it.

## Decisions made by the controller

1. **No compile-time subclass count.** The brief wanted "at least one
   generated subclass in the compilation". The pipeline analyzes a module
   before the shared-object models are collected, so the check would need
   a compilation-wide type scan or new model plumbing. The runtime `is`
   test is complete on its own because only generated sealed classes are
   ever registered. Cost if wrong: an ownership-pass check added later for
   a base with zero subclasses, which today compiles to a parameter that
   rejects every value.
2. **An explicit `isParameter` flag** on the validator instead of matching
   the position description string or relying on the `DecodeOnly` filters
   downstream. The downstream filters do not cover properties, and a
   string match would be a hidden contract. Cost if wrong: one bool on five
   call sites.
3. **The base must be abstract and unattributed.** A concrete non-sealed
   class, attributed or not, keeps its existing rejection. An abstract
   class never has an instance of its own in the registry, which is what
   makes the `is` test sufficient. Cost if wrong: none; it is the brief's
   recommendation.
4. **Nullable base stays rejected** with its own reason, because
   `NullableSharedObjectCodec<Base>` would compile and then reject every
   non-null instance at runtime. Cost if wrong: one more helper later.
5. **Parameters only.** Encode of a base has no prototype to install.

## Delta against `docs/specs/modules-core-boundary.md`

### MODIFIED Requirement: Shared-Object Codecs Preserve Managed And JavaScript Identity

Keep every sentence. Change "Assignable base/derived compatibility SHALL NOT
select a codec or prototype." to: "Assignable base/derived compatibility
SHALL NOT select an exact codec or a prototype. An authored abstract base
is decoded only through `SharedObjectBaseCodec<TBase>` (see "Authored
Abstract Bases Decode As Parameters")." The paragraph that names
`SharedObject`, `SharedRef<T>`, "an unannotated shared-object base, and
other polymorphic base types" as non-codec types drops "an unannotated
shared-object base" and adds: "An authored abstract base SHALL NOT be an
encode-side codec type."

### ADDED Requirement: Authored Abstract Bases Decode As Parameters

A top-level, non-generic, abstract class whose base chain reaches
`SharedObject`, that is not `SharedObject` or `SharedRef<T>` itself, and
that carries no `[ExpoSharedObject]` attribute, SHALL be accepted as a
parameter type of generated module methods and of supported shared-object
constructors and methods. It SHALL select `SharedObjectBaseCodec<TBase>`
with the descriptor `StaticHelper`, `DecodeOnly`, requiring
`DotnetRuntimeContext`, and `Borrowed`. Decoding SHALL resolve the registry
entry exactly as the exact codec does and SHALL accept the managed instance
when it is an instance of `TBase`; it SHALL NOT construct, clone, or
substitute an instance. The codec has no `Release`: a shared object is
registry-owned, never a leaf holder. The same type as a return, async result, or
property, with a nullable annotation, nested, generic, or inside a composed
codec SHALL report `EXPOJSI023` with a reason that names the rule. A
concrete non-sealed class SHALL keep its existing diagnostic.

#### Scenario: Base parameter receives any generated subclass

- **GIVEN** an abstract `PathBase : SharedObject` with generated sealed
  subclasses `FileEntry` and `DirEntry`, a module method `string Describe(PathBase path)`,
  an async method `Task<string> DescribeLater(PathBase path)`, and a
  shared-object method `string Compare(PathBase other)` and constructor
  `DirEntry(string name, PathBase parent)`
- **WHEN** JavaScript passes a `FileEntry`, a `DirEntry`, and a JavaScript
  subclass instance of `FileEntry`
- **THEN** authored code SHALL receive the original managed instance each
  time, typed as `PathBase`
- **AND** no pairing step SHALL be observed during decode

#### Scenario: Existing failures stay

- **GIVEN** the `PathBase` parameter
- **WHEN** JavaScript passes a plain object, a primitive, `null`,
  `undefined`, a released instance, an instance from another context, or an
  instance of a shared class that does not derive from `PathBase`
- **THEN** the call SHALL fail before authored code runs
- **AND** the message for the unrelated class SHALL be the base codec's
  `is not a '...PathBase' instance` text

#### Scenario: Non-parameter positions and the nullable form stay rejected

- **GIVEN** `PathBase` as a return type, a `Task<PathBase>` result, a
  property type, a `PathBase?` parameter, a nested `PathBase` inside a
  list, dictionary, record, callback, or typed event payload, a generic or
  nested abstract base, and `SharedObject` and `SharedRef<X>` parameters
- **WHEN** the generator runs
- **THEN** it SHALL report `EXPOJSI023` (or the event code) with the reason
  the design names, and SHALL NOT emit the member

#### Scenario: Concrete non-sealed classes keep their diagnostic

- **GIVEN** a non-abstract, non-sealed class deriving from `SharedObject`
  used as a parameter, with and without `[ExpoSharedObject]`
- **WHEN** the generator runs
- **THEN** it SHALL report the existing `EXPOJSI023` reasons

### Shared-object position and descriptor tables

Add a row for "Authored abstract base `TBase` (parameter only)":
`SharedObjectBaseCodec<TBase>`, `StaticHelper`, `Borrowed`, decode-only.

## Module authoring guide

Section 8 boundary rule: add that a parameter may be typed as an authored
abstract base of several generated classes, and authored code receives the
concrete instance; returns and properties still use the exact sealed class;
`SharedObject` and `SharedRef<T>` stay rejected.

## Roadmap

Row 034 `IN PROGRESS` with this spec, `DONE` at close with the test count
in a new 034 note. The brief and the change folder leave the repo at close;
git history keeps them.

## Verification

```sh
scripts/test-managed.sh
scripts/format.sh --check --all
git diff --check
rg -n 'CodecRelease[^\n]*SharedObject|\bOwn[^\n]*Shared(Object|Ref)' \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator
rg -n 'SharedObjectBaseCodec<(global::Expo\.ModulesCore\.)?(SharedObject|SharedRef)' \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests
rg -n 'IsAssignableFrom|IsAssignableTo|\.IsSubclassOf' \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs
git diff --unified=0 d9af9c6..HEAD -- docs packages | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\'
```

All scans print nothing.
