using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

public sealed class JavaScriptPromiseTests
{
  [Fact]
  public void UnresolvedPromiseIsRegisteredAndAbruptTeardownAbandonsItExactlyOnce()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();
    var promise = fixture.Runtime.Execute(runtime => runtime.CreatePromise());

    Assert.Equal(1u, fixture.Counters.LongLivedObjectsRemaining);
    Assert.Equal(0u, fixture.Counters.LongLivedPromisesReleased);
    Assert.Equal(0u, fixture.Counters.LongLivedPromisesAbandoned);

    fixture.ReleaseBridgeRuntimeHandle();
    Assert.Equal(0u, fixture.Counters.LongLivedObjectsRemaining);
    Assert.Equal(0u, fixture.Counters.LongLivedPromisesReleased);
    Assert.Equal(1u, fixture.Counters.LongLivedPromisesAbandoned);

    promise.Dispose();
    promise.Dispose();
    Assert.Equal(0u, fixture.Counters.LongLivedPromisesReleased);
    Assert.Equal(1u, fixture.Counters.LongLivedPromisesAbandoned);
  }

  [Fact]
  public void PromiseConstructorReenteringPreparationCannotRegisterAfterTerminalSweep()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();

    fixture.Runtime.Execute(runtime =>
    {
      using var global = runtime.Global();
      using var prepareRuntime = runtime.CreateHostFunction(
          "prepareRuntime",
          0,
          (_, _, _, _) =>
          {
            var result = runtime.CreateUndefined();
            fixture.PrepareRuntimeForInvalidation();
            return result;
          },
          new object()
      );
      using var prepareRuntimeValue = prepareRuntime.AsValue();
      global.SetProperty("prepareRuntime", prepareRuntimeValue);
      using var replace = fixture.Evaluate(
          "globalThis.Promise = function(executor) { prepareRuntime(); executor(() => {}, () => {}); return {}; }; undefined;",
          "promise-reentrant-prepare.js"
      );
      Assert.Throws<InvalidOperationException>(() => runtime.CreatePromise());
      return true;
    });

    Assert.Equal(0u, fixture.Counters.LongLivedObjectsRemaining);
    Assert.Equal(0u, fixture.Counters.LongLivedPromisesReleased);
    Assert.Equal(0u, fixture.Counters.LongLivedPromisesAbandoned);
  }

  [Fact]
  public void PromiseHandleAllocationFailureRollsBackTheRegisteredEntry()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();
    fixture.FailNextPromiseHandleAllocation();

    fixture.Runtime.Execute(runtime =>
    {
      Assert.Throws<InvalidOperationException>(() => runtime.CreatePromise());
      return true;
    });

    Assert.Equal(0u, fixture.Counters.LongLivedObjectsRemaining);
    Assert.Equal(1u, fixture.Counters.LongLivedPromisesReleased);
    Assert.Equal(0u, fixture.Counters.LongLivedPromisesAbandoned);
  }

  [Fact]
  public async Task PromiseRegistrationRacingPreparationIsEitherRejectedOrSwept()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();
    var created = false;
    Task? preparation = null;
    Task creation = Task.CompletedTask;
    fixture.PauseNextPromiseRegistration();
    try
    {
      creation = Task.Run(() => fixture.Runtime.Execute(runtime =>
      {
        try { using var promise = runtime.CreatePromise(); created = true; }
        catch (InvalidOperationException) { }
        return true;
      }), TestContext.Current.CancellationToken);
      fixture.WaitUntilPromiseRegistrationPaused();
      preparation = Task.Run(
          fixture.PrepareRuntimeForInvalidation,
          TestContext.Current.CancellationToken
      );
      fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate);
      fixture.ResumePromiseRegistration();
      await Task.WhenAll(creation, preparation);
    }
    finally
    {
      fixture.ResumePromiseRegistration();
      await Task.WhenAll(creation, preparation ?? Task.CompletedTask);
    }

    Assert.Equal(0u, fixture.Counters.LongLivedObjectsRemaining);
    Assert.Equal(created ? 1u : 0u, fixture.Counters.LongLivedPromisesReleased);
    Assert.Equal(0u, fixture.Counters.LongLivedPromisesAbandoned);
  }

  [Fact]
  public async Task PromiseRegistrationGateCancelsWhenConstructionFailsBeforeRegistration()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.Runtime.Execute(_ =>
    {
      using var setup = fixture.Evaluate(
          "globalThis.Promise = function() { throw new Error('before registration'); }; undefined;",
          "promise-registration-pre-gate-failure.js"
      );
      return true;
    });

    fixture.PauseNextPromiseRegistration();
    var waiter = Task.Run(
        fixture.WaitUntilPromiseRegistrationPaused,
        TestContext.Current.CancellationToken
    );
    var creation = Task.Run(() => fixture.Runtime.Execute(runtime =>
    {
      Assert.Throws<InvalidOperationException>(() => runtime.CreatePromise());
      return true;
    }), TestContext.Current.CancellationToken);

    try
    {
      await creation;
      fixture.ResumePromiseRegistration();
      Assert.False(await waiter.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
    }
    finally
    {
      fixture.ResumePromiseRegistration();
      await Task.WhenAll(creation, waiter);
    }
  }

  [Fact]
  public void OwnedPromiseResultClaimsStateExactlyOnce()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var probe = new DisposableProbe();
    var result = JavaScriptPromiseResult.ResolveOwned(
        probe,
        static (runtime, _) => runtime.CreateUndefined(),
        static state => state.Dispose()
    );

    using var value = fixture.Runtime.Execute(runtime => result.CreateValue(runtime));
    result.Abandon();

    Assert.Equal(0, probe.DisposeCount);
  }

  [Fact]
  public void OwnedPromiseResultAbandonsUnclaimedState()
  {
    var probe = new DisposableProbe();
    var result = JavaScriptPromiseResult.ResolveOwned(
        probe,
        static (runtime, _) => runtime.CreateUndefined(),
        static state => state.Dispose()
    );

    result.Abandon();
    result.Abandon();

    Assert.Equal(1, probe.DisposeCount);
  }

  [Fact]
  public async Task DroppedSettlementAbandonsOwnedResult()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var abandoned = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    var probe = new DisposableProbe(() => abandoned.TrySetResult());
    var operation = new TaskCompletionSource<JavaScriptPromiseResult>(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    using var promiseValue = fixture.Runtime.Execute(_ =>
        fixture.Runtime.CreatePromise(_ => operation.Task)
    );
    fixture.PauseRuntimeExecutor();
    operation.SetResult(JavaScriptPromiseResult.ResolveOwned(
        probe,
        static (runtime, _) => runtime.CreateUndefined(),
        static state => state.Dispose()
    ));
    fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate);
    fixture.DropQueuedRuntimeTask(JavaScriptTaskPriority.Immediate);

    await abandoned.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
    fixture.ResumeRuntimeExecutor();
    fixture.WaitUntilIdle();

    Assert.Equal(1, probe.DisposeCount);
  }

  private sealed class DisposableProbe : IDisposable
  {
    private readonly Action? onDispose;

    public DisposableProbe(Action? onDispose = null) => this.onDispose = onDispose;

    public int DisposeCount { get; private set; }
    public void Dispose()
    {
      DisposeCount++;
      onDispose?.Invoke();
    }
  }

  [Fact]
  public void CreatePromiseCreatesJavaScriptVisiblePromise()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var global = runtime.Global();
      using var promise = runtime.CreatePromise();
      using var promiseValue = promise.AsValue();
      global.SetProperty("managedPromise", promiseValue);

      using var isPromise = fixture.Evaluate(
          "globalThis.managedPromise instanceof Promise",
          "promise-create.js"
      );

      Assert.True(isPromise.AsBool());
      return true;
    });
  }

  [Fact]
  public void ResolveFulfillsPromiseWithProvidedValue()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var global = runtime.Global();
      using var promise = runtime.CreatePromise();
      using var promiseValue = promise.AsValue();
      global.SetProperty("managedPromise", promiseValue);

      using var setup = fixture.Evaluate(
          """
          globalThis.promiseResult = 0;
          globalThis.managedPromise.then(value => {
            globalThis.promiseResult = value;
          });
          undefined;
          """,
          "promise-resolve-setup.js"
      );

      using var value = runtime.CreateNumber(42);
      promise.Resolve(value);
      return true;
    });

    fixture.WaitUntilIdle();

    fixture.Runtime.Execute(_ =>
    {
      using var result = fixture.Evaluate("globalThis.promiseResult", "promise-resolve-result.js");
      Assert.Equal(42, result.AsDouble());
      return true;
    });
  }

  [Fact]
  public void RejectRejectsPromiseWithProvidedValue()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var global = runtime.Global();
      using var promise = runtime.CreatePromise();
      using var promiseValue = promise.AsValue();
      global.SetProperty("managedPromise", promiseValue);

      using var setup = fixture.Evaluate(
          """
          globalThis.rejectionReason = "";
          globalThis.managedPromise.catch(reason => {
            globalThis.rejectionReason = reason;
          });
          undefined;
          """,
          "promise-reject-setup.js"
      );

      using var reason = runtime.CreateString("failed");
      promise.Reject(reason);
      return true;
    });

    fixture.WaitUntilIdle();

    fixture.Runtime.Execute(_ =>
    {
      using var result = fixture.Evaluate(
          "globalThis.rejectionReason",
          "promise-reject-result.js"
      );
      Assert.Equal("failed", result.AsString());
      return true;
    });
  }

  [Fact]
  public void ResolveThenResolveInvokesThenOnceWithFirstValue()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var global = runtime.Global();
      using var promise = runtime.CreatePromise();
      using var promiseValue = promise.AsValue();
      global.SetProperty("managedPromise", promiseValue);

      using var setup = fixture.Evaluate(
          """
          globalThis.promiseResult = "";
          globalThis.promiseFulfillmentCount = 0;
          globalThis.managedPromise.then(value => {
            globalThis.promiseResult = value;
            globalThis.promiseFulfillmentCount += 1;
          });
          undefined;
          """,
          "promise-second-settlement-setup.js"
      );

      using var first = runtime.CreateString("first");
      using var second = runtime.CreateString("second");
      promise.Resolve(first);
      promise.Resolve(second);
      return true;
    });

    fixture.WaitUntilIdle();

    fixture.Runtime.Execute(_ =>
    {
      using var result = fixture.Evaluate(
          "globalThis.promiseResult",
          "promise-second-settlement-result.js"
      );
      using var fulfillmentCount = fixture.Evaluate(
          "globalThis.promiseFulfillmentCount",
          "promise-second-settlement-count.js"
      );
      Assert.Equal("first", result.AsString());
      Assert.Equal(1, fulfillmentCount.AsDouble());
      return true;
    });
  }

  [Fact]
  public void ResolveThenRejectInvokesThenOnceAndDoesNotInvokeCatch()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var global = runtime.Global();
      using var promise = runtime.CreatePromise();
      using var promiseValue = promise.AsValue();
      global.SetProperty("managedPromise", promiseValue);

      using var setup = fixture.Evaluate(
          """
          globalThis.promiseResult = "";
          globalThis.promiseFulfillmentCount = 0;
          globalThis.promiseRejectionCount = 0;
          globalThis.managedPromise.then(value => {
            globalThis.promiseResult = value;
            globalThis.promiseFulfillmentCount += 1;
          });
          globalThis.managedPromise.catch(() => {
            globalThis.promiseRejectionCount += 1;
          });
          undefined;
          """,
          "promise-resolve-then-reject-setup.js"
      );

      using var value = runtime.CreateString("resolved");
      using var reason = runtime.CreateString("rejected");
      promise.Resolve(value);
      promise.Reject(reason);
      return true;
    });

    fixture.WaitUntilIdle();

    fixture.Runtime.Execute(_ =>
    {
      using var result = fixture.Evaluate(
          "globalThis.promiseResult",
          "promise-resolve-then-reject-result.js"
      );
      using var fulfillmentCount = fixture.Evaluate(
          "globalThis.promiseFulfillmentCount",
          "promise-resolve-then-reject-fulfillment-count.js"
      );
      using var rejectionCount = fixture.Evaluate(
          "globalThis.promiseRejectionCount",
          "promise-resolve-then-reject-rejection-count.js"
      );
      Assert.Equal("resolved", result.AsString());
      Assert.Equal(1, fulfillmentCount.AsDouble());
      Assert.Equal(0, rejectionCount.AsDouble());
      return true;
    });
  }

  [Fact]
  public void RejectThenResolveInvokesCatchOnceWithFirstReason()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var global = runtime.Global();
      using var promise = runtime.CreatePromise();
      using var promiseValue = promise.AsValue();
      global.SetProperty("managedPromise", promiseValue);

      using var setup = fixture.Evaluate(
          """
          globalThis.rejectionReason = "";
          globalThis.promiseFulfillmentCount = 0;
          globalThis.promiseRejectionCount = 0;
          globalThis.managedPromise.then(
            () => {
              globalThis.promiseFulfillmentCount += 1;
            },
            () => {}
          );
          globalThis.managedPromise.catch(reason => {
            globalThis.rejectionReason = reason;
            globalThis.promiseRejectionCount += 1;
          });
          undefined;
          """,
          "promise-reject-then-resolve-setup.js"
      );

      using var reason = runtime.CreateString("rejected");
      using var value = runtime.CreateString("resolved");
      promise.Reject(reason);
      promise.Resolve(value);
      return true;
    });

    fixture.WaitUntilIdle();

    fixture.Runtime.Execute(_ =>
    {
      using var reason = fixture.Evaluate(
          "globalThis.rejectionReason",
          "promise-reject-then-resolve-reason.js"
      );
      using var fulfillmentCount = fixture.Evaluate(
          "globalThis.promiseFulfillmentCount",
          "promise-reject-then-resolve-fulfillment-count.js"
      );
      using var rejectionCount = fixture.Evaluate(
          "globalThis.promiseRejectionCount",
          "promise-reject-then-resolve-rejection-count.js"
      );
      Assert.Equal("rejected", reason.AsString());
      Assert.Equal(0, fulfillmentCount.AsDouble());
      Assert.Equal(1, rejectionCount.AsDouble());
      return true;
    });
  }

  [Fact]
  public void DisposingPromiseIncrementsReleaseCounter()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();

    fixture.Runtime.Execute(runtime =>
    {
      using (runtime.CreatePromise())
      {
      }

      return true;
    });

    Assert.True(fixture.Counters.ReleasedPromises >= 1);
  }

  [Fact]
  public void UsingDisposedPromiseThrows()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      var promise = runtime.CreatePromise();
      promise.Dispose();

      Assert.Throws<ObjectDisposedException>(() =>
      {
        using var _ = promise.AsValue();
      });
      return true;
    });
  }

  [Fact]
  public async Task CreatePromiseFromManagedTaskResolvesWithRuntimeCreatedValue()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var unblock = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

    fixture.Runtime.Execute(runtime =>
    {
      using var global = runtime.Global();
      using var promise = runtime.CreatePromise(async cancellationToken =>
      {
        var result = await unblock.Task.WaitAsync(cancellationToken);
        return JavaScriptPromiseResult.Resolve(js => js.CreateString(result));
      }, TestContext.Current.CancellationToken);
      using var promiseValue = promise.AsValue();
      global.SetProperty("managedTaskPromise", promiseValue);

      using var setup = fixture.Evaluate(
          """
          globalThis.managedTaskPromiseResult = "";
          globalThis.managedTaskPromise.then(value => {
            globalThis.managedTaskPromiseResult = value;
          });
          undefined;
          """,
          "promise-managed-task-resolve-setup.js"
      );

      return true;
    });

    unblock.SetResult("done");

    await EventuallyAsync(
        fixture,
        "globalThis.managedTaskPromiseResult",
        value => value.AsString() == "done"
    );
  }

  [Fact]
  public async Task CreatePromiseFromManagedTaskReleasesCapabilityOnRuntimeThread()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var unblock = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    fixture.ResetCounters();

    fixture.Runtime.Execute(runtime =>
    {
      using var global = runtime.Global();
      using var promise = runtime.CreatePromise(async cancellationToken =>
      {
        var result = await unblock.Task.WaitAsync(cancellationToken);
        return JavaScriptPromiseResult.Resolve(js => js.CreateString(result));
      }, TestContext.Current.CancellationToken);
      using var promiseValue = promise.AsValue();
      global.SetProperty("managedTaskPromise", promiseValue);

      using var setup = fixture.Evaluate(
          """
          globalThis.managedTaskPromiseResult = "";
          globalThis.managedTaskPromise.then(value => {
            globalThis.managedTaskPromiseResult = value;
          });
          undefined;
          """,
          "promise-managed-task-release-thread-setup.js"
      );

      return true;
    });

    unblock.SetResult("done");

    await EventuallyAsync(
        fixture,
        "globalThis.managedTaskPromiseResult",
        value => value.AsString() == "done"
    );
    await EventuallyCounterAsync(
        fixture,
        counters => counters.ReleasedPromises > 0 || counters.ReleasedPromisesOffRuntimeThread > 0
    );

    Assert.Equal(0u, fixture.Counters.ReleasedPromisesOffRuntimeThread);
    Assert.True(fixture.Counters.ReleasedPromises >= 1);
  }

  [Fact]
  public async Task CreatePromiseFromManagedTaskRejectsThrownExceptionWithJavaScriptErrorObject()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var global = runtime.Global();
      using var promise = runtime.CreatePromise(_ =>
        Task.FromException<JavaScriptPromiseResult>(
            new InvalidOperationException("managed failure")
        )
      );
      using var promiseValue = promise.AsValue();
      global.SetProperty("managedTaskPromise", promiseValue);

      using var setup = fixture.Evaluate(
          """
          globalThis.managedTaskPromiseRejectedWithError = false;
          globalThis.managedTaskPromiseRejectionMessage = "";
          globalThis.managedTaskPromise.catch(reason => {
            globalThis.managedTaskPromiseRejectedWithError = reason instanceof Error;
            globalThis.managedTaskPromiseRejectionMessage = reason.message;
          });
          undefined;
          """,
          "promise-managed-task-reject-setup.js"
      );

      return true;
    });

    await EventuallyAsync(
        fixture,
        "globalThis.managedTaskPromiseRejectedWithError",
        value => value.AsBool()
    );

    fixture.Runtime.Execute(_ =>
    {
      using var message = fixture.Evaluate(
          "globalThis.managedTaskPromiseRejectionMessage",
          "promise-managed-task-reject-message.js"
      );
      Assert.Equal("managed failure", message.AsString());
      return true;
    });
  }

  [Fact]
  public void CreateErrorObjectCreatesJavaScriptErrorObject()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var global = runtime.Global();
      using var error = runtime.CreateErrorObject("boom");

      Assert.Equal("Error", error.Name);
      Assert.Equal("boom", error.Message);
      var stack = error.Stack;
      Assert.NotNull(stack);
      Assert.Contains("boom", stack);

      using var errorValue = error.AsValue();
      global.SetProperty("managedError", errorValue);

      using var isError = fixture.Evaluate(
          "globalThis.managedError instanceof Error",
          "javascript-error-instanceof.js"
      );
      using var message = fixture.Evaluate(
          "globalThis.managedError.message",
          "javascript-error-message.js"
      );

      Assert.True(isError.AsBool());
      Assert.Equal("boom", message.AsString());
      return true;
    });
  }

  [Fact]
  public void JavaScriptValueCanBeCheckedAndWrappedAsPromiseValue()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var promise = runtime.CreatePromise();
      using var promiseValue = promise.AsValue();

      Assert.True(promiseValue.IsPromise);

      using var wrappedPromise = promiseValue.AsPromiseValue();
      using var wrappedPromiseValue = wrappedPromise.AsValue();
      using var global = runtime.Global();
      global.SetProperty("wrappedPromise", wrappedPromiseValue);

      using var isPromise = fixture.Evaluate(
          "globalThis.wrappedPromise instanceof Promise",
          "promise-value-checked-wrap.js"
      );

      Assert.True(isPromise.AsBool());
      return true;
    });
  }

  [Fact]
  public void NonPromiseValueCannotBeWrappedAsPromiseValue()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var notPromise = runtime.CreateString("not a promise");

      Assert.False(notPromise.IsPromise);
      Assert.Throws<InvalidOperationException>(() => notPromise.AsPromiseValue());
      return true;
    });
  }

  private static async Task EventuallyAsync(
      HermesRuntimeFixture fixture,
      string expression,
      Func<JavaScriptValue, bool> predicate
  )
  {
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
    while (DateTime.UtcNow < deadline)
    {
      fixture.WaitUntilIdle();
      var matched = fixture.Runtime.Execute(_ =>
      {
        using var value = fixture.Evaluate(expression, "promise-eventually.js");
        return predicate(value);
      });
      if (matched)
      {
        return;
      }

      await Task.Delay(10, TestContext.Current.CancellationToken);
    }

    Assert.Fail($"Timed out waiting for JavaScript expression to match: {expression}");
  }

  private static async Task EventuallyCounterAsync(
      HermesRuntimeFixture fixture,
      Func<NativeTestHost.Counters, bool> predicate
  )
  {
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
    while (DateTime.UtcNow < deadline)
    {
      fixture.WaitUntilIdle();
      if (predicate(fixture.Counters))
      {
        return;
      }

      await Task.Delay(10, TestContext.Current.CancellationToken);
    }

    Assert.Fail("Timed out waiting for native testhost counters to match.");
  }
}
