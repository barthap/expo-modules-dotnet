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
  public void SecondSettlementIsIgnored()
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
          globalThis.managedPromise.then(value => {
            globalThis.promiseResult = value;
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
      Assert.Equal("first", result.AsString());
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
}
