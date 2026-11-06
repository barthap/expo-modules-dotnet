# 034: Accept an abstract shared-object base as a parameter type

> **Deliver with `living-spec-workflow`**: brainstorm, delta spec, plan,
> implement, verify, merge into `docs/specs/`. Set this item's row in
> `docs/roadmap.md` to `IN PROGRESS` when you start and `DONE` when the
> slice closes, then delete this brief (git history keeps it).
>
> **Drift check (run first)**: `git diff --stat d9af9c6..HEAD -- packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedObjectRegistry.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.SharedObjectValidation.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoCodecDescriptor.cs docs/specs/modules-core-boundary.md`
> If an in-scope file changed, compare "Current state" with the live code.
> A mismatch is a STOP condition.

## Status

- **Priority**: P1
- **Effort**: M
- **Risk**: MED. Decode must accept every sealed subclass of the base
  without weakening exact-type, context, or released-object checks for any
  other position.
- **Depends on**: 030, 033
- **Category**: direction
- **Planned at**: `d9af9c6` (the 033 close), 2026-11-06

## Why this matters

Upstream `expo-file-system` declares `copy(to: FileSystemPath)` and
`move(to: FileSystemPath)`, where `FileSystemPath` is the abstract base of
`FileSystemFile` and `FileSystemDirectory`. The 024 port already has that
class shape, but the generator rejects any non-sealed shared-object type at
the boundary, so `copy` and `move` were left out of 024. Roadmap 038 needs
them. This item lets a method take an authored abstract base whose concrete
subclasses are all generated shared-object classes, and decodes the exact
managed instance that JavaScript holds.

## Current state

- `Expo.ModulesCore/Codecs/SharedObjectCodec.cs`: exact-class codec.
  `DecodeOwned` resolves the registry entry and requires
  `managed is T instance && instance.GetType() == typeof(T)`.
- `Expo.ModulesCore.Generator/ExpoModulesGenerator.SharedObjectValidation.cs:113-120`:
  `GetDirectSharedObjectBoundaryIssue` rejects `SharedObject`, `SharedRef<T>`,
  un-attributed classes, and "which must be a top-level, non-generic, sealed
  [ExpoSharedObject] class". An abstract base hits the attribute rule or the
  sealed rule and reports `EXPOJSI023`.
- `ExpoModulesGenerator.SharedObjectValidation.cs:124-136`:
  `TryAnalyzeSharedObjectBoundaryType` runs before `GetCodec` for every
  boundary and selects `SharedObjectCodec<Exact>` with the descriptor row
  `StaticHelper`, `DecodeAndEncode`, `DotnetRuntimeContext`, `Borrowed`.
- `ExpoModulesGenerator.SharedObjectValidation.cs:80-96`:
  `TranslateSharedObjectMemberDiagnostic` maps member diagnostics such as
  `EXPOJSI002` and `EXPOJSI015` to `EXPOJSI023` inside shared-object classes.
- `packages/expo-file-system-dotnet/dotnet/ExpoFileSystemDotnet/FileSystemPath.cs`:
  `internal abstract class FileSystemPath : SharedObject` with sealed
  `[ExpoSharedObject]` subclasses `FileSystemFile` and `FileSystemDirectory`.
  This is the exemplar base.
- `SharedObjectRegistry.ResolveManaged` returns the registered managed
  instance for a JavaScript object. Only
  `GetOrCreateJavaScriptObject(value, registration)` adds entries, and every
  registration belongs to a generated sealed class.
- `docs/specs/modules-core-boundary.md`, "Shared-Object Codecs Preserve
  Managed And JavaScript Identity": "`SharedObject`, `SharedRef<T>`, an
  unannotated shared-object base, and other polymorphic base types SHALL NOT
  be generated-boundary codec types" and "Assignable base/derived
  compatibility SHALL NOT select a codec or prototype." This item narrows
  the second sentence for decode of an authored abstract base.

## Constraints

- No ABI change. Decode uses the existing registry and the managed type
  system.
- Keep `SharedObjectCodec<T>` exact. The base path is a separate helper.
- Encode of a base stays rejected: a prototype belongs to one exact class.
- A shared object is registry-owned, never a leaf holder: the base codec
  has no `Release`, and the glue never releases it.
- No reflection on the hot path. The managed `is` test is a type check, not
  reflection.

## Scope

**In scope**:

- `Expo.ModulesCore/Codecs/SharedObjectBaseCodec.cs` (new, decode-only).
- `ExpoModulesGenerator.SharedObjectValidation.cs`: accept an authored
  abstract base on parameter positions; select the base codec; keep every
  other rejection.
- `ExpoCodecDescriptor.cs` only if the shared-object descriptor row needs a
  decode-only variant.
- Generator tests, Hermes-backed runtime tests with a new abstract-base
  fixture family.
- `docs/specs/modules-core-boundary.md`, `docs/module-authoring-guide.md`,
  `docs/roadmap.md`.

**Out of scope**:

- `packages/expo-file-system-dotnet` members (`copy`, `move`): roadmap 038.
- Base types as return, `Task<T>`, or property types: encode needs an exact
  prototype.
- `SharedObject` and `SharedRef<T>` as the base: they stay `EXPOJSI023`.
- Nullable base (`FileSystemPath?`): not needed by 038. Goes to the
  unplanned backlog if a module asks for it.
- Nested positions (lists, records, callbacks, events): stay rejected.

## Open design questions

1. **What makes a base acceptable?** Recommended: a top-level, non-generic,
   abstract class whose base chain reaches `SharedObject`, that is not
   `SharedObject` or `SharedRef<T>` itself, and that has at least one
   emittable sealed `[ExpoSharedObject]` subclass in the same compilation.
   The attribute is not required on the base. Evidence: `FileSystemPath` has
   no attribute, is `internal abstract`, and both subclasses are emitted.
   An abstract base with zero generated subclasses reports `EXPOJSI023`
   with a reason that names the rule.
2. **Runtime check.** Recommended: `managed is TBase instance` after
   `ResolveManaged`, with no `GetType` equality. Every registry entry was
   created through a generated sealed class registration, so a `TBase`
   entry is always one of the generated subclasses. Foreign-context,
   released, plain-object, and wrong-class failures are unchanged because
   they come from the registry and the `is` test.
3. **Descriptor.** Recommended: the existing shared-object row with
   `DecodeOnly` capability. Return and property positions then report the
   existing decode-only diagnostics, translated to `EXPOJSI023` in
   shared-object classes. Check that module-level return and property
   positions also end in `EXPOJSI023`, because
   `TryAnalyzeSharedObjectBoundaryType` runs before `GetCodec`.
4. **Codec shape.** Recommended: `public static class SharedObjectBaseCodec<TBase>
where TBase : SharedObject` with the two `Decode(value, runtime,
runtimeContext)` overloads of `SharedObjectCodec<T>` and no `Encode`.
   Same call text, so emission is unchanged.

## Done criteria

- [ ] `scripts/test-managed.sh` passes with no skips; new tests cover an
      abstract base parameter on a module method, a shared-object
      constructor, and a shared-object method, with both subclasses, and
      every existing failure mode.
- [ ] `scripts/format.sh --check --all` passes.
- [ ] Generator tests prove `EXPOJSI023` for a base return, a base property,
      `SharedObject`, `SharedRef<T>`, a base with no generated subclass, and a
      base inside a list, record, callback, and event payload.
- [ ] `rg -n 'CodecRelease[^\n]*SharedObject|\bOwn[^\n]*Shared(Object|Ref)' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator` prints nothing.
- [ ] Accepted behavior is merged into `docs/specs/modules-core-boundary.md`.
- [ ] Roadmap row is `DONE`, and this brief is deleted.

## STOP conditions

Stop and report instead of improvising if:

- Current state does not match the excerpts.
- Decode of a base needs a registry change or a new registry lookup.
- The base codec would need a prototype, an encode path, or a `Release`.
- The generator cannot find the generated subclasses of a base from the
  compilation it already analyzes.

## Maintenance notes

038 uses this for `copy` and `move`. 033's nullable helper delegates to the
exact codec; a nullable base would need its own one-line helper and goes to
the backlog until a module asks for it. A reviewer should check that no
assignability test was added to the exact codec or to prototype selection.
