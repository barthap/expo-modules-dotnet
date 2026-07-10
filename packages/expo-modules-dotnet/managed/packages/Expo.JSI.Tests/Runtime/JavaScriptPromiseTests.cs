using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

public sealed class JavaScriptPromiseTests
{
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
