using Expo.JSI;
using Expo.ModulesCore.Codecs;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Generated;

public sealed class ArrayBufferCodecTests
{
  [Fact]
  public void ByteArrayCodecCopiesInputAndOutput()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var source = new byte[] { 1, 2, 3 };
    using var buffer = ArrayBuffer.CopyFrom(source);
    source[0] = 99;

    Assert.Equal(new byte[] { 1, 2, 3 }, buffer.ToArray());

    using var value = fixture.Runtime.Execute(runtime => ByteArrayCodec.Encode(source, runtime));
    source[1] = 88;
    var decoded = fixture.Runtime.Execute(runtime => ByteArrayCodec.Decode(value, runtime));
    Assert.Equal(new byte[] { 99, 2, 3 }, decoded);
  }

  [Fact]
  public void MemoryByteCodecsCopyAtBothBoundariesAndPreserveSlices()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.Runtime.Execute(runtime =>
    {
      using var source = fixture.Evaluate("new Uint8Array([1, 2, 3]).buffer", "memory-codec-source.js");
      var mutable = MemoryByteCodec.Decode(source, runtime);
      var readOnly = ReadOnlyMemoryByteCodec.Decode(source, runtime);
      using var global = runtime.Global();
      global.SetProperty("memoryCodecSource", source);
      using var mutation = fixture.Evaluate("new Uint8Array(memoryCodecSource)[0] = 99", "memory-codec-mutation.js");

      Assert.Equal(new byte[] { 1, 2, 3 }, mutable.ToArray());
      Assert.Equal(new byte[] { 1, 2, 3 }, readOnly.ToArray());

      var mutableSource = new byte[] { 4, 5, 6, 7 };
      var readOnlySource = new byte[] { 8, 9, 10, 11 };
      using var encodedMutable = MemoryByteCodec.Encode(mutableSource.AsMemory(1, 2), runtime);
      using var encodedReadOnly = ReadOnlyMemoryByteCodec.Encode(readOnlySource.AsMemory(1, 2), runtime);
      using var encodedDefault = MemoryByteCodec.Encode(default, runtime);
      using var encodedEmpty = ReadOnlyMemoryByteCodec.Encode(ReadOnlyMemory<byte>.Empty, runtime);
      mutableSource[1] = 99;
      readOnlySource[1] = 99;

      Assert.Equal(new byte[] { 5, 6 }, ByteArrayCodec.Decode(encodedMutable, runtime));
      Assert.Equal(new byte[] { 9, 10 }, ByteArrayCodec.Decode(encodedReadOnly, runtime));
      Assert.Empty(ByteArrayCodec.Decode(encodedDefault, runtime));
      Assert.Empty(ByteArrayCodec.Decode(encodedEmpty, runtime));
      return true;
    });
  }

  [Fact]
  public async Task NativeBackedAsyncAccessRunsInlineAndCopies()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var buffer = ArrayBuffer.CopyFrom(new byte[] { 1, 2 });
    var callerThread = Environment.CurrentManagedThreadId;

    var callbackThread = await buffer.WithBytesAsync(bytes =>
    {
      bytes[0] = 7;
      return Environment.CurrentManagedThreadId;
    }, TestContext.Current.CancellationToken);

    Assert.Equal(callerThread, callbackThread);
    Assert.Equal(
        new byte[] { 7, 2 },
        await buffer.ToArrayAsync(TestContext.Current.CancellationToken)
    );
    using var copy = buffer.Copy();
    buffer.WithBytes(bytes => bytes[0] = 42);
    Assert.Equal(new byte[] { 7, 2 }, copy.ToArray());
  }

  [Fact]
  public void JavaScriptBackedCodecPreservesSameRuntimeIdentity()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.Runtime.Execute(runtime =>
    {
      using var value = fixture.Evaluate("new ArrayBuffer(2)", "codec-identity.js");
      using var decoded = ArrayBufferCodec.Decode(value.Ref, runtime);
      using var encoded = ArrayBufferCodec.Encode(decoded, runtime);

      Assert.True(runtime.StrictEquals(value, encoded));
      return true;
    });
  }

  [Fact]
  public void JavaScriptBackedCodecRejectsCrossRuntimeEncoding()
  {
    using var first = HermesRuntimeFixture.Create();
    using var second = HermesRuntimeFixture.Create();
    using var decoded = first.Runtime.Execute(runtime =>
    {
      using var value = first.Evaluate("new ArrayBuffer(2)", "codec-cross-runtime.js");
      return ArrayBufferCodec.Decode(value.Ref, runtime);
    });

    Assert.Throws<InvalidOperationException>(() =>
        second.Runtime.Execute(runtime => ArrayBufferCodec.Encode(decoded, runtime))
    );
  }

  [Fact]
  public void NativeBackedCodecRequiresActiveRuntimeAccess()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var buffer = ArrayBuffer.CopyFrom(new byte[] { 3 });

    var error = Assert.Throws<InvalidOperationException>(() =>
        EncodeOutsideRuntimeAccess(buffer, fixture.Runtime)
    );

    Assert.Equal("Scoped JavaScript refs require active runtime access.", error.Message);
  }

  [Fact]
  public void ConcurrentDisposeOfNativeBackedBuffersLeavesDisposedBuffer()
  {
    using var fixture = HermesRuntimeFixture.Create();
    const int count = 64;

    for (var index = 0; index < count; index++)
    {
      var buffer = ArrayBuffer.CopyFrom(new byte[] { 3 });
      Parallel.Invoke(buffer.Dispose, buffer.Dispose);
      Assert.Throws<ObjectDisposedException>(() => _ = buffer.ByteLength);
    }
  }

  [Fact]
  public void ConcurrentDisposeOfJavaScriptBackedBufferReleasesOnce()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var buffer = fixture.Runtime.Execute(runtime =>
    {
      using var value = fixture.Evaluate("new ArrayBuffer(1)", "codec-concurrent-dispose.js");
      return ArrayBufferCodec.Decode(value.Ref, runtime);
    });
    fixture.ResetCounters();

    Parallel.Invoke(buffer.Dispose, buffer.Dispose);
    fixture.WaitUntilIdle();

    Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal(0u, fixture.Counters.LongLivedArrayBuffersAbandoned);
  }

  [Fact]
  public void NativeBackedCodecAliasesBytesAcrossRuntimes()
  {
    using var first = HermesRuntimeFixture.Create();
    using var second = HermesRuntimeFixture.Create();
    using var buffer = ArrayBuffer.CopyFrom(new byte[] { 3 });

    using var firstValue = first.Runtime.Execute(runtime => ArrayBufferCodec.Encode(buffer, runtime));
    using var secondValue = second.Runtime.Execute(runtime => ArrayBufferCodec.Encode(buffer, runtime));
    buffer.WithBytes(bytes => bytes[0] = 9);

    var firstObserved = first.Runtime.Execute(runtime =>
    {
      using var global = runtime.Global();
      global.SetProperty("firstBuffer", firstValue);
      using var observed = first.Evaluate("new Uint8Array(firstBuffer)[0]", "codec-native-first.js");
      return observed.AsDouble();
    });
    Assert.Equal(9, firstObserved);

    var secondObserved = second.Runtime.Execute(runtime =>
    {
      using var global = runtime.Global();
      global.SetProperty("secondBuffer", secondValue);
      using var observed = second.Evaluate("new Uint8Array(secondBuffer)[0]", "codec-native-second.js");
      return observed.AsDouble();
    });
    Assert.Equal(9, secondObserved);
  }

  [Fact]
  public void DisposedBufferValidationPrecedesAsyncCancellation()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var buffer = ArrayBuffer.Allocate(1);
    buffer.Dispose();
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();

    Assert.Throws<ObjectDisposedException>(() => InvokeDisposedAsync(buffer, cancellation.Token));
  }

  private static void InvokeDisposedAsync(ArrayBuffer buffer, CancellationToken cancellationToken)
  {
    _ = buffer.WithBytesAsync(_ => { }, cancellationToken);
  }

  private static void EncodeOutsideRuntimeAccess(ArrayBuffer buffer, JavaScriptRuntime runtime)
  {
    using var value = ArrayBufferCodec.Encode(buffer, runtime);
  }
}
