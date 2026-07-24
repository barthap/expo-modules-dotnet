# Authored Module Test Core

## Goal

Give each repo-local authored .NET Expo module a package-owned test project
that can combine ordinary C# unit tests with public-behavior tests through the
real generated bindings and Hermes runtime.

The first consumer is `ExampleModule.Tests`. Future authored packages under
`packages/*` use the same test library and managed runner without moving their
tests into `Expo.ModulesCore.Tests`.

## Scope

### In scope

- Add the repo-local, test-only `Expo.ModulesCore.Testing` managed project.
- Expose a low-level Hermes test runtime and a higher-level authored-module
  test host.
- Register generated module providers explicitly without reflection.
- Support synchronous JavaScript evaluation and event-driven Promise
  settlement with deterministic cleanup and bounded failure behavior.
- Move ExampleModule's Hermes-backed tests into a package-owned
  `ExampleModule.Tests` project.
- Discover and run repo-local authored-module test projects from the canonical
  macOS/Linux and Windows managed test runners.
- Document the split between core binding tests and authored package tests.

### Out of scope

- Publishing `Expo.ModulesCore.Testing` or supporting module packages in
  separate repositories.
- Shipping, downloading, or restoring RID-specific Hermes/testhost binaries.
- Making a clean standalone `dotnet test` invocation provision the native
  Hermes testhost.
- Adding another module invocation API, assertion framework, mock framework,
  or source-generated test fixture.
- Testing an authored module's TypeScript facade. Package JavaScript tests
  continue to use the repository's JavaScript test tools.

## Accepted design

### Package boundary

`Expo.ModulesCore.Testing` lives at
`packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Testing` and is
marked `IsPackable=false` for this local-only release. It references
`Expo.ModulesCore` and exposes test runtime facilities, but it does not
reference xUnit or another assertion framework.

The existing module-layer Hermes fixture and native testhost loader move from
`Expo.ModulesCore.Tests` into this project. Public authored-module APIs expose
only normal evaluation and module-host behavior. Queue manipulation,
invalidation, native counters, and other core stress controls remain internal
and are available to `Expo.ModulesCore.Tests` through an explicit friend
assembly relationship.

`Expo.JSI.Tests` keeps its separate low-level fixture. The JSI test project
must not acquire a dependency on the higher-level ModulesCore testing package.

### Public runtime API

The public namespace is `Expo.ModulesCore.Testing`.

`HermesTestRuntime`:

- owns one native Hermes test runtime;
- is created through an explicit `Create` factory;
- exposes its `JavaScriptRuntime`;
- evaluates JavaScript source and returns an owned `JavaScriptValue`;
- exposes `DrainTasks` and `WaitUntilIdle`; and
- disposes the native test runtime deterministically and idempotently.

`ExpoModuleTestHost`:

- creates and owns one `HermesTestRuntime`;
- creates one `DotnetRuntimeContext`;
- gets the context's normal `_expoDotnet.modules` object;
- invokes an explicit
  `Action<DotnetRuntimeContext, JavaScriptObject>` registration callback;
- exposes the test runtime, its `JavaScriptRuntime`, and synchronous
  evaluation; and
- disposes the module context on the runtime executor before releasing Hermes.

Provider registration uses a method group from the authored assembly:

```csharp
using var host = ExpoModuleTestHost.Create(
    ExpoModulesProvider_ExampleModule.Register
);
```

TestCore does not scan assemblies or use runtime reflection to find generated
providers.

### Promise evaluation

`ExpoModuleTestHost.EvaluatePromiseAsync` accepts a JavaScript expression that
must return a real Promise. It returns an owned `JavaScriptValue` containing
the fulfillment value.

The helper installs uniquely named temporary managed host callbacks and
attaches them to the Promise. Callback completion settles a
`TaskCompletionSource` configured to run continuations asynchronously. The
helper must not poll JavaScript globals or use delay loops to observe
settlement.

The operation:

- rejects a non-Promise result instead of wrapping it with `Promise.resolve`;
- accepts a `CancellationToken`;
- uses a configurable timeout with a five-second default;
- throws `OperationCanceledException` for caller cancellation;
- throws `TimeoutException` when the timeout expires; and
- throws `JavaScriptPromiseRejectedException` for Promise rejection.

`JavaScriptPromiseRejectedException` preserves the rejected error's available
`name`, `message`, and `stack` fields. Non-Error rejection values use their
JavaScript string representation as the message and leave unavailable fields
null.

Temporary global properties, TestCore-owned function wrappers, retained
fulfillment values, cancellation registrations, and timeout registrations are
released on fulfillment, rejection, synchronous setup failure, cancellation,
timeout, and host disposal. A callback still retained by an unresolved
JavaScript Promise becomes an inert no-op and follows normal JSI lifetime.
Late Promise settlement after cancellation or timeout must remain safe and
must not complete the caller-visible task twice.

The fulfillment value is owned by the caller, which must dispose it before
disposing the host.

### Failure and teardown behavior

If runtime creation or provider registration fails, construction releases all
owners that were created before rethrowing the failure. If module teardown
throws, the host still releases Hermes and reports the teardown failure after
cleanup. Repeated disposal is a no-op.

Creating a Hermes-backed host without `EXPO_JSI_TESTHOST_LIBRARY`, or with a
missing library, fails with an actionable message that names the canonical
managed test runner.

### Test ownership

Core binding, conversion, registry, lifecycle, and generator behavior remain
in `Expo.ModulesCore.Tests` and `Expo.ModulesCore.Generator.Tests`.

Behavior specific to an authored module lives in that package's managed test
project. `ExampleModule.Tests` moves under
`packages/example-module/dotnet/ExampleModule.Tests` and references:

- `ExampleModule`;
- `Expo.ModulesCore.Testing`;
- `Microsoft.NET.Test.Sdk`;
- xUnit v3; and
- the Visual Studio xUnit runner.

The project contains both ordinary unit tests for pure C# behavior and
Hermes-backed tests for generated names, conversion, Promise, callback, event,
shared-object, and error behavior. TestCore does not require every test to use
Hermes.

Authored-module test projects disable xUnit parallel execution in v1. Parallel
native test runtime behavior is not part of this release's contract.

`Expo.ModulesCore.Tests` no longer references `ExampleModule` and no longer
contains ExampleModule-specific tests.

### Managed runners

`scripts/test-managed.sh` and `scripts/test-managed.ps1` build the native
testhost once, export `EXPO_JSI_TESTHOST_LIBRARY`, and run:

1. `Expo.ModulesCore.Generator.Tests`;
2. `Expo.JSI.Tests`;
3. `Expo.ModulesCore.Tests`; and
4. every authored-module test project matching
   `packages/*/dotnet/*.Tests/*.Tests.csproj`, in deterministic path order.

Both runners support selecting one or more explicit repo-relative managed test
project paths. Project selection still builds the managed prerequisites and
native testhost, but it runs only the selected test projects. Existing
`dotnet test` arguments remain pass-through arguments. A selected path outside
the repository, a missing project, or a non-test project is rejected before
the runner builds native code.

The default CI managed-test jobs use the unchanged canonical runners, so newly
discovered authored-module test projects join macOS/Linux and Windows
verification without workflow edits.

Direct `dotnet test` remains valid for pure tests that do not create a Hermes
host. Hermes-backed tests use a canonical managed runner.

### Documentation and deferred work

The module authoring guide documents:

- the module-owned `.Tests` project shape;
- direct unit tests versus Hermes-backed public-behavior tests;
- explicit generated-provider registration;
- Promise evaluation and owned result disposal;
- the full-suite and project-selected runner commands; and
- the non-parallel v1 constraint.

The roadmap records a deferred external-consumption milestone covering a
packable testing product, RID-specific native Hermes/testhost delivery, and a
standalone test command for module repositories outside this workspace.

## Delta requirements

### ADDED: Repo-local ModulesCore testing package

The repository SHALL provide a non-packable `Expo.ModulesCore.Testing` project
for repo-local authored module tests.

#### Scenario: Authored test project references TestCore

- **GIVEN** an authored package test project is inside this repository
- **WHEN** it references `Expo.ModulesCore.Testing`
- **THEN** it SHALL receive the public Hermes runtime and module test host APIs
- **AND** it SHALL choose its own assertion and test framework packages
- **AND** TestCore SHALL NOT use runtime reflection to find a provider

#### Scenario: Lower-level JSI tests remain independent

- **GIVEN** `Expo.JSI.Tests` verifies the low-level ABI and wrapper layer
- **WHEN** TestCore is added above `Expo.ModulesCore`
- **THEN** `Expo.JSI.Tests` SHALL retain its low-level fixture
- **AND** it SHALL NOT reference `Expo.ModulesCore.Testing`

### ADDED: Authored module test host

The test host SHALL own one Hermes runtime and one `DotnetRuntimeContext`,
register an explicit generated provider under `_expoDotnet.modules`, and tear
down the context before the runtime.

#### Scenario: Generated provider is registered

- **GIVEN** an authored test passes its generated provider's `Register` method
- **WHEN** `ExpoModuleTestHost.Create` succeeds
- **THEN** the provider SHALL be registered against the host's normal dotnet
  modules object
- **AND** JavaScript evaluation SHALL observe the authored module through its
  generated public surface

#### Scenario: Registration fails

- **GIVEN** provider registration throws
- **WHEN** host construction unwinds
- **THEN** every created managed and native owner SHALL be released
- **AND** the registration failure SHALL be rethrown

#### Scenario: Host is disposed

- **GIVEN** a host owns a module context and Hermes runtime
- **WHEN** the host is disposed
- **THEN** it SHALL dispose the module context on the runtime executor before
  releasing Hermes
- **AND** repeated disposal SHALL be a no-op

### ADDED: Event-driven Promise evaluation

The authored module test host SHALL settle JavaScript Promises through
temporary host callbacks without polling.

#### Scenario: Promise fulfills

- **GIVEN** an authored async method returns a JavaScript Promise
- **WHEN** `EvaluatePromiseAsync` observes fulfillment
- **THEN** it SHALL return an owned fulfillment value
- **AND** it SHALL release temporary callbacks and registrations
- **AND** the caller SHALL dispose the value before disposing the host

#### Scenario: Promise rejects

- **GIVEN** an authored async method's Promise rejects
- **WHEN** `EvaluatePromiseAsync` observes rejection
- **THEN** it SHALL throw `JavaScriptPromiseRejectedException`
- **AND** it SHALL preserve the available JavaScript error name, message, and
  stack
- **AND** it SHALL release temporary callbacks and registrations

#### Scenario: Evaluated expression is not a Promise

- **GIVEN** a Promise evaluation expression returns a non-Promise value
- **WHEN** `EvaluatePromiseAsync` checks the result
- **THEN** it SHALL fail instead of wrapping the value in a resolved Promise

#### Scenario: Promise wait is canceled or times out

- **GIVEN** a Promise has not settled
- **WHEN** caller cancellation occurs or the configured timeout expires
- **THEN** the visible task SHALL fail with the matching cancellation or
  timeout exception
- **AND** TestCore-owned wait resources and global properties SHALL be
  released
- **AND** a callback still retained by JavaScript SHALL become an inert no-op
- **AND** later Promise settlement SHALL remain safe

### MODIFIED: Module test ownership

`Expo.ModulesCore.Tests` SHALL own framework binding and conversion behavior.
Each authored module package SHALL own tests for its module-specific behavior.

#### Scenario: Framework behavior is tested

- **GIVEN** a test proves generated binding, codec, registry, lifecycle, event,
  callback, or shared-object behavior independent of one authored package
- **WHEN** the test is added
- **THEN** it SHALL live in `Expo.ModulesCore.Tests` or the generator test
  project

#### Scenario: Authored module behavior is tested

- **GIVEN** a test proves behavior defined by one authored module package
- **WHEN** the test is added
- **THEN** it SHALL live in that package's `.Tests` project
- **AND** it MAY use pure C# tests, Hermes-backed TestCore tests, or both

### MODIFIED: Canonical managed runners

The canonical managed runners SHALL include discovered repo-local authored
module tests after building one shared native testhost.

#### Scenario: Full managed suite runs

- **GIVEN** authored test projects match the repository convention
- **WHEN** the canonical managed runner executes without project selection
- **THEN** it SHALL run core tests and every matching authored test project in
  deterministic order
- **AND** all Hermes-backed projects SHALL receive the same built testhost
  library path

#### Scenario: Developer selects one authored test project

- **GIVEN** a developer supplies a repo-relative managed test project path
- **WHEN** the canonical runner executes
- **THEN** it SHALL build the required managed and native prerequisites
- **AND** it SHALL run only the selected test project with the supplied
  `dotnet test` arguments

#### Scenario: Selected project is invalid

- **GIVEN** a selected path is missing, outside the repository, or does not
  name a managed test project
- **WHEN** the canonical runner validates project selection
- **THEN** it SHALL fail before configuring or building native code

## Verification

`Expo.ModulesCore.Tests` SHALL cover TestCore's public host behavior and its
internal cleanup controls. A separate TestCore test assembly is not required
for v1.

Implementation verification SHALL include:

- TestCore unit and Hermes-backed integration tests for construction,
  registration, evaluation, Promise fulfillment, rejection, cancellation,
  timeout, cleanup, and ordered disposal;
- moved ExampleModule public-behavior tests passing from
  `ExampleModule.Tests`;
- a project-selected ExampleModule test run;
- the full managed suite on the current host;
- Windows managed-suite validation for project discovery and selection;
- `scripts/format.sh --check --all`; and
- `git diff --check`.

No verification step may require public network access.
