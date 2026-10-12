# Plan 013: camelCase JS naming defaults and `[JS]` property support

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving to the
> next step. If anything in the "STOP conditions" section occurs, stop and
> report — do not improvise. When done, update the status row for this plan
> in `docs/plans/README.md` — unless a reviewer dispatched you and told you
> they maintain the index.
>
> **Drift check (run first)**:
> `git diff --stat b6a702a6..HEAD -- packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator packages/example-module docs/module-authoring-guide.md docs/specs/modules-core-boundary.md`
> If any in-scope file changed since this plan was written, compare the
> "Current state" excerpts against the live code before proceeding; on a
> mismatch, treat it as a STOP condition.

## Status

- **Priority**: P2
- **Effort**: M
- **Risk**: MED (deliberate breaking change to the JS-facing naming contract)
- **Depends on**: none (014 depends on this)
- **Category**: dx / migration
- **Planned at**: commit `b6a702a6`, 2026-07-19

## Why this matters

This repo is aligning its C# module authoring syntax with the Expo Modules
2.0 direction (annotated plain classes, the declaration *is* the contract —
see the "An early look at Expo Modules 2.0" article summarized in "Current
state"). Two gaps remain on the naming/surface side:

1. A `[JS]` method without an explicit name exports its C# name **verbatim**
   (`Add` → JS `Add`), and record fields cross the boundary as PascalCase
   (`user.Name`). Idiomatic JS is camelCase, so today every module author
   writes redundant `[JS("add")]` names and facades translate record casing
   by hand.
2. `[JS]` cannot be placed on properties at all, while Expo Modules 2.0
   treats an annotated native property as a JS property (writable when the
   native declaration is writable, read-only otherwise).

After this plan: `[JS]` members and record fields default to camelCase JS
names (explicit `[JS("name")]` still overrides), and `[JS]` on an instance
property exposes a JS get/set property on the module object. The authoring
guide and normative spec describe the new defaults.

The repo is pre-1.0 with a single in-repo consumer (`packages/example-module`
plus the example apps), so the operator explicitly accepted the breaking
rename of record fields (decision recorded 2026-07-19).

## Current state

Article target syntax (Swift, for reference — the C# mapping is this repo's
existing attribute style, not a new DSL):

```swift
@ExpoModule
public final class MyModule {
  @JS func add(a: Double, b: Double) -> Double { a + b }
  @JS var ready: Bool { isReady }   // read-only JS property
}
```

Relevant files:

- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/JSAttribute.cs`
  — `[AttributeUsage(AttributeTargets.Method, Inherited = false)]`; has
  parameterless and `(string name)` constructors. Must gain
  `AttributeTargets.Property`.
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
  — the Roslyn generator. Key locations (line numbers at `b6a702a6`):
  - `:60-71` — module name default: class name with a trailing `Module`
    suffix stripped; explicit `[ExpoModule("Name")]` overrides. (Already
    matches the 2.0 direction — do not change.)
  - `:455-520` — `[JS]` method discovery. `:501` is the naming default to
    change:
    ```csharp
    var javaScriptName = member.Name;
    if (jsAttribute.ConstructorArguments.Length == 1 &&
        jsAttribute.ConstructorArguments[0].Value is string explicitName)
    {
      javaScriptName = explicitName;
    }
    ```
    Only `IMethodSymbol` members are scanned (`GetMembers().OfType<IMethodSymbol>()`);
    properties are silently ignored today.
  - `:1805-1806` — existing helper to reuse:
    ```csharp
    private static string LowerCamel(string value) =>
        value.Length == 0 ? value : char.ToLowerInvariant(value[0]) + value.Substring(1);
    ```
  - `:1738-1800` — `TryGetRecordCodec`: builds
    `ExpoGeneratedRecordFieldModel(LowerCamel(parameter.Name), property.Name, ...)`.
    The second component (`PropertyName`) is used **both** as the JS property
    string and as the C# member access in emitted code — see
    `EmitRecordCodec` at `:1206-1245`, e.g.
    `obj.GetProperty("{field.PropertyName}")` (JS side) and
    `value.{field.PropertyName}` (C# side). To camelCase the JS name you must
    add a separate JS-name component to the model instead of mutating
    `PropertyName`.
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs`
  — `:88` region defines `ExpoGeneratedRecordFieldModel(ParameterName, PropertyName, TypeName, CodecExpression, Location)`.
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs`
  — diagnostics `EXPOJSI001`–`EXPOJSI013` exist; new property-shape
  diagnostics start at `EXPOJSI014`.
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/ModuleRegistry.cs`
  — modules are plain JS objects; generated functions are installed as value
  properties (`modules.SetProperty(moduleName, moduleValue)` at `:194`).
  There is **no** `defineProperty` wrapper anywhere in `Expo.JSI` (verified
  at `b6a702a6`), so JS accessor properties must be installed by fetching
  `Object.defineProperty` from the global object and calling it through the
  existing function-call wrappers with a descriptor object whose `get`/`set`
  are host functions. Do not add a new native ABI entry for this.
- `packages/example-module/dotnet/ExampleModule/ExampleMathModule.cs` — the
  reference module; uses explicit `[JS("add")]`, `[JS("describeUser")]`, etc.
- `packages/example-module/src/index.ts` — facade; `:21` and `:43` read
  PascalCase record fields:
  ```ts
  const result = nativeModule.describeUser({ Age: user.age, Name: user.name });
  ```
  This is exactly the wart the camelCase default removes.
- `docs/module-authoring-guide.md` — sections 3 (module definition), 8 (JS
  facade; documents the PascalCase crossing and manual translation).
- `docs/specs/modules-core-boundary.md` — the normative spec (810 lines,
  GIVEN/WHEN/THEN "SHALL" scenario style — match it exactly when adding
  scenarios).

Repo conventions that apply:

- **Living-spec workflow is mandatory for this change** (AGENTS.md): write a
  delta spec at `docs/changes/2026-<mm-dd>-camelcase-naming-and-properties/spec.md`
  first, commit it, then a `plan.md` next to it, then implement with focused
  verified commits, then merge accepted deltas into
  `docs/specs/modules-core-boundary.md` and archive the change folder
  content per `.agents/skills/living-spec-workflow/SKILL.md`. Read that
  skill file before starting.
- No runtime reflection on the hot path; everything compile-time
  (NativeAOT-compatible).
- Generated glue owns argument wrappers for the call's duration; returned
  wrappers transfer ownership to generated glue (see
  `docs/specs/ownership-and-scoped-refs.md`). Property getter/setter host
  functions must follow the same ownership rules as existing function glue.
- Never commit absolute local paths, usernames, or machine names.
- Commit style: conventional-commit-ish, e.g.
  `feat(generator): default [JS] members to camelCase names` (see `git log`).

## Commands you will need

| Purpose | Command (repo root) | Expected on success |
|---|---|---|
| Managed test suite (builds Hermes testhost first) | `scripts/test-managed.sh` | exit 0, all tests pass |
| Generator unit tests only | `dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj` | exit 0 |
| Formatting | `scripts/format.sh --check --all` (run `scripts/format.sh` then re-check if it fails) | exit 0 |
| Mobile facade typecheck | `pnpm --filter mobile-app typecheck` | exit 0 |

## Suggested executor toolkit

- Repo skill `.agents/skills/living-spec-workflow/SKILL.md` — mandatory
  workflow reference (artifact locations, archive step).
- Skill `expo-jsi-managed-handle-lifetime` (if available) — before writing
  the property getter/setter host-function glue and the
  `Object.defineProperty` call path; it covers the owned-wrapper pitfalls.

## Scope

**In scope** (the only files you should modify or create):

- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/JSAttribute.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/` — new
  small helper for accessor-property installation (e.g. next to
  `JavaScriptObjectFactory.cs`) if needed
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/`
  (`ExpoModulesGenerator.cs`, `ExpoModuleModel.cs`, `ExpoModulesDiagnostics.cs`)
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/`
- `packages/example-module/` (module C#, facade TS, its tests)
- `docs/module-authoring-guide.md`, `docs/specs/modules-core-boundary.md`
- `docs/changes/2026-<mm-dd>-camelcase-naming-and-properties/` (create)
- `docs/plans/README.md` (status row only)

**Out of scope** (do NOT touch, even though they look related):

- `[Events]` / event emission / `[Event]` typed members — that is plan 014.
- `packages/expo-modules-dotnet-autolinking/` — naming changes don't affect
  autolinking.
- Anything under `packages/expo-modules-dotnet/managed/packages/Expo.JSI/`
  or native/C++ code — no ABI change is needed for this plan.
- `[ExpoModule]` module-name defaulting — already correct.
- Record shape extensions beyond naming (custom field naming attributes,
  non-positional constructors — roadmap items, not this plan).

## Git workflow

- Branch: `advisor/013-camelcase-naming-and-properties` off `main`.
- Commit per step (delta spec, generator naming, record naming + example
  migration, property support, docs merge).
- Do NOT push or open a PR unless the operator instructed it.

## Steps

### Step 1: Delta spec

Read `.agents/skills/living-spec-workflow/SKILL.md`. Write
`docs/changes/2026-<mm-dd>-camelcase-naming-and-properties/spec.md` (use
today's date) as a delta against `docs/specs/modules-core-boundary.md`,
covering these behaviors in the spec's GIVEN/WHEN/THEN SHALL style:

1. A `[JS]` method with no explicit name SHALL be exported under the
   lower-camel-case form of its C# name (`Add` → `add`, `GetMessageAsync` →
   `getMessageAsync`). Explicit `[JS("name")]` SHALL be used verbatim.
2. Generated record codecs SHALL encode/decode fields under the
   lower-camel-case form of the C# property name (`Name` → `name`);
   decode SHALL read the camelCase JS property only (no PascalCase
   fallback — a silent dual-read would hide contract drift).
3. `[JS]` on an instance property SHALL expose a JS accessor property on the
   module object: a property with a getter and setter SHALL be writable from
   JS; a getter-only property SHALL be read-only in JS (assignment throws a
   `TypeError` in strict mode via a descriptor without `set`). The JS name
   defaults to lower-camel-case, `[JS("name")]` overrides.
4. Property get/set glue SHALL reuse the compile-time codecs, run as direct
   host functions (no thread hop), and surface managed exceptions as
   catchable JS errors — same contract as sync `[JS]` methods.
5. Unsupported property shapes SHALL be build diagnostics, not runtime
   failures: static properties, indexers, setter-only properties, `init`
   accessors, and property types without a codec (new IDs from `EXPOJSI014`
   up, following the existing message style in `ExpoModulesDiagnostics.cs`).

Present the delta spec to the operator/reviewer for approval before
implementing. **Verify**: file exists; operator approved; committed
(`git log -1 --stat` shows only the spec file).

### Step 2: Implementation plan artifact

Write `docs/changes/<same-folder>/plan.md` mapping steps 3–6 below onto
focused commits. **Verify**: committed.

### Step 3: camelCase default for `[JS]` methods

In `ExpoModulesGenerator.cs` change the default at `:501` to
`var javaScriptName = LowerCamel(member.Name);` (explicit-name branch
unchanged). Update generator tests in
`Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`: adjust
existing name assertions and add cases for (a) no-arg `[JS]` producing
camelCase, (b) explicit `[JS("ExactName")]` staying verbatim.

**Verify**: `dotnet test .../Expo.ModulesCore.Generator.Tests.csproj` → exit 0.

### Step 4: camelCase record field names + example migration

Add a JS-name component to `ExpoGeneratedRecordFieldModel`
(`ExpoModuleModel.cs:88` region), populate it with
`LowerCamel(property.Name)` in `TryGetRecordCodec` (`:1786` region), and use
it for the three JS-string sites in `EmitRecordCodec` (`:1215`, `:1225`,
`:1237`) while keeping `PropertyName` for the C# member access at `:1236`.

Migrate consumers in the same commit so the tree never has a broken
contract: update `packages/example-module/src/index.ts` (drop the
`{ Age: ..., Name: ... }` translation — pass camelCase straight through, fix
the `describeUser` type at `:21`), and update any
`Expo.ModulesCore.Tests` fixtures asserting PascalCase record fields or
verbatim method names (search: `grep -rn '"Name"\|"Age"\|"Summary"'
packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests
packages/example-module`).

**Verify**: `scripts/test-managed.sh` → exit 0;
`pnpm --filter mobile-app typecheck` → exit 0.

### Step 5: `[JS]` property support

1. `JSAttribute.cs`: extend to
   `AttributeTargets.Method | AttributeTargets.Property`.
2. Runtime helper in `Expo.ModulesCore` (new file, internal): install an
   accessor property on a `JavaScriptObject` by calling the global
   `Object.defineProperty` function with a descriptor object
   (`enumerable: true`, `get` and optional `set` host functions built via
   `runtime.CreateHostFunction`, retained the same way
   `EventEmitterPrototype.Install` retains its host functions — see
   `EventEmitterPrototype.cs:7-40` for the retain pattern).
3. Generator: scan `IPropertySymbol` members for `[JS]`; build a property
   model (JS name, type codec, has-setter); emit getter/setter glue methods
   that reuse the existing codec expressions (`GetCodecExpression`) and
   registration code that installs the accessor via the new helper alongside
   function installation. Add the diagnostics from Step 1 item 5 for
   unsupported shapes.
4. Add a `[JS]` property to `ExampleMathModule.cs` (e.g. read-write
   `Ready`/`ready` bool or similar) and expose it in the facade type.
5. Tests: generator tests for the emitted glue and each diagnostic;
   Hermes-backed dispatch tests in `Expo.ModulesCore.Tests` for read,
   write, read-only rejection (strict-mode assignment throws), codec error
   surfacing as catchable JS `Error`.

**Verify**: `dotnet test .../Expo.ModulesCore.Generator.Tests.csproj` →
exit 0; `scripts/test-managed.sh` → exit 0.

### Step 6: Docs merge and archive

Merge the accepted delta into `docs/specs/modules-core-boundary.md`. Update
`docs/module-authoring-guide.md`: section 3 examples drop redundant explicit
names (`[JS] public double Add(...)` → JS `add`), add a short "Properties"
subsection, and section 8 removes the PascalCase-translation caveat.
Archive/trim the `docs/changes/` folder per the living-spec skill. Run
formatting.

**Verify**: `scripts/format.sh --check --all` → exit 0; guide contains no
remaining `[JS("add")]`-style redundant rename in its examples
(`grep -n 'JS("' docs/module-authoring-guide.md` shows only genuinely
renamed exports, if any).

## Test plan

- Generator tests (`ExpoModulesGeneratorTests.cs`): camelCase default for
  methods; explicit name verbatim; record field JS names camelCase; property
  glue generation for get-only and get/set; diagnostics for static property,
  indexer, setter-only, `init` accessor, and codec-less property type.
- Hermes-backed tests (`Expo.ModulesCore.Tests`, model after the existing
  dispatch/conversion fixtures under `Fixtures/`): calling a camelCase
  exported method from JS; record round-trip with camelCase JS fields;
  property read/write from JS; strict-mode write to read-only property
  throws `TypeError`; property getter throwing managed exception surfaces
  as catchable JS `Error`.
- Verification: `scripts/test-managed.sh` and the generator test command →
  all pass including the new tests.

## Done criteria

Machine-checkable. ALL must hold:

- [ ] `scripts/test-managed.sh` exits 0
- [ ] `dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj` exits 0
- [ ] `pnpm --filter mobile-app typecheck` exits 0
- [ ] `scripts/format.sh --check --all` exits 0
- [ ] `grep -n 'Age:' packages/example-module/src/index.ts` returns no matches (PascalCase translation removed)
- [ ] `docs/specs/modules-core-boundary.md` contains the merged scenarios for camelCase defaults and `[JS]` properties
- [ ] No files outside the in-scope list are modified (`git status` / `git diff --stat main`)
- [ ] `docs/plans/README.md` status row for 013 updated

## STOP conditions

Stop and report back (do not improvise) if:

- The excerpts in "Current state" don't match the live code (drift since
  `b6a702a6`).
- The operator rejects or wants changes to the delta spec (Step 1) — do not
  implement past an unapproved spec.
- Installing an accessor property via `Object.defineProperty` through the
  existing `Expo.JSI` wrappers turns out to be impossible without a new
  native ABI entry — the no-ABI-change assumption is load-bearing.
- Ownership/lifetime of the getter/setter host functions can't follow the
  `EventEmitterPrototype` retain pattern (e.g. teardown crashes in
  `scripts/test-managed.sh`).
- You find consumers of PascalCase record fields beyond `example-module`
  and the test fixtures (search apps under `apps/` before Step 4; if an app
  breaks in a way `pnpm --filter mobile-app typecheck` doesn't catch,
  report it).
- A step's verification fails twice after a reasonable fix attempt.

## Maintenance notes

- Plan 014 (typed `[Event]` members) builds on this: it reuses `LowerCamel`
  naming and the property-scanning machinery. Land 013 first.
- The future generated-TypeScript tool (roadmap / Expo 2.0 article) will
  treat these JS names as the contract; the camelCase mapping decided here
  becomes load-bearing for it.
- Reviewer should scrutinize: wrapper ownership in the property glue (no
  undisposed owned wrappers — see `expo-jsi-managed-handle-lifetime`
  pitfalls), and that decode has no PascalCase fallback.
- Deferred deliberately: custom field-naming attributes and non-positional
  record constructors (roadmap "Record shape extensions"); `[Record]`
  attribute itself (C# records are detected structurally, which already
  exceeds the article's `@Record` ergonomics — no attribute needed).
