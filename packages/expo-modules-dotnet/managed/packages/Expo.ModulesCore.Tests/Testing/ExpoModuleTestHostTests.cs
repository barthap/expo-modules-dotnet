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

    host.Runtime.Execute(_ =>
    {
      using var result = host.Evaluate(
          "globalThis._expoDotnet.modules.HostTest.answer",
          "module-host-registration.js"
      );
      Assert.Equal(42, result.AsDouble());
      return true;
    });
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

  [Fact]
  public async Task PromiseFulfillmentReturnsOwnedValue()
  {
    using var host = ExpoModuleTestHost.Create((_, _) => { });
    var result = await host.EvaluatePromiseAsync(
        "Promise.resolve('ready')",
        TestContext.Current.CancellationToken
    );

    host.Runtime.Execute(_ =>
    {
      using (result)
      {
        Assert.Equal("ready", result.AsString());
      }
      return true;
    });
    Assert.Equal(0, host.ActivePromiseEvaluationCount);
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
    Assert.Equal(0, host.ActivePromiseEvaluationCount);
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
    Assert.Equal(0, host.ActivePromiseEvaluationCount);
  }

  [Fact]
  public async Task RepeatedPromiseFulfillmentReleasesEveryOwnedValue()
  {
    using var host = ExpoModuleTestHost.Create((_, _) => { });
    host.TestRuntime.ResetCounters();

    for (var iteration = 0; iteration < 10; iteration++)
    {
      using var result = await host.EvaluatePromiseAsync(
          "Promise.resolve('ready')",
          TestContext.Current.CancellationToken
      );
      host.Runtime.Execute(_ =>
      {
        Assert.Equal("ready", result.AsString());
        return true;
      });
    }

    host.TestRuntime.WaitUntilIdle();
    Assert.Equal(110u, host.TestRuntime.Counters.ReleasedValues);
  }

  [Fact]
  public async Task RejectionWithThrowingToStringSettlesWithExtractionContext()
  {
    using var host = ExpoModuleTestHost.Create((_, _) => { });

    var exception = await Assert.ThrowsAsync<JavaScriptPromiseRejectedException>(
        () => host.EvaluatePromiseAsync(
            "Promise.reject({ toString() { throw new Error('toString failed'); } })",
            TestContext.Current.CancellationToken
        )
    );

    Assert.StartsWith("Failed to extract JavaScript Promise rejection:", exception.Message);
    Assert.Contains("toString failed", exception.Message);
    Assert.Equal(0, host.ActivePromiseEvaluationCount);
  }

  [Fact]
  public async Task RejectionWithThrowingErrorPropertySettlesWithExtractionContext()
  {
    using var host = ExpoModuleTestHost.Create((_, _) => { });

    var exception = await Assert.ThrowsAsync<JavaScriptPromiseRejectedException>(
        () => host.EvaluatePromiseAsync(
            """
            (() => {
              const error = new Error('original failure');
              Object.defineProperty(error, 'message', {
                get() { throw new Error('message getter failed'); }
              });
              return Promise.reject(error);
            })()
            """,
            TestContext.Current.CancellationToken
        )
    );

    Assert.StartsWith("Failed to extract JavaScript Promise rejection:", exception.Message);
    Assert.Contains("message getter failed", exception.Message);
    Assert.Equal(0, host.ActivePromiseEvaluationCount);
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
    Assert.Equal(0, host.ActivePromiseEvaluationCount);
  }

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
    Assert.Equal(0, host.ActivePromiseEvaluationCount);
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
    Assert.Equal(0, host.ActivePromiseEvaluationCount);
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
    Assert.Equal(0, host.ActivePromiseEvaluationCount);

    host.Runtime.Execute(_ =>
    {
      using var settled = host.Evaluate(
          "globalThis.__resolveTimedOutPromise('late'); true",
          "late-promise-settlement.js"
      );
      return true;
    });
    host.TestRuntime.WaitUntilIdle();
    Assert.Equal(0, host.ActivePromiseEvaluationCount);
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
    Assert.Equal(0, host.ActivePromiseEvaluationCount);
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
