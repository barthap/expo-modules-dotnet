using Expo.JSI;
using Expo.ModulesCore.Codecs;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Codecs;

public sealed class Uint8ArrayCodecTests
{
  private const string Buffer16 =
      "globalThis.buf = new Uint8Array(16).map((_, i) => i).buffer";

  private const string Sub =
      "(() => { function Sub() {} Sub.prototype = Object.create(Uint8Array.prototype); "
      + "Sub.prototype.constructor = Sub; const v = new Uint8Array(4); "
      + "Object.setPrototypeOf(v, Sub.prototype); return v; })()";

  [Fact]
  public void DecodeSharesBytesWithJavaScript()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var source = fixture.Evaluate("globalThis.v = new Uint8Array(4); v", "u8-share.js");
      using var view = Uint8ArrayCodec.Decode(source.Ref, runtime);

      Assert.Equal(0, view.ByteOffset);
      Assert.Equal(4, view.Length);
      Assert.Equal(4, view.Buffer.ByteLength);

      view.WithBytes(bytes => bytes.Fill(0x42));
      Assert.Equal("66,66,66,66", EvaluateString(fixture, "Array.from(v).join(',')"));

      fixture.Evaluate("v[1] = 7", "u8-share-write.js").Dispose();
      Assert.Equal(new byte[] { 66, 7, 66, 66 }, view.ToArray());
      return 0;
    });
  }

  [Fact]
  public void DecodeKeepsSubViewOffsetAndLength()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      fixture.Evaluate(Buffer16, "u8-sub-buffer.js").Dispose();
      using var source = fixture.Evaluate("globalThis.s = new Uint8Array(buf, 4, 8); s", "u8-sub.js");
      using var view = Uint8ArrayCodec.Decode(source.Ref, runtime);

      Assert.Equal(4, view.ByteOffset);
      Assert.Equal(8, view.Length);
      Assert.Equal(16, view.Buffer.ByteLength);
      Assert.Equal(new byte[] { 4, 5, 6, 7, 8, 9, 10, 11 }, view.ToArray());
      Assert.Equal(8, view.WithReadOnlyBytes(b => b.Length));

      view.WithBytes(b => b[0] = 99);
      Assert.Equal(
          "3:99:12",
          EvaluateString(
              fixture,
              "[new Uint8Array(buf)[3], new Uint8Array(buf)[4], new Uint8Array(buf)[12]].join(':')"));
      return 0;
    });
  }

  [Fact]
  public void EmptyViewsDecodeAndEncode()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using (var emptySource = fixture.Evaluate("new Uint8Array(0)", "u8-empty.js"))
      {
        using var empty = Uint8ArrayCodec.Decode(emptySource.Ref, runtime);
        Assert.Equal(0, empty.ByteOffset);
        Assert.Equal(0, empty.Length);
        Assert.Empty(empty.ToArray());
      }

      fixture.Evaluate(Buffer16, "u8-empty-buffer.js").Dispose();
      using var endSource = fixture.Evaluate("new Uint8Array(buf, 16, 0)", "u8-empty-end.js");
      using var end = Uint8ArrayCodec.Decode(endSource.Ref, runtime);
      // Hermes's byteOffset getter returns 0 when the view length is 0, so offset 16 is not
      // observable from JavaScript; the codec passes the engine's value through unchanged.
      Assert.Equal(0, end.ByteOffset);
      Assert.Equal(0, end.Length);

      using var encoded = Uint8ArrayCodec.Encode(end, runtime);
      using var global = runtime.Global();
      global.SetProperty("r", encoded);
      Assert.Equal(
          "true:0:0",
          EvaluateString(fixture, "[r instanceof Uint8Array, r.byteOffset, r.length].join(':')"));

      using var copied = Uint8Array.CopyFrom(ReadOnlySpan<byte>.Empty);
      Assert.Equal(0, copied.Length);

      using var encodedCopy = Uint8ArrayCodec.EncodeCopy(ReadOnlySpan<byte>.Empty, runtime);
      global.SetProperty("r", encodedCopy);
      Assert.Equal(
          "true:0:0",
          EvaluateString(fixture, "[r instanceof Uint8Array, r.byteOffset, r.length].join(':')"));
      return 0;
    });
  }

  [Fact]
  public void EncodeBuildsAUint8ArrayOverTheSameBuffer()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      fixture.Evaluate(Buffer16, "u8-encode-buffer.js").Dispose();
      using var source = fixture.Evaluate("globalThis.s = new Uint8Array(buf, 4, 8); s", "u8-encode.js");
      using var view = Uint8ArrayCodec.Decode(source.Ref, runtime);
      using var encoded = Uint8ArrayCodec.Encode(view, runtime);
      using var global = runtime.Global();
      global.SetProperty("r", encoded);

      Assert.Equal(
          "true:true:true:4:8:false",
          EvaluateString(
              fixture,
              "[r instanceof Uint8Array, r.constructor === Uint8Array, r.buffer === buf, "
              + "r.byteOffset, r.length, r === s].join(':')"));
      return 0;
    });
  }

  [Fact]
  public void NativeViewsAllocateCopyRetainAndEncode()
  {
    using var fixture = HermesRuntimeFixture.Create();

    using (var allocated = Uint8Array.Allocate(4))
    {
      Assert.Equal(0, allocated.ByteOffset);
      Assert.Equal(4, allocated.Length);
      Assert.Equal(new byte[4], allocated.ToArray());
    }
    Assert.Throws<ArgumentOutOfRangeException>(() => Uint8Array.Allocate(-1));

    var original = Uint8Array.CopyFrom(new byte[] { 1, 2, 3 });
    using var copy = original.Retain();
    original.Dispose();
    Assert.Equal(new byte[] { 1, 2, 3 }, copy.ToArray());
    Assert.Equal(0, copy.ByteOffset);
    Assert.Equal(3, copy.Length);

    fixture.Runtime.Execute(runtime =>
    {
      using var encoded = Uint8ArrayCodec.Encode(copy, runtime);
      using var global = runtime.Global();
      global.SetProperty("r", encoded);
      Assert.Equal(
          "true:0:3:1,2,3",
          EvaluateString(
              fixture,
              "[r instanceof Uint8Array, r.byteOffset, r.length, Array.from(r).join(',')].join(':')"));

      using var copied = Uint8ArrayCodec.EncodeCopy(new byte[] { 5, 6 }, runtime);
      global.SetProperty("c", copied);
      Assert.Equal(
          "true:0:2:2:5,6",
          EvaluateString(
              fixture,
              "[c instanceof Uint8Array, c.byteOffset, c.length, c.buffer.byteLength, "
              + "Array.from(c).join(',')].join(':')"));
      return 0;
    });
  }

  [Fact]
  public async Task ViewSlicesNativeStorageSyncAndAsync()
  {
    var ct = TestContext.Current.CancellationToken;
    using var buffer = ArrayBuffer.CopyFrom(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 });
    using var view = new Uint8Array(buffer, 2, 3);

    Assert.Equal(new byte[] { 2, 3, 4 }, view.ToArray());
    view.WithBytes(b => b[0] = 9);
    Assert.Equal(9, buffer.ToArray()[2]);
    Assert.Equal(new byte[] { 9, 3, 4 }, await view.ToArrayAsync(ct));
    Assert.Equal(3, await view.WithReadOnlyBytesAsync(b => b.Length, ct));
    await view.WithBytesAsync(b => b[2] = 8, ct);
    Assert.Equal(8, buffer.ToArray()[4]);
  }

  [Theory]
  [InlineData(0, 4, true)]
  [InlineData(4, 0, true)]
  [InlineData(-1, 0, false)]
  [InlineData(0, -1, false)]
  [InlineData(2, 3, false)]
  [InlineData(int.MaxValue, 1, false)]
  public void ViewConstructorChecksBounds(int byteOffset, int length, bool valid)
  {
    using var buffer = ArrayBuffer.CopyFrom(new byte[4]);

    if (valid)
    {
      using var view = new Uint8Array(buffer, byteOffset, length);
      Assert.Equal(byteOffset, view.ByteOffset);
      Assert.Equal(length, view.Length);
    }
    else
    {
      Assert.Throws<ArgumentOutOfRangeException>(() => new Uint8Array(buffer, byteOffset, length));
      Assert.Equal(4, buffer.ByteLength);
    }
  }

  [Fact]
  public void DisposedViewThrowsAndReleasesTheLeaseOnce()
  {
    using var fixture = HermesRuntimeFixture.Create();

    var view = fixture.Runtime.Execute(runtime =>
    {
      using var source = fixture.Evaluate("new Uint8Array(4)", "u8-dispose.js");
      return Uint8ArrayCodec.Decode(source.Ref, runtime);
    });

    fixture.ResetCounters();
    view.Dispose();
    view.Dispose();
    fixture.WaitUntilIdle();

    Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Throws<ObjectDisposedException>(() => _ = view.Buffer);
    Assert.Throws<ObjectDisposedException>(() => _ = view.ByteOffset);
    Assert.Throws<ObjectDisposedException>(() => _ = view.Length);
    Assert.Throws<ObjectDisposedException>(() => view.ToArray());
    Assert.Throws<ObjectDisposedException>(() => view.Retain());
    Assert.Throws<ObjectDisposedException>(() => view.WithBytes(_ => { }));
  }

  [Fact]
  public void ScopedDecodeRegistersOneLease()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var scope = new GeneratedConversionScope();

    var decoded = fixture.Runtime.Execute(runtime =>
    {
      using var source = fixture.Evaluate("new Uint8Array(4)", "u8-scoped-decode.js");
      var decoded = Uint8ArrayCodec.Decode(source.Ref, runtime, scope);
      Assert.Equal(4, decoded.Length);
      return decoded;
    });

    fixture.ResetCounters();
    scope.Dispose();
    fixture.WaitUntilIdle();

    Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Throws<ObjectDisposedException>(() => _ = decoded.Length);
  }

  [Fact]
  public void ScopedDecodeIntoDisposedScopeReleasesTheLease()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var scope = new GeneratedConversionScope();
    scope.Dispose();

    fixture.Runtime.Execute(runtime =>
    {
      using var source = fixture.Evaluate("new Uint8Array(4)", "u8-scoped-disposed.js");
      fixture.ResetCounters();
      Assert.Throws<ObjectDisposedException>(() => Uint8ArrayCodec.Decode(source.Ref, runtime, scope));
      return 0;
    });
    fixture.WaitUntilIdle();

    Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void TransferAndEncodeBorrowedKeepTheView()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var scope = new GeneratedConversionScope();

    fixture.Runtime.Execute(runtime =>
    {
      Uint8ArrayCodec.Transfer(null!, scope);

      using var source = fixture.Evaluate("new Uint8Array(2)", "u8-transfer.js");
      var view = Uint8ArrayCodec.Decode(source.Ref, runtime);
      Uint8ArrayCodec.Transfer(view, scope);
      Uint8ArrayCodec.EncodeBorrowed(view, runtime).Dispose();
      Uint8ArrayCodec.EncodeBorrowed(view, runtime).Dispose();
      Assert.Equal(2, view.Length);
      return 0;
    });

    fixture.ResetCounters();
    scope.Dispose();
    fixture.WaitUntilIdle();

    Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Theory]
  [InlineData("new ArrayBuffer(4)")]
  [InlineData("new Int32Array(2)")]
  [InlineData("new DataView(new ArrayBuffer(4))")]
  [InlineData("({})")]
  [InlineData("5")]
  [InlineData("null")]
  [InlineData("undefined")]
  [InlineData("({ constructor: Uint8Array })")]
  public void DecodeRejectsEverythingButAUint8Array(string input)
  {
    using var fixture = HermesRuntimeFixture.Create();
    var scope = new GeneratedConversionScope();

    fixture.Runtime.Execute(runtime =>
    {
      using var source = fixture.Evaluate(input, "u8-reject.js");
      fixture.ResetCounters();

      AssertNotUint8Array(Assert.Throws<InvalidOperationException>(
          () => Uint8ArrayCodec.Decode(source.Ref, runtime)));
      AssertNotUint8Array(Assert.Throws<InvalidOperationException>(
          () => Uint8ArrayCodec.Decode(source, runtime)));
      AssertNotUint8Array(Assert.Throws<InvalidOperationException>(
          () => Uint8ArrayCodec.Decode(source.Ref, runtime, scope)));
      return 0;
    });

    fixture.ResetCounters();
    scope.Dispose();
    fixture.WaitUntilIdle();

    Assert.Equal(0u, fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void DecodeAcceptsAUint8ArraySubclassInstance()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var source = fixture.Evaluate("globalThis.v = " + Sub + "; v", "u8-subclass.js");
      using var view = Uint8ArrayCodec.Decode(source.Ref, runtime);

      Assert.Equal(0, view.ByteOffset);
      Assert.Equal(4, view.Length);
      view.WithBytes(b => b.Fill(0x42));
      Assert.Equal(
          "true:false:66,66,66,66",
          EvaluateString(
              fixture,
              "[v instanceof Uint8Array, v.constructor === Uint8Array, Array.from(v).join(',')].join(':')"));
      return 0;
    });
  }

  [Fact]
  public void DecodeAcceptsAPrototypeSpoofWithinItsBuffer()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();

    fixture.Runtime.Execute(runtime =>
    {
      using var accepted = fixture.Evaluate(Spoof(4), "u8-spoof-ok.js");
      using var view = Uint8ArrayCodec.Decode(accepted.Ref, runtime);
      Assert.Equal(2, view.ByteOffset);
      Assert.Equal(4, view.Length);
      Assert.Equal(new byte[] { 2, 3, 4, 5 }, view.ToArray());

      using var rejected = fixture.Evaluate(Spoof(9), "u8-spoof-bad.js");
      Assert.Throws<ArgumentOutOfRangeException>(() => Uint8ArrayCodec.Decode(rejected.Ref, runtime));
      return 0;
    });
    fixture.WaitUntilIdle();

    Assert.Equal(2u, fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Theory]
  [InlineData("byteOffset", "1.5", false, 0u)]
  [InlineData("byteLength", "-1", false, 0u)]
  [InlineData("buffer", "5", false, 0u)]
  [InlineData("byteOffset", "8", true, 1u)]
  public void DecodeRejectsShadowedViewFields(string name, string value, bool outOfRange, uint released)
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();

    fixture.Runtime.Execute(runtime =>
    {
      using var source = fixture.Evaluate(
          $"Object.defineProperty(new Uint8Array(4), '{name}', {{ value: {value} }})",
          "u8-shadow.js");
      if (outOfRange)
      {
        Assert.Throws<ArgumentOutOfRangeException>(() => Uint8ArrayCodec.Decode(source.Ref, runtime));
      }
      else
      {
        AssertNotUint8Array(Assert.Throws<InvalidOperationException>(
            () => Uint8ArrayCodec.Decode(source.Ref, runtime)));
      }
      return 0;
    });
    fixture.WaitUntilIdle();

    Assert.Equal(released, fixture.Counters.LongLivedArrayBuffersReleased);
  }

  private static string Spoof(int byteLength) =>
      "Object.defineProperties(Object.create(Uint8Array.prototype), { "
      + "buffer: { value: new Uint8Array([0, 1, 2, 3, 4, 5, 6, 7]).buffer }, "
      + "byteOffset: { value: 2 }, "
      + $"byteLength: {{ value: {byteLength} }} }})";

  private static string EvaluateString(HermesRuntimeFixture fixture, string source)
  {
    using var value = fixture.Evaluate(source, "u8-result.js");
    return value.AsString();
  }

  private static void AssertNotUint8Array(InvalidOperationException exception) =>
      Assert.Equal("Value is not a Uint8Array.", exception.Message);
}
