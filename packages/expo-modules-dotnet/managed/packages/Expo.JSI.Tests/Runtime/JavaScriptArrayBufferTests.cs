using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

[Collection("JavaScriptArrayBuffer")]
public sealed class JavaScriptArrayBufferTests
{
  [Fact]
  public void NativeMutableBufferIsZeroFilledAndCanBeReadOutsideRuntime()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var buffer = JavaScriptMutableBuffer.Allocate(4);

    Assert.Equal(4, buffer.ByteLength);
    buffer.WithBytes(bytes => bytes[1] = 42);
    buffer.WithReadOnlyBytes(bytes => Assert.Equal(42, bytes[1]));
  }

  [Fact]
  public void NativeMutableBufferOutlivesItsOriginatingRuntime()
  {
    JavaScriptMutableBuffer buffer;
    using (var fixture = HermesRuntimeFixture.Create())
    {
      buffer = JavaScriptMutableBuffer.Allocate(2);
      buffer.WithBytes(bytes => bytes[0] = 7);
      fixture.PoisonMutableBufferDispatchForTesting();
      using var allocatedWhilePoisoned = JavaScriptMutableBuffer.Allocate(1);
      using var copiedWhilePoisoned = JavaScriptMutableBuffer.CopyFrom([3]);
      allocatedWhilePoisoned.WithBytes(bytes => bytes[0] = 5);
      copiedWhilePoisoned.WithReadOnlyBytes(bytes => Assert.Equal(3, bytes[0]));
      using var retained = buffer.Retain();
      retained.WithBytes(bytes => bytes[1] = 9);
      buffer.WithReadOnlyBytes(bytes => Assert.Equal([7, 9], bytes.ToArray()));
    }

    try
    {
      using var allocatedAfterOrigin = JavaScriptMutableBuffer.Allocate(1);
      allocatedAfterOrigin.WithBytes(bytes => bytes[0] = 5);
      buffer.WithReadOnlyBytes(bytes => Assert.Equal([7, 9], bytes.ToArray()));
      allocatedAfterOrigin.WithReadOnlyBytes(bytes => Assert.Equal(5, bytes[0]));
    }
    finally
    {
      buffer.Dispose();
    }
  }

  [Fact]
  public void NativeMutableBufferCreatesDistinctAliasingArrayBuffers()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var storage = JavaScriptMutableBuffer.Allocate(4);
    storage.WithBytes(bytes => bytes[0] = 7);

    fixture.Runtime.Execute(runtime =>
    {
      using var left = storage.AsValue(runtime);
      using var right = storage.AsValue(runtime);
      Assert.False(runtime.StrictEquals(left, right));
      using var global = runtime.Global();
      global.SetProperty("leftBuffer", left);
      global.SetProperty("rightBuffer", right);
      storage.WithBytes(bytes => bytes[0] = 31);

      using var observedLeft = fixture.Evaluate("new Uint8Array(leftBuffer)[0]", "left.js");
      using var observedRight = fixture.Evaluate("new Uint8Array(rightBuffer)[0]", "right.js");
      Assert.Equal(31, observedLeft.AsDouble());
      Assert.Equal(31, observedRight.AsDouble());
      return true;
    });
  }

  [Fact]
  public void ArrayBufferAsValueRequiresActiveRuntimeAccess()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var javaScriptBuffer = fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Evaluate("new ArrayBuffer(2)", "as-value-scope.js");
      return value.Ref.AsArrayBuffer().Retain();
    });
    using var mutableBuffer = JavaScriptMutableBuffer.Allocate(2);

    var javaScriptError = Assert.Throws<InvalidOperationException>(() =>
        EncodeOutsideRuntimeAccess(javaScriptBuffer, fixture.Runtime)
    );
    var mutableError = Assert.Throws<InvalidOperationException>(() =>
        EncodeOutsideRuntimeAccess(mutableBuffer, fixture.Runtime)
    );

    const string expected = "Scoped JavaScript refs require active runtime access.";
    Assert.Equal(expected, javaScriptError.Message);
    Assert.Equal(expected, mutableError.Message);
  }

  [Fact]
  public void ArrayBufferRefRejectsTypedArrayViews()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Evaluate("new Uint8Array(4)", "typed-array.js");
      Assert.False(value.Ref.IsArrayBuffer);
      Assert.Throws<InvalidOperationException>(() => value.Ref.AsArrayBuffer());
      return true;
    });
  }

  [Fact]
  public void SnapshotValidationRejectsDetachmentResizeAndManagedOverflow()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.ValidateArrayBufferSnapshot(detached: false, currentLength: 4, capturedLength: 4);
    Assert.Throws<InvalidOperationException>(() =>
        fixture.ValidateArrayBufferSnapshot(detached: true, currentLength: 4, capturedLength: 4)
    );
    Assert.Throws<InvalidOperationException>(() =>
        fixture.ValidateArrayBufferSnapshot(detached: false, currentLength: 3, capturedLength: 4)
    );
    fixture.ValidateArrayBufferLength((ulong)int.MaxValue);
    Assert.Throws<InvalidOperationException>(() =>
        fixture.ValidateArrayBufferLength((ulong)int.MaxValue + 1)
    );
  }

  [Fact]
  public void ZeroLengthMutableBufferHasAnEmptySpan()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var buffer = JavaScriptMutableBuffer.Allocate(0);
    buffer.WithReadOnlyBytes(bytes => Assert.Empty(bytes.ToArray()));
  }

  [Fact]
  public void DisposedMutableBufferFailsBeforeNativeAccess()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var buffer = JavaScriptMutableBuffer.Allocate(1);
    buffer.Dispose();

    Assert.Throws<ObjectDisposedException>(() => buffer.ByteLength);
    Assert.Throws<ObjectDisposedException>(() => buffer.WithBytes(_ => { }));
  }

  [Fact]
  public void ConcurrentDisposeReleasesEachJavaScriptArrayBufferOnce()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();
    const int count = 64;

    for (var index = 0; index < count; index++)
    {
      var buffer = fixture.Runtime.Execute(_ =>
      {
        using var value = fixture.Evaluate("new ArrayBuffer(1)", "concurrent-dispose.js");
        return value.Ref.AsArrayBuffer().Retain();
      });

      Parallel.Invoke(buffer.Dispose, buffer.Dispose);
    }

    fixture.WaitUntilIdle();

    Assert.Equal((uint)count, fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal(0u, fixture.Counters.LongLivedArrayBuffersAbandoned);
  }

  [Fact]
  public void ConcurrentDisposeOfMutableBuffersDoesNotCrash()
  {
    using var fixture = HermesRuntimeFixture.Create();
    const int count = 64;

    for (var index = 0; index < count; index++)
    {
      var buffer = JavaScriptMutableBuffer.Allocate(1);
      Parallel.Invoke(buffer.Dispose, buffer.Dispose);
      Assert.Throws<ObjectDisposedException>(() => _ = buffer.ByteLength);
    }
  }

  [Fact]
  public async Task AsyncCallbackFailureReleasesSchedulingLease()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var buffer = fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Evaluate("new ArrayBuffer(2)", "async-throw.js");
      return value.Ref.AsArrayBuffer().Retain();
    });

    var task = buffer.WithBytesAsync<int>(
        _ => throw new InvalidOperationException("callback failed"),
        TestContext.Current.CancellationToken
    );
    buffer.Dispose();

    await Assert.ThrowsAsync<InvalidOperationException>(async () => await task);
    fixture.WaitUntilIdle();

    Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal(0u, fixture.Counters.LongLivedArrayBuffersAbandoned);
  }

  [Fact]
  public async Task AsyncPreRunCancellationReleasesSchedulingLeaseWithoutCallback()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var buffer = fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Evaluate("new ArrayBuffer(2)", "async-cancel.js");
      return value.Ref.AsArrayBuffer().Retain();
    });
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    var callbackInvoked = false;

    fixture.PauseRuntimeExecutor();
    var task = buffer.WithBytesAsync(_ => callbackInvoked = true, cancellation.Token);
    buffer.Dispose();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
    fixture.ResumeRuntimeExecutor();
    fixture.WaitUntilIdle();

    Assert.False(callbackInvoked);
    Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal(0u, fixture.Counters.LongLivedArrayBuffersAbandoned);
  }

  [Fact]
  public async Task DroppedAsyncAccessReleasesSchedulingLeaseWithoutCallback()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var buffer = fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Evaluate("new ArrayBuffer(2)", "async-drop.js");
      return value.Ref.AsArrayBuffer().Retain();
    });
    var callbackInvoked = false;

    fixture.PauseRuntimeExecutor();
    var task = buffer.WithBytesAsync(
        _ => callbackInvoked = true,
        TestContext.Current.CancellationToken
    );
    buffer.Dispose();
    fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate);
    fixture.DropQueuedRuntimeTask(JavaScriptTaskPriority.Immediate);

    await Assert.ThrowsAnyAsync<ObjectDisposedException>(async () => await task);
    fixture.ResumeRuntimeExecutor();
    fixture.WaitUntilIdle();

    Assert.False(callbackInvoked);
    Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal(0u, fixture.Counters.LongLivedArrayBuffersAbandoned);
  }

  [Fact]
  public async Task EarlyTeardownOfDroppedAsyncAccessReleasesSchedulingLease()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var buffer = fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Evaluate("new ArrayBuffer(2)", "async-early.js");
      return value.Ref.AsArrayBuffer().Retain();
    });
    var callbackInvoked = false;

    fixture.PauseRuntimeExecutor();
    var task = buffer.WithBytesAsync(
        _ => callbackInvoked = true,
        TestContext.Current.CancellationToken
    );
    buffer.Dispose();
    fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate);
    var preparation = Task.Run(
        fixture.PrepareRuntimeForInvalidation,
        TestContext.Current.CancellationToken
    );
    fixture.WaitUntilRuntimeTasksQueued(JavaScriptTaskPriority.Immediate, 2);
    fixture.DropQueuedRuntimeTask(JavaScriptTaskPriority.Immediate);
    fixture.ResumeRuntimeExecutor();

    await Assert.ThrowsAnyAsync<ObjectDisposedException>(async () => await task);
    await preparation;
    fixture.WaitUntilIdle();

    Assert.False(callbackInvoked);
    Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal(0u, fixture.Counters.LongLivedArrayBuffersAbandoned);
  }

  [Fact]
  public async Task LateTeardownOfPendingAsyncAccessAbandonsWithoutCallback()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var buffer = fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Evaluate("new ArrayBuffer(2)", "async-late.js");
      return value.Ref.AsArrayBuffer().Retain();
    });
    var callbackInvoked = false;

    fixture.PauseRuntimeExecutor();
    var task = buffer.WithBytesAsync(
        _ => callbackInvoked = true,
        TestContext.Current.CancellationToken
    );
    buffer.Dispose();
    fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate);
    fixture.InvalidateRuntime();
    fixture.ResumeRuntimeExecutor();
    fixture.WaitUntilIdle();

    await Assert.ThrowsAnyAsync<Exception>(async () => await task);
    Assert.False(callbackInvoked);
    Assert.Equal(0u, fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersAbandoned);
  }

  [Fact]
  public void EarlyRuntimePreparationReleasesRetainedJavaScriptBufferOnRuntime()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var retained = fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Evaluate("new ArrayBuffer(2)", "early-teardown.js");
      return value.Ref.AsArrayBuffer().Retain();
    });

    fixture.PrepareRuntimeForInvalidation();
    fixture.InvalidateRuntime();

    Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal(0u, fixture.Counters.LongLivedArrayBuffersAbandoned);
  }

  [Fact]
  public void LateRuntimeInvalidationAbandonsRetainedJavaScriptBuffer()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var retained = fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Evaluate("new ArrayBuffer(2)", "late-teardown.js");
      return value.Ref.AsArrayBuffer().Retain();
    });

    fixture.InvalidateRuntime();
    retained.Dispose();

    Assert.Equal(0u, fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersAbandoned);
  }

  [Fact]
  public void DroppedReleaseDrainsOnNextRuntimeAccess()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var retained = fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Evaluate("new ArrayBuffer(2)", "dropped-release.js");
      return value.Ref.AsArrayBuffer().Retain();
    });

    fixture.PauseRuntimeExecutor();
    fixture.DropNextRuntimeTask(JavaScriptTaskPriority.Normal);
    retained.Dispose();
    fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Normal);
    fixture.ResumeRuntimeExecutor();
    fixture.WaitUntilIdle();

    Assert.Equal(0u, fixture.Counters.LongLivedArrayBuffersReleased);
    fixture.Runtime.Execute(runtime =>
    {
      using var value = runtime.CreateUndefined();
      return true;
    });

    Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal(0u, fixture.Counters.LongLivedArrayBuffersAbandoned);
  }

  [Fact]
  public void QueuedReleaseAfterBridgeHandleReleaseAbandonsWithoutJSI()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var retained = fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Evaluate("new ArrayBuffer(2)", "queued-late-release.js");
      return value.Ref.AsArrayBuffer().Retain();
    });

    fixture.PauseRuntimeExecutor();
    retained.Dispose();
    fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Normal);
    fixture.ReleaseBridgeRuntimeHandle();
    fixture.ResumeRuntimeExecutor();
    fixture.WaitUntilIdle();

    Assert.Equal(0u, fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersAbandoned);
  }

  [Fact]
  public async Task TeardownSweepWinsWhenLastLeaseIsDisposedDuringClosing()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var retained = fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Evaluate("new ArrayBuffer(2)", "closing-release.js");
      return value.Ref.AsArrayBuffer().Retain();
    });

    fixture.PauseRuntimeExecutor();
    var preparation = Task.Run(
        fixture.PrepareRuntimeForInvalidation,
        TestContext.Current.CancellationToken
    );
    fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate);
    retained.Dispose();
    fixture.ResumeRuntimeExecutor();
    await preparation;
    fixture.WaitUntilIdle();

    Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal(0u, fixture.Counters.LongLivedArrayBuffersAbandoned);
  }

  private static void EncodeOutsideRuntimeAccess(
      JavaScriptArrayBuffer buffer,
      JavaScriptRuntime runtime
  )
  {
    using var value = buffer.AsValue(runtime);
  }

  private static void EncodeOutsideRuntimeAccess(
      JavaScriptMutableBuffer buffer,
      JavaScriptRuntime runtime
  )
  {
    using var value = buffer.AsValue(runtime);
  }
}
