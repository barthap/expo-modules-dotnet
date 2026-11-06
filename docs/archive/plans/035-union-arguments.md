# 035: Accept a two-member union argument such as `string | ArrayBuffer`

> **Deliver with `living-spec-workflow`**: brainstorm, delta spec, plan,
> implement, verify, merge into `docs/specs/`. Set this item's row in
> `docs/roadmap.md` to `IN PROGRESS` when you start and `DONE` when the
> slice closes, then move this brief to `docs/archive/plans/`.
>
> **Drift check (run first)**: `git diff --stat fbb3459..HEAD -- packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.Codecs.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoCodecDescriptor.cs packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptValueKind.cs docs/specs/modules-core-boundary.md docs/specs/ownership-and-scoped-refs.md`
> If an in-scope file changed, compare "Current state" with the live code.
> A mismatch is a STOP condition.

## Status

- **Priority**: P1
- **Effort**: M
- **Risk**: MED. A union member can be a scoped resource (`ArrayBuffer`),
  so the union codec must compose through the conversion scope exactly as
  a direct member does.
- **Depends on**: 030
- **Category**: direction
- **Planned at**: `fbb3459`, 2026-11-06

## Why this matters

Upstream `expo-file-system` declares `write(content: Either<String,
NativeArrayBuffer>, options)`. C# has no union type, so 024 left `write`
out. Roadmap 038 needs it. This item adds one managed union carrier and one
codec that picks the member by the JavaScript value kind, so authored code
receives a typed value and never inspects JavaScript itself.

## Current state

- `Expo.JSI/JavaScriptValueKind.cs`: `Undefined`, `Null`, `Bool`, `Number`,
  `String`, `Object`, `Function`, `ArrayBuffer`. The bridge's
  `getValueKind` classifies an `ArrayBuffer` object as `ArrayBuffer` and every
  other non-function object as `Object`. `JavaScriptValueRef.Kind` exposes it.
- `ExpoModulesGenerator.Codecs.cs:519-574`: `ArrayBufferCodec` is a
  `ScopedCodec`; `ByteArrayCodec`, `MemoryByteCodec`,
  `ReadOnlyMemoryByteCodec`, `BoolCodec`, `StringCodec`, and
  `NumberCodec<T>` are ordinary codecs.
- `ExpoModulesGenerator.Codecs.cs:80-86`: `AsCompositeChildCodec` and
  `AsJavaScriptCodecArgument` gate which descriptors may nest.
- `Codecs/GeneratedScopedNullableReferenceCodec.cs` and
  `GeneratedScopedNullableCodec` (`Codecs.cs:173`): the exemplar for a
  composite scoped codec with `Decode(value, runtime, scope)`, `Transfer`,
  and `EncodeBorrowed`.
- `docs/specs/modules-core-boundary.md`, "Generated Bindings Compose Leaves
  Through One Scope" (line 1395) and `docs/specs/ownership-and-scoped-refs.md`,
  "Conversion Scope Owns Boundary Wrapper Leaves" (line 155): composed
  codecs register each leaf once, in registration order.
- There is no union type anywhere in `Expo.ModulesCore` today. A JavaScript
  `string` passed to an `ArrayBuffer` parameter throws from `AsArrayBuffer`.

## Constraints

- No ABI change. `JavaScriptValueKind` already carries the discriminator.
- No try-one-then-the-other decoding. Member selection is by kind, before
  any member codec runs, so a wrong value fails once with one message.
- The union is a plain carrier. No implicit conversions, no reflection.
- A scoped member keeps its scoped lifetime. The union codec is a composite
  child in the 030 sense.

## Scope

**In scope**:

- `Expo.ModulesCore/Either.cs` (new): `readonly struct Either<TFirst,
TSecond>` with `IsFirst`, `First`, `Second`, and two static factories.
- `Expo.ModulesCore/Codecs/EitherCodec.cs` (new) or a scoped variant,
  decided by the spec.
- `ExpoModulesGenerator.Codecs.cs`: resolve `Either<,>`, map each member
  type to one `JavaScriptValueKind`, reject members that share a kind or
  have no kind mapping.
- Generator tests, Hermes-backed runtime tests.
- `docs/specs/modules-core-boundary.md`, `docs/specs/ownership-and-scoped-refs.md`,
  `docs/module-authoring-guide.md`, `docs/roadmap.md`.

**Out of scope**:

- `packages/expo-file-system-dotnet` `write`: roadmap 038.
- Three or more members.
- Members classified as `Object` (records, lists, dictionaries, shared
  objects, typed arrays, `JavaScriptValue`): two `Object` members cannot be
  told apart by kind. They report a diagnostic.
- Union returns and properties: decode-only, unless the spec shows encode is
  one line per member with no new rule.

## Open design questions

1. **Discrimination table.** Recommended: a generator-side map from member
   type to kind: `string` to `String`; `bool` to `Bool`; the numeric types
   that resolve to `NumberCodec<T>` to `Number`; `ArrayBuffer` to
   `ArrayBuffer`; `byte[]`, `Memory<byte>`, `ReadOnlyMemory<byte>` to
   `ArrayBuffer`. Any other member type, and two members with the same
   kind, report `EXPOJSI001` for the parameter with a reason that names the
   rule. Evidence: `getValueKind` in the bridge.
2. **Nullish input.** Recommended: `Either<,>` itself rejects `null` and
   `undefined` with the kind error; `Either<,>?` composes through
   `GeneratedScopedNullableCodec` as any other value type and gives C#
   `null`. Evidence: `Codecs.cs:173`.
3. **Codec shape.** Recommended: one `IGeneratedScopedCodec<Either<TFirst,
TSecond>>` with four type parameters (`TFirst, TFirstCodec, TSecond,
TSecondCodec`) and the two kind values as the generated expression
   cannot carry enum arguments, so the codec reads the kinds from a
   `static abstract` or takes them as generic `const`-like parameters; the
   spec picks the exact mechanism after reading how
   `GeneratedScopedListCodec` is declared. Decode switches on
   `value.Kind`, then calls the member codec; `Transfer` and
   `EncodeBorrowed` delegate to the active member.
4. **Ownership descriptor.** Recommended: `ScopedResource` when either
   member is `ScopedResource`, else `Borrowed`; `DecodeOnly` capability.

## Done criteria

- [ ] `scripts/test-managed.sh` passes with no skips; new tests cover
      `Either<string, ArrayBuffer>` and `Either<double, bool>` parameters on
      a module method and a shared-object method, the nullable form, both
      member paths, the kind error for a wrong value, and scoped release of
      the `ArrayBuffer` member at invocation end.
- [ ] `scripts/format.sh --check --all` passes.
- [ ] Generator tests prove a diagnostic for two same-kind members, an
      `Object` member, and a union return or property.
- [ ] `rg -n 'Either<' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests` shows only the named fixtures.
- [ ] Accepted behavior is merged into `docs/specs/modules-core-boundary.md`
      and `docs/specs/ownership-and-scoped-refs.md`.
- [ ] Roadmap row is `DONE`, and this brief is in `docs/archive/plans/`.

## STOP conditions

Stop and report instead of improvising if:

- Current state does not match the excerpts.
- Member selection cannot be done from `JavaScriptValueRef.Kind` alone.
- The union codec needs a scope rule that the 030 scope does not have.
- The generator needs a new codec-string or `StartsWith` policy check.

## Maintenance notes

038 uses `Either<string, ArrayBuffer>` for `write`. 036 adds `Uint8Array`
as an `Object`-kind type; a union with a typed-array member needs a second
discriminator (constructor identity) and is not covered here. If 038 shows
that upstream JavaScript passes a `Uint8Array` to `write`, that follow-up
belongs to the 038 brief.
