using Expo.JSI.Tests.Fixtures;
using Expo.JSI.Internal;
using Expo.JSI.Interop;
using Xunit;

namespace Expo.JSI.Tests.HostFunctions;

[Collection("JavaScriptArrayBuffer")]
public sealed class HostFunctionCallbackStateTests
{
  [Fact]
  public void CreateHostFunctionPreservesBinaryCompatibleOverloads()
  {
    var externalStateMethod = typeof(JavaScriptRuntime).GetMethod(
        nameof(JavaScriptRuntime.CreateHostFunction),
        [
          typeof(string),
          typeof(uint),
          typeof(JavaScriptHostFunction),
          typeof(object),
        ]
    );
    var ownedStateMethod = typeof(JavaScriptRuntime).GetMethod(
        nameof(JavaScriptRuntime.CreateHostFunction),
        [
          typeof(string),
          typeof(uint),
          typeof(JavaScriptHostFunction),
          typeof(object),
          typeof(Action<object>),
        ]
    );

    Assert.NotNull(externalStateMethod);
    Assert.Equal(4, externalStateMethod.GetParameters().Length);
    Assert.NotNull(ownedStateMethod);
    Assert.False(ownedStateMethod.GetParameters()[4].HasDefaultValue);
  }

  [Fact]
  public void DuplicateContextReleaseIsASafeNoOp()
  {
    var disposeCount = 0;
    var context = new HostFunctionContext(
        default,
        ReturnTrue,
        new object(),
        _ => Interlocked.Increment(ref disposeCount)
    );
    var pointer = context.ToIntPtr();

    HostFunctionContext.Release(pointer);
    Assert.Throws<ObjectDisposedException>(() => HostFunctionContext.FromIntPtr(pointer));
    HostFunctionContext.Release(pointer);

    Assert.Equal(1, disposeCount);
  }

  [Fact]
  public async Task ConcurrentContextReleaseDisposesStateExactlyOnce()
  {
    var disposeCount = 0;
    var context = new HostFunctionContext(
        default,
        ReturnTrue,
        new object(),
        _ => Interlocked.Increment(ref disposeCount)
    );
    var pointer = context.ToIntPtr();

    var releases = Enumerable.Range(0, 16)
        .Select(_ => Task.Run(() => HostFunctionContext.Release(pointer)))
        .ToArray();
    await Task.WhenAll(releases);

    Assert.Equal(1, disposeCount);
    Assert.Throws<ObjectDisposedException>(() => HostFunctionContext.FromIntPtr(pointer));
  }

  [Fact]
  public void OwnedCallbackStateIsDisposedExactlyOnceAfterJavaScriptCollection()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var disposeCount = 0;

    fixture.Runtime.Execute(runtime =>
    {
      using var function = runtime.CreateHostFunction(
          "ownedState",
          0,
          ReturnTrue,
          new object(),
          _ => Interlocked.Increment(ref disposeCount)
      );
      return true;
    });

    Assert.Equal(0, disposeCount);
    fixture.CollectGarbageForTesting();
    fixture.WaitUntilIdle();
    Assert.Equal(1, disposeCount);

    fixture.CollectGarbageForTesting();
    fixture.WaitUntilIdle();
    Assert.Equal(1, disposeCount);
  }

  [Fact]
  public void OwnedCallbackStateIsDisposedExactlyOnceAtRuntimeTeardown()
  {
    var fixture = HermesRuntimeFixture.Create();
    var disposeCount = 0;
    try
    {
      fixture.Runtime.Execute(runtime =>
      {
        using var global = runtime.Global();
        using var function = runtime.CreateHostFunction(
            "ownedState",
            0,
            ReturnTrue,
            new object(),
            _ => Interlocked.Increment(ref disposeCount)
        );
        using var functionValue = function.AsValue();
        global.SetProperty("ownedState", functionValue);
        return true;
      });

      Assert.Equal(0, disposeCount);
    }
    finally
    {
      fixture.Dispose();
    }

    Assert.Equal(1, disposeCount);
    fixture.Dispose();
    Assert.Equal(1, disposeCount);
  }

  [Fact]
  public void OwnedCallbackStateIsDisposedWhenHostFunctionCreationFails()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var disposeCount = 0;

    fixture.InvalidateRuntime();

    Assert.Throws<InvalidOperationException>(() =>
    {
      using var function = fixture.Runtime.CreateHostFunction(
          "ownedState",
          0,
          ReturnTrue,
          new object(),
          _ => Interlocked.Increment(ref disposeCount)
      );
    });

    Assert.Equal(1, disposeCount);
  }

  [Fact]
  public void OmittedDisposerLeavesCallbackStateExternallyOwned()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var state = new DisposableCallbackState();

    fixture.Runtime.Execute(runtime =>
    {
      using var function = runtime.CreateHostFunction("sharedState", 0, ReturnTrue, state);
      return true;
    });

    fixture.CollectGarbageForTesting();
    fixture.WaitUntilIdle();

    Assert.Equal(0, state.DisposeCount);
    state.Dispose();
    Assert.Equal(1, state.DisposeCount);
  }

  [Fact]
  public void ThrowingDisposerIsContainedAndOtherContextsAreReleased()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var throwingDisposeCount = 0;
    var otherDisposeCount = 0;

    fixture.Runtime.Execute(runtime =>
    {
      using var throwingFunction = runtime.CreateHostFunction(
          "throwingDisposer",
          0,
          ReturnTrue,
          new object(),
          _ =>
          {
            Interlocked.Increment(ref throwingDisposeCount);
            throw new InvalidOperationException("disposer boom");
          }
      );
      using var otherFunction = runtime.CreateHostFunction(
          "otherDisposer",
          0,
          ReturnTrue,
          new object(),
          _ => Interlocked.Increment(ref otherDisposeCount)
      );
      return true;
    });

    fixture.CollectGarbageForTesting();
    fixture.WaitUntilIdle();

    Assert.Equal(1, throwingDisposeCount);
    Assert.Equal(1, otherDisposeCount);
  }

  [Fact]
  public void OwnedWeakObjectIsDisposedAfterJavaScriptCollection()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();
    JavaScriptWeakObject? weak = null;
    try
    {
      fixture.Runtime.Execute(runtime =>
      {
        using var strong = runtime.CreateObject();
        var ownedWeak = strong.CreateWeak();
        weak = ownedWeak;
        using var function = runtime.CreateHostFunction(
            "ownedWeakObject",
            0,
            ReturnTrue,
            ownedWeak,
            state => ((JavaScriptWeakObject)state).Dispose()
        );
        return true;
      });

      fixture.CollectGarbageForTesting();
      fixture.WaitUntilIdle();

      var disposedWeak = Assert.IsType<JavaScriptWeakObject>(weak);
      Assert.Throws<ObjectDisposedException>(() => _ = disposedWeak.Handle);
      Assert.Equal(1u, fixture.Counters.LongLivedWeakObjectsReleased);
      Assert.Equal(0u, fixture.Counters.LongLivedWeakObjectsAbandoned);
      Assert.Equal(0u, fixture.Counters.LongLivedObjectsRemaining);
    }
    finally
    {
      weak?.Dispose();
    }
  }

  [Fact]
  public void OwnedWeakObjectIsDisposedAtRuntimeTeardown()
  {
    var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();
    JavaScriptWeakObject? weak = null;
    try
    {
      fixture.Runtime.Execute(runtime =>
      {
        using var global = runtime.Global();
        using var strong = runtime.CreateObject();
        var ownedWeak = strong.CreateWeak();
        weak = ownedWeak;
        using var function = runtime.CreateHostFunction(
            "ownedWeakObject",
            0,
            ReturnTrue,
            ownedWeak,
            state => ((JavaScriptWeakObject)state).Dispose()
        );
        using var functionValue = function.AsValue();
        global.SetProperty("ownedWeakObject", functionValue);
        return true;
      });

      fixture.PrepareRuntimeForInvalidation();
      fixture.WaitUntilIdle();
      Assert.Equal(1u, fixture.Counters.LongLivedWeakObjectsReleased);
      Assert.Equal(0u, fixture.Counters.LongLivedWeakObjectsAbandoned);
      Assert.Equal(0u, fixture.Counters.LongLivedObjectsRemaining);
    }
    finally
    {
      fixture.Dispose();
    }

    Assert.NotNull(weak);
    Assert.Throws<ObjectDisposedException>(() => _ = weak.Handle);
    weak.Dispose();
  }

  private static JavaScriptValue ReturnTrue(
      JavaScriptRuntime runtime,
      JavaScriptValueRef thisValue,
      JavaScriptArguments arguments,
      object state
  ) => runtime.CreateBool(true);

  private sealed class DisposableCallbackState : IDisposable
  {
    public int DisposeCount { get; private set; }

    public void Dispose()
    {
      DisposeCount++;
    }
  }
}
