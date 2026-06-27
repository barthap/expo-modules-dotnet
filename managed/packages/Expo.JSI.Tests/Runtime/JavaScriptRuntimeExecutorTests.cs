using System.Threading;
using System.Threading.Tasks;
using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

public sealed class JavaScriptRuntimeExecutorTests
{
  [Fact]
  public async Task ExecuteAsyncRunsOnlyAfterDrain()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var ran = false;

    var task = fixture.Runtime.ExecuteAsync(js =>
    {
      ran = true;
      using var value = js.CreateNumber(42);
      return value.AsDouble();
    });

    Assert.False(ran);
    Assert.False(task.IsCompleted);

    fixture.DrainTasks();

    Assert.True(ran);
    Assert.Equal(42, await task);
  }

  [Fact]
  public async Task ExecuteAsyncPropagatesManagedExceptions()
  {
    using var fixture = HermesRuntimeFixture.Create();

    var task = fixture.Runtime.ExecuteAsync<double>(_ =>
    {
      throw new InvalidOperationException("runtime body failed");
    });

    fixture.DrainTasks();

    var error = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
    Assert.Equal("runtime body failed", error.Message);
  }

  [Fact]
  public async Task ScheduleAsyncReturnsFaultedTaskWhenBodyThrows()
  {
    using var fixture = HermesRuntimeFixture.Create();

    var task = fixture.Runtime.ScheduleAsync(_ =>
    {
      throw new InvalidOperationException("scheduled body failed");
    });

    fixture.DrainTasks();

    var error = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
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
    using var cts = new CancellationTokenSource();
    var ran = false;

    var task = fixture.Runtime.ScheduleAsync(
        _ =>
        {
          ran = true;
        },
        cancellationToken: cts.Token
    );

    cts.Cancel();
    fixture.DrainTasks();

    Assert.False(ran);
    Assert.True(task.IsCanceled);
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    Assert.Equal(1u, fixture.Counters.ReleasedTaskContexts);
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

    fixture.DrainTasks();

    Assert.True(bodyStarted);
    Assert.Equal("finished", await task);
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

    var task = fixture.Runtime.ScheduleAsync(js =>
    {
      using var value = js.CreateBool(true);
      Assert.True(value.AsBool());
    });

    fixture.DrainTasks();

    await task;
    Assert.Equal(1u, fixture.Counters.ReleasedTaskContexts);
  }
}
