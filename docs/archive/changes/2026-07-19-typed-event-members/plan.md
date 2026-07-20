# Typed Event Members Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> `superpowers:subagent-driven-development` (recommended) or
> `superpowers:executing-plans` to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add generated, cached `[Event]` properties whose `Func<Task>` or
`Func<T, Task>` invocation dispatches a declared module event and returns its
real completion task.

**Architecture:** The Roslyn generator validates typed event members, merges
their names with legacy `[Events]`, emits the authored module's partial
property implementation, and has the generated provider inject codec-specific
delegates before `OnCreate`. `ModuleEventEmitter` keeps the existing scheduling
path; two overloads add the distinct ownership rules required by direct
`JavaScriptValue` and `ArrayBuffer` payloads. No native ABI or JavaScript
prototype changes are needed.

**Tech Stack:** C# 14 / .NET 10, Roslyn incremental generator, `Expo.ModulesCore`,
`Expo.JSI`, Hermes testhost, xUnit, TypeScript, pnpm.

## Global Constraints

- Baseline: start from `a671c0be`, the approved typed-event delta-spec commit,
  and preserve unrelated work on the shared `development` branch.
- C++ owns JSI mechanics, C# owns module logic, and the bridge remains the
  existing opaque-handle C ABI. Do not change native/C++, the ABI version, or
  platform adapters.
- The authoring types are exactly `Func<Task>` and `Func<T, Task>`. Do not add
  `Action`, `async void`, task dropping, synchronous blocking, or a global
  failure sink.
- Default event names lowercase only the first property character. Explicit
  `[Event("name")]` names are verbatim; never strip `On`.
- Keep `[Events]` and `SendEventAsync` working as the legacy path. Typed and
  legacy names merge before NativeModule selection, event attachment,
  observing-hook validation, and reserved-name checks.
- Generated bindings use direct typed calls and compile-time codecs. Do not add
  reflection, `dynamic`, JSON conversion, `object?[]`, or hot-path type tests.
- The provider owns generated record codec references and injects dispatch
  delegates. The generated module partial must not widen codec visibility.
- An initialized delegate invocation always returns a non-null task. Target,
  codec, scheduler, disposed-context, and teardown errors fault or cancel that
  task instead of escaping from `Func.Invoke`.
- A direct `ArrayBuffer` gets an invocation-owned lease before the task is
  returned. A direct `JavaScriptValue` is retained only during owning-runtime
  access, so the caller keeps the original alive until task completion.
- Reject callbacks and nested `JavaScriptValue` / `ArrayBuffer` before event
  codec resolution mutates generated record codecs. Do not add a
  `JavaScriptObject` codec in this change.
- Owned JSI wrappers are disposed exactly once. Scoped refs never escape their
  callback. Keep listener exception isolation unchanged.
- Do not publish, push, open a PR, or use a worktree. Before every commit,
  inspect staged paths, run `git diff --cached --check`, and scan staged content
  for local absolute paths, usernames, machine names, private hostnames, and
  machine-specific install paths.

## File Map

| File | Responsibility |
| --- | --- |
| `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/EventAttribute.cs` | Public typed-event attribute and author-facing XML documentation. |
| `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/ModuleEventEmitter.cs` | Existing scheduling plus direct `JavaScriptValue` / `ArrayBuffer` ownership overloads. |
| `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs` | Typed-event, payload-kind, namespace, and generated-partial models. |
| `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs` | Diagnostics `EXPOJSI018`-`EXPOJSI020`. |
| `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs` | Event discovery, preflight safety, name merge, partial source, provider factories, delegate injection. |
| `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs` | Source, compilation, lifecycle-shape, and diagnostic coverage. |
| `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/ModuleEventEmitterTests.cs` | Direct payload scheduling and ownership tests independent of source generation. |
| `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs` | Typed and legacy generated-module fixtures. |
| `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedEventModuleTests.cs` | Hermes delivery, lifecycle, hooks, coexistence, failure, and lifetime tests. |
| `packages/example-module/dotnet/ExampleModule/ExampleMathModule.cs` | Reference module migration to `[Event]`. |
| `docs/module-authoring-guide.md` | Preferred typed-event API, legacy migration path, task and payload lifetime rules. |
| `docs/specs/modules-core-boundary.md` | Durable accepted typed-event requirements. |
| `docs/plans/README.md` | Plan 014 completion status. |
| `docs/archive/changes/2026-07-19-typed-event-members/` | Accepted delta spec and completed transient plan. |

---

### Task 1: Direct Payload Runtime Ownership

**Files:**

- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/ModuleEventEmitter.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/ModuleEventEmitterTests.cs`

**Interfaces:**

- Consumes: existing `ModuleEventEmitter.ExecuteEventAsync`, `GetTarget`,
  `ArrayBuffer.Retain()`, and runtime-affine `JavaScriptValue.Retain()`.
- Produces:

  ```csharp
  public Task EmitAsync(
      object module,
      string eventName,
      JavaScriptValue payload,
      CancellationToken cancellationToken = default);

  public Task EmitAsync(
      object module,
      string eventName,
      ArrayBuffer payload,
      CancellationToken cancellationToken = default);
  ```

  `ExecuteEventAsync` also gains a managed emitter-liveness check before it
  reads `context.Runtime`, `CanExecuteSync`, or `HasExclusiveRuntimeAccess`.

- [ ] **Step 1: Write failing direct-wrapper tests.**

  Add a fixture helper that creates a NativeModule, attaches one managed
  module object with `new[] { "onValue", "onBuffer" }`, and installs listeners
  which copy the received value into globals. Add these tests:

  ```csharp
  [Fact]
  public async Task JavaScriptValuePayloadKeepsCallerOwnerAndReleasesInvocationCopy()
  {
    // Create the caller-owned value during runtime access, disable synchronous
    // execution, invoke off-runtime, resume, and await the returned task.
    // Prove the original still reads the same string after dispatch. Record the
    // release count after dispatch, dispose the original, and assert disposal
    // adds exactly one release, so dispatch did not consume caller ownership.
  }

  [Fact]
  public async Task CrossRuntimeJavaScriptValueReturnsFaultedTask()
  {
    // Create the payload in runtime A, attach the event target in runtime B,
    // and invoke through B. Assert invocation returns a Task and awaiting it
    // fails at the scope check before any A handle is cloned or inspected.
  }

  [Fact]
  public async Task ArrayBufferPayloadRetainsLeaseBeforeScheduling()
  {
    // Decode a JavaScript-backed ArrayBuffer before resetting counters. Pause
    // the executor, invoke off-runtime, dispose the original immediately after
    // invocation returns, resume, and assert JS received the bytes.
    // The caller and invocation leases share one native long-lived entry, so
    // terminal cleanup must produce exactly one LongLivedArrayBuffersReleased
    // and zero abandoned entries after the original is disposed.
  }

  [Theory]
  [InlineData("value")]
  [InlineData("buffer")]
  public async Task DisposedDirectPayloadReturnsFaultedTask(string kind)
  {
    // Dispose before invocation. Assert the call itself returns a non-null Task,
    // then Assert.ThrowsAsync<ObjectDisposedException> when it is awaited.
  }


  [Fact]
  public async Task DisposedEmitterFaultsBeforeRuntimeScheduling()
  {
    // Capture the emitter, dispose its DotnetRuntimeContext, reset counters,
    // invoke, and await the fault. Assert SyncExecuteCalls and released runtime
    // task-context counters do not change; a dropped-task sentinel scheduled
    // afterward must consume the next-drop marker, proving this invocation did
    // not enqueue work.
  }
  ```

  Keep the caller wrapper alive through the `JavaScriptValue` task. For
  `ArrayBuffer`, deliberately dispose it immediately after `EmitAsync` returns
  and before queued runtime work is allowed to run.

- [ ] **Step 2: Verify RED.**

  Run:

  ```sh
  dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj --filter FullyQualifiedName~ModuleEventEmitterTests
  ```

  Expected: compilation fails because the two direct-payload overloads do not
  exist. The test setup must use the current NativeModule and event attachment
  APIs; do not implement a second emitter.

- [ ] **Step 3: Implement the two overloads on the existing scheduler path.**

  The `JavaScriptValue` overload must retain inside the callback passed to
  `ExecuteEventAsync`:

  ```csharp
  public async Task EmitAsync(
      object module,
      string eventName,
      JavaScriptValue payload,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(module);
    ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
    ArgumentNullException.ThrowIfNull(payload);

    await ExecuteEventAsync(
        runtime =>
        {
          using var invocationPayload = payload.Ref.Retain();
          using var target = GetTarget(module, eventName);
          using var eventNameValue = runtime.CreateString(eventName);
          using var emitValue = target.Target.GetProperty("emit");
          using var emit = emitValue.AsFunction();
          using var result = emit.CallWithThis(
              target.Target,
              eventNameValue,
              invocationPayload);
          return true;
        },
        cancellationToken
    ).ConfigureAwait(false);
  }
  ```

  The `ArrayBuffer` overload must enter its async state machine, retain before
  the first `await`, and dispose the lease after scheduled work terminates:

  ```csharp
  public async Task EmitAsync(
      object module,
      string eventName,
      ArrayBuffer payload,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(module);
    ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
    ArgumentNullException.ThrowIfNull(payload);

    using var invocationPayload = payload.Retain();
    await ExecuteEventAsync(
        runtime =>
        {
          using var eventValue = invocationPayload.Encode(runtime);
          return Emit(runtime, module, eventName, eventValue);
        },
        cancellationToken
    ).ConfigureAwait(false);
  }
  ```

  Factor only the repeated target/event-name/`emit.CallWithThis` sequence into
  a private overload if needed. Do not route the caller-owned
  `JavaScriptValue` through `JavaScriptValueCodec.Encode`, because that codec
  returns the same wrapper and the generic emitter disposes its encoded value.
  `payload.Ref.Retain()` is required: `Ref` validates that the currently active
  handle scope belongs to the payload's runtime before cloning its handle.
  Add XML remarks documenting when each caller may dispose its original.

  At the start of `ExecuteEventAsync`, lock the emitter gate only long enough to
  call `ThrowIfDisposedLocked()`, then release it before selecting or entering a
  scheduler path. This makes invocation after completed context teardown fault
  before any runtime state is queried without holding the emitter lock across
  a callback that later calls `GetTarget`.

- [ ] **Step 4: Verify task faults and exact cleanup.**

  Run:

  ```sh
  dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj --filter FullyQualifiedName~ModuleEventEmitterTests
  scripts/test-managed.sh --filter FullyQualifiedName~ModuleRegistryTests
  ```

  Expected: all tests pass; disposed/cross-runtime arguments return faulted
  tasks rather than throwing from invocation; invoking a disposed emitter does
  not query or queue runtime work; the shared ArrayBuffer entry is released
  exactly once with zero abandonments; existing listener isolation stays green.

- [ ] **Step 5: Review and commit the runtime slice.**

  Have a lifetime-aware reviewer check retain timing, every terminal cleanup
  path, caller ownership, and reuse of `ExecuteEventAsync`. Then stage only the
  two Task 1 files and commit:

  ```sh
  git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/ModuleEventEmitter.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/ModuleEventEmitterTests.cs
  git diff --cached --check
  git commit -m "feat(modules-core): preserve typed event payload ownership"
  ```

### Task 2: Typed Event Generator And Provider Initialization

**Files:**

- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/EventAttribute.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`

**Interfaces:**

- Consumes: Task 1's direct-payload overloads, existing compile-time codec
  resolution, `ModuleRegistry.GetOrCreateModule`, and provider-private record
  codecs.
- Produces: public `EventAttribute`; `ExpoEventModel`; payload kind
  `None`, `Codec`, `JavaScriptValue`, or `ArrayBuffer`; a module partial with
  cached event properties; and provider helpers equivalent to:

  ```csharp
  private static DeviceModule CreateDevice(DotnetRuntimeContext context)
  {
    var module = new DeviceModule(context);
    InitializeDeviceEvents(context, module);
    return module;
  }

  private static void InitializeDeviceEvents(
      DotnetRuntimeContext context,
      DeviceModule module)
  {
    var emitter = context.Events;
    module.__ExpoModulesCoreInitializeEvents(
        context,
        () => emitter.EmitAsync(module, "onReady"),
        value => emitter.EmitAsync<ProgressCodec, Progress>(module, "onProgress", value));
  }
  ```

- [ ] **Step 1: Add failing valid-source and provider-order tests.**

  Add generator tests using a top-level partial module with payload-less,
  scalar, record, direct `JavaScriptValue`, and direct `ArrayBuffer` events.
  Assert two generated sources (provider plus module partial) compile without
  C# errors and contain:

  ```csharp
  [Event]
  public partial Func<Task> OnReady { get; }

  [Event("StatusChanged")]
  internal partial Func<Progress, Task> OnProgress { get; }
  ```

  Required output assertions:

  - event names are `onReady` and verbatim `StatusChanged`, never `ready`;
  - the generated partial uses cached delegate fields and an uninitialized
    getter throws `InvalidOperationException`;
  - provider factory initialization appears before the generated
    `GetOrCreateModule` path can invoke `OnCreate`;
  - provider calls initialization again after `GetOrCreateModule` for an
    existing instance;
  - payload-less/scalar/record calls use the correct existing emitter overload,
    with the record codec referenced only inside the provider;
  - direct wrappers call Task 1's non-generic overloads;
  - parameterless and `DotnetRuntimeContext` constructor strategies both call
    their generated create-and-initialize helper.

- [ ] **Step 2: Add failing diagnostics and mutation-safety tests.**

  Add data-driven `EXPOJSI018` tests for null/empty/blank explicit names,
  static/indexed/non-partial/setter/body/explicit-interface/ref-return/`[JS]`
  properties, `Action`/wrong `Func`, unsupported property modifiers, and
  file-local/nested/generic/non-partial containers. Add `EXPOJSI019` tests for
  unsupported codecs, callback payloads, and nested direct wrappers. Add
  `EXPOJSI020` tests for typed/typed and typed/legacy duplicate names while
  retaining `EXPOJSI009` for legacy-only errors. For every rejected member that
  is nevertheless a reproducible, valid partial-property definition, assert
  the generator emits an inert matching implementation and the compilation has
  no secondary `CS9248` or other C# error. This includes invalid names, wrong
  delegate types, `[Event]` plus `[JS]`, and duplicate-name cases. Container or
  member shapes which cannot be reproduced safely get only `EXPOJSI018` and no
  attempted generated declaration.

  For each record, list, and dictionary containing `JavaScriptCallback`, assert:

  ```csharp
  Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI019");
  Assert.DoesNotContain("JavaScriptCallbackCodec", GeneratedText(result));
  Assert.DoesNotContain(
      result.Diagnostics,
      item => item.Id.StartsWith("CS", StringComparison.Ordinal));
  ```

  Add this test-only helper in `ExpoModulesGeneratorTests.cs` so the assertion
  covers every generated file:

  ```csharp
  private static string GeneratedText(GeneratorRunResult result) =>
      string.Join("\n", result.GeneratedSources.Select(source => source.Text));
  ```

  Also prove a record's unrelated computed callback property is ignored when
  its selected constructor parameters are event-safe.

- [ ] **Step 3: Verify RED.**

  Run:

  ```sh
  dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter 'FullyQualifiedName~TypedEvent|FullyQualifiedName~EventProperty'
  ```

  Expected: sources using `[Event]` fail because the attribute and generated
  partial do not exist, and diagnostic assertions fail because IDs 018-020 are
  not defined.

- [ ] **Step 4: Add the public attribute, models, and diagnostics.**

  Implement `EventAttribute` with property-only, single-use, non-inherited
  usage and default/explicit-name constructors:

  ```csharp
  [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
  public sealed class EventAttribute : Attribute
  {
    public EventAttribute() { }

    public EventAttribute(string name) => Name = name;

    public string? Name { get; }
  }
  ```

  Document the exact delegate types, naming rule, returned-task contract,
  registration timing, and direct-wrapper ownership. Add descriptors
  `UnsupportedEventProperty` (`EXPOJSI018`), `UnsupportedEventPayload`
  (`EXPOJSI019`), and `DuplicateEventName` (`EXPOJSI020`) with messages that
  name the module/property and rejected reason.

  Extend `ExpoModuleModel` with namespace/simple-type information and an
  `EquatableArray<ExpoEventModel>`. The event model must separately retain the
  C# property name, resolved JavaScript name, accessibility text, exact delegate
  and payload type names, payload kind, codec expression, location, and whether
  a shape-valid property is dispatchable.

- [ ] **Step 5: Validate shape and payloads before general codec mutation.**

  Discover `[Event]` properties before observing hooks, functions, and `[JS]`
  properties. Validate the accepted syntax from `spec.md` using both symbol and
  `PropertyDeclarationSyntax` data. Skip `[Event]` members in ordinary `[JS]`
  property discovery so `[Event] [JS]` reports only its intended typed-event
  diagnostic.

  Implement a recursive preflight with a recursion guard. It must inspect only:

  - the selected public/internal record constructor parameters;
  - `IReadOnlyList<T>` elements;
  - `Dictionary<string, T>` / `IReadOnlyDictionary<string, T>` values;
  - nullable value types.

  A top-level `JavaScriptValue` or `ArrayBuffer` selects its dedicated payload
  kind. Either wrapper below the top level, or `JavaScriptCallback` at any
  level, reports `EXPOJSI019` before `GetCodecExpression` runs. Resolve other
  payload codecs against a scratch copy of `recordCodecs`; merge new record
  codecs only after resolution succeeds. Keep a shape model for every rejected
  member whose partial-property implementation can still be reproduced safely.
  Those models, including `EXPOJSI018`, `EXPOJSI019`, and `EXPOJSI020` cases,
  receive inert backing fields/getters but are omitted from provider
  initialization and event registration. The consuming compilation must report
  the Expo diagnostic without `CS9248`, invalid callback `Encode` output, or
  another secondary C# error.

- [ ] **Step 6: Merge names and emit the module partial.**

  Merge valid typed names after validating legacy `[Events]`. Preserve source
  order, report `EXPOJSI020` for typed/typed or typed/legacy collisions, and
  feed the merged set to NativeModule selection, attachment, observing hooks,
  and reserved `startObserving` / `stopObserving` checks.

  Emit one additional source per event-bearing module in its authored namespace:

  ```csharp
  partial class DeviceModule
  {
    private readonly object __expoEventInitializationGate = new();
    private DotnetRuntimeContext? __expoEventContext;
    private Func<Task>? __expoEvent_OnReady;

    public partial Func<Task> OnReady
    {
      get => __expoEvent_OnReady ?? throw new InvalidOperationException(
          "Event member 'DeviceModule.OnReady' is unavailable before module registration.");
    }

    internal void __ExpoModulesCoreInitializeEvents(
        DotnetRuntimeContext context,
        Func<Task> onReady)
    {
      lock (__expoEventInitializationGate)
      {
        if (__expoEventContext is not null)
        {
          if (!ReferenceEquals(__expoEventContext, context))
            throw new InvalidOperationException(
                "Module event members cannot be rebound to a different runtime context.");
          return;
        }
        __expoEvent_OnReady = onReady ?? throw new ArgumentNullException(nameof(onReady));
        __expoEventContext = context ?? throw new ArgumentNullException(nameof(context));
      }
    }
  }
  ```

  Reproduce only the property's accessibility plus `partial`; globally qualify
  framework and ModulesCore types in emitted code. Reject unsupported
  containers instead of attempting nested or generic declarations.

- [ ] **Step 7: Emit provider construction and initialization.**

  For a typed-event module, replace the inline constructor expression with a
  private create helper that constructs the instance and initializes events
  before returning it to `ModuleRegistry.GetOrCreateModule`. After the registry
  call, invoke the same initializer for an existing instance. Capture
  `context.Events` while the context is active and have delegates close over
  that emitter; do not evaluate `context.Events` during later invocation,
  because that would throw synchronously after teardown.

  Use provider-private codec types only in provider lambdas. Do not emit codec
  references into the authored module partial or change `EmitRecordCodec` from
  `private`.

- [ ] **Step 8: Verify GREEN and legacy stability.**

  Run:

  ```sh
  dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj
  scripts/test-managed.sh --filter FullyQualifiedName~GeneratedEventModuleTests
  ```

  Expected: generator suite passes with no unexpected compiler diagnostics;
  legacy event tests remain green. Inspect generated source for one record
  event to confirm the codec stays private and appears only in the provider.

- [ ] **Step 9: Review and commit the generator slice.**

  First request a spec-conformance review, then a code-quality review. The
  reviewers must check every 018-020 branch, codec preflight ordering, partial
  syntax, same-context identity, cross-context rejection, and factory-before-
  `OnCreate` ordering. Stage exactly the five Task 2 files and commit:

  ```sh
  git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/EventAttribute.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs
  git diff --cached --check
  git commit -m "feat(generator): add typed event members"
  ```

### Task 3: Generated Event Lifecycle And Hermes Integration Proof

**Files:**

- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedEventModuleTests.cs`

**Interfaces:**

- Consumes: Tasks 1-2's overloads and generated partial/provider contract.
- Produces: one real generated module proving payload-less, scalar, record,
  direct-wrapper, lifecycle, observing-hook, legacy coexistence, and teardown
  behavior under Hermes.

- [ ] **Step 1: Add the typed fixture and behavior tests.**

  Keep `GeneratedEventsModule` as the unchanged legacy fixture. Add a separate
  module with both typed and distinct legacy names:

  ```csharp
  public readonly record struct TypedProgress(int Percent);

  [ExpoModule("GeneratedTypedEvents")]
  [Events("onLegacy")]
  public sealed partial class GeneratedTypedEventsModule : Module
  {
    public GeneratedTypedEventsModule(DotnetRuntimeContext context) : base(context)
    {
      try { _ = OnReady; }
      catch (InvalidOperationException exception) { ConstructorEventError = exception.Message; }
    }

    [Event] public partial Func<Task> OnReady { get; }
    [Event] public partial Func<string, Task> OnChange { get; }
    [Event] public partial Func<TypedProgress, Task> OnProgress { get; }
    [Event] public partial Func<JavaScriptValue, Task> OnValue { get; }
    [Event] public partial Func<ArrayBuffer, Task> OnBuffer { get; }

    public string? ConstructorEventError { get; }
    public Delegate? ReadySeenOnCreate { get; private set; }
    public string Started { get; private set; } = string.Empty;
    public string Stopped { get; private set; } = string.Empty;

    [OnCreate] public void Create() => ReadySeenOnCreate = OnReady;

    [OnStartObserving("onChange")]
    public void Start() => Started = "onChange";

    [OnStopObserving("onChange")]
    public void Stop() => Stopped = "onChange";

    public Task EmitLegacyAsync(string value) =>
        SendEventAsync<StringCodec, string>("onLegacy", value);
  }
  ```

  Add tests for:

  - payload-less, string, and lower-camel record delivery;
  - first/last listener observing hooks on a typed-only name;
  - a distinct legacy name on the same module;
  - one throwing listener followed by a successful listener, with the dispatch
    task completing successfully;
  - constructor getter failure text and `OnCreate` seeing the initialized cached
    delegate;
  - registering the provider twice in one context preserving `ReferenceEquals`
    for every event delegate;
  - invoking the generated initializer with a different context rejecting the
    rebind;
  - a cached delegate invoked after context disposal returning a task which
    faults or cancels without stale JSI access, with unchanged scheduler/task
    counters proving it did not query or queue runtime work.

- [ ] **Step 2: Add scheduled direct-wrapper lifetime cases.**

  For `JavaScriptValue`, pause/disable synchronous execution, invoke `OnValue`
  with a caller-owned value, then resume and await. Assert JS identity/value,
  use the original successfully after dispatch, and prove disposing the
  original adds exactly one release beyond the dispatch count.

  For JavaScript-backed `ArrayBuffer`, reset counters after setup, pause the
  executor, invoke `OnBuffer`, dispose the original as soon as invocation
  returns, then resume and await. Assert received bytes and exactly one shared
  long-lived buffer-entry release with zero abandoned handles. Repeat the
  retained invocation lease assertion for a dropped queued task and for context
  teardown: every returned task terminates and each lease releases once, never
  twice.

  Repeat Task 1's wrong-runtime `JavaScriptValue` case through the generated
  delegate to prove the returned task faults before cross-runtime handle access.
  Never dispose the caller's `JavaScriptValue` before its task terminates.

- [ ] **Step 3: Run the generated integration proof.**

  Run:

  ```sh
  scripts/test-managed.sh --filter FullyQualifiedName~GeneratedEventModuleTests
  ```

  Expected: new and existing legacy tests pass because Tasks 1-2 already
  implement the behavior. A failure here is a contract mismatch or integration
  bug; diagnose it before changing production code or expectations.

- [ ] **Step 4: Make only fixture-level corrections needed by real execution.**

  Adjust generated code from Task 2 only if a test exposes a contract mismatch;
  do not weaken an expectation to fit the implementation. If a change touches
  Task 1 or Task 2 production files, rerun that task's complete test command and
  include the production file in this task's review and commit.

- [ ] **Step 5: Verify and commit the integration proof.**

  Run:

  ```sh
  scripts/test-managed.sh --filter FullyQualifiedName~GeneratedEventModuleTests
  scripts/test-managed.sh --filter FullyQualifiedName~ModuleEventEmitterTests
  dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj
  ```

  Have a lifetime-aware reviewer inspect task completion, dropped-work cleanup,
  exact release counts, same-context identity, and listener isolation. Stage
  the two fixture/test files plus any reviewed Task 1-2 correction and commit:

  ```sh
  git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedEventModuleTests.cs
  git diff --cached --check
  git commit -m "test(modules-core): prove typed event lifecycle"
  ```

### Task 4: Reference Module And Author Documentation

**Files:**

- Modify: `packages/example-module/dotnet/ExampleModule/ExampleMathModule.cs`
- Modify: `docs/module-authoring-guide.md`

**Interfaces:**

- Consumes: the accepted `[Event]` API and the existing Plan 012 typed
  JavaScript facade.
- Produces: the example module's `onStatus` event through a cached typed member,
  plus durable author guidance for normal and advanced payloads.

- [ ] **Step 1: Migrate the example module.**

  Replace `[Events("onStatus")]` and the stringly typed emit call with:

  ```csharp
  [Event]
  public partial Func<string, Task> OnStatus { get; }

  [JS]
  public Task EmitStatusAsync(string label) => OnStatus($"C# event: {label}");
  ```

  Remove the codec namespace import only if it becomes unused. The TypeScript
  event map already declares `onStatus(payload: string): void`; do not change
  its public shape or add generated TypeScript in this plan.

- [ ] **Step 2: Rewrite the authoring guide's event section.**

  Lead with `[Event] Func<Task>` / `Func<T, Task>`, awaiting invocation, default
  and explicit naming, initialization before `OnCreate`, constructor access
  failure, observing hooks, and the unchanged JS `addListener` facade. Add a
  clearly labeled legacy subsection for `[Events]` plus `SendEventAsync`.

  Include these ownership rules in direct language:

  - ordinary mutable payload state stays stable until the task completes;
  - an `ArrayBuffer` original may be disposed after invocation returns and must
    not race that invocation;
  - a `JavaScriptValue` original stays alive until the task completes;
  - nested owned wrappers and callbacks are rejected;
  - `JavaScriptObject` is not currently a generated module codec, but remains a
    possible future advanced convertible.

- [ ] **Step 3: Verify the real consumer and docs.**

  Run:

  ```sh
  scripts/test-managed.sh --filter FullyQualifiedName~ExampleModuleShowcaseTests
  pnpm --filter mobile-app typecheck
  pnpm --filter desktop-app typecheck
  rg -n 'SendEventAsync|\[Events\("onStatus"\)\]' packages/example-module docs/module-authoring-guide.md
  git diff --check
  ```

  Expected: tests and typechecks pass. Search hits are allowed only in the
  guide's labeled legacy section, never in the example module's preferred path.

- [ ] **Step 4: Review and commit the author-facing slice.**

  Have a reviewer compare every guide statement with the accepted delta and
  code XML comments, especially the two distinct direct-wrapper lifetimes.
  Stage and commit:

  ```sh
  git add packages/example-module/dotnet/ExampleModule/ExampleMathModule.cs \
    docs/module-authoring-guide.md
  git diff --cached --check
  git commit -m "docs(modules-core): document typed event members"
  ```

### Task 5: Living Spec Closure And Full Verification

**Files:**

- Modify: `docs/specs/modules-core-boundary.md`
- Modify: `docs/plans/README.md`
- Move: `docs/changes/2026-07-19-typed-event-members/spec.md` to `docs/archive/changes/2026-07-19-typed-event-members/spec.md`
- Move: `docs/changes/2026-07-19-typed-event-members/plan.md` to `docs/archive/changes/2026-07-19-typed-event-members/plan.md`

**Interfaces:**

- Consumes: the accepted implementation and fresh verification evidence.
- Produces: one authoritative living spec, archived provenance, and Plan 014
  marked DONE.

- [ ] **Step 1: Merge the accepted delta into the living spec.**

  Update the existing event requirements in
  `docs/specs/modules-core-boundary.md` instead of pasting a parallel appendix.
  Preserve legacy requirements and add the accepted typed member, task outcome,
  initialization, name merge, diagnostics, and payload ownership scenarios.
  State that listener exceptions remain isolated. Keep `JavaScriptObject` as a
  future optional convertible, not a current codec.

- [ ] **Step 2: Run the complete verification matrix.**

  Run, in order:

  ```sh
  dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj
  scripts/test-managed.sh
  pnpm --filter mobile-app typecheck
  pnpm --filter desktop-app typecheck
  scripts/format.sh --check --all
  rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" packages/expo-modules-dotnet/managed
  rg "\\.AsValue\\(\\)\\.(AsObject|AsArray|AsFunction)\\(" packages/expo-modules-dotnet
  git diff --check
  ```

  Expected: every test/typecheck/format command exits `0`; hot-path and owned-
  wrapper scans introduce no new violations. Inspect and classify any existing
  scan hit instead of reporting an empty scan when it is not empty.

- [ ] **Step 3: Mark completion and archive transient artifacts.**

  Change only Plan 014's row to DONE with the actual managed-test count and
  verification summary. Move the accepted `spec.md` and completed `plan.md`
  together under `docs/archive/changes/2026-07-19-typed-event-members/`.
  Confirm `docs/changes/2026-07-19-typed-event-members/` no longer exists and
  living docs contain every accepted requirement.

- [ ] **Step 4: Run documentation and privacy checks.**

  Run:

  ```sh
  rg "self[-]contained planning package|planning[ ]artifacts,[ ]not[ ]implementation|expo[-]modules[-]windows[-]core|Phase[ ]1:[ ]clean[ ]separate[ ]research[ ]repo|create[ ]a[ ]clean[ ]local[ ]research[ ]repository" docs/README.md docs/specs docs/roadmap.md AGENTS.md .agents/skills
  rg -n "TODO|TBD|FIXME" docs/specs/modules-core-boundary.md docs/archive/changes/2026-07-19-typed-event-members docs/plans/README.md
  git diff --check
  ```

  Expected: no obsolete planning-language hit and no unresolved placeholder in
  the Plan 014 artifacts. Before committing, stage the closure files and scan
  the staged diff for `/Users/`, `/home/`, Windows user profiles, usernames,
  machine names, private hostnames, and machine-specific install paths.

- [ ] **Step 5: Request the final full-range reviewer.**

  Give an independent reviewer the full range `a671c0be..HEAD` plus the staged
  closure diff. Require an explicit APPROVE or actionable findings covering
  spec conformance, generator output validity, scheduling, teardown, ownership,
  diagnostics, docs, and scope. Fix and re-run affected verification for every
  valid finding before proceeding.

- [ ] **Step 6: Commit closure and report.**

  ```sh
  git add docs/specs/modules-core-boundary.md docs/plans/README.md \
    docs/archive/changes/2026-07-19-typed-event-members
  git diff --cached --check
  git commit -m "docs: close typed event member plan"
  git status --short
  ```

  Expected: the commit succeeds and the working tree is clean. Report the
  commits, exact verification counts, review result, and any deliberately
  deferred scope. Do not push or create a PR.
