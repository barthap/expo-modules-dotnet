using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

[Collection("JavaScriptArrayBuffer")]
public sealed class JavaScriptWeakObjectTests
{
  [Fact]
  public void CreateWeakAndLockRequireTheOriginatingAccessFrame()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var strong = fixture.Runtime.Execute(runtime => runtime.CreateObject());
    using var weak = fixture.Runtime.Execute(_ => strong.CreateWeak());

    Assert.Throws<InvalidOperationException>(() => { strong.CreateWeak(); });
    Assert.Throws<InvalidOperationException>(() => { weak.Lock(); });
  }

  [Fact]
  public void CreateWeakOnDisposedObjectThrowsBeforeTheAbi()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var strong = fixture.Runtime.Execute(runtime => runtime.CreateObject());
    strong.Dispose();

    Assert.Throws<ObjectDisposedException>(() =>
      fixture.Runtime.Execute(_ => { return strong.CreateWeak(); })
    );
  }

  [Fact]
  public void LockRejectsAWrongRuntime()
  {
    using var firstFixture = HermesRuntimeFixture.Create();
    using var secondFixture = HermesRuntimeFixture.Create();
    using var strong = firstFixture.Runtime.Execute(runtime => runtime.CreateObject());
    using var weak = firstFixture.Runtime.Execute(_ => strong.CreateWeak());

    var result = secondFixture.Runtime.Execute(_ => secondFixture.LockWeakObjectRaw(weak.Handle));

    Assert.False(result.IsOk);
    Assert.Contains("different JavaScript runtime", result.Error.GetMessageAndRelease());
  }

  [Fact]
  public void CreateWeakAndLockRejectAnInvalidatedRuntime()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var strong = fixture.Runtime.Execute(runtime => runtime.CreateObject());
    var weak = fixture.Runtime.Execute(_ => strong.CreateWeak());
    var weakHandle = weak.Handle;
    strong.Dispose();

    fixture.InvalidateRuntime();
    var createResult = fixture.CreateWeakObjectRaw(default);
    var lockResult = fixture.LockWeakObjectRaw(weakHandle);

    Assert.False(createResult.IsOk);
    Assert.False(lockResult.IsOk);
    Assert.NotEmpty(createResult.Error.GetMessageAndRelease());
    Assert.NotEmpty(lockResult.Error.GetMessageAndRelease());
    weak.Dispose();
    fixture.ReleaseBridgeRuntimeHandle();
  }

  [Fact]
  public void LockReturnsIndependentOwnedObjectsForALiveReferent()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var strong = fixture.Runtime.Execute(runtime => runtime.CreateObject());
    using var weak = fixture.Runtime.Execute(_ => strong.CreateWeak());

    fixture.Runtime.Execute(runtime =>
    {
      using var first = Assert.IsType<JavaScriptObject>(weak.Lock());
      using var second = Assert.IsType<JavaScriptObject>(weak.Lock());
      first.Dispose();
      using var secondValue = second.AsValue();
      using var strongValue = strong.AsValue();
      Assert.True(runtime.StrictEquals(strongValue, secondValue));
      return true;
    });
  }

  [Fact]
  public void LockReturnsNullAfterDeterministicCollection()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var weak = fixture.Runtime.Execute(runtime =>
    {
      using var strong = runtime.CreateObject();
      return strong.CreateWeak();
    });
    try
    {
      fixture.CollectGarbageForTesting();
      fixture.WaitUntilIdle();
      fixture.Runtime.Execute(_ =>
      {
        Assert.Null(weak.Lock());
        return true;
      });
    }
    finally
    {
      weak.Dispose();
    }
  }

  [Fact]
  public void DisposeIsIdempotentAndLockAfterDisposeThrows()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var strong = fixture.Runtime.Execute(runtime => runtime.CreateObject());
    var weak = fixture.Runtime.Execute(_ => strong.CreateWeak());

    weak.Dispose();
    weak.Dispose();
    Assert.Throws<ObjectDisposedException>(() =>
      fixture.Runtime.Execute(_ => { return weak.Lock(); })
    );
  }

  [Fact]
  public async Task LockAndDisposeAreSerialized()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var strong = fixture.Runtime.Execute(runtime => runtime.CreateObject());
    var weak = fixture.Runtime.Execute(_ => strong.CreateWeak());
    var barrier = new Barrier(3);
    var cancellationToken = TestContext.Current.CancellationToken;

    var lockTask = Task.Run(() =>
    {
      barrier.SignalAndWait(cancellationToken);
      try
      {
        fixture.Runtime.Execute(_ =>
        {
          weak.Lock()?.Dispose();
          return true;
        });
      }
      catch (ObjectDisposedException)
      {
      }
    }, cancellationToken);
    var disposeTask = Task.Run(() =>
    {
      barrier.SignalAndWait(cancellationToken);
      weak.Dispose();
    }, cancellationToken);
    barrier.SignalAndWait(cancellationToken);
    await Task.WhenAll(lockTask, disposeTask).WaitAsync(cancellationToken);
  }

  [Fact]
  public async Task DisposeWinsAgainstAQueuedLock()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var strong = fixture.Runtime.Execute(runtime => runtime.CreateObject());
    var weak = fixture.Runtime.Execute(_ => strong.CreateWeak());

    fixture.PauseRuntimeExecutor();
    var lockTask = Task.Run(
      () => fixture.Runtime.Execute(_ => weak.Lock()),
      TestContext.Current.CancellationToken
    );
    try
    {
      fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate);
      weak.Dispose();
    }
    finally
    {
      fixture.ResumeRuntimeExecutor();
      try
      {
        var locked = await lockTask;
        locked?.Dispose();
      }
      catch (ObjectDisposedException)
      {
      }
    }

    await Assert.ThrowsAsync<ObjectDisposedException>(async () => await lockTask);
  }

  [Fact]
  public void BridgeHandleReleaseAbandonsQueuedWeakReleaseAndErasesTheEntry()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();
    var strong = fixture.Runtime.Execute(runtime => runtime.CreateObject());
    var weak = fixture.Runtime.Execute(_ => strong.CreateWeak());

    strong.Dispose();
    fixture.PauseRuntimeExecutor();
    try
    {
      weak.Dispose();
      fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Normal);
      fixture.ReleaseBridgeRuntimeHandle();
    }
    finally
    {
      fixture.ResumeRuntimeExecutor();
    }
    fixture.WaitUntilIdle();

    Assert.Equal(1u, fixture.Counters.LongLivedWeakObjectsAbandoned);
    Assert.Equal(0u, fixture.Counters.LongLivedObjectsRemaining);
  }

  [Fact]
  public async Task EarlyPreparationReleasesAndErasesTheEntry()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();
    using var strong = fixture.Runtime.Execute(runtime => runtime.CreateObject());
    var weak = fixture.Runtime.Execute(_ => strong.CreateWeak());

    fixture.PauseRuntimeExecutor();
    var preparation = Task.Run(fixture.PrepareRuntimeForInvalidation,
        TestContext.Current.CancellationToken);
    try
    {
      fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate);
      weak.Dispose();
    }
    finally
    {
      fixture.ResumeRuntimeExecutor();
      try
      {
        await preparation;
      }
      catch
      {
      }
    }
    await preparation;
    fixture.WaitUntilIdle();

    Assert.Equal(1u, fixture.Counters.LongLivedWeakObjectsReleased);
    Assert.Equal(0u, fixture.Counters.LongLivedObjectsRemaining);
  }
}
