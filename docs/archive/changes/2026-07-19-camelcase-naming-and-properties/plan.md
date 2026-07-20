# camelCase JavaScript Names And `[JS]` Properties Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the generated module surface idiomatic JavaScript by lower-camel-casing implicit `[JS]` method and record-field names, and by exposing valid `[JS]` instance properties as direct JavaScript accessor properties.

**Architecture:** The Roslyn generator continues to emit direct typed calls and codec expressions. It gains separate models for a record's C# and JavaScript names and a property model for generated accessor callbacks. A small public, generated-glue-only `GeneratedProperty` helper owns descriptor construction and context-bound callback registration; it uses existing JSI object/function wrappers to call `Object.defineProperty`, without a new ABI entry or scheduler hop.

**Tech Stack:** C# / Roslyn incremental generator, `Expo.ModulesCore`, `Expo.JSI`, Hermes testhost, xUnit, TypeScript, pnpm.

## Global Constraints

- Baseline: review the complete implementation diff from `536d3f0f` before each task; preserve unrelated changes on the shared `development` branch.
- C++ owns JSI mechanics, C# owns module logic, and the bridge remains the existing opaque-handle C ABI. Do not change `Expo.JSI`, native/C++, or the ABI for this plan.
- Generated bindings must use compile-time codecs and direct C# member access. Do not introduce reflection, dynamic invocation, JSON conversion, `object?[]`, or a scheduler hop on the generated property path.
- A parameterless `[JS]` lowercases only the first character invariantly. `[JS("ExactName")]` is verbatim.
- Record models must retain the C# member name separately from the JavaScript field name. Decode must never probe or fall back to a PascalCase JavaScript field.
- `[JS]` properties must be instance, non-indexed, readable through a public/internal ordinary getter, non-`init`, and codec-supported. A public/internal ordinary setter is writable; no setter or an inaccessible setter is read-only.
- Accessor descriptors are own properties, enumerable and configurable. Getter arity is `0`; writable setter arity is `1`; a read-only descriptor has no `set` member.
- Register every generated getter/setter through `DotnetRuntimeContext.RegisterHostFunction`. Context teardown invalidates all current and previously replaced registrations exactly once. A failed descriptor installation must leave registrations context-owned, never unowned.
- Owned wrappers include `JavaScriptValue`, `JavaScriptObject`, and `JavaScriptFunction`; dispose each returned wrapper unless ownership is explicitly transferred. `JavaScriptValueRef`, `JavaScriptObjectRef`, and `JavaScriptArguments` are callback-scoped and must not escape.
- `JavaScriptValueCodec.Decode` returns an owned retained wrapper. Generated property setters own it only for the synchronous invocation and dispose it in `finally`; a getter returning `JavaScriptValue` transfers that returned wrapper to the host-function bridge. Authored module code must retain a separate copy for later use.
- `JavaScriptObject` remains a future optional module-convertible. Do not add a `JavaScriptObject` codec in this change.
- Keep diagnostics `EXPOJSI004` and `EXPOJSI005` stable for method-only invalid/reserved and duplicate-method cases. Add and use `EXPOJSI014`–`EXPOJSI017` exactly as specified in `spec.md`.
- Do not publish, push, open a PR, or use a worktree. Before each commit, scan staged content for absolute paths, usernames, machine names, private hostnames, and machine-specific paths.

## File Map

| File | Responsibility |
| --- | --- |
| `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/JSAttribute.cs` | Public `[JS]` target and author-facing XML documentation. |
| `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/GeneratedFunction.cs` | Existing public generated-method helper; expose only the internal shared registration invocation needed by `GeneratedProperty`. |
| `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/GeneratedProperty.cs` | New public generated-glue-only accessor installation helper. |
| `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs` | Generator models: JavaScript record field name and property descriptors. |
| `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs` | `EXPOJSI014`–`EXPOJSI017`. |
| `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs` | Discovery, validation, collision filtering, source emission, and direct accessor callbacks. |
| `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs` | Generator source and diagnostic tests. |
| `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs` | Generated-module fixtures for methods, records, properties, and explicit `JavaScriptValue` retention. |
| `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModuleTests.cs` | Hermes-backed generated method, record, and property contract tests. |
| `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedPropertyTests.cs` | New direct runtime-helper lifecycle, replacement, failure-path, and temporary-wrapper tests independent of the generator. |
| `packages/example-module/dotnet/ExampleModule/ExampleMathModule.cs` | Real module removes redundant lower-camel names and supplies the documented read-only property. |
| `packages/example-module/src/index.ts` | Typed facade records use camelCase directly and exposes the accessor property. |
| `docs/module-authoring-guide.md` | Public authoring and ownership guidance. |
| `docs/specs/modules-core-boundary.md` | Durable normative requirements after acceptance. |
| `docs/plans/README.md` | Mark Plan 013 done only during accepted closure. |

## Task 1: Implicit Method camelCase and Consumer Migration

**Files:**

- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModuleTests.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedBinaryModuleTests.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedEventModuleTests.cs`
- Modify: every JavaScript call-site file found by the exhaustive Task 1 search (the current direct-match inventory is `GeneratedAttributeModuleTests.cs` and `GeneratedBinaryModuleTests.cs`; retain `GeneratedEventModuleTests.cs` in the review/staged set because it exercises generated implicit member naming through event behavior)
- Modify: `packages/example-module/dotnet/ExampleModule/ExampleMathModule.cs`
- Modify: `packages/example-module/src/index.ts`

**Consumes:** Existing `LowerCamel(string)` in `ExpoModulesGenerator.cs`; Plan 012's `DotnetModule` typed facade.

**Produces:** An implicit generated method name that is lower camel case and no PascalCase compatibility alias. All current in-repo JavaScript call sites and facade declarations compile against this contract.

- [ ] **Step 1: Establish the exact current contract and write failing generator/runtime tests.**

  In `ExpoModulesGeneratorTests.cs`, update `GeneratorEmitsDefaultAndExplicitFunctionNames` so the module has all three cases below and asserts the generated source contains the quoted JavaScript registration names, while keeping C# invocation names unchanged:

  ```csharp
  [JS]
  public double Add(double a, double b) => a + b;

  [JS]
  public string GetMessageAsync() => "message";

  [JS("ExactName")]
  public double Increment(double value) => value + 1.0;
  ```

  Required source assertions are `"add"`, `"getMessageAsync"`, and `"ExactName"`; assert `"Add"` is not used as a generated registration-name string. Keep assertions that generated direct calls are `module.Add(...)`, `module.GetMessageAsync(...)`, and `module.Increment(...)`.

  In the generated fixture/test pair, make an existing implicit method (`AddOneWhen`) execute through `addOneWhen` and assert a script evaluating both `typeof module.addOneWhen` and `typeof module.AddOneWhen` returns `"function:undefined"`. Keep the explicit `add` test to prove explicit names remain unchanged.

- [ ] **Step 2: Verify RED.**

  Run:

  ```sh
  dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter FullyQualifiedName~GeneratorEmitsDefaultAndExplicitFunctionNames
  scripts/test-managed.sh --filter FullyQualifiedName~GeneratedProviderDispatchesDefaultNamedSyncFunction
  ```

  Expected: both fail because generated implicit names are still `Add` / `AddOneWhen`, while the explicit `add` test remains green. If a test fails for compilation, fixture setup, or an unrelated existing failure, correct the test before implementation; do not make production changes until it fails for the missing lower-camel behavior.

- [ ] **Step 3: Implement only the implicit-name default.**

  In method discovery, replace the default assignment with:

  ```csharp
  var javaScriptName = LowerCamel(member.Name);
  if (jsAttribute.ConstructorArguments.Length == 1 &&
      jsAttribute.ConstructorArguments[0].Value is string explicitName)
  {
    javaScriptName = explicitName;
  }
  ```

  Do not change module-name derivation, explicit-name validation, or diagnostic IDs in this task. Existing duplicate/reserved checks must consume the now-resolved `javaScriptName` so implicit collisions are checked against their JavaScript names.

- [ ] **Step 4: Migrate all affected in-repo method callers in the same commit.**

  Remove only redundant `[JS("...")]` annotations in `ExampleMathModule.cs` where `LowerCamel(CSharpName)` is identical to the explicit text: `Add`, `GetMessageAsync`, `DescribeUser`, `TransformWithCallback`, and `EmitStatusAsync`. Do not remove a genuinely custom explicit name elsewhere.

  Update the `ExampleModuleType` method declarations and exported facade calls in `src/index.ts` only where a JavaScript name changes. Preserve the Plan 012 shape `declare class ExampleModuleType extends DotnetModule<ExampleModuleEvents>` and keep `requireDotnetModule<ExampleModule>` typed.

  Search and migrate every real JavaScript invocation of now-implicit module methods under `packages/`, `apps/`, and managed tests. The known in-scope callers include `GeneratedBinaryModuleTests.cs` and `GeneratedEventModuleTests.cs`; add every additional actual call-site file discovered by the exhaustive search to this task's staged set. Use `rg` results as a checklist; do not hide a stale PascalCase call by retaining an alias.

- [ ] **Step 5: Verify GREEN and scan the tree.**

  Run:

  ```sh
  dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj
  scripts/test-managed.sh --filter FullyQualifiedName~GeneratedAttributeModuleTests
  scripts/test-managed.sh
  rg -n 'globalThis\._expoDotnet\.modules\.[A-Za-z0-9_]+\.[A-Z][A-Za-z0-9_]*\(|\[JS\("(add|getMessageAsync|describeUser|transformWithCallback|emitStatusAsync)"\)' packages apps
  ```

  Expected: every test command exits `0`; the exhaustive search has no redundant example annotation or stale implicit PascalCase JavaScript call. Inspect every remaining hit before proceeding; it is valid only if it is test evidence that asserts the absence of the PascalCase alias.

- [ ] **Step 6: Review and commit the bounded slice.**

  Run `git diff --check`, review the complete diff against `536d3f0f`, and have a task reviewer verify both the direct-name source output and absence of compatibility aliases. After approval, commit only this task's files:

  ```sh
  git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModuleTests.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedBinaryModuleTests.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedEventModuleTests.cs \
    packages/example-module/dotnet/ExampleModule/ExampleMathModule.cs \
    packages/example-module/src/index.ts
  git diff --cached --name-only
  git diff --cached --check
  git diff --cached | rg -n '/Users/|/home/|[A-Za-z]:\\\\Users\\\\|localhost|127\\.0\\.0\\.1' && exit 1 || true
  git commit -m "feat(generator): default JS methods to camelCase names"
  ```

  STOP if lower-casing an implicit name creates a collision whose required diagnostic cannot be determined from the accepted spec. Do not choose declaration order or add a compatibility alias.

## Task 2: Separate Record JavaScript Names and Migrate Facades

**Files:**

- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedCodecExpansionModuleTests.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModuleTests.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/ExampleModuleShowcaseTests.cs`
- Modify: `packages/example-module/src/index.ts`

**Consumes:** Task 1's lower-camel `LowerCamel`, current `JavaScriptValueCodec`, current nullable codec semantics.

**Produces:** Record codecs read/write lower-camel JavaScript fields while retaining original C# property accesses; facades use those fields directly.

- [ ] **Step 1: Write failing source-output and Hermes behavior tests before changing the model.**

  Change record-codec source assertions to require generated code of this form:

  ```csharp
  var name = StringCodec.Decode(obj.GetProperty("name"), runtime);
  using var name = StringCodec.Encode(value.Name, runtime);
  obj.SetProperty("name", name);
  ```

  Add a `CodecNullableUser(string Name, string? Nickname)` fixture and assert generated output reads `name` and `nickname`, never `Name` or `Nickname` on the JavaScript side.

  In Hermes-backed tests, pass and receive `{ name: 'Ada', age: 37 }` directly. Add a required-string failure test:

  ```js
  try {
    globalThis._expoDotnet.modules.GeneratedRecords.rename({ Name: 'Ada', Age: 37 });
    'no error';
  } catch (error) {
    error instanceof Error;
  }
  ```

  Assert the result is `true`. Add a separate nullable case whose PascalCase-only input leaves the lower-camel nullable property absent and proves the existing nullable codec result is `null`; do not assert a new error policy for nullable fields.

- [ ] **Step 2: Verify RED.**

  Run:

  ```sh
  dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter 'FullyQualifiedName~GeneratorEmitsSimpleRecordCodecs|FullyQualifiedName~GeneratorEmitsNestedSimpleRecordCodecs'
  scripts/test-managed.sh --filter FullyQualifiedName~GeneratedCodecExpansionModuleTests
  ```

  Expected: source output and camelCase runtime tests fail because emitted codecs still use PascalCase fields. The required-string test must fail because stale `Name` is still accepted; nullable expectations must be checked against the current nullable codec, not guessed.

- [ ] **Step 3: Split the model fields and emit the correct name in each domain.**

  Replace the ambiguous record-field model with fields that make accidental cross-use difficult:

  ```csharp
  internal sealed record ExpoGeneratedRecordFieldModel(
      string ParameterName,
      string CSharpPropertyName,
      string JavaScriptName,
      string TypeName,
      string CodecExpression,
      Location? Location);
  ```

  Construct it with `property.Name` for `CSharpPropertyName` and `LowerCamel(property.Name)` for `JavaScriptName`. In `EmitRecordCodec`, all `obj.GetProperty("...")` and `obj.SetProperty("...")` strings use `JavaScriptName`; only `value.{...}` uses `CSharpPropertyName`. Do not add an `if`/fallback read for PascalCase fields.

- [ ] **Step 4: Migrate all actual consumers, including the Plan 012 facade.**

  In `ExampleModuleType`, express the direct contract:

  ```ts
  declare class ExampleModuleType extends DotnetModule<ExampleModuleEvents> {
    add(a: number, b: number): number;
    describeUser(user: ExampleUser): ExampleUserSummary;
  }
  ```

  Do not add `ready` in this task; Task 4 introduces the native property and its facade declaration together. Replace the body of `describeUser` with a direct call and return:

  ```ts
  return nativeModule.describeUser(user);
  ```

  Migrate all `GeneratedRecords` and example-module JavaScript test inputs/outputs to lower camel case. Search the full in-scope tree for quoted and object-literal forms of `Name`, `Age`, `Summary`, `Address`, `City`, and `Status`; classify each hit. Leave only C# record member accesses, C# type declarations, and explicit stale-PascalCase negative tests.

- [ ] **Step 5: Verify GREEN and the downstream consumer.**

  Run:

  ```sh
  scripts/test-managed.sh --filter FullyQualifiedName~GeneratedCodecExpansionModuleTests
  scripts/test-managed.sh --filter FullyQualifiedName~GeneratedAttributeModuleTests
  pnpm --filter mobile-app typecheck
  pnpm --filter desktop-app typecheck
  scripts/test-managed.sh
  ```

  Expected: every command exits `0`. The full suite catches generated-source compilation, Hermes behavior, and the module facade as a whole.

- [ ] **Step 6: Review and commit the bounded slice.**

  The reviewer must specifically verify the three emitted JavaScript string sites use `JavaScriptName`, `value.CSharpPropertyName` remains direct typed C# access, and no fallback reads exist. Stage exactly the Task 2 files (including `ExampleModuleShowcaseTests.cs`), inspect the staged set and staged diff, then scan the entire staged diff for local-machine data:

  ```sh
  git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedCodecExpansionModuleTests.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModuleTests.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/ExampleModuleShowcaseTests.cs \
    packages/example-module/src/index.ts
  git diff --cached --name-only
  git diff --cached --check
  git diff --cached | rg -n '/Users/|/home/|[A-Za-z]:\\\\Users\\\\|localhost|127\\.0\\.0\\.1' && exit 1 || true
  git commit -m "feat(generator): camelcase generated record fields"
  ```

  STOP if a required-field result differs from the existing required codec semantics, or if a real facade consumer needs a temporary PascalCase translation layer. Report the exact codec behavior and caller instead of introducing silent dual reads.

## Task 3: Public `GeneratedProperty` Descriptor Helper and Lifetime Proofs

**Files:**

- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/GeneratedProperty.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/GeneratedFunction.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedPropertyTests.cs`

**Consumes:** `DotnetRuntimeContext.RegisterHostFunction`, `GeneratedHostFunctionRegistration`, `JavaScriptRuntime.Global`, owned JSI wrapper APIs, and the existing `GeneratedFunction.DefineSync` callback ownership pattern.

**Produces:** A public cross-assembly generated-glue-only API:

```csharp
public static void Define(
    DotnetRuntimeContext runtimeContext,
    JavaScriptObject module,
    string name,
    JavaScriptHostFunction getter,
    JavaScriptHostFunction? setter,
    object context);
```

It creates a configurable/enumerable own accessor descriptor, with context-owned registrations, without depending on generator source emission.

- [ ] **Step 1: Write direct helper tests that fail because the helper does not exist.**

  Add `GeneratedPropertyTests.cs`; use `HermesRuntimeFixture`, a real `DotnetRuntimeContext`, and a plain module object. The first tests call `GeneratedProperty.Define` directly with callbacks that read/update a small managed state object and assert JavaScript sees:

  ```js
  const descriptor = Object.getOwnPropertyDescriptor(module, 'ready');
  [module.ready, descriptor.enumerable, descriptor.configurable,
   descriptor.get.length, descriptor.set.length].join(':')
  ```

  Expected result for a writable property: `"false:true:true:0:1"`; after `module.ready = true`, reading is `true`.

  Add independent tests for all required lifecycle cases:

  1. **Successful installation wrapper release:** reset testhost counters immediately before a single successful writable installation, then assert its own exact deterministic `ReleasedValues` delta after all local scopes end. In the test comment, inventory every owned wrapper and every `CallWithThis` clone: receiver `Object`, plus the target module, property-name string, and descriptor arguments. Also inventory global, `Object` property value/object, `defineProperty` value/function, descriptor, enumerable/configurable booleans, getter/setter functions, and each `AsValue` wrapper. Derive and assert the exact total from that inventory; do not use a loose `>` assertion.
  2. **Failed installation wrapper release and callback ownership:** replace `Object.defineProperty` in the test realm with a function that first saves `descriptor.get` to `globalThis.__capturedFailedGetter` and then throws `Error('define failed')`. Reset counters immediately before this call, assert `GeneratedProperty.Define` throws, and assert the failure path's separately derived exact `ReleasedValues` delta. Call the captured getter before `context.Dispose()` and assert its managed value still works; after disposal, call it again and assert a `DotnetRuntimeContext` error. This proves the failed call did not leak an unowned registration or prematurely invalidate it.
  3. **Replacement and captured function:** install getter A, capture `Object.getOwnPropertyDescriptor(module, 'ready').get`, install getter B with the same name, assert ordinary lookup uses B but `capturedA.call(module)` still uses A before teardown.
  4. **Teardown:** dispose the context, then call the previously captured getter and assert JavaScript receives an error containing `DotnetRuntimeContext`; repeat `context.Dispose()` and assert no double-disposal effect.

  Include a read-only installation test proving the descriptor lacks own `set` and strict-mode assignment returns a catchable `TypeError`. Do not route these callbacks through `GeneratedFunction.DefineSync`; the helper itself is what is under test.

- [ ] **Step 2: Verify RED.**

  Run:

  ```sh
  scripts/test-managed.sh --filter FullyQualifiedName~GeneratedPropertyTests
  ```

  Expected: compilation failure because `GeneratedProperty` does not exist. Do not add test-only production inspection APIs to make registrations observable.

- [ ] **Step 3: Implement the smallest public generated-glue-only helper.**

  Create `GeneratedProperty` as a `public static` class with XML documentation that says:

  - it is for generated binding glue, not ordinary module author code;
  - the runtime context owns getter/setter registrations through teardown;
  - the helper disposes all temporary owned wrappers after the synchronous descriptor installation call;
  - JavaScript itself retains descriptor accessor values.

  Validate every non-null argument and non-empty name. Register the getter and optional setter before constructing their host functions, using `runtimeContext.RegisterHostFunction(callback, context)`. Make `GeneratedFunction.InvokeGeneratedHostFunction` `internal static` (or extract an equivalently named internal shared trampoline) so both helpers invoke a `GeneratedHostFunctionRegistration` through the existing teardown-safe boundary; do not duplicate the lifetime policy.

  In `Define`, use named `using var` locals in this order so all owned paths are auditable. Keep the `CallWithThis` receiver and all three cloned arguments in the test's release-count inventory:

  ```csharp
  using var global = runtime.Global();
  using var objectValue = global.GetProperty("Object");
  using var objectConstructor = objectValue.AsObject();
  using var definePropertyValue = objectConstructor.GetProperty("defineProperty");
  using var defineProperty = definePropertyValue.AsFunction();
  using var propertyName = runtime.CreateString(name);
  using var descriptor = runtime.CreateObject();
  // boolean values, host-function wrappers, and AsValue wrappers are also named using locals
  using var ignoredResult = defineProperty.CallWithThis(objectConstructor, module, propertyName, descriptor);
  ```

  Fill the descriptor with `enumerable: true`, `configurable: true`, `get`, and, only if supplied, `set`. Create host function names only for diagnostics (for example `"<name> getter"` / `"<name> setter"`), with arities `0` and `1`. `Object.defineProperty` must receive `objectConstructor` as `this`, the module object, the name string, and the descriptor. Do not set the accessor on a prototype and do not call the runtime scheduler.

  Let `Object.defineProperty` exceptions propagate. The `using` scopes still release every temporary owned wrapper. Do not attempt to unregister a callback on failure: its registration is already context-owned and must remain invalidated by normal context disposal, matching method callback semantics.

- [ ] **Step 4: Verify GREEN, including exact cleanup.**

  Run:

  ```sh
  scripts/test-managed.sh --filter FullyQualifiedName~GeneratedPropertyTests
  scripts/test-managed.sh --filter FullyQualifiedName~DotnetRuntimeContextTests
  scripts/format.sh --check --all
  ```

  Expected: all exit `0`; no warnings; direct helper tests prove descriptor shape, failure cleanup, repeated replacement, captured-old callback behavior, exactly-once teardown invalidation, and the counter deltas.

- [ ] **Step 5: Complete the ownership review and commit.**

  Review each owned-wrapper acquisition from the helper against a same-scope `using`; verify no `JavaScriptValueRef`, `JavaScriptObjectRef`, or `JavaScriptArguments` reference is captured. Run:

  ```sh
  rg -n '\.AsValue\(\)\.(AsObject|AsArray|AsFunction)\(' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore
  rg -n '\.(AsObject|AsArray|AsFunction)\(\)\.(AsValue|AsObject|GetProperty)\(' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore
  git diff --check
  ```

  Classify every match in the task diff; replace a new fluent chain with named wrappers. Have a reviewer inspect only this task's diff for wrapper disposal and callback-registration ownership. Stage/inspect only the declared runtime-helper files, and scan the entire staged diff for local-machine data before committing:

  ```sh
  git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/GeneratedFunction.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/GeneratedProperty.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedPropertyTests.cs
  git diff --cached --name-only
  git diff --cached --check
  git diff --cached | rg -n '/Users/|/home/|[A-Za-z]:\\\\Users\\\\|localhost|127\\.0\\.0\\.1' && exit 1 || true
  git commit -m "feat(modules-core): add generated property helper"
  ```

  STOP if `Object.defineProperty` cannot be reached through existing public wrapper APIs, if calling it needs an ABI addition, or if the exact temporary wrapper count cannot be made deterministic. Report the missing API/counter evidence; do not add a native entry or weaken the assertion.

## Task 4: `[JS]` Property Discovery, Diagnostics, Generated Accessors, and End-to-End Proofs

**Files:**

- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/JSAttribute.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModuleTests.cs`
- Modify: `packages/example-module/dotnet/ExampleModule/ExampleMathModule.cs`
- Modify: `packages/example-module/src/index.ts`

**Consumes:** Task 1 naming, Task 2 record/codec conventions, Task 3's public `GeneratedProperty.Define`, `JavaScriptValueCodec` ownership rules.

**Produces:** Valid generated `[JS]` property source; stable diagnostics for invalid shapes/collisions; end-to-end direct accessor behavior.

- [ ] **Step 1: Add every generator and Hermes fixture/test before changing production code.**

  First change the generator test sources and `GeneratedAttributeModules.cs` / `GeneratedAttributeModuleTests.cs` together so `[JS]` is used on properties while `JSAttribute` is still method-only. The project must fail because the public feature is absent, not because tests are skipped. Add source-output tests for:

  ```csharp
  [JS] public bool Ready { get; set; }
  [JS] public bool IsReadOnly => true;
  [JS("isReady")] public bool ReadyWithExplicitName => true;
  [JS] internal string InternalGetter { get; private set; } = "internal";
  ```

  Assert emitted code calls `GeneratedProperty.Define`, uses `"ready"`, `"isReadOnly"`, and `"isReady"`, emits a getter callback with no argument-count requirement, emits a setter callback with `RequireArgumentCount("<module>.ready", arguments, 1)`, and omits a setter callback/argument for read-only or private-setter members.

  Add separate source-compilation tests which each require exactly one relevant diagnostic and inspect its message:

  | Source shape | Expected ID | Required message terms |
  | --- | --- | --- |
  | static, indexer, setter-only, inaccessible getter, or `init` accessor | `EXPOJSI014` | property name and unsupported shape |
  | readable `decimal` (or another no-codec type) property | `EXPOJSI015` | property name and unsupported type |
  | readable `Span<byte>` or `ReadOnlySpan<byte>` property | `EXPOJSI015` | property name and unsupported type |
  | two properties, or a method/property, resolving to one JavaScript name | `EXPOJSI016` | module and duplicate JavaScript name |
  | event module property resolving to `startObserving` / `stopObserving` | `EXPOJSI017` | property and reserved hook name |

  Keep explicit method-only regression tests proving duplicate methods remain `EXPOJSI005` and a method reserved observing hook remains `EXPOJSI004`.

  Add the full Hermes fixture now, before production implementation: writable/read-only/private-setter/internal-getter/explicit-name/throwing properties plus a `GeneratedPropertiesModule : IDisposable` with a disposable `JavaScriptValue` owner. The owner must use the actual lifetime pattern below, not an auto-property:

  ```csharp
  private JavaScriptValue? storedValue;

  [JS]
  public JavaScriptValue Value
  {
    get => storedValue?.Retain() ?? throw new InvalidOperationException("No value.");
    set
    {
      var retained = value.Retain();
      storedValue?.Dispose();
      storedValue = retained;
    }
  }

  public void Dispose()
  {
    storedValue?.Dispose();
    storedValue = null;
  }
  ```

  Write the end-to-end scripts now: descriptor flags/arity and read-write behavior; strict-mode `TypeError` for getter-only/private setter without authored side effects; explicit-name/no-alias; managed getter and codec setter errors; captured accessor teardown; and both `JavaScriptValue` proofs. For the setter proof, reset counters immediately before assignment, assert the exact release delta that includes generated invocation-wrapper disposal but not the module's retained owner, read through `Value` successfully, dispose the context/module, then assert the separately expected owner-release delta. For the getter proof, retrieve the returned value, then read the module's stored original/retained value again before disposal; do not use GC to infer either ownership edge.

- [ ] **Step 2: Verify RED.**

  Run:

  ```sh
  dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter FullyQualifiedName~Property
  scripts/test-managed.sh --filter FullyQualifiedName~GeneratedAttributeModuleTests
  ```

  Expected: the generator/property test source or Hermes fixture fails because `[JS]` does not permit properties and `GeneratedProperty` is absent; no production code has changed. A method-only regression failure is a test error to resolve before implementation.

- [ ] **Step 3: Update the public attribute and models.**

  Change the target declaration to:

  ```csharp
  [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, Inherited = false)]
  ```

  Add XML docs that state it marks generated JavaScript methods and instance accessor properties, unnamed members use lower camel case, explicit names are verbatim, readable properties become getters, accessible ordinary setters become setters, and authored users should use the generated module declaration rather than the helper APIs directly.

  Extend `ExpoModuleModel` with an `EquatableArray<ExpoPropertyModel> Properties` and define `ExpoPropertyModel` with, at minimum, `PropertyName`, `JavaScriptName`, `Location`, `TypeName`, `CodecExpression`, `HasSetter`, and `OwnsDecodedValue`. Store the C# property name and JavaScript name separately. Thread this array through model construction, equality, source generation, and diagnostics without turning properties into fake `ExpoFunctionModel` instances.

- [ ] **Step 4: Discover and validate properties before final collision filtering.**

  Add a `GetProperties(...)` pass alongside `GetFunctions(...)`. For every attributed `IPropertySymbol`, resolve the name with the same `LowerCamel` / explicit-name branch as methods and validate in this order:

  1. Reject static, indexed, missing public/internal getter, setter-only, and `init` properties with `EXPOJSI014` and no model.
  2. Resolve the property codec through `GetCodecExpression`; when absent, emit `EXPOJSI015` and no model. `Span<byte>` and `ReadOnlySpan<byte>` properties are explicitly unsupported and must produce `EXPOJSI015`, even though synchronous methods have special borrowed-span support; property codecs are limited to actual generated codecs, including `ArrayBufferCodec` and `JavaScriptValueCodec` where applicable.
  3. Treat only a public/internal ordinary setter as `HasSetter`; absent/private/protected setters are generated read-only and must not be diagnostics.
  4. On an `[Events]` module, reject a property resolving to an observing-hook name with `EXPOJSI017`.

  Preserve `GetFunctions` method validation behavior. After functions and properties are both collected, run one final name pass. It must:

  - keep `EXPOJSI005` for duplicate method/method names;
  - emit `EXPOJSI016` for property/property and method/property duplicates;
  - reject all colliding generated property output instead of relying on source order.

  Use the first relevant property/member locations and module/name arguments required by the delta spec. Do not silently omit a member, choose a winner, or defer validation to JavaScript runtime behavior.

- [ ] **Step 5: Emit direct getter and setter callbacks with precise ownership.**

  In each generated module registration, after event prototype setup and method registrations, emit one `GeneratedProperty.Define(...)` call per property. It receives the shared `context`, module object, resolved JavaScript name, getter callback, optional setter callback, and module instance.

  Emit generated callbacks with these required shapes:

  ```csharp
  private static global::Expo.JSI.JavaScriptValue Module_ready_Getter(
      global::Expo.JSI.JavaScriptRuntime runtime,
      global::Expo.JSI.JavaScriptValueRef thisValue,
      global::Expo.JSI.JavaScriptArguments arguments,
      object context)
  {
    GeneratedFunction.RequireArgumentCount("Module.ready", arguments, 0);
    var module = (global::Namespace.ModuleType)context;
    // Encode the direct property read using the same special cases as synchronous methods.
  }
  ```

  The setter must require exactly one argument, decode `arguments.GetValue(0)` through the compile-time codec, assign `module.PropertyName = __expoValue`, and return `runtime.CreateUndefined()`.

  For normal codecs, decode into a `var`. For owning codecs, decode into `using var __expoValue` so disposal happens whether the direct property assignment succeeds or throws. The setter never stores/captures a scoped ref. For a `JavaScriptValue` setter, this is required:

  ```csharp
  using var __expoValue = JavaScriptValueCodec.Decode(arguments.GetValue(0), runtime);
  module.Value = __expoValue;
  return runtime.CreateUndefined();
  ```

  The authored setter must be documented/tested not to dispose the invocation-owned `__expoValue`; it keeps a long-lived value only by `__expoValue.Retain()` into its own owned field, disposing the previous retained field. For a `JavaScriptValue` getter, return the authored retained copy directly through `JavaScriptValueCodec.Encode`; do not wrap it in `using`, because host-function return ownership transfers to the bridge. Match existing special handling only for actual property codecs such as `ArrayBufferCodec` and `JavaScriptValueCodec`. Do not emit any property path for `Span<byte>` or `ReadOnlySpan<byte>`.

  Generator callbacks are direct host functions. They must not invoke `Runtime.Execute`, schedule work, or use reflection/dynamic invocation. Managed getter exceptions and codec decode exceptions must flow through the existing host-function boundary as catchable JavaScript errors.

- [ ] **Step 6: Verify the prewritten fixtures turn GREEN after emission.**

  Run the exact generator and Hermes tests written in Steps 1–2. They must now prove own descriptor flags/arity, read/write behavior, strict-mode read-only behavior, explicit-name/no-alias, catchable getter/codec errors, registration teardown, and the concrete `JavaScriptValue` retained-copy/counter lifecycle. Keep `JavaScriptObject` out of every fixture: it has no supported module codec in this plan.

- [ ] **Step 7: Migrate the example to the final authoring surface.**

  Add a read-only `[JS] public bool Ready => true;` (or equivalent stable module state) to `ExampleMathModule`. In the typed `ExampleModuleType`, add `readonly ready: boolean`. Do not add a facade wrapper that calls a method, and do not represent the registry object as an actual `DotnetModule` instance. Update tests/typechecks that use the facade if needed.

- [ ] **Step 8: Verify GREEN with focused, full, and ownership gates.**

  Run, in order:

  ```sh
  dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj
  scripts/test-managed.sh --filter FullyQualifiedName~GeneratedPropertyTests
  scripts/test-managed.sh --filter FullyQualifiedName~GeneratedAttributeModuleTests
  pnpm --filter mobile-app typecheck
  pnpm --filter desktop-app typecheck
  scripts/test-managed.sh
  rg -n 'Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\?\[\]|JsonSerializer' packages/expo-modules-dotnet/managed
  rg -n '\.AsValue\(\)\.(AsObject|AsArray|AsFunction)\(' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore
  rg -n '\.(AsObject|AsArray|AsFunction)\(\)\.(AsValue|AsObject|GetProperty)\(' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore
  ```

  Expected: every test/typecheck command exits `0`; the reflection scan has no generated-binding hot-path match; every owned-wrapper scan match in the task diff is documented and safe, with no hidden fluent conversion introduced by property installation/emission.

- [ ] **Step 9: Review and commit the bounded slice.**

  The task reviewer must check diagnostics precedence/IDs, the exact property accessibility rules, absence of PascalCase/property aliases, descriptor shape, generated `using` disposal for owning setter decode, and cross-assembly API docs. Stage all and only Task 4 implementation/tests/example files, inspect the staged names and full staged diff, and scan all staged content for local-machine data before committing:

  ```sh
  git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/JSAttribute.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModuleTests.cs \
    packages/example-module/dotnet/ExampleModule/ExampleMathModule.cs \
    packages/example-module/src/index.ts
  git diff --cached --name-only
  git diff --cached --check
  git diff --cached | rg -n '/Users/|/home/|[A-Za-z]:\\\\Users\\\\|localhost|127\\.0\\.0\\.1' && exit 1 || true
  git commit -m "feat(generator): support JS accessor properties"
  ```

  STOP if public/internal property accessibility cannot be determined from Roslyn symbols without changing the accepted shape, a property special codec needs a new runtime feature, or testhost counters cannot prove the `JavaScriptValue` transfer. Report the concrete type/branch/counter result instead of adding reflection, an ABI entry, or a weak test.

## Task 5: Documentation, Living-Spec Merge, Archive, and Final Verification

**Files:**

- Modify: `docs/module-authoring-guide.md`
- Modify: `docs/specs/modules-core-boundary.md`
- Modify: `docs/plans/README.md`
- Move: `docs/changes/2026-07-19-camelcase-naming-and-properties/` to `docs/archive/changes/2026-07-19-camelcase-naming-and-properties/`

**Consumes:** Accepted Tasks 1–4 behavior and their fresh verification evidence.

**Produces:** Current docs are authoritative; the transient delta and implementation plan are archived; Plan 013 status records completion.

- [ ] **Step 1: Write docs tests/checks before editing prose.**

  Identify each accepted `spec.md` requirement and map it to a durable section in `docs/specs/modules-core-boundary.md`: implicit/explicit method names, lower-camel record fields with no fallback, accessor shape/restrictions, direct typed execution, lifetime ownership, diagnostics, migration/strict-mode behavior. Note the target headings in the task report so review can prove no requirement is silently lost.

  Use searches as pre-edit negative checks:

  ```sh
  rg -n 'PascalCase|\[JS\("add"\)\]|property|Properties|JavaScriptValue' docs/module-authoring-guide.md docs/specs/modules-core-boundary.md
  ```

  Expected: current docs are missing or contradicting at least one new contract, proving the documentation change is necessary.

- [ ] **Step 2: Merge accepted requirements into the living spec.**

  Follow the existing `Requirement` / `Scenario` GIVEN-WHEN-THEN style. State exactly:

  - parameterless `[JS]` is lower camel and explicit names are verbatim;
  - record encode/decode uses lower-camel JavaScript names only and missing-field behavior belongs to each existing codec;
  - valid property shapes, public/internal access rules, descriptor ownership/shape, read-only strict-mode `TypeError`, and error propagation;
  - generated direct calls/codec and `JavaScriptValue` retain/dispose rules;
  - `EXPOJSI014`–`017` and preservation of `004`/`005` method-only meanings.

  Do not document a `JavaScriptObject` codec or an author-facing need to manipulate JSI wrappers to define ordinary properties.

- [ ] **Step 3: Update the authoring guide with the friendly surface and ownership rule.**

  Replace redundant explicit lower-camel method annotation examples with parameterless `[JS]`; show their JavaScript lower-camel calls. Update record examples to pass/receive lower-camel fields directly, with no translation object. Add a concise properties subsection showing read/write and getter-only syntax, direct JS access, and strict-mode assignment behavior. Explain that a property accepting `JavaScriptValue` gets an invocation-owned wrapper that the author must not store/dispose; code that needs it later calls `Retain()` and owns that copy. Mention `JavaScriptObject` is not yet a module convertible only if it is needed to prevent confusion; do not broaden scope.

- [ ] **Step 4: Archive and update status only after implementation is accepted.**

  Confirm all Tasks 1–4 have review approval and the living spec matches implemented behavior. Update the Plan 013 row in `docs/plans/README.md` to `DONE` with the actual completion date. Move the entire change directory, including `spec.md` and this plan, with:

  ```sh
  git mv docs/changes/2026-07-19-camelcase-naming-and-properties \
    docs/archive/changes/2026-07-19-camelcase-naming-and-properties
  ```

  Do not leave a duplicate transient `docs/changes` copy.

- [ ] **Step 5: Format after archive/status edits, stage the complete closure, and run final verification.**

  Run:

  ```sh
  git add docs/module-authoring-guide.md docs/specs/modules-core-boundary.md docs/plans/README.md \
    docs/archive/changes/2026-07-19-camelcase-naming-and-properties
  dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj
  scripts/test-managed.sh
  pnpm --filter mobile-app typecheck
  pnpm --filter desktop-app typecheck
  scripts/format.sh --check --all
  rg "self[-]contained planning package|planning[ ]artifacts,[ ]not[ ]implementation|expo[-]modules[-]windows[-]core|Phase[ ]1:[ ]clean[ ]separate[ ]research[ ]repo|create[ ]a[ ]clean[ ]local[ ]research[ ]repository" docs/README.md docs/specs docs/roadmap.md AGENTS.md .agents/skills
  git diff --cached --name-only
  git diff --cached --name-status
  test ! -e docs/changes/2026-07-19-camelcase-naming-and-properties
  git diff --cached --check
  git diff --cached | rg -n '/Users/|/home/|[A-Za-z]:\\\\Users\\\\|localhost|127\\.0\\.0\\.1' && exit 1 || true
  ```

  Expected: all required commands exit `0`; the obsolete-planning scan has no unintended matches; `git diff --cached --name-only` contains all intended docs and the entire archived change folder; `git diff --cached --name-status` records the change-folder rename/deletions, and the source change directory no longer exists; the privacy scan includes archive content and produces no committed local-machine data. If formatting changes files, run `scripts/format.sh`, review and stage the formatter diff, then repeat every check after staging.

- [ ] **Step 6: Commit closure, then review the complete committed range.**

  Commit the already reviewed/staged closure artifacts:

  ```sh
  git add docs/module-authoring-guide.md docs/specs/modules-core-boundary.md docs/plans/README.md \
    docs/archive/changes/2026-07-19-camelcase-naming-and-properties
  git commit -m "docs: close camelcase property bindings change"
  ```

  Record the closure commit ID, produce the whole-branch review package for `536d3f0f^..closure-commit`, and request a reviewer. The review must cover all five accepted requirements, especially: no stale field fallback, no behavior-changing PascalCase aliases, diagnostics stability, callback teardown, exact wrapper ownership, and docs/code alignment. For each finding, make a focused follow-up commit, rerun the covering tests, regenerate the package through the follow-up commit, and obtain re-review. Do not rewrite the closure commit or claim completion until the final re-review is clean.

  STOP if docs closure would assert a behavior not proved by Tasks 1–4 or if the final reviewer finds a scope/design contradiction. Escalate with the exact requirement and evidence.

## Execution and Review Protocol

1. Dispatch one fresh implementer per task, never concurrent implementation agents because Tasks 2–5 build on prior generator/runtime state.
2. Each implementer follows TDD: add the named behavior tests first, run the named RED command and report why it failed, then write the minimum implementation and run the named GREEN command.
3. For each task, generate a review package with the task baseline commit and dispatch an independent task reviewer. Do not advance with unresolved Critical/Important findings.
4. Treat task reports and reviewed commit IDs as the durable checkpoints. Record Minor findings in the final reviewer handoff; do not create an additional tracking file.
5. The final reviewer receives the package from `536d3f0f^` through the closure commit, not `HEAD~1`, so it sees the full feature, accepted delta, migration, and docs closure.

## Final Done Criteria

- [ ] Every task was implemented test-first, reviewed, and recorded by its report and reviewed commit ID.
- [ ] Implicit methods are lower camel, explicit names stay verbatim, and no PascalCase method compatibility aliases remain.
- [ ] Record JavaScript fields are lower camel with no PascalCase fallback; required and nullable missing-field behavior is covered by real codec tests.
- [ ] `GeneratedProperty.Define` is public/documented generated-glue-only API, uses `Object.defineProperty`, keeps all callbacks context-owned, and releases all temporary wrappers on success and failure.
- [ ] `[JS]` properties generate direct typed getter/setter callbacks, correct descriptor shape, read-only semantics, errors, and `JavaScriptValue` ownership proof.
- [ ] `EXPOJSI014`–`EXPOJSI017` are covered, and `EXPOJSI004`/`EXPOJSI005` method-only behavior remains stable.
- [ ] Example facade directly uses `ExampleUser` / `ExampleUserSummary`, extends `DotnetModule<ExampleModuleEvents>`, and exposes `readonly ready: boolean`.
- [ ] The direct generator suite, `scripts/test-managed.sh`, both app typechecks, `scripts/format.sh --check --all`, and `git diff --cached --check` all exit `0` from the final staged tree.
- [ ] Ownership/reflection scans are reviewed; no new hidden owned-wrapper chain, hot-path reflection, `JavaScriptObject` codec, ABI change, scheduler hop, local-machine path, or unapproved scope expansion remains.
- [ ] `docs/specs/modules-core-boundary.md` is updated, the change folder is archived, and Plan 013 is marked `DONE`.
