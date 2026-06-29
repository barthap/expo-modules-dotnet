# Hermes-backed .NET Test Suite Design

## Purpose

Build a real test suite for the portable C# / JSI bridge using the existing
headless Hermes direction, but expose it through ordinary `dotnet test` instead
of growing assertion logic inside a proof executable.

The suite should document bridge behavior as tests. It should make low-level
JSI wrapper semantics easy to test from C#, and make JavaScript-visible module
behavior easy to test through evaluated JS snippets.

## Assumptions

- `dotnet test` is the developer-facing test runner.
- xUnit is the initial test framework.
- The native side owns Hermes, real JSI objects, and runtime teardown.
- Managed tests own assertions and wrapper usage.
- The production `Expo.JSI` package consumes only the production bridge ABI.
- The test suite may use a test-only native ABI extension to create runtimes,
  evaluate scripts, and inspect test counters.
- The existing HostFXR console proof remains a smoke/e2e proof for now.

## Architecture

Add a native Hermes testhost that includes the production ABI and adds only
testhost-owned APIs:

```text
native/include/expo_jsi.h
  Production bridge ABI consumed by Expo.JSI.

native/testhost/include/expo_jsi_testhost.h
  Includes expo_jsi.h.
  Adds test-only runtime creation, script evaluation, teardown, error, and
  counter helpers.
```

The testhost ABI is an extension of the production ABI, not a separate model.
The production managed package should not depend on `expo_jsi_testhost.h`.

Managed tests live in:

```text
managed/packages/Expo.JSI.Tests/
  Expo.JSI.Tests.csproj
  Fixtures/
    HermesRuntimeFixture.cs
    NativeTestHost.cs
    JavaScriptTestRuntime.cs
  Runtime/
    JavaScriptRuntimeTests.cs
    JavaScriptValueTests.cs
    JavaScriptStringTests.cs
    OwnershipTests.cs
  HostFunctions/
    HostFunctionTests.cs
    HostFunctionErrorTests.cs
  Modules/
    GeneratedModuleDispatchTests.cs
    ConversionTests.cs
    ErrorPropagationTests.cs
```

`Modules/` is temporary. It may hold JS-facing module dispatch, conversion, and
error propagation tests only until an `Expo.ModulesCore` package exists. Once
`Expo.ModulesCore` arrives, those tests should move to `Expo.ModulesCore.Tests`.
`Expo.JSI.Tests` should remain focused on runtime, values, ABI, ownership,
strings, host functions, and the Hermes fixture.

## Data Flow

The default test flow is:

```text
dotnet test
  -> HermesRuntimeFixture.Create()
    -> native testhost creates a Hermes runtime
    -> native testhost creates an expo_jsi_runtime_handle
    -> native testhost returns:
       - const expo_jsi_api*
       - expo_jsi_runtime_handle
       - expo_jsi_testhost_runtime_handle
  -> managed fixture constructs JavaScriptRuntime.FromNative(api, runtime)
  -> test body uses direct C# wrapper calls and/or fixture.Evaluate(...)
  -> fixture dispose:
       - checks configured counters
       - releases the runtime handle
       - destroys the Hermes runtime
```

Ordinary JSI operations should go through `Expo.JSI` wrappers. Test-only host
operations, such as creating the runtime, evaluating scripts, and reading
counters, should go through `NativeTestHost`.

`Evaluate(...)` starts as testhost functionality. The first slice does not
decide whether script evaluation belongs on the production `JavaScriptRuntime`
API.

## First Slice

The first implementation slice should prove the harness shape, not exhaust the
future test matrix.

Success criteria:

- `dotnet test managed/packages/Expo.JSI.Tests/Expo.JSI.Tests.csproj` builds and
  runs.
- Tests create a real Hermes-backed JSI runtime through the native testhost.
- C#-facing runtime tests cover:
  - create/read number;
  - create/read boolean `true`;
  - create/read boolean `false`;
  - create/read ASCII string;
  - create/read non-ASCII string;
  - create/read embedded NUL string.
- Narrow host-function tests cover the native -> managed -> native callback
  path without requiring module registration:
  - C# creates a host function directly with `runtime.CreateHostFunction(...)`;
  - C# sets it on `global`;
  - JS evaluates a call such as `global.addOne(41.5)`;
  - the C# callback receives borrowed arguments and returns an owned value;
  - the result is asserted from JS evaluation.
- A temporary JS-facing module test may stay in `Expo.JSI.Tests/Modules/` only
  if it reuses existing generated-looking proof code cheaply. It is not
  required for first-slice success if direct host-function coverage proves the
  callback path.
- Fixture teardown releases runtime handles and reports basic owned-value
  release counters.
- `scripts/run-hermes-experiment.sh` remains working.

Out of scope for the first slice:

- full conversion matrix;
- promises;
- scheduler behavior;
- NativeAOT testhost;
- `Expo.ModulesCore.Tests`;
- Jest;
- CI integration;
- retiring `experiments/hermes-console-hostfxr`;
- broad production API reshaping just to make tests nicer.

## Error Handling

Native testhost functions should return structured `expo_jsi_error` values
through result structs. `NativeTestHost` should translate failed native results
into managed exceptions with the native message.

Managed test failures should use normal xUnit assertions.

Host-function callback exceptions need narrow coverage in the first slice:

- a managed callback that throws should surface as a JS error when called from
  `Evaluate(...)`;
- it should not crash the process;
- it should not cross unmanaged frames as a raw managed exception.

If implementing tests reveals that current error handling is swallowed,
impossible to assert, or turns expected JS/managed errors into uncatchable
process crashes, stop and notify the user. Treat that as a possible bridge
architecture flaw, not as evidence that the test approach is wrong. Fixing the
architecture during test-suite work is allowed only with explicit notice and a
clear explanation of the bridge boundary being changed.

## Ownership Checks

The fixture should track owned-value releases through the same counter idea used
by the current Hermes console proof.

The first slice should assert "no obvious leak" at fixture teardown. Exact
release counts should be used only for small deterministic operations. Tests may
also expose scoped counter helpers:

- reset counters;
- run one operation;
- assert at least one release happened, or assert an exact count when the
  operation is intentionally tiny.

Borrowed host-function arguments should be tested by reading them inside the
callback. The first slice does not need to prove every borrowed-value escape
hazard unless the current API naturally exposes such a test.

## Developer Workflow

The canonical workflow command should be:

```sh
scripts/test-jsi.sh
```

The script should:

1. Verify the Hermes prebuilt exists, or tell the user to run
   `scripts/build-hermes-macos.sh`.
2. Build `managed/packages/Expo.JSI`.
3. Configure and build `native/testhost`.
4. Run `dotnet test managed/packages/Expo.JSI.Tests/Expo.JSI.Tests.csproj` with
   the native testhost library path passed through the environment.

The underlying runner remains plain `dotnet test`. The managed fixture should
fail loudly if `EXPO_JSI_TESTHOST_LIBRARY` is missing or points to a missing
library.

`direnv` may provide convenience defaults, but scripts define correctness. If
committed `.envrc` entries are added, they must use repo-relative shell values
such as `$PWD/build/...`, not local absolute paths. Machine-specific overrides
belong in ignored local files.

Expected verification before finishing the first implementation:

```sh
scripts/test-jsi.sh
scripts/run-hermes-experiment.sh
scripts/format.sh --check --all
```

## Future Decisions

Leave these undecided until a later design or implementation slice:

- whether `JavaScriptRuntime.Evaluate(...)` becomes production API;
- whether to opt into Microsoft Testing Platform instead of the default
  `dotnet test` path;
- whether to add Jest for JS-only API tests;
- whether the testhost should support NativeAOT;
- how strict release-counter assertions should become across the whole suite;
- scheduler and promise test design;
- when to retire or replace `experiments/hermes-console-hostfxr`.
