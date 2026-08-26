# Authored Module Test Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> `superpowers:subagent-driven-development` (recommended) or
> `superpowers:executing-plans` to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add repo-local authored-module test infrastructure that combines pure
C# tests with generated-binding tests against the real Hermes runtime.

**Architecture:** A non-packable `Expo.ModulesCore.Testing` project owns the
module-layer native testhost loader, a public `HermesTestRuntime`, and a public
`ExpoModuleTestHost`. Promise evaluation attaches managed host functions
directly to a real JavaScript Promise and completes an internal state machine
without polling. The canonical shell and PowerShell runners discover authored
test projects by convention and reuse one built native testhost.

**Tech Stack:** .NET 10, C# source-generated Expo module bindings, `Expo.JSI`,
Hermes, xUnit v3, Bash, PowerShell, CMake.

## Global Constraints

- Stay on `advisor/022-expo-asset-dotnet`; do not create a worktree.
- Keep `Expo.ModulesCore.Testing` repo-local and `IsPackable=false`.
- Do not add xUnit or another assertion framework to
  `Expo.ModulesCore.Testing`.
- Keep `Expo.JSI.Tests` independent from `Expo.ModulesCore.Testing`.
- Register generated providers through an explicit
  `Action<DotnetRuntimeContext, JavaScriptObject>`; do not scan assemblies or
  use reflection.
- `EvaluatePromiseAsync` must require a real Promise, default to a five-second
  timeout, support cancellation, and settle through host callbacks without
  polling or delay loops.
- A Promise fulfillment value is owned by its caller and must be disposed
  before its host.
- Keep native testhost queue, counter, and invalidation controls internal to
  `Expo.ModulesCore.Tests`.
- Authored package behavior belongs to the package's `.Tests` project; core
  binding behavior remains in `Expo.ModulesCore.Tests`.
- Disable xUnit cross-test parallel execution in authored-module test projects.
- The managed runners must discover
  `packages/*/dotnet/*.Tests/*.Tests.csproj` in deterministic path order and
  reuse one native testhost.
- Do not add public-network dependencies to any test.
- Do not modify `docs/plans/022-expo-asset-dotnet.md`; the Expo Asset advisor
  owns that file.
- Before every commit, inspect staged content for local absolute paths,
  usernames, machine names, private hostnames, and machine-specific install
  paths.

---

### Task 1: Extract the reusable Hermes test runtime

**Files:**

- Create:
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Testing/Expo.ModulesCore.Testing.csproj`
- Create:
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Testing/AssemblyInfo.cs`
- Move:
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Fixtures/NativeTestHost.cs`
  to
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Testing/Internal/NativeTestHost.cs`
- Create:
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Testing/HermesTestRuntime.cs`
- Modify:
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj`
- Modify:
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Fixtures/HermesRuntimeFixture.cs`
- Delete:
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Fixtures/JavaScriptTestRuntime.cs`
- Create:
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Testing/HermesTestRuntimeTests.cs`

**Interfaces:**

- Consumes: the current `expo_jsi_testhost_*` exports and
  `JavaScriptRuntime.FromNative`.
- Produces:
  `HermesTestRuntime.Create()`,
  `HermesTestRuntime.Runtime`,
  `HermesTestRuntime.Evaluate(string, string)`,
  `HermesTestRuntime.DrainTasks()`,
  `HermesTestRuntime.WaitUntilIdle()`, and deterministic `Dispose()`.
- Preserves the existing internal queue/counter/invalidation behavior for
  `Expo.ModulesCore.Tests` through friend-assembly access.

- [ ] **Step 1: Add failing public-runtime and loader-validation tests**

Create `HermesTestRuntimeTests.cs` with tests that encode the public contract
and the actionable runner error without forcing the process-wide lazy native
loader into a failed state:

```csharp
using Expo.ModulesCore.Testing;
using Expo.ModulesCore.Testing.Internal;
using Xunit;

namespace Expo.ModulesCore.Tests.Testing;

public sealed class HermesTestRuntimeTests
{
  [Fact]
  public void RuntimeEvaluatesJavaScriptAndDisposesIdempotently()
  {
    var testRuntime = HermesTestRuntime.Create();
    using (var result = testRuntime.Evaluate("20 + 22", "hermes-test-runtime.js"))
    {
      Assert.Equal(42, result.AsDouble());
    }
    testRuntime.WaitUntilIdle();
    testRuntime.DrainTasks();
    testRuntime.Dispose();
    testRuntime.Dispose();
  }

  [Fact]
  public void MissingLibraryConfigurationNamesCanonicalRunner()
  {
    var exception = Assert.Throws<InvalidOperationException>(
        () => NativeTestHost.ValidateLibraryPath(null)
    );

    Assert.Contains("EXPO_JSI_TESTHOST_LIBRARY", exception.Message);
    Assert.Contains("scripts/test-managed", exception.Message);
  }

  [Fact]
  public void MissingLibraryFileNamesCanonicalRunner()
  {
    var missingPath = Path.Combine(
        Path.GetTempPath(),
        $"missing-testhost-{Guid.NewGuid():N}"
    );
    var exception = Assert.Throws<FileNotFoundException>(
        () => NativeTestHost.ValidateLibraryPath(missingPath)
    );

    Assert.Contains("scripts/test-managed", exception.Message);
  }
}
```

Add a project reference from `Expo.ModulesCore.Tests.csproj` to the new TestCore
project before creating that project. Keep the existing generator analyzer
reference.

- [ ] **Step 2: Run the tests and confirm the missing project/API failure**

Run:

```sh
scripts/test-managed.sh --filter FullyQualifiedName~HermesTestRuntimeTests
```

Expected: FAIL during restore or compilation because
`Expo.ModulesCore.Testing.csproj`, `HermesTestRuntime`, and `NativeTestHost`
under the new namespace do not exist.

- [ ] **Step 3: Create the non-packable TestCore project and friend boundary**

Create `Expo.ModulesCore.Testing.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../Expo.ModulesCore/Expo.ModulesCore.csproj" />
  </ItemGroup>
</Project>
```

Create `AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Expo.ModulesCore.Tests")]
```

Do not add any test-framework package reference.

- [ ] **Step 4: Move the native loader and make path validation testable**

Use `git mv` for `NativeTestHost.cs`. Change its namespace to
`Expo.ModulesCore.Testing.Internal`.

Keep every current native delegate, exported symbol, counter field, and native
operation unchanged. Change only ownership/visibility and split library-path
validation from loading:

```csharp
internal static string ValidateLibraryPath(string? path)
{
  if (string.IsNullOrWhiteSpace(path))
  {
    throw new InvalidOperationException(
        "EXPO_JSI_TESTHOST_LIBRARY is not set. Run scripts/test-managed.sh " +
        "or scripts/test-managed.ps1."
    );
  }
  if (!File.Exists(path))
  {
    throw new FileNotFoundException(
        "EXPO_JSI_TESTHOST_LIBRARY points to a missing library. Run " +
        "scripts/test-managed.sh or scripts/test-managed.ps1.",
        path
    );
  }
  return path;
}

private static nint LoadLibrary()
{
  var path = ValidateLibraryPath(
      Environment.GetEnvironmentVariable("EXPO_JSI_TESTHOST_LIBRARY")
  );
  return NativeLibrary.Load(path);
}
```

The loader remains `internal static unsafe`. Its `CreateResult`, `Counters`,
and all control methods remain internal.

- [ ] **Step 5: Add `HermesTestRuntime` and preserve advanced internal controls**

Implement this public surface:

```csharp
using Expo.JSI;
using Expo.ModulesCore.Testing.Internal;

namespace Expo.ModulesCore.Testing;

public sealed class HermesTestRuntime : IDisposable
{
  private nint testHostRuntime;

  private HermesTestRuntime(JavaScriptRuntime runtime, nint testHostRuntime)
  {
    Runtime = runtime;
    this.testHostRuntime = testHostRuntime;
  }

  public JavaScriptRuntime Runtime { get; }

  public static HermesTestRuntime Create()
  {
    var result = NativeTestHost.CreateRuntime();
    if (result.Ok == 0 || result.Api == 0 || result.Runtime == 0 ||
        result.TestHostRuntime == 0)
    {
      var message = result.Error.GetMessageAndRelease();
      throw new InvalidOperationException(
          string.IsNullOrEmpty(message)
              ? "Failed to create Hermes test runtime."
              : message
      );
    }

    return new HermesTestRuntime(
        JavaScriptRuntime.FromNative(result.Api, result.Runtime),
        result.TestHostRuntime
    );
  }

  public JavaScriptValue Evaluate(
      string source,
      string sourceUrl = "expo-modules-test-core.js"
  )
  {
    ObjectDisposedException.ThrowIf(testHostRuntime == 0, this);
    return NativeTestHost.Evaluate(Runtime, testHostRuntime, source, sourceUrl);
  }

  public void DrainTasks() => WaitUntilIdle();

  public void WaitUntilIdle()
  {
    ObjectDisposedException.ThrowIf(testHostRuntime == 0, this);
    NativeTestHost.WaitUntilIdle(testHostRuntime);
  }

  public void Dispose()
  {
    var runtime = Interlocked.Exchange(ref testHostRuntime, 0);
    if (runtime != 0)
    {
      NativeTestHost.ReleaseRuntime(runtime);
    }
  }
}
```

Add internal delegations on `HermesTestRuntime` for the current core-test
surface, each guarded against a disposed `testHostRuntime`:

| Member | Delegates to |
| --- | --- |
| `Counters` | `NativeTestHost.GetCounters` |
| `ResetCounters()` | `NativeTestHost.ResetCounters` |
| `CollectGarbageForTesting()` | `NativeTestHost.CollectGarbageForTesting` |
| `SetSyncExecutionSupportedForTesting(bool)` | `NativeTestHost.SetSyncExecutionSupported` |
| `PauseRuntimeExecutor()` | `NativeTestHost.PauseRuntimeExecutor` |
| `ResumeRuntimeExecutor()` | `NativeTestHost.ResumeRuntimeExecutor` |
| `DropNextRuntimeTask(JavaScriptTaskPriority)` | `NativeTestHost.DropNextRuntimeTask` |
| `WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority)` | `NativeTestHost.WaitUntilRuntimeTaskQueued` |
| `DropQueuedRuntimeTask(JavaScriptTaskPriority)` | `NativeTestHost.DropQueuedRuntimeTask` |
| `ReleaseBridgeRuntimeHandle()` | `NativeTestHost.ReleaseBridgeRuntimeHandle` |
| `InvalidateRuntimeForTesting()` | `NativeTestHost.InvalidateRuntime` |
| `PrepareRuntimeForInvalidation()` | `NativeTestHost.PrepareRuntimeForInvalidation` |

- [ ] **Step 6: Turn the old fixture into a thin compatibility adapter**

Keep `HermesRuntimeFixture` so existing core tests do not need a mechanical
rewrite. Replace its native handle and `JavaScriptTestRuntime` ownership with
one `HermesTestRuntime` field. `Create`, `Runtime`, `Evaluate`, all advanced
control methods, and `Dispose` delegate directly to that field. Delete
`JavaScriptTestRuntime.cs`.

The adapter must contain no native delegates, environment lookup, or native
library loading.

- [ ] **Step 7: Run the extracted-runtime tests and the full core test project**

Run:

```sh
scripts/test-managed.sh --filter FullyQualifiedName~HermesTestRuntimeTests
scripts/test-managed.sh --filter FullyQualifiedName~Expo.ModulesCore.Tests
```

Expected: PASS. The second command proves the existing core tests still use
their advanced controls through the compatibility adapter.

- [ ] **Step 8: Commit the reusable runtime**

Stage only the files from this task, run the local-path security scan, then
commit:

```sh
git commit -m "feat(test): add reusable Hermes test runtime"
```

---

### Task 2: Add the authored-module host and ordered teardown

**Files:**

- Create:
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Testing/ExpoModuleTestHost.cs`
- Create:
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Testing/ExpoModuleTestHostTests.cs`

**Interfaces:**

- Consumes: `HermesTestRuntime` from Task 1 and
  `Action<DotnetRuntimeContext, JavaScriptObject>`.
- Produces:
  `ExpoModuleTestHost.Create`,
  `ExpoModuleTestHost.TestRuntime`,
  `ExpoModuleTestHost.Runtime`,
  `ExpoModuleTestHost.Evaluate`, and ordered idempotent `Dispose`.
- Reserves an internal pending-Promise registry used by Task 3.

- [ ] **Step 1: Write failing registration and teardown tests**

Create `ExpoModuleTestHostTests.cs` with these initial tests:

```csharp
using Expo.ModulesCore.Testing;
using Xunit;

namespace Expo.ModulesCore.Tests.Testing;

public sealed partial class ExpoModuleTestHostTests
{
  [Fact]
  public void ExplicitProviderRegistersUnderDotnetModules()
  {
    using var host = ExpoModuleTestHost.Create((context, modules) =>
    {
      using var module = context.ModuleRegistry.DefineModule(modules, "HostTest");
      using var answer = context.Runtime.CreateNumber(42);
      module.SetProperty("answer", answer);
    });

    using var result = host.Evaluate(
        "globalThis._expoDotnet.modules.HostTest.answer",
        "module-host-registration.js"
    );
    Assert.Equal(42, result.AsDouble());
  }

  [Fact]
  public void RegistrationFailureDisposesCreatedContext()
  {
    DotnetRuntimeContext? captured = null;

    Assert.Throws<InvalidOperationException>(() =>
        ExpoModuleTestHost.Create((context, _) =>
        {
          captured = context;
          throw new InvalidOperationException("registration failed");
        })
    );

    Assert.NotNull(captured);
    Assert.Throws<ObjectDisposedException>(() => _ = captured!.ModuleRegistry);
  }

  [Fact]
  public void DisposeRunsModuleTeardownBeforeReleasingRuntime()
  {
    var callbacks = new List<string>();
    var host = ExpoModuleTestHost.Create((context, _) =>
    {
      context.ModuleRegistry.GetOrCreateModule(
          "Lifecycle",
          () => new LifecycleProbe(callbacks, context.Runtime),
          onCreate: null,
          onDestroy: probe => probe.OnDestroy()
      );
    });

    host.Dispose();
    host.Dispose();

    Assert.Equal(["destroy:runtime-live", "dispose"], callbacks);
  }

  [Fact]
  public void DisposeReleasesRuntimeWhenModuleTeardownThrows()
  {
    var host = ExpoModuleTestHost.Create((context, _) =>
    {
      context.ModuleRegistry.GetOrCreateModule(
          "ThrowingLifecycle",
          static () => new object(),
          onCreate: null,
          onDestroy: static _ =>
              throw new InvalidOperationException("destroy failed")
      );
    });
    var testRuntime = host.TestRuntime;

    var exception = Assert.Throws<AggregateException>(host.Dispose);

    Assert.Contains(
        exception.InnerExceptions,
        error => error.Message == "destroy failed"
    );
    Assert.Throws<ObjectDisposedException>(
        () => testRuntime.Evaluate("true").Dispose()
    );
  }

  private sealed class LifecycleProbe(
      List<string> callbacks,
      Expo.JSI.JavaScriptRuntime runtime
  ) : IDisposable
  {
    public void OnDestroy()
    {
      using var value = runtime.CreateString("runtime-live");
      callbacks.Add($"destroy:{value.AsString()}");
    }

    public void Dispose() => callbacks.Add("dispose");
  }
}
```

- [ ] **Step 2: Run the tests and confirm the host API is missing**

Run:

```sh
scripts/test-managed.sh --filter FullyQualifiedName~ExpoModuleTestHostTests
```

Expected: FAIL to compile because `ExpoModuleTestHost` does not exist.

- [ ] **Step 3: Implement construction, evaluation, and failure unwinding**

Implement this public shape:

```csharp
public sealed class ExpoModuleTestHost : IDisposable
{
  private readonly object gate = new();
  private HermesTestRuntime? testRuntime;
  private DotnetRuntimeContext? context;
  private bool disposed;

  private ExpoModuleTestHost(
      HermesTestRuntime testRuntime,
      DotnetRuntimeContext context)
  {
    this.testRuntime = testRuntime;
    this.context = context;
  }

  public HermesTestRuntime TestRuntime =>
      GetLiveTestRuntime();

  public JavaScriptRuntime Runtime =>
      GetLiveTestRuntime().Runtime;

  public static ExpoModuleTestHost Create(
      Action<DotnetRuntimeContext, JavaScriptObject> register)
  {
    ArgumentNullException.ThrowIfNull(register);
    HermesTestRuntime? testRuntime = null;
    try
    {
      testRuntime = HermesTestRuntime.Create();
      var context = testRuntime.Runtime.Execute(runtime =>
      {
        var created = new DotnetRuntimeContext(runtime);
        try
        {
          using var modules =
              created.ModuleRegistry.GetOrCreateDotnetModulesObject();
          register(created, modules);
          return created;
        }
        catch (Exception registrationException)
        {
          try
          {
            created.Dispose();
          }
          catch (Exception cleanupException)
          {
            throw new AggregateException(
                registrationException,
                cleanupException
            );
          }
          System.Runtime.ExceptionServices.ExceptionDispatchInfo
              .Capture(registrationException)
              .Throw();
          throw new System.Diagnostics.UnreachableException();
        }
      });
      return new ExpoModuleTestHost(testRuntime, context);
    }
    catch
    {
      testRuntime?.Dispose();
      throw;
    }
  }

  public JavaScriptValue Evaluate(
      string source,
      string sourceUrl = "expo-module-test.js"
  ) => GetLiveTestRuntime().Evaluate(source, sourceUrl);
}
```

`GetLiveTestRuntime` must lock `gate` and throw
`ObjectDisposedException(nameof(ExpoModuleTestHost))` after disposal.

- [ ] **Step 4: Implement ordered idempotent teardown**

Inside the `gate`, mark the host disposed and detach the context/runtime once.
Outside the lock:

1. Dispose the `DotnetRuntimeContext` through
   `testRuntime.Runtime.Execute`.
2. Dispose `HermesTestRuntime` even when context teardown throws.
3. Rethrow one captured exception with its original stack through
   `ExceptionDispatchInfo`.
4. Throw `AggregateException` if both context and runtime teardown fail.
5. Make later `Dispose` calls no-ops.

Do not release Hermes before the context's module destroy/dispose callbacks
finish.

- [ ] **Step 5: Run host tests and core regressions**

Run:

```sh
scripts/test-managed.sh --filter FullyQualifiedName~ExpoModuleTestHostTests
scripts/test-managed.sh --filter FullyQualifiedName~DotnetRuntimeContextTests
```

Expected: PASS.

- [ ] **Step 6: Commit the authored-module host**

Stage only the two task files, scan staged content, then commit:

```sh
git commit -m "feat(test): add authored module test host"
```

---

### Task 3: Add event-driven Promise evaluation

**Files:**

- Create:
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Testing/JavaScriptPromiseRejectedException.cs`
- Create:
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Testing/Internal/PromiseEvaluationState.cs`
- Modify:
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Testing/ExpoModuleTestHost.cs`
- Modify:
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Testing/ExpoModuleTestHostTests.cs`

**Interfaces:**

- Produces:

```csharp
Task<JavaScriptValue> EvaluatePromiseAsync(
    string expression,
    CancellationToken cancellationToken = default);

Task<JavaScriptValue> EvaluatePromiseAsync(
    string expression,
    TimeSpan timeout,
    CancellationToken cancellationToken = default);
```

- Produces `JavaScriptPromiseRejectedException.JavaScriptName` and
  `.JavaScriptStack`; normal `Exception.Message` carries the JavaScript
  rejection message.
- Uses no global property for callback wiring and no polling loop.

- [ ] **Step 1: Add failing fulfillment, rejection, and non-Promise tests**

Append these tests to `ExpoModuleTestHostTests`:

```csharp
[Fact]
public async Task PromiseFulfillmentReturnsOwnedValue()
{
  using var host = ExpoModuleTestHost.Create((_, _) => { });
  using var result = await host.EvaluatePromiseAsync(
      "Promise.resolve('ready')",
      TestContext.Current.CancellationToken
  );

  Assert.Equal("ready", result.AsString());
}

[Fact]
public async Task PromiseRejectionPreservesErrorDetails()
{
  using var host = ExpoModuleTestHost.Create((_, _) => { });

  var exception = await Assert.ThrowsAsync<JavaScriptPromiseRejectedException>(
      () => host.EvaluatePromiseAsync(
          "Promise.reject(new TypeError('bad input'))",
          TestContext.Current.CancellationToken
      )
  );

  Assert.Equal("TypeError", exception.JavaScriptName);
  Assert.Equal("bad input", exception.Message);
  Assert.False(string.IsNullOrWhiteSpace(exception.JavaScriptStack));
}

[Fact]
public async Task NonErrorRejectionUsesJavaScriptString()
{
  using var host = ExpoModuleTestHost.Create((_, _) => { });

  var exception = await Assert.ThrowsAsync<JavaScriptPromiseRejectedException>(
      () => host.EvaluatePromiseAsync(
          "Promise.reject('plain failure')",
          TestContext.Current.CancellationToken
      )
  );

  Assert.Null(exception.JavaScriptName);
  Assert.Equal("plain failure", exception.Message);
  Assert.Null(exception.JavaScriptStack);
}

[Fact]
public async Task PromiseEvaluationRejectsSynchronousValue()
{
  using var host = ExpoModuleTestHost.Create((_, _) => { });

  var exception = await Assert.ThrowsAsync<InvalidOperationException>(
      () => host.EvaluatePromiseAsync(
          "42",
          TestContext.Current.CancellationToken
      )
  );

  Assert.Contains("Promise", exception.Message);
}
```

- [ ] **Step 2: Add failing cancellation, timeout, late-settlement, and disposal tests**

Add:

```csharp
[Fact]
public async Task PendingPromiseHonorsCancellation()
{
  using var host = ExpoModuleTestHost.Create((_, _) => { });
  using var cancellation = new CancellationTokenSource();
  var pending = host.EvaluatePromiseAsync(
      "new Promise(() => {})",
      cancellation.Token
  );

  cancellation.Cancel();

  await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
}

[Fact]
public async Task PendingPromiseUsesConfiguredTimeout()
{
  using var host = ExpoModuleTestHost.Create((_, _) => { });

  await Assert.ThrowsAsync<TimeoutException>(() =>
      host.EvaluatePromiseAsync(
          "new Promise(() => {})",
          TimeSpan.FromMilliseconds(25),
          TestContext.Current.CancellationToken
      )
  );
}

[Fact]
public async Task LateSettlementAfterTimeoutIsSafe()
{
  using var host = ExpoModuleTestHost.Create((_, _) => { });
  var pending = host.EvaluatePromiseAsync(
      """
      new Promise(resolve => {
        globalThis.__resolveTimedOutPromise = resolve;
      })
      """,
      TimeSpan.FromMilliseconds(25),
      TestContext.Current.CancellationToken
  );
  await Assert.ThrowsAsync<TimeoutException>(() => pending);

  host.Evaluate(
      "globalThis.__resolveTimedOutPromise('late'); true",
      "late-promise-settlement.js"
  ).Dispose();
  host.TestRuntime.WaitUntilIdle();
}

[Fact]
public async Task DisposingHostFaultsPendingPromiseWait()
{
  var host = ExpoModuleTestHost.Create((_, _) => { });
  var pending = host.EvaluatePromiseAsync(
      "new Promise(() => {})",
      TestContext.Current.CancellationToken
  );

  host.Dispose();

  await Assert.ThrowsAsync<ObjectDisposedException>(() => pending);
}
```

- [ ] **Step 3: Run the Promise tests and confirm the APIs are missing**

Run:

```sh
scripts/test-managed.sh --filter FullyQualifiedName~ExpoModuleTestHostTests
```

Expected: FAIL to compile because the Promise overloads and rejection exception
do not exist.

- [ ] **Step 4: Implement the rejection exception**

Create:

```csharp
namespace Expo.ModulesCore.Testing;

public sealed class JavaScriptPromiseRejectedException : Exception
{
  internal JavaScriptPromiseRejectedException(
      string message,
      string? javaScriptName,
      string? javaScriptStack
  ) : base(message)
  {
    JavaScriptName = javaScriptName;
    JavaScriptStack = javaScriptStack;
  }

  public string? JavaScriptName { get; }

  public string? JavaScriptStack { get; }
}
```

When a callback receives a rejection:

- If `JavaScriptValueRef.Retain().IsError` is true, wrap it with
  `AsErrorObject()` and copy `Name`, `Message`, and `Stack`.
- Otherwise, use `JavaScriptValueRef.CoerceToString()` for the message and
  pass null for the name and stack.

- [ ] **Step 5: Implement a race-safe Promise evaluation state**

`PromiseEvaluationState` owns:

- one lock;
- `TaskCompletionSource<bool>` with
  `TaskCreationOptions.RunContinuationsAsynchronously`;
- one optional retained fulfillment `JavaScriptValue`;
- one optional rejection `Exception`; and
- a state enum with `Waiting`, `Settled`, `Transferred`, and `Abandoned`.

Implement these exact transitions:

| Method | Transition |
| --- | --- |
| `TryResolve(value)` | `Waiting -> Settled`, take ownership of the supplied retained value and signal; otherwise dispose the incoming retained value |
| `TryReject(exception)` | `Waiting -> Settled`, store exception, signal; otherwise ignore |
| `TakeOutcome()` | `Settled -> Transferred`, return the value or throw the rejection |
| `Abandon()` | `Waiting/Settled -> Abandoned`, dispose a stored value, make later callbacks no-ops |
| `FailFromHostDisposal()` | `Waiting/Settled -> Settled` with `ObjectDisposedException`, disposing any untransferred result and signaling; no-op after `Transferred` or `Abandoned` |

`TakeOutcome` must clear its retained value before returning it so ownership
transfers exactly once. No method may dispose a value after it has been
transferred to the caller.

- [ ] **Step 6: Attach managed callbacks directly to the evaluated Promise**

In `ExpoModuleTestHost`, use `Runtime.Execute` to:

1. Evaluate the expression.
2. Check `JavaScriptValue.IsPromise`; throw `InvalidOperationException` for
   any other value.
3. Convert the Promise to an object and get its `then` function.
4. Create `onFulfilled` and `onRejected` with
   `JavaScriptRuntime.CreateHostFunction`.
5. Call `then.CallWithThis(promiseObject, callbacks)`.
6. Dispose TestCore's owned Promise, object, `then`, callback, and chained
   Promise wrappers after attachment.

The callback shape is:

```csharp
private static JavaScriptValue ResolvePromise(
    JavaScriptRuntime runtime,
    JavaScriptValueRef _,
    JavaScriptArguments arguments,
    object stateObject)
{
  var state = (PromiseEvaluationState)stateObject;
  var value = arguments.Count == 0
      ? runtime.CreateUndefined()
      : arguments.GetValue(0).Retain();
  state.TryResolve(value);
  return runtime.CreateUndefined();
}
```

`TryResolve` takes ownership of `value` in every path. The rejection callback
builds `JavaScriptPromiseRejectedException`, passes it to `TryReject`, and
returns `runtime.CreateUndefined()`.

Do not install host functions on `globalThis`. JavaScript's Promise reaction
list may retain the callbacks after TestCore disposes its wrappers; the state
therefore becomes an inert no-op after abandonment and follows normal JSI
lifetime.

- [ ] **Step 7: Implement timeout/cancellation and host-disposal coordination**

Use a private five-second `DefaultPromiseTimeout`. The no-timeout overload
delegates to the explicit timeout overload. Reject `timeout <= TimeSpan.Zero`
with `ArgumentOutOfRangeException`.

Track active `PromiseEvaluationState` instances under the host's existing
`gate`. Add an internal `ActivePromiseEvaluationCount` property for
`Expo.ModulesCore.Tests` cleanup assertions. For each call:

1. Reject calls after host disposal.
2. Throw for an already-canceled token before attaching callbacks.
3. Add the state before attaching callbacks.
4. If callback attachment fails, call `Abandon` and rethrow.
5. Await `state.Signal.Task.WaitAsync(timeout, cancellationToken)`.
6. Call `TakeOutcome` after the signal.
7. On timeout or cancellation, call `Abandon` before rethrowing.
8. Remove the state from the active set in `finally`.

At the start of `ExpoModuleTestHost.Dispose`, detach the active states and call
`FailFromHostDisposal` before disposing the module context. This guarantees
pending callers finish with `ObjectDisposedException` while late JS callbacks
remain harmless.

After every fulfillment, rejection, cancellation, timeout, and disposal test,
assert `host.ActivePromiseEvaluationCount == 0` when the host remains
available. For the disposal case, retain the state count before detaching or
expose an internal post-disposal count that returns zero instead of throwing.

- [ ] **Step 8: Run all Promise and host lifecycle tests**

Run:

```sh
scripts/test-managed.sh --filter FullyQualifiedName~ExpoModuleTestHostTests
```

Expected: PASS with no polling delays in production TestCore code.

- [ ] **Step 9: Commit Promise evaluation**

Stage only the Task 3 files, scan staged content, then commit:

```sh
git commit -m "feat(test): evaluate module promises through Hermes"
```

---

### Task 4: Discover and select managed test projects

**Files:**

- Modify: `scripts/test-managed.sh`
- Modify: `scripts/test-managed.ps1`

**Interfaces:**

- Bash selection:
  `scripts/test-managed.sh --project <repo-relative-test-csproj> [--project <...>] [dotnet test args...]`
- PowerShell selection:
  `scripts/test-managed.ps1 -Project <repo-relative-test-csproj>[,<...>] [dotnet test args...]`
- Default discovery:
  `packages/*/dotnet/*.Tests/*.Tests.csproj`, sorted by repo-relative path.

- [ ] **Step 1: Prove invalid selection is not yet handled**

Run:

```sh
scripts/test-managed.sh --project ../outside/Outside.Tests.csproj
```

Expected before implementation: the script treats `--project` as a
`dotnet test` argument or reaches the Hermes prebuilt check instead of
reporting an invalid repo-relative test project before native setup.

- [ ] **Step 2: Parse and validate Bash project selection before native checks**

Add arrays `selected_projects` and `dotnet_test_args`. Parse repeatable
`--project <path>`; all other arguments remain `dotnet test` arguments. Update
help text with the new syntax.

For each selected path:

1. Reject an absolute path.
2. Join it to `repo_root`.
3. Require an existing regular, non-symlink file named `*.Tests.csproj`.
4. Resolve its parent with `pwd -P`.
5. Require the resolved path to start with `"$repo_root/"`.
6. Store the resolved absolute path once, rejecting duplicates.

Perform validation before checking `HERMES_PREBUILT_ROOT` so invalid selection
fails without configuring or building native code.

- [ ] **Step 3: Discover authored test projects in deterministic Bash order**

When no project is selected, build the test list from:

```text
packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj
packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Expo.JSI.Tests.csproj
packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj
```

Then append regular files found at depth four below `packages/` whose path
matches `*/dotnet/*.Tests/*.Tests.csproj`. Sort discovered authored paths with
`LC_ALL=C sort` and reject duplicates against the fixed projects.

After building the native testhost, loop over the final project list:

```bash
for test_project in "${test_projects[@]}"; do
  echo
  echo "==> Running $(basename "$test_project" .csproj)"
  EXPO_JSI_TESTHOST_LIBRARY="$testhost_library" \
    dotnet test "$test_project" -c "$configuration" "${dotnet_test_args[@]}"
done
```

Keep the three prerequisite `dotnet build` calls. Do not run
`Expo.ModulesCore.Generator.Tests` separately once it is in `test_projects`.

- [ ] **Step 4: Add equivalent PowerShell selection and discovery**

Add this parameter before `DotNetTestArgs`:

```powershell
[string[]]$Project = @(),
```

Validate each selected path with `[IO.Path]::GetFullPath`, require it to begin
with `$repoRoot + [IO.Path]::DirectorySeparatorChar` using
`OrdinalIgnoreCase`, require a non-symlink regular file, and require the leaf
name to end in `.Tests.csproj`.

When `$Project.Count -eq 0`, append authored projects discovered only from
direct `packages\<package>\dotnet\<name>.Tests\<name>.Tests.csproj` paths and
sort by full path. Run the same fixed core list followed by discovered authored
projects. Set `$env:EXPO_JSI_TESTHOST_LIBRARY` once before the loop.

- [ ] **Step 5: Verify invalid selection and selected core execution**

Run:

```sh
scripts/test-managed.sh --project ../outside/Outside.Tests.csproj
scripts/test-managed.sh \
  --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj \
  --filter FullyQualifiedName~ExpoModuleTestHostTests
```

Expected:

- First command: FAIL before Hermes setup with an invalid project-path message.
- Second command: PASS and run only `Expo.ModulesCore.Tests`.

On Windows, run:

```powershell
scripts/test-managed.ps1 `
  -Project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj `
  --filter FullyQualifiedName~ExpoModuleTestHostTests
```

Expected: PASS and run only `Expo.ModulesCore.Tests`.

- [ ] **Step 6: Commit runner discovery and selection**

Stage both runners, scan staged content, then commit:

```sh
git commit -m "feat(test): discover authored module test projects"
```

---

### Task 5: Make ExampleModule the first TestCore consumer

**Files:**

- Create:
  `packages/example-module/dotnet/ExampleModule.Tests/ExampleModule.Tests.csproj`
- Create:
  `packages/example-module/dotnet/ExampleModule.Tests/AssemblyInfo.cs`
- Move:
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Modules/ExampleModuleShowcaseTests.cs`
  to
  `packages/example-module/dotnet/ExampleModule.Tests/ExampleModuleShowcaseTests.cs`
- Create:
  `packages/example-module/dotnet/ExampleModule.Tests/ExampleCounterTests.cs`
- Modify:
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj`

**Interfaces:**

- Consumes:
  `ExpoModuleTestHost.Create(ExpoModulesProvider_ExampleModule.Register)` and
  `EvaluatePromiseAsync`.
- Produces the first discovered mixed pure-C#/Hermes authored-module test
  project.

- [ ] **Step 1: Create the test project and non-parallel assembly setting**

Create:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.0" />
    <PackageReference Include="xunit.v3" Version="3.2.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../ExampleModule/ExampleModule.csproj" />
    <ProjectReference Include="../../../expo-modules-dotnet/managed/packages/Expo.ModulesCore.Testing/Expo.ModulesCore.Testing.csproj" />
  </ItemGroup>
</Project>
```

Create `AssemblyInfo.cs`:

```csharp
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

Remove the `ExampleModule.csproj` reference from
`Expo.ModulesCore.Tests.csproj`.

- [ ] **Step 2: Move the showcase tests and confirm they no longer compile**

Use `git mv` for `ExampleModuleShowcaseTests.cs`. Change its namespace to
`ExampleModule.Tests` and imports to:

```csharp
using Expo.JSI;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Testing;
using Xunit;
```

Run:

```sh
scripts/test-managed.sh \
  --project packages/example-module/dotnet/ExampleModule.Tests/ExampleModule.Tests.csproj
```

Expected: FAIL because the moved tests still construct
`HermesRuntimeFixture` and manually own `DotnetRuntimeContext`.

- [ ] **Step 3: Rewrite the async showcase around one owned module host**

Replace manual fixture/context registration and the polling helper with:

```csharp
using var host = ExpoModuleTestHost.Create(
    ExpoModulesProvider_ExampleModule.Register
);
using var value = await host.EvaluatePromiseAsync(
    """
    (async () => {
      const module = globalThis._expoDotnet.modules.ExampleModule;
      let eventPayload = null;
      const subscription = module.addListener(
        'onStatus',
        value => { eventPayload = value; }
      );
      try {
        const add = module.add(20, 22);
        const message = await module.getMessageAsync();
        const record = module.describeUser({ name: 'Ada', age: 37 });
        const callbackResult =
          module.transformWithCallback('JS', value => `callback(${value})`);
        await module.emitStatusAsync('ready');
        return {
          add,
          asyncMessage: message,
          recordSummary: `${record.name}:${record.age}:${record.summary}`,
          callbackResult,
          eventPayload
        };
      } finally {
        subscription.remove();
      }
    })()
    """,
    TestContext.Current.CancellationToken
);
using var outcome = value.AsObject();
```

Read the five result properties with owned wrappers and keep the current exact
value assertions: `42`, `"Hello from async C#"`,
`"Ada:37:Ada is 37"`, `"callback(C# sent JS)"`, and
`"C# event: ready"`. Remove the obsolete `EventDone` record field and
assertion. Delete `WaitForShowcaseAsync` and its delay loop.

Rewrite the shared-object test to create one `ExpoModuleTestHost`, evaluate the
existing JavaScript expression, and assert the same
`"12:12:6:true:true"` result. Do not manually create or dispose a runtime
context.

- [ ] **Step 4: Add a direct pure-C# ExampleCounter test**

Create:

```csharp
using Xunit;

namespace ExampleModule.Tests;

public sealed class ExampleCounterTests
{
  [Fact]
  public void IncrementUpdatesCount()
  {
    var counter = new global::ExampleModule.ExampleCounter(10);

    var result = counter.Increment(2);

    Assert.Equal(12, result);
    Assert.Equal(12, counter.Count);
  }
}
```

This test must not create TestCore or require a JavaScript runtime.

- [ ] **Step 5: Run the selected project and full discovery**

Run:

```sh
dotnet test \
  packages/example-module/dotnet/ExampleModule.Tests/ExampleModule.Tests.csproj \
  --filter FullyQualifiedName~ExampleCounterTests
scripts/test-managed.sh \
  --project packages/example-module/dotnet/ExampleModule.Tests/ExampleModule.Tests.csproj
scripts/test-managed.sh
```

Expected:

- Direct filtered run: PASS without `EXPO_JSI_TESTHOST_LIBRARY`.
- Selected run: PASS for the pure and Hermes ExampleModule tests.
- Full run: PASS and output includes `Running ExampleModule.Tests` without an
  explicit runner list edit.

- [ ] **Step 6: Commit the first authored consumer**

Stage only the ExampleModule test project, moved tests, new pure test, and core
csproj reference removal. Scan staged content, then commit:

```sh
git commit -m "test(example-module): own authored module coverage"
```

---

### Task 6: Merge accepted behavior into living docs and close the delta

**Files:**

- Modify: `docs/README.md`
- Modify: `docs/specs/modules-core-boundary.md`
- Modify: `docs/specs/hermes-testhost.md`
- Modify: `docs/module-authoring-guide.md`
- Modify: `docs/roadmap.md`
- Delete:
  `docs/changes/2026-07-24-authored-module-test-core/spec.md`
- Delete:
  `docs/changes/2026-07-24-authored-module-test-core/plan.md`

**Interfaces:**

- Consumes all verified implementation behavior from Tasks 1-5.
- Produces durable current-state documentation and the external-consumption
  backlog item.

- [ ] **Step 1: Update current-state package and test ownership docs**

In `docs/README.md`, add `Expo.ModulesCore.Testing` to the current repository
state and state that `ExampleModule.Tests` owns ExampleModule behavior.

In `docs/specs/modules-core-boundary.md`, replace “ModulesCore Owns Module
Tests” with two atomic requirements:

```markdown
### Requirement: ModulesCore Owns Framework Module Tests

`Expo.ModulesCore.Tests` SHALL own generated binding, codec, registry,
lifecycle, event, callback, and shared-object behavior that is independent of
one authored package.

### Requirement: Authored Packages Own Their Behavior Tests

Each repo-local authored module package SHALL own its module-specific tests in
a `.Tests` project and MAY combine pure C# tests with Hermes-backed
`Expo.ModulesCore.Testing` tests.
```

Add scenarios matching the implemented ExampleModule move and explicit
provider registration.

- [ ] **Step 2: Update the Hermes runner specification**

In `docs/specs/hermes-testhost.md`:

- Add `Expo.ModulesCore.Testing` as the module-layer testhost owner.
- Keep `Expo.JSI.Tests` independent.
- Change both platform runner scenarios to include deterministic discovery of
  `packages/*/dotnet/*.Tests/*.Tests.csproj`.
- Specify Bash `--project` and PowerShell `-Project` selection, validation
  before native setup, and one shared testhost path.
- Replace the old statement that all module behavior tests live in
  `Expo.ModulesCore.Tests` with the framework-versus-authored ownership split.

- [ ] **Step 3: Add the authored testing guide and external backlog**

Replace the short verification paragraph in `docs/module-authoring-guide.md`
with:

- the exact `ExampleModule.Tests.csproj` reference shape;
- the `AssemblyInfo.cs` parallelization attribute;
- direct pure-test guidance;
- explicit generated-provider registration;
- `using` ownership for the host and fulfilled values;
- synchronous `Evaluate` and Promise `EvaluatePromiseAsync` examples;
- full-suite and selected-project commands; and
- the rule that unfiltered mixed projects require the canonical runner.

In `docs/roadmap.md`, add one deferred item under the authoring path:

```markdown
**External authored-module testing**

- Package `Expo.ModulesCore.Testing` for separate repositories.
- Deliver RID-specific Hermes/testhost native assets.
- Provide a standalone test command that provisions the native runtime.
```

Do not claim these deferred capabilities exist.

- [ ] **Step 4: Run complete verification before removing change artifacts**

Run:

```sh
scripts/test-managed.sh \
  --project packages/example-module/dotnet/ExampleModule.Tests/ExampleModule.Tests.csproj
scripts/test-managed.sh
scripts/format.sh --check --all
git diff --check
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" packages/expo-modules-dotnet/managed/packages
```

Expected:

- Both managed commands PASS.
- Formatting and diff checks exit 0.
- The hot-path search has no new match introduced by TestCore.

On Windows, run:

```powershell
scripts/test-managed.ps1 `
  -Project packages/example-module/dotnet/ExampleModule.Tests/ExampleModule.Tests.csproj
scripts/test-managed.ps1
```

Expected: both PASS. If Windows execution is unavailable locally, do not mark
the implementation complete until the existing Windows `native-tests` lane
records the required pass.

- [ ] **Step 5: Merge the delta and remove transient artifacts**

After all required verification passes, delete this change directory. Confirm
that every accepted requirement now lives in `docs/specs/`, the authoring
guide, or the roadmap, and that no current-state document points at the
deleted delta.

Run:

```sh
git diff --check
rg "2026-07-24-authored-module-test-core" docs AGENTS.md .agents/skills
rg "self[-]contained planning package|planning[ ]artifacts,[ ]not[ ]implementation|expo[-]modules[-]windows[-]core|Phase[ ]1:[ ]clean[ ]separate[ ]research[ ]repo|create[ ]a[ ]clean[ ]local[ ]research[ ]repository" docs/README.md docs/specs docs/roadmap.md AGENTS.md .agents/skills
```

Expected: all checks exit 0 with no unintended match.

- [ ] **Step 6: Commit durable docs and change closure**

Stage the living docs and deleted change artifacts, run the staged
machine-local path scan, then commit:

```sh
git commit -m "docs: document authored module testing"
```

After the commit, verify `git status --short` is empty. Do not push or open a
PR without explicit operator approval.
