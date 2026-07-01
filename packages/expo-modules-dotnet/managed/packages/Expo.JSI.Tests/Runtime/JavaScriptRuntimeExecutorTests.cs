using System.Threading;
using System.Threading.Tasks;
using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

public sealed class JavaScriptRuntimeExecutorTests
{
  [Fact]
  public async Task ExecuteAsyncRunsOnExecutorThreadWithoutManualDrain()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var callerThread = Environment.CurrentManagedThreadId;

    var executorThread = await fixture.Runtime.ExecuteAsync(
        js =>
        {
          using var value = js.CreateNumber(42);
          Assert.Equal(42, value.AsDouble());
          return Environment.CurrentManagedThreadId;
        },
        cancellationToken: TestContext.Current.CancellationToken
    ).WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

    Assert.NotEqual(callerThread, executorThread);
  }

  [Fact]
  public void ExecuteFromOutsideRuntimeThreadPostsAndWaits()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var callerThread = Environment.CurrentManagedThreadId;

    var executorThread = fixture.Runtime.Execute(js =>
    {
      using var value = js.CreateNumber(7);
      Assert.Equal(7, value.AsDouble());
      return Environment.CurrentManagedThreadId;
    });

    Assert.NotEqual(callerThread, executorThread);
  }

  [Fact]
  public void NestedExecuteFromExecutorThreadRunsInline()
  {
    using var fixture = HermesRuntimeFixture.Create();

    var result = fixture.Runtime.Execute(js =>
    {
      var outerThread = Environment.CurrentManagedThreadId;
      var innerThread = js.Execute(inner =>
      {
        using var value = inner.CreateBool(true);
        Assert.True(value.AsBool());
        return Environment.CurrentManagedThreadId;
      });

      return (outerThread, innerThread);
    });

    Assert.Equal(result.outerThread, result.innerThread);
  }

  [Fact]
  public async Task WaitUntilIdleIncludesWorkQueuedByRunningWork()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var outerRan = false;
    var innerRan = false;

    var outerTask = fixture.Runtime.ScheduleAsync(
        js =>
        {
          outerRan = true;
          _ = js.ScheduleAsync(
              inner =>
              {
                using var value = inner.CreateString("inner");
                Assert.Equal("inner", value.AsString());
                innerRan = true;
              },
              cancellationToken: TestContext.Current.CancellationToken
          );
        },
        cancellationToken: TestContext.Current.CancellationToken
    );

    fixture.WaitUntilIdle();

    await outerTask.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
    Assert.True(outerRan);
    Assert.True(innerRan);
  }

  [Fact]
  public async Task ExecuteAsyncPropagatesManagedExceptions()
  {
    using var fixture = HermesRuntimeFixture.Create();

    var task = fixture.Runtime.ExecuteAsync<double>(
        _ =>
        {
          throw new InvalidOperationException("runtime body failed");
        },
        cancellationToken: TestContext.Current.CancellationToken
    );

    var error = await Assert.ThrowsAsync<InvalidOperationException>(
        async () => await task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken
        )
    );
    Assert.Equal("runtime body failed", error.Message);
  }

  [Fact]
  public async Task ScheduleAsyncReturnsFaultedTaskWhenBodyThrows()
  {
    using var fixture = HermesRuntimeFixture.Create();

    var task = fixture.Runtime.ScheduleAsync(
        _ =>
        {
          throw new InvalidOperationException("scheduled body failed");
        },
        cancellationToken: TestContext.Current.CancellationToken
    );

    var error = await Assert.ThrowsAsync<InvalidOperationException>(
        async () => await task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken
        )
    );
    Assert.Equal("scheduled body failed", error.Message);
  }

  [Fact]
  public async Task CancellationBeforeSchedulingReturnsCanceledTask()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    var task = fixture.Runtime.ExecuteAsync(
        js =>
        {
          using var value = js.CreateNumber(1);
          return value.AsDouble();
        },
        cancellationToken: cts.Token
    );

    Assert.True(task.IsCanceled);
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
  }

  [Fact]
  public async Task CancellationWhileQueuedSkipsBodyAndReleasesContext()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();
    using var blockerStarted = new ManualResetEventSlim(false);
    using var releaseBlocker = new ManualResetEventSlim(false);
    using var cts = new CancellationTokenSource();
    var testCancellation = TestContext.Current.CancellationToken;
    var ran = false;

    var blockerTask = fixture.Runtime.ScheduleAsync(
        _ =>
        {
          blockerStarted.Set();
          if (!releaseBlocker.Wait(TimeSpan.FromSeconds(5), testCancellation))
          {
            throw new TimeoutException("Timed out waiting to release executor blocker.");
          }
        },
        priority: JavaScriptTaskPriority.Immediate,
        cancellationToken: testCancellation
    );

    Assert.True(blockerStarted.Wait(TimeSpan.FromSeconds(1), testCancellation));

    var task = fixture.Runtime.ScheduleAsync(
        _ =>
        {
          ran = true;
        },
        priority: JavaScriptTaskPriority.Idle,
        cancellationToken: cts.Token
    );

    cts.Cancel();
    releaseBlocker.Set();
    fixture.WaitUntilIdle();
    await blockerTask.WaitAsync(TimeSpan.FromSeconds(1), testCancellation);

    Assert.False(ran);
    Assert.True(task.IsCanceled);
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
        async () => await task.WaitAsync(
            TimeSpan.FromSeconds(1),
            testCancellation
        )
    );
    Assert.Equal(2u, fixture.Counters.ReleasedTaskContexts);
  }

  [Fact]
  public async Task CancellationAfterRuntimeWorkStartsDoesNotInterruptBody()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var cts = new CancellationTokenSource();
    var bodyStarted = false;

    var task = fixture.Runtime.ExecuteAsync(
        js =>
        {
          bodyStarted = true;
          cts.Cancel();
          using var value = js.CreateString("finished");
          return value.AsString();
        },
        cancellationToken: cts.Token
    );

    var result = await task.WaitAsync(
        TimeSpan.FromSeconds(1),
        TestContext.Current.CancellationToken
    );

    Assert.True(bodyStarted);
    Assert.Equal("finished", result);
  }

  [Fact]
  public void ExecuteRunsSynchronouslyOnHeadlessRuntime()
  {
    using var fixture = HermesRuntimeFixture.Create();

    var result = fixture.Runtime.Execute(js =>
    {
      using var value = js.CreateNumber(7);
      return value.AsDouble();
    });

    Assert.True(fixture.Runtime.CanExecuteSync);
    Assert.Equal(7, result);
  }

  [Fact]
  public void ExecuteThrowsBeforeBodyWhenSyncUnsupported()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.DisableSyncExecutionForTesting();
    var ran = false;

    var error = Assert.Throws<NotSupportedException>(() =>
      fixture.Runtime.Execute(js =>
      {
        ran = true;
        using var value = js.CreateNumber(1);
        return value.AsDouble();
      }));

    Assert.False(ran);
    Assert.Equal(0u, fixture.Counters.SyncExecuteCalls);
    Assert.Contains("Synchronous JavaScript runtime execution is not supported", error.Message);
  }

  [Fact]
  public async Task ScheduledTaskContextIsReleasedExactlyOnce()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();

    var task = fixture.Runtime.ScheduleAsync(
        js =>
        {
          using var value = js.CreateBool(true);
          Assert.True(value.AsBool());
        },
        cancellationToken: TestContext.Current.CancellationToken
    );

    fixture.WaitUntilIdle();

    await task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
    Assert.Equal(1u, fixture.Counters.ReleasedTaskContexts);
  }

  [Fact]
  public async Task PendingScheduledTaskFaultsWhenRuntimeIsDisposed()
  {
    var fixture = HermesRuntimeFixture.Create();
    using var blockerStarted = new ManualResetEventSlim(false);
    using var releaseBlocker = new ManualResetEventSlim(false);
    var testCancellation = TestContext.Current.CancellationToken;
    var pendingRan = false;

    var blockerTask = fixture.Runtime.ScheduleAsync(
        _ =>
        {
          blockerStarted.Set();
          if (!releaseBlocker.Wait(TimeSpan.FromSeconds(5), testCancellation))
          {
            throw new TimeoutException("Timed out waiting to release executor blocker.");
          }
        },
        priority: JavaScriptTaskPriority.Immediate,
        cancellationToken: testCancellation
    );

    Assert.True(blockerStarted.Wait(TimeSpan.FromSeconds(1), testCancellation));

    var pendingTask = fixture.Runtime.ScheduleAsync(
        _ =>
        {
          pendingRan = true;
        },
        priority: JavaScriptTaskPriority.Idle,
        cancellationToken: testCancellation
    );

    var releaseTask = Task.Run(async () =>
    {
      await Task.Delay(TimeSpan.FromMilliseconds(50), testCancellation);
      releaseBlocker.Set();
    }, testCancellation);

    fixture.Dispose();
    await releaseTask.WaitAsync(TimeSpan.FromSeconds(1), testCancellation);
    await blockerTask.WaitAsync(TimeSpan.FromSeconds(1), testCancellation);

    Assert.False(pendingRan);
    var error = await Assert.ThrowsAsync<ObjectDisposedException>(
        async () => await pendingTask.WaitAsync(
            TimeSpan.FromSeconds(1),
            testCancellation
        )
    );
    Assert.Equal(nameof(JavaScriptRuntime), error.ObjectName);
  }

  [Fact]
  public async Task PendingExecuteSyncFaultsOnceWhenRuntimeIsDisposed()
  {
    var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();
    using var blockerStarted = new ManualResetEventSlim(false);
    using var releaseBlocker = new ManualResetEventSlim(false);
    using var executeStarted = new ManualResetEventSlim(false);
    var testCancellation = TestContext.Current.CancellationToken;
    var syncBodyRan = false;

    var blockerTask = fixture.Runtime.ScheduleAsync(
        _ =>
        {
          blockerStarted.Set();
          if (!releaseBlocker.Wait(TimeSpan.FromSeconds(5), testCancellation))
          {
            throw new TimeoutException("Timed out waiting to release executor blocker.");
          }
        },
        priority: JavaScriptTaskPriority.Immediate,
        cancellationToken: testCancellation
    );

    Assert.True(blockerStarted.Wait(TimeSpan.FromSeconds(1), testCancellation));

    var executeTask = Task.Run(() =>
    {
      executeStarted.Set();
      return fixture.Runtime.Execute(js =>
      {
        syncBodyRan = true;
        using var value = js.CreateNumber(1);
        return value.AsDouble();
      });
    }, testCancellation);

    Assert.True(executeStarted.Wait(TimeSpan.FromSeconds(1), testCancellation));

    var observedNativeSyncCall = false;
    for (var attempt = 0; attempt < 50; attempt++)
    {
      if (fixture.Counters.SyncExecuteCalls >= 1)
      {
        observedNativeSyncCall = true;
        break;
      }
      await Task.Delay(TimeSpan.FromMilliseconds(10), testCancellation);
    }
    Assert.True(observedNativeSyncCall);

    var releaseTask = Task.Run(async () =>
    {
      await Task.Delay(TimeSpan.FromMilliseconds(50), testCancellation);
      releaseBlocker.Set();
    }, testCancellation);

    fixture.Dispose();
    await releaseTask.WaitAsync(TimeSpan.FromSeconds(1), testCancellation);
    await blockerTask.WaitAsync(TimeSpan.FromSeconds(1), testCancellation);

    Assert.False(syncBodyRan);
    await Assert.ThrowsAsync<InvalidOperationException>(
        async () => await executeTask.WaitAsync(TimeSpan.FromSeconds(1), testCancellation)
    );
  }
}
