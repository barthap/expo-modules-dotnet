using Expo.JSI;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests;

public sealed class SharedObjectRegistryTests
{
  [Fact]
  public void ManagedToJavaScriptReturnsStrictlyEqualLiveObject()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      var instance = new TestSharedObject();
      using var first = context.SharedObjects.GetOrCreateJavaScriptObject(instance);
      using var second = context.SharedObjects.GetOrCreateJavaScriptObject(instance);
      using var firstValue = first.AsValue();
      using var secondValue = second.AsValue();

      Assert.True(runtime.StrictEquals(firstValue, secondValue));
      return true;
    });
  }

  [Fact]
  public void JavaScriptObjectRoundTripsToTheSameManagedInstance()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      var instance = new TestSharedObject();
      using var value = context.SharedObjects.GetOrCreateJavaScriptObject(instance);

      Assert.Same(instance, context.SharedObjects.ResolveManaged(value));
      return true;
    });
  }

  [Fact]
  public void ExplicitReleaseAndLaterNativeStateCallbackRunOnce()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      var registry = context.SharedObjects;
      var instance = new TestSharedObject();
      using var value = registry.GetOrCreateJavaScriptObject(instance);
      using var valueHandle = value.AsValue();
      using var global = runtime.Global();
      global.SetProperty("__shared", valueHandle);

      fixture.Evaluate("globalThis.__shared.release(); globalThis.__shared.release()", "shared-release.js")
          .Dispose();

      Assert.Equal(1, instance.ReleaseCount);
      Assert.Equal(0, registry.Count);
      Assert.Throws<InvalidOperationException>(() => registry.ResolveManaged(value));
      Assert.Throws<InvalidOperationException>(() => registry.GetOrCreateJavaScriptObject(instance));

      value.ClearNativeState<SharedObjectNativeState>();
      Assert.Equal(1, instance.ReleaseCount);
      Assert.Equal(0, registry.Count);
      return true;
    });
  }

  [Fact]
  public void ReentrantNativeStateReleaseDefersUntilPairCreationLeavesRegistryGate()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      var invokeReentrantRelease = false;
      TestSharedObject? firstInstance = null;
      SharedObjectNativeState? firstState = null;
      using var registry = new SharedObjectRegistry(
          runtime,
          () =>
          {
            if (!invokeReentrantRelease)
            {
              return;
            }

            firstState!.Dispose();
            Assert.Equal(0, firstInstance!.ReleaseCount);
          }
      );

      firstInstance = new TestSharedObject();
      var first = registry.GetOrCreateJavaScriptObject(firstInstance);
      firstState = first.GetNativeState<SharedObjectNativeState>();
      first.Dispose();

      invokeReentrantRelease = true;
      var secondInstance = new TestSharedObject();
      using var second = registry.GetOrCreateJavaScriptObject(secondInstance);

      Assert.Equal(1, firstInstance.ReleaseCount);
      Assert.Equal(1, registry.Count);
      Assert.Throws<InvalidOperationException>(() => registry.GetOrCreateJavaScriptObject(firstInstance));
      Assert.Same(secondInstance, registry.ResolveManaged(second));

      registry.Dispose();
      Assert.Equal(1, secondInstance.ReleaseCount);
      Assert.Equal(0, registry.Count);
      return true;
    });
  }

  [Fact]
  public void DeterministicCollectionReleasesThePairOnce()
  {
    using var fixture = HermesRuntimeFixture.Create();
    TestSharedObject? instance = null;
    SharedObjectRegistry? registry = null;

    fixture.Runtime.Execute(runtime =>
    {
      registry = new SharedObjectRegistry(runtime);
      instance = new TestSharedObject();
      using var value = registry.GetOrCreateJavaScriptObject(instance);
      return true;
    });

    fixture.CollectGarbageForTesting();
    fixture.WaitUntilIdle();

    Assert.Equal(1, instance!.ReleaseCount);
    Assert.Equal(0, registry!.Count);
    registry.Dispose();
  }

  [Fact]
  public void StaleAndForeignObjectsFailWithoutAllocatingAPair()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      var registry = context.SharedObjects;
      var instance = new TestSharedObject();
      using var stale = registry.GetOrCreateJavaScriptObject(instance);
      registry.ReleaseFromJavaScript(stale);
      using var foreign = runtime.CreateObject();

      Assert.Throws<InvalidOperationException>(() => registry.ResolveManaged(stale));
      Assert.Throws<InvalidOperationException>(() => registry.ResolveManaged(foreign));
      Assert.Equal(0, registry.Count);
      return true;
    });
  }

  [Fact]
  public void ContextTeardownDrainsTheRegistryWhileRuntimeIsActive()
  {
    using var fixture = HermesRuntimeFixture.Create();
    TestSharedObject? instance = null;
    SharedObjectRegistry? registry = null;

    fixture.Runtime.Execute(runtime =>
    {
      var context = new DotnetRuntimeContext(runtime);
      registry = context.SharedObjects;
      instance = new TestSharedObject();
      using var value = registry.GetOrCreateJavaScriptObject(instance);
      context.Dispose();
      return true;
    });

    fixture.WaitUntilIdle();
    Assert.Equal(1, instance!.ReleaseCount);
    Assert.Equal(0, registry!.Count);
    Assert.Equal(0u, fixture.Counters.LongLivedObjectsRemaining);
  }

  [Fact]
  public void ContextDisposeContinuesAfterRegistryFailureAndDrainsLaterOwners()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      var context = new DotnetRuntimeContext(runtime);
      var failing = new TestSharedObject(throwOnRelease: true);
      var succeeding = new TestSharedObject();
      using var first = context.SharedObjects.GetOrCreateJavaScriptObject(failing);
      using var second = context.SharedObjects.GetOrCreateJavaScriptObject(succeeding);
      var tracker = context.RegisterRetainedCallback(new CleanupTracker());

      Assert.Throws<AggregateException>(context.Dispose);
      Assert.Equal(1, failing.ReleaseCount);
      Assert.Equal(1, succeeding.ReleaseCount);
      Assert.Equal(1, tracker.DisposeCount);
      Assert.Throws<ObjectDisposedException>(() => _ = context.SharedObjects);
      return true;
    });
  }

  [Fact]
  public async Task ConcurrentDisposeWaitsForTerminalState()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var enteredCleanup = new ManualResetEventSlim();
    using var allowCleanupToFinish = new ManualResetEventSlim();
    var context = fixture.Runtime.Execute(runtime =>
    {
      var created = new DotnetRuntimeContext(runtime);
      created.RegisterRetainedCallback(new BlockingCleanupTracker(enteredCleanup, allowCleanupToFinish));
      return created;
    });
    Task? second = null;
    var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var secondReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var first = Task.Run(
        () => fixture.Runtime.Execute(_ =>
        {
          context.Dispose();
          return true;
        }),
        TestContext.Current.CancellationToken
    );
    var failures = new List<Exception>();
    try
    {
      enteredCleanup.Wait(TestContext.Current.CancellationToken);
      second = Task.Run(
          () =>
          {
            secondEntered.SetResult();
            context.Dispose();
            secondReturned.SetResult();
          },
          TestContext.Current.CancellationToken
      );
      await secondEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
      Assert.Throws<ObjectDisposedException>(() => _ = context.SharedObjects);
      Assert.False(secondReturned.Task.IsCompleted);
    }
    catch (Exception exception)
    {
      failures.Add(exception);
    }
    finally
    {
      allowCleanupToFinish.Set();
      try { await first; } catch (Exception exception) { failures.Add(exception); }
      if (second is not null)
      {
        try { await second; } catch (Exception exception) { failures.Add(exception); }
      }
    }

    if (failures.Count == 1)
    {
      System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
    }
    if (failures.Count > 1)
    {
      throw new AggregateException(failures);
    }
    Assert.Throws<ObjectDisposedException>(() => _ = context.SharedObjects);
  }

  [Fact]
  public void PairConstructionFailureReleasesTemporaryWrappersAndUsesWeakCallbackState()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      var before = fixture.Counters;
      using var registry = new SharedObjectRegistry(
          runtime,
          () => throw new InvalidOperationException("install failure")
      );
      var instance = new TestSharedObject();

      var failure = Assert.Throws<AggregateException>(
          () => _ = registry.GetOrCreateJavaScriptObject(instance)
      );
      var installFailure = Assert.IsType<InvalidOperationException>(Assert.Single(failure.InnerExceptions));
      Assert.Equal("install failure", installFailure.Message);
      Assert.Equal(before.ReleasedValues + 3u, fixture.Counters.ReleasedValues);
      Assert.Equal(0, registry.Count);
      Assert.Equal(0, instance.ReleaseCount);
      Assert.IsType<WeakReference<SharedObjectRegistry>>(
          SharedObjectPrototype.CreateReleaseCallbackState(registry)
      );
      return true;
    });
  }

  [Fact]
  public void RegistryDisposeAfterPreparedInvalidationUsesOnlyTeardownSafeWeakRelease()
  {
    using var fixture = HermesRuntimeFixture.Create();
    SharedObjectRegistry? registry = null;
    TestSharedObject? instance = null;

    fixture.Runtime.Execute(runtime =>
    {
      registry = new SharedObjectRegistry(runtime);
      instance = new TestSharedObject();
      using var value = registry.GetOrCreateJavaScriptObject(instance);
      return true;
    });

    var before = fixture.Counters;
    fixture.PrepareRuntimeForInvalidation();
    fixture.InvalidateRuntimeForTesting();
    registry!.Dispose();

    Assert.Equal(1, instance!.ReleaseCount);
    Assert.Equal(0, registry.Count);
    Assert.Equal(before.LongLivedWeakObjectsReleased + 1u, fixture.Counters.LongLivedWeakObjectsReleased);
    Assert.Equal(0u, fixture.Counters.LongLivedObjectsRemaining);
  }

  private sealed class TestSharedObject(bool throwOnRelease = false) : ISharedObjectLifetime
  {
    public int ReleaseCount { get; private set; }

    public void ReleaseFromSharedObjectRegistry()
    {
      ReleaseCount++;
      if (throwOnRelease)
      {
        throw new InvalidOperationException("test release failure");
      }
    }
  }

  private sealed class CleanupTracker : IDisposable
  {
    public int DisposeCount { get; private set; }

    public void Dispose() => DisposeCount++;
  }

  private sealed class BlockingCleanupTracker(
      ManualResetEventSlim enteredCleanup,
      ManualResetEventSlim allowCleanupToFinish) : IDisposable
  {
    public void Dispose()
    {
      enteredCleanup.Set();
      allowCleanupToFinish.Wait();
    }
  }
}
