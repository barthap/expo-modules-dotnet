# Plan 030: Add one compositional conversion scope for owned wrapper codecs

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving on. Touch
> only the files in the In-scope list. If a STOP condition occurs, stop and
> report, do not improvise. Follow the repo's living-spec workflow: approved
> delta spec first, then an approved change-local plan, then implementation.
> Update the status row in `docs/plans/README.md` when done unless a reviewer
> says they maintain it.
>
> **Drift check (run first)**:
> ```sh
> git diff --stat 512ab46e..HEAD -- packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests docs/specs/ownership-and-scoped-refs.md docs/specs/modules-core-boundary.md docs/module-authoring-guide.md docs/plans/README.md
> ```
> Plans 028 and 029 must be DONE. Re-run this drift check from plan 029's final
> commit before implementation. If `ExpoCodecDescriptor`, generated invocation
> cleanup, ArrayBuffer/JavaScriptValue encode behavior, collection codecs, or
> record emission differs from "Current state," stop and reconcile the plan.

## Status

- **Priority**: P1
- **Effort**: L
- **Risk**: HIGH (changes cleanup and encode ownership across sync, async,
  property, record, collection, event, and shared-object generated paths)
- **Depends on**: plans 028 and 029
- **Blocks**: plans 031–033
- **Category**: core capability
- **Planned at**: commit `512ab46e`, 2026-07-25

## Why this matters

Generated glue can clean up a direct `ArrayBuffer` or `JavaScriptValue`, but it
cannot see those resources once they sit inside a record, list, or dictionary.
The collection codecs also reject `ArrayBuffer` as an element codec because
`ArrayBufferCodec` does not implement `IJavaScriptCodec<ArrayBuffer>`. A decode
that fails after one retained field leaks that field, and nested encode can
dispose a caller-owned `JavaScriptValue`.

Do not solve each occurrence with another ownership Boolean. This plan adds one
explicit conversion scope and one scope-aware codec protocol. Codec descriptors
say whether and how a value participates; the scope owns registration,
reference-identity deduplication, reverse cleanup, and failure handling.
Recursive non-null `ArrayBuffer` support is a required result, including records
and collections.

## Current state

### Direct resources work because emission recognizes the top-level codec

`ExpoModulesGenerator.Emission.cs:823-929` declares nullable locals for
invocation-owned async parameters, decodes them, and disposes them in the
Promise callback's `finally`. Sync parameters use `using var`.
`Emission.cs:895-915` has separate async return branches:

- `JavaScriptValueCodec` uses `JavaScriptPromiseResult.ResolveOwned`;
- `ArrayBufferCodec` encodes in a `try` and disposes in `finally`;
- all other codecs use ordinary `Resolve`.

Sync return and property getter emission repeat exact special cases around
`:954-1013`. Plan 029 replaces these text checks with descriptor policy but
preserves the direct behavior.

### Collections cannot express ArrayBuffer and have no rollback

`Codecs/JavaScriptArrayCodec.cs:5-33` constrains
`TCodec : IJavaScriptCodec<T>`. It decodes each element in sequence and returns
the array. If element N throws, elements 0 through N-1 receive no cleanup.
Encoding wraps every `TCodec.Encode` result in `using var`.

`Codecs/JavaScriptDictionaryCodec.cs:5-51` has the same constraint and failure
shape. Plan-026 nullable collection adapters delegate to these helpers, so they
inherit the same limit.

`ArrayBufferCodec.cs:5-29` is a static helper, not an
`IJavaScriptCodec<ArrayBuffer>`. Its decode returns an owned `ArrayBuffer`
backing lease. `JavaScriptValueCodec.Decode` similarly retains an owned wrapper.

### Records emit field operations independently

`ExpoModulesGenerator.Emission.cs:1172-1205` emits field decodes in sequence and
field encodes as:

```csharp
using var __field = FieldCodec.Encode(value.Field, runtime);
obj.SetProperty("field", __field);
```

No aggregate owner exists. If a later field throws, earlier owned wrappers
leak. If `FieldCodec` is `JavaScriptValueCodec`, `Encode` returns the same
wrapper (`Codecs/JavaScriptValueCodec.cs:22-23`), so the generated `using`
disposes the caller's wrapper. The same alias appearing twice becomes invalid
after the first field.

### Events are borrowed inputs; returns are transferred inputs

The current contracts distinguish ownership even when the implementation does
not compose it:

- `docs/specs/modules-core-boundary.md:255-289`: generated argument wrappers are
  invocation-owned, and wrapper returns transfer ownership to generated glue.
- `docs/specs/modules-core-boundary.md:395-429`: a property setter receives an
  invocation-owned wrapper; authored code calls `Retain()` before storing. A
  getter returns a retained wrapper that generated glue consumes.
- Typed event dispatch borrows authored payloads. Existing direct
  `ArrayBuffer` event code retains the backing lease before asynchronous
  scheduling; it does not consume the author's wrapper.
- `docs/specs/ownership-and-scoped-refs.md:9-15`: owned wrappers release exactly
  once unless detached; duplicate `Dispose()` is safe.

The composite design must preserve that distinction. "Everything disposable is
owned" is wrong: authored records may implement `IDisposable`, and event payload
wrappers stay caller-owned.

## Target design

### One explicit scope

Add a generated-glue support type named `JavaScriptConversionScope` under
`Expo.ModulesCore/Codecs`. It is public only because generated code in authored
assemblies must reference it; mark it as generated infrastructure using the
project's existing API-hiding convention if one exists.

The scope must:

- register only resources explicitly handed to it by a codec;
- deduplicate by object reference identity, never `Equals`;
- dispose registrations in reverse order;
- make `Dispose` idempotent;
- clean all registrations if a later decode or encode throws;
- never own the final `JavaScriptValue` returned to the host;
- contain no ambient, thread-static, async-local, reflection, or finalizer-based
  ownership inference.

The scope may allocate lazily. Generated bindings with descriptors that contain
no managed resource should keep the existing no-scope path.

### One scope-aware codec protocol

Keep `IJavaScriptCodec<T>` source-compatible. Add a separate generated
infrastructure protocol, `IJavaScriptScopedCodec<T>`, with static operations
equivalent to:

```csharp
T Decode(JavaScriptValueRef value, JavaScriptRuntime runtime,
    JavaScriptConversionScope scope);
T Decode(JavaScriptValue value, JavaScriptRuntime runtime,
    JavaScriptConversionScope scope);
void Transfer(T value, JavaScriptConversionScope scope);
JavaScriptValue EncodeBorrowed(T value, JavaScriptRuntime runtime);
```

`Transfer` recursively registers source wrapper leaves but performs no runtime
work. `EncodeBorrowed` creates an independent JS value and never consumes the
source. Generated function returns and property getters call `Transfer` before
`EncodeBorrowed`.

This separation is required for async results. After the authored Task
completes, generated code must register transferred leaves before it returns a
`JavaScriptPromiseResult.ResolveOwned` state. If Promise settlement never
reaches the runtime, the owned state's abandon callback disposes the scope. If
settlement reaches the runtime, its value factory calls `EncodeBorrowed` and
disposes the scope after encoding. Registering only inside encode would leak an
abandoned composite result.

Provide one small generic generated-infrastructure state, for example
`JavaScriptTransferredResult<T,TCodec>`, so every async return does not hand-code
that protocol. Its factory creates a scope and calls `TCodec.Transfer`
transactionally. Its runtime-only value factory calls
`TCodec.EncodeBorrowed`. Its success, encode-failure, and abandon paths dispose
the scope exactly once. If scope cleanup fails after a JS result was created,
the helper must dispose that result before rethrowing.

Do not add scope methods to `IJavaScriptCodec<T>` if that breaks external codec
implementers. Instead add these adapters:

- an ordinary adapter for `TCodec : IJavaScriptCodec<T>` that delegates and
  never registers a managed resource;
- a JavaScriptValue adapter;
- an ArrayBuffer adapter;
- recursive array/list and dictionary adapters;
- nullable-value and nullable-reference adapters needed to preserve all plan-026
  regular nullable types;
- generated record codecs that implement the scoped protocol for their fields.

Exact names may follow the codebase, but there must be one protocol and one
scope, not per-container cleanup frameworks.

### Wrapper rules

For `ArrayBuffer`:

- Decode through `ArrayBufferCodec`, then register the returned wrapper.
- `Transfer` registers the source wrapper.
- `EncodeBorrowed` always creates an independent JS value through
  `ArrayBufferCodec`.

For `JavaScriptValue`:

- Decode through `JavaScriptValueCodec`, then register the retained wrapper.
- `Transfer` registers the source wrapper.
- `EncodeBorrowed` always returns `value.Retain()` or an equivalent independent
  wrapper. Never return the same managed wrapper to a container encoder.

This adds one retain/release pair to direct scoped JavaScriptValue returns but
preserves the authored contract: the returned source wrapper is consumed, while
the host receives an independent owned result. It also makes aliases safe.

For records and collections:

- pass the same scope recursively;
- `Transfer` traverses the current supported shape and calls each leaf codec's
  `Transfer`;
- `EncodeBorrowed` recursively encodes independent JS values;
- do not register the record/container object merely because it implements
  `IDisposable`;
- dispose temporary encoded JS values after insertion into the parent;
- rely on the outer scope to clean source managed wrappers.

### Generated invocation lifetime

- Sync decode/execute/encode: create the scope before the first resource-bearing
  decode and dispose it in one outer `finally`.
- Decode failure: the same `finally` cleans fields decoded before the failure.
- Authored method exception: the same `finally` cleans decoded inputs.
- Async call: hold the input scope through Task completion, and dispose it from
  the existing async operation's `finally`, where direct decoded wrappers are
  disposed today. This `finally` runs for Task success, fault, and cancellation
  before `JavaScriptPromiseScheduler` turns the outcome into a resolve or
  rejection. It may run without runtime access and needs no `RejectOwned`;
  therefore it may call only the same managed-wrapper `Dispose` operations used
  by current direct async parameters.
- Async result encode: after the Task completes, create a result scope and call
  the result codec's `Transfer` before constructing `ResolveOwned`, through the
  generic transferred-result state above. The runtime factory calls
  `EncodeBorrowed` and disposes the scope; the abandon callback also disposes
  the scope. The abandon path may run without runtime access, so scope cleanup
  may call only the same managed-wrapper `Dispose` operations already used by
  direct `ResolveOwned` abandonment.
- Property setters use invocation/decode ownership. Property getters call
  `Transfer` and then `EncodeBorrowed`.
- Existing direct `JavaScriptValue` and `ArrayBuffer` typed-event paths keep
  their current dedicated scheduling rules. Nested resource-bearing typed event
  payloads remain unsupported, because safely borrowing them across asynchronous
  dispatch needs synchronous per-leaf capture and is not part of this scope.

Null values introduced by later plans register nothing. The scope itself must
already make that behavior natural, but nullable ArrayBuffer/callback/shared
support is out of this plan.

## Support delivered by this plan

After this plan, non-null `ArrayBuffer` and `JavaScriptValue` leaves are
supported recursively wherever the enclosing non-null record/list/dictionary
shape is already supported:

- sync and async function parameters and returns;
- properties, under the existing retain-before-store/retained-getter contract;
- generated record fields and nested records;
- `IReadOnlyList<T>`, `Dictionary<string,T>`, and
  `IReadOnlyDictionary<string,T>`;
- supported shared-object constructor/member parameter and return surfaces.

This plan does not make an otherwise unsupported container, callback result, or
polymorphic shared-object boundary valid. It also preserves the current
diagnostic for a typed event payload record/list/dictionary containing a nested
`JavaScriptValue` or `ArrayBuffer`.

## Commands you will need

| Purpose | Command | Expected on success |
| --- | --- | --- |
| Generator tests | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj` | all generator tests pass |
| Runtime tests | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj` | all ModulesCore tests pass |
| Full regression | `scripts/test-managed.sh` | all discovered managed tests pass, none skipped |
| Format | `scripts/format.sh --check --all` | exit 0 |
| Ambient-state scan | `rg -n 'ThreadStatic|AsyncLocal|static .*JavaScriptConversionScope|CurrentConversionScope' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator` | no production matches |
| Disposal-inference scan | `rg -n ' is IDisposable|as IDisposable|GetInterfaces|typeof\\(IDisposable\\)' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator` | no resource ownership inferred from runtime type |

## Suggested executor toolkit

- Read `.agents/skills/living-spec-workflow/SKILL.md`.
- Use `.agents/skills/expo-jsi-managed-handle-lifetime/SKILL.md` for every
  wrapper registration, transfer, and async cleanup change.
- Use test-driven development: add counter-based failure and alias tests before
  switching generated emission.

## Scope

**In scope**:

- `docs/changes/<yyyy-mm-dd>-compositional-codec-resource-scope/`
- `docs/archive/changes/<yyyy-mm-dd>-compositional-codec-resource-scope/`
- `docs/specs/ownership-and-scoped-refs.md`
- `docs/specs/modules-core-boundary.md`
- `docs/module-authoring-guide.md`
- `docs/plans/README.md`
- New scope, scoped protocol, and adapter files under
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/`
- Existing codec files in that directory only where delegation or visibility
  must change
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoCodecDescriptor.cs`
  or the descriptor file chosen in plan 029
- `ExpoModuleModel.cs`
- `ExpoSharedObjectModel.cs`
- `ExpoModulesGenerator.Codecs.cs`
- `ExpoModulesGenerator.ModuleAnalysis.cs`
- `ExpoModulesGenerator.SharedObjectModel.cs`
- `ExpoModulesGenerator.Emission.cs`
- Generator tests and their fixture/helper files under
  `Expo.ModulesCore.Generator.Tests/`
- Runtime tests and fixture files under `Expo.ModulesCore.Tests/`

**Out of scope**:

- `JavaScriptValue?`
- `ArrayBuffer?` (plan 031)
- Nullable callbacks (plan 032)
- Nullable concrete shared objects (plan 033)
- Callback argument/result shapes that are already unsupported
- New collection families such as mutable `List<T>` if non-resource values do
  not already support them
- Changes to wrapper `Dispose`/`Retain` implementation, the C ABI, native code,
  runtime scheduler, or platform adapters
- Ambient ownership state or automatic disposal of arbitrary `IDisposable`
  values

## Git workflow

- Branch: `advisor/030-compositional-codec-resource-scope`
- Commit the approved delta spec and change-local plan before implementation.
- Suggested logical commits:
  1. resource-scope primitive and unit tests;
  2. scoped codec protocol/adapters and codec tests;
  3. generator descriptors/emission and generator tests;
  4. integration fixtures and merged docs.
- Match conventional commit style, for example
  `feat(modules-core): compose managed codec resources`.
- Do not push or open a PR without explicit operator approval.

## Steps

### Step 1: Specify the ownership matrix

Create the delta spec before code. Include a table with:

| Boundary operation | Input/source policy | Scope end |
| --- | --- | --- |
| Sync parameter decode | invocation-owned leaves | after call/throw |
| Async parameter decode | invocation-owned leaves | after Task settles |
| Function return encode | transferred leaves | after independent JS result is encoded |
| Property setter | invocation-owned leaves | after setter/throw |
| Property getter | transferred leaves | after independent JS result is encoded |
| Direct typed event resource payload | existing dedicated borrow/lease path | unchanged |

Specify identity deduplication, reverse order, partial-failure cleanup,
runtime-only encoding, the non-runtime abandonment constraint, and recursive
records/collections. Specify that authored code must retain every wrapper leaf
it stores beyond a setter/method invocation. Specify that a transferred returned
record gives all contained wrapper leaves to generated glue. Preserve the
diagnostic for nested resource-bearing typed event payloads.

Get approval, commit the delta, then approve and commit the change-local plan.

**Verify**: `git log -2 --oneline --name-only` shows the spec and change-local
plan commits, with no source files.

### Step 2: Build and test `JavaScriptConversionScope`

Add the scope with explicit registration. Unit-test:

- empty and single registration;
- reverse order;
- duplicate registration by reference disposes once;
- distinct objects that compare equal both dispose;
- idempotent scope disposal;
- a disposal exception does not prevent remaining resources from being
  attempted, matching `DotnetRuntimeContext` aggregate-cleanup conventions;
- registration after disposal fails loudly and does not leak the incoming
  resource.

If the project has no existing aggregate-disposal convention suitable here,
specify and approve whether `AggregateException` is used before coding.

**Verify**: ModulesCore tests pass with the new primitive unused by generation.

### Step 3: Add the scoped codec protocol and leaf adapters

Add the protocol and ordinary adapter. Add ArrayBuffer and JavaScriptValue
adapters with the exact wrapper rules above. Keep all old codec entry points so
current generated code and external codec implementers compile.

Add the generic transferred-result state and codec tests with native counters
for:

- decoded wrapper registered and released once;
- `EncodeBorrowed` leaves source usable and undisposed;
- `Transfer` disposes source exactly once when scope ends;
- the encoded JS result remains usable after scope disposal;
- the same JavaScriptValue encoded twice through one transfer scope works and
  source cleanup happens once;
- encode failure still cleans transferred sources;
- an async result abandoned before runtime settlement cleans all transferred
  leaves once;
- cleanup failure after JS result creation disposes that result before the
  exception escapes.

**Verify**: runtime tests pass; existing plan-026 codec tests remain unchanged.

### Step 4: Add recursive collection, nullable-regular, and record composition

Add scoped adapters for current list/dictionary shapes and existing regular
nullable codecs. Extend the typed descriptor from plan 029 with its
scope-aware codec expression and a recursive `ContainsManagedResource` policy.
The field is set by codec resolution, never inferred from expression text.
Propagate it only through nullable wrappers, supported boundary containers, and
generated record fields that decode inline.

Do not propagate boundary resource policy through callback argument/result
codec children. A `JavaScriptCallback<JavaScriptValue>` parameter retains a
context-owned callback at the boundary; its JavaScriptValue result is decoded
later when authored code invokes the callback. The outer callback descriptor
remains context-retained and non-resource. Shared-object descriptors likewise
remain registry/context values, not conversion-scope resources.

Generate record codecs that pass one scope to every field. Add tests for:

- record with one ArrayBuffer;
- nested record → list → dictionary → ArrayBuffer;
- record with JavaScriptValue aliases in two fields;
- nullable regular record/list/dictionary shapes from plan 026;
- a supported callback returning `JavaScriptValue`, proving callback child
  codecs do not make the parameter resource-scoped;
- a record that implements `IDisposable` but has no wrapper leaf, proving the
  record itself is not registered;
- decode failure at a later field/element cleans earlier leaves.

**Verify**: generator and runtime project tests pass.

### Step 5: Replace per-parameter cleanup with the scope

For resource-bearing generated functions/properties/shared-object members:

- open one input scope before decode;
- route every resource-bearing decode through scoped expressions;
- remove individual top-level resource locals/disposal for those paths;
- hold the input scope until authored Task success/fault/cancellation and
  dispose it in the async operation's `finally`;
- open a result scope, call `Transfer`, and encode through `EncodeBorrowed`;
- carry async result scopes through `ResolveOwned`, with the same scope disposed
  by both the runtime factory and abandon callback;
- leave the direct event special cases and nested-resource event diagnostics
  unchanged.

Keep the no-scope output path for descriptors with no resource. Do not remove
nested-resource rejection from event analysis.

Add generated-source tests that assert one outer scope and no per-field
hand-written cleanup. Add integration tests for sync success, sync throw, async
success, async rejection, property get/set, and shared-object member paths.
Keep direct event regressions and nested-resource rejection tests.

**Verify**: generator and runtime tests pass. Ambient-state and
disposal-inference scans have no matches.

### Step 6: Prove recursive ArrayBuffer behavior

Use `Expo.ModulesCore.Tests` authored fixtures, not only source snapshots. At
minimum round-trip:

- `ArrayBuffer` inside a record;
- list of buffers;
- dictionary of buffers;
- record containing list containing dictionary containing buffer;
- duplicate reference in two leaves;
- later-field decode failure;
- async method that reads the buffer after an `await`;
- transferred return source disposed while the JS result stays usable.

Assert native backing-store or wrapper counters where available. A value-only
assertion cannot prove absence of a leak or premature release.

**Verify**: ModulesCore tests pass with all new cases.

### Step 7: Run regressions and merge the contract

Run full managed tests, formatting, `git diff --check`, and both scans. Merge the
delta into the two living specs and authoring guide. Archive the change package
and mark plan 030 DONE with test counts and commit IDs.

**Verify**:

```sh
git status --short
git diff --unified=0 512ab46e..HEAD -- docs packages/expo-modules-dotnet/managed/packages | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\[A-Za-z0-9._-]+\\\\'
```

Expected: clean tree after commits; no introduced local paths, so `rg` prints
nothing and exits 1.

## Test plan

- Scope unit tests prove cleanup order, identity, and exception behavior.
- Codec tests prove borrow versus transfer and independent JS results.
- Generator source tests prove one scope wraps each resource-bearing operation.
- Hermes-backed integration tests prove recursive behavior and native counters.
- Every in-scope failure phase needs a test: second field decode, authored
  method throw, async rejection, result encode failure, and abandoned async
  result settlement.
- No test may rely on finalizers or nondeterministic GC for the primary
  exactly-once assertion.

## Done criteria

- [ ] Plans 028 and 029 are DONE.
- [ ] Approved delta spec and change-local plan were committed first.
- [ ] One explicit, non-ambient conversion scope handles registrations.
- [ ] Registration deduplicates by reference identity and cleans in reverse.
- [ ] One scope-aware codec protocol composes through records and collections.
- [ ] `IJavaScriptCodec<T>` remains source-compatible.
- [ ] Recursive non-null ArrayBuffer works in records, lists, and dictionaries.
- [ ] Recursive non-null JavaScriptValue does not dispose borrowed sources or
  invalidate aliases.
- [ ] Partial decode and encode failures clean earlier registered leaves.
- [ ] Async input wrappers live until Task settlement, then release once.
- [ ] Return/property-get encoding calls `Transfer` before `EncodeBorrowed`.
- [ ] Existing direct resource event behavior and nested-resource event
  diagnostics are unchanged.
- [ ] Callback child codecs do not propagate conversion-scope ownership to the
  context-retained callback parameter.
- [ ] No ambient state or arbitrary `IDisposable` inference exists.
- [ ] Generator, runtime, and full managed tests pass with no skips.
- [ ] Format and privacy scans pass.
- [ ] Living specs and authoring guide are merged; plan 030 is DONE.

## STOP conditions

Stop and report if:

- The implementation needs thread-static, async-local, or global current scope.
- It can only compose by treating every `IDisposable` as boundary-owned.
- Adding the scoped protocol requires a source-breaking change to
  `IJavaScriptCodec<T>`.
- Async result encoding or any JSI access would occur outside the
  `ResolveOwned` runtime factory.
- A registered resource requires runtime-affine cleanup and therefore cannot be
  disposed by the existing async-parameter `finally` or `ResolveOwned` abandon
  path.
- Resource policy propagates through callback argument/result codecs or causes a
  callback to enter the conversion scope.
- An encoded JS result becomes invalid when the scope is disposed.
- Recursive ArrayBuffer works only by copying bytes without an approved
  contract change.
- Event encode consumes the author's payload wrapper.
- Alias deduplication cannot be proved with counters.
- An existing supported non-resource generated source path changes without a
  reason in the approved delta.
- A verification fails twice or an out-of-scope file is needed.

## Maintenance notes

This scope is the single cleanup mechanism for generated codec composition.
Future resource-bearing codecs must declare scoped behavior in
`ExpoCodecDescriptor` and implement the same protocol. Do not add another
per-parameter `OwnsDecodedValue` branch.

`JavaScriptValue?` remains intentionally unsupported: the non-null
`JavaScriptValue` API already exposes explicit null/undefined inspection.
Plans 031–033 add only the nullable families whose C# null representation is
useful and whose non-null boundary positions already work.
