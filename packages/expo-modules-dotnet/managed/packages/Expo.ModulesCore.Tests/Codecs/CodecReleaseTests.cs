using Expo.JSI;
using Expo.ModulesCore.Codecs;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Codecs;

public sealed class CodecReleaseTests
{
  [Fact]
  public void CodecReleaseAttemptsEveryValueAndAggregatesFailures()
  {
    var failure = new InvalidOperationException("middle leaf failed");
    var leaves = new[] { new RecordingLeaf(), new RecordingLeaf(failure), new RecordingLeaf() };

    List<Exception>? failures = null;
    foreach (var leaf in leaves)
    {
      CodecRelease.Release<RecordingLeaf, RecordingLeafCodec>(leaf, ref failures);
    }

    var thrown = Assert.Throws<AggregateException>(() => CodecRelease.ThrowIfFailed(failures));
    Assert.Same(failure, Assert.Single(thrown.InnerExceptions));
    Assert.All(leaves, leaf => Assert.Equal(1, leaf.DisposeCount));
  }

  [Fact]
  public void CodecReleaseFlattensNestedAggregateFailures()
  {
    var first = new InvalidOperationException("first");
    var second = new ArgumentException("second");
    var leaf = new RecordingLeaf(new AggregateException(first, second));

    List<Exception>? failures = null;
    CodecRelease.Release<RecordingLeaf, RecordingLeafCodec>(leaf, ref failures);

    var thrown = Assert.Throws<AggregateException>(() => CodecRelease.ThrowIfFailed(failures));
    Assert.Equal(new Exception[] { first, second }, thrown.InnerExceptions);
  }

  [Fact]
  public void ThrowIfFailedWithNoFailuresDoesNothing()
  {
    CodecRelease.ThrowIfFailed(null);
  }

  [Fact]
  public void JavaScriptValueEncodeReturnsARetainedCopy()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var source = fixture.Evaluate("({})", "codec-release-value-encode.js");

    var encoded = fixture.Runtime.Execute(runtime => JavaScriptValueCodec.Encode(source, runtime, CodecTestContext.Unused));
    Assert.NotSame(source, encoded);
    encoded.Dispose();
    Assert.Equal(JavaScriptValueKind.Object, fixture.Runtime.Execute(_ => source.Kind));

    fixture.ResetCounters();
    ReleaseVia<JavaScriptValue, JavaScriptValueCodec>(source);
    ReleaseVia<JavaScriptValue, JavaScriptValueCodec>(source);
    fixture.WaitUntilIdle();

    Assert.Equal(1u, fixture.Counters.ReleasedValues);
    ReleaseVia<JavaScriptValue, JavaScriptValueCodec>(null!);
  }

  [Fact]
  public void ArrayBufferReleaseFreesTheLeaseOnce()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var decoded = fixture.Runtime.Execute(runtime =>
    {
      using var source = fixture.Evaluate("new ArrayBuffer(4)", "codec-release-array-buffer.js");
      return ArrayBufferCodec.Decode(source.Ref, runtime, CodecTestContext.Unused);
    });

    fixture.ResetCounters();
    ReleaseVia<ArrayBuffer, ArrayBufferCodec>(decoded);
    ReleaseVia<ArrayBuffer, ArrayBufferCodec>(decoded);
    fixture.WaitUntilIdle();

    Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersReleased);
    ReleaseVia<ArrayBuffer, ArrayBufferCodec>(null!);
  }

  [Fact]
  public void ValueTupleEncodeFailureKeepsAuthorValue()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var value = fixture.Evaluate("({})", "codec-release-tuple-failure.js");

    fixture.Runtime.Execute(runtime =>
    {
      Assert.Throws<InvalidOperationException>(() =>
          ValueTupleCodec<JavaScriptValue, JavaScriptValueCodec, string, ThrowingStringCodec>
              .Encode((value, "x"), runtime, CodecTestContext.Unused));
      return 0;
    });

    // The failed encode disposed only its retained copy of the author's value.
    Assert.Equal(JavaScriptValueKind.Object, fixture.Runtime.Execute(_ => value.Kind));
  }

  [Fact]
  public void ListReleasesEveryBuffer()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var decoded = DecodeFromScript<IReadOnlyList<ArrayBuffer>, JavaScriptArrayCodec<ArrayBuffer, ArrayBufferCodec>>(
        fixture,
        "[new ArrayBuffer(1), new ArrayBuffer(2)]");

    Assert.Equal(2u, ReleaseAndCountBuffers<IReadOnlyList<ArrayBuffer>, JavaScriptArrayCodec<ArrayBuffer, ArrayBufferCodec>>(fixture, decoded));
    ReleaseVia<IReadOnlyList<ArrayBuffer>, JavaScriptArrayCodec<ArrayBuffer, ArrayBufferCodec>>(default!);
  }

  [Fact]
  public void DictionaryReleasesThroughBothInterfaces()
  {
    using var fixture = HermesRuntimeFixture.Create();
    const string source = "({ a: new ArrayBuffer(1), b: new ArrayBuffer(2) })";

    var concrete = DecodeFromScript<Dictionary<string, ArrayBuffer>, JavaScriptDictionaryCodec<ArrayBuffer, ArrayBufferCodec>>(fixture, source);
    Assert.Same(StringComparer.Ordinal, concrete.Comparer);
    Assert.Equal(2u, ReleaseAndCountBuffers<Dictionary<string, ArrayBuffer>, JavaScriptDictionaryCodec<ArrayBuffer, ArrayBufferCodec>>(fixture, concrete));

    var readOnly = DecodeFromScript<IReadOnlyDictionary<string, ArrayBuffer>, JavaScriptDictionaryCodec<ArrayBuffer, ArrayBufferCodec>>(fixture, source);
    Assert.Equal(2u, ReleaseAndCountBuffers<IReadOnlyDictionary<string, ArrayBuffer>, JavaScriptDictionaryCodec<ArrayBuffer, ArrayBufferCodec>>(fixture, readOnly));

    ReleaseVia<Dictionary<string, ArrayBuffer>, JavaScriptDictionaryCodec<ArrayBuffer, ArrayBufferCodec>>(default!);
    ReleaseVia<IReadOnlyDictionary<string, ArrayBuffer>, JavaScriptDictionaryCodec<ArrayBuffer, ArrayBufferCodec>>(default!);
  }

  [Fact]
  public void ListOfDictionariesReleasesEveryBuffer()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var decoded = DecodeFromScript<
        IReadOnlyList<Dictionary<string, ArrayBuffer>>,
        JavaScriptArrayCodec<Dictionary<string, ArrayBuffer>, JavaScriptDictionaryCodec<ArrayBuffer, ArrayBufferCodec>>>(
        fixture,
        "[{ a: new ArrayBuffer(1) }, { b: new ArrayBuffer(2) }]");

    Assert.Equal(2u, ReleaseAndCountBuffers<
        IReadOnlyList<Dictionary<string, ArrayBuffer>>,
        JavaScriptArrayCodec<Dictionary<string, ArrayBuffer>, JavaScriptDictionaryCodec<ArrayBuffer, ArrayBufferCodec>>>(
        fixture,
        decoded));
  }

  [Fact]
  public void NullableValueReleasesItsInnerValue()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var decoded = DecodeFromScript<BufferSlot?, NullableCodec<BufferSlot, BufferSlotCodec>>(
        fixture,
        "({ buffer: new ArrayBuffer(1) })");

    Assert.Equal(1u, ReleaseAndCountBuffers<BufferSlot?, NullableCodec<BufferSlot, BufferSlotCodec>>(fixture, decoded));
    ReleaseVia<BufferSlot?, NullableCodec<BufferSlot, BufferSlotCodec>>(null);
  }

  [Fact]
  public void NullableReferenceReleasesItsInnerValue()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var decoded = DecodeFromScript<ArrayBuffer?, NullableReferenceCodec<ArrayBuffer, ArrayBufferCodec>>(
        fixture,
        "new ArrayBuffer(1)");

    Assert.Equal(1u, ReleaseAndCountBuffers<ArrayBuffer?, NullableReferenceCodec<ArrayBuffer, ArrayBufferCodec>>(fixture, decoded));
    ReleaseVia<ArrayBuffer?, NullableReferenceCodec<ArrayBuffer, ArrayBufferCodec>>(default);
  }

  [Fact]
  public void NullableListReleasesEveryBuffer()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var decoded = DecodeFromScript<IReadOnlyList<ArrayBuffer>?, NullableReadOnlyListCodec<ArrayBuffer, ArrayBufferCodec>>(
        fixture,
        "[new ArrayBuffer(1), new ArrayBuffer(2)]");

    Assert.Equal(2u, ReleaseAndCountBuffers<IReadOnlyList<ArrayBuffer>?, NullableReadOnlyListCodec<ArrayBuffer, ArrayBufferCodec>>(fixture, decoded));
    ReleaseVia<IReadOnlyList<ArrayBuffer>?, NullableReadOnlyListCodec<ArrayBuffer, ArrayBufferCodec>>(default);
  }

  [Fact]
  public void NullableDictionaryReleasesEveryBuffer()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var decoded = DecodeFromScript<Dictionary<string, ArrayBuffer>?, NullableDictionaryCodec<ArrayBuffer, ArrayBufferCodec>>(
        fixture,
        "({ a: new ArrayBuffer(1), b: new ArrayBuffer(2) })");

    Assert.Equal(2u, ReleaseAndCountBuffers<Dictionary<string, ArrayBuffer>?, NullableDictionaryCodec<ArrayBuffer, ArrayBufferCodec>>(fixture, decoded));
    ReleaseVia<Dictionary<string, ArrayBuffer>?, NullableDictionaryCodec<ArrayBuffer, ArrayBufferCodec>>(default);
  }

  [Fact]
  public void NullableReadOnlyDictionaryReleasesEveryBuffer()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var decoded = DecodeFromScript<IReadOnlyDictionary<string, ArrayBuffer>?, NullableReadOnlyDictionaryCodec<ArrayBuffer, ArrayBufferCodec>>(
        fixture,
        "({ a: new ArrayBuffer(1), b: new ArrayBuffer(2) })");

    Assert.Equal(2u, ReleaseAndCountBuffers<IReadOnlyDictionary<string, ArrayBuffer>?, NullableReadOnlyDictionaryCodec<ArrayBuffer, ArrayBufferCodec>>(fixture, decoded));
    ReleaseVia<IReadOnlyDictionary<string, ArrayBuffer>?, NullableReadOnlyDictionaryCodec<ArrayBuffer, ArrayBufferCodec>>(default);
  }

  [Fact]
  public void ListReleaseAggregatesAndContinues()
  {
    var failure = new InvalidOperationException("middle leaf failed");
    var leaves = new[] { new RecordingLeaf(), new RecordingLeaf(failure), new RecordingLeaf() };

    var thrown = Assert.Throws<AggregateException>(() =>
        ReleaseVia<IReadOnlyList<RecordingLeaf>, JavaScriptArrayCodec<RecordingLeaf, RecordingLeafCodec>>(leaves));

    Assert.Same(failure, Assert.Single(thrown.InnerExceptions));
    Assert.All(leaves, leaf => Assert.Equal(1, leaf.DisposeCount));
  }

  [Fact]
  public void DictionaryReleaseAggregatesAndContinues()
  {
    var failure = new InvalidOperationException("middle leaf failed");
    var leaves = new Dictionary<string, RecordingLeaf>
    {
      ["a"] = new RecordingLeaf(),
      ["b"] = new RecordingLeaf(failure),
      ["c"] = new RecordingLeaf(),
    };

    var thrown = Assert.Throws<AggregateException>(() =>
        ReleaseVia<Dictionary<string, RecordingLeaf>, JavaScriptDictionaryCodec<RecordingLeaf, RecordingLeafCodec>>(leaves));

    Assert.Same(failure, Assert.Single(thrown.InnerExceptions));
    Assert.All(leaves.Values, leaf => Assert.Equal(1, leaf.DisposeCount));
  }

  [Fact]
  public void ListDecodeFailureReleasesDecodedElements()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var source = fixture.Evaluate("[new ArrayBuffer(4), 1]", "codec-release-list-decode-failure.js");
    fixture.WaitUntilIdle();

    fixture.ResetCounters();
    fixture.Runtime.Execute(runtime =>
    {
      Assert.Throws<InvalidOperationException>(() =>
          JavaScriptArrayCodec<ArrayBuffer, ArrayBufferCodec>.DecodeToArray(source.Ref, runtime, CodecTestContext.Unused));
      return 0;
    });
    fixture.WaitUntilIdle();
    Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersReleased);

    fixture.ResetCounters();
    fixture.Runtime.Execute(runtime =>
    {
      Assert.Throws<InvalidOperationException>(() =>
          JavaScriptArrayCodec<ArrayBuffer, ArrayBufferCodec>.DecodeToArray(source, runtime, CodecTestContext.Unused));
      return 0;
    });
    fixture.WaitUntilIdle();
    Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void DictionaryDecodeFailureReleasesDecodedValues()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var source = fixture.Evaluate("({ a: new ArrayBuffer(4), b: 1 })", "codec-release-dictionary-decode-failure.js");
    fixture.WaitUntilIdle();

    fixture.ResetCounters();
    fixture.Runtime.Execute(runtime =>
    {
      Assert.Throws<InvalidOperationException>(() =>
          JavaScriptDictionaryCodec<ArrayBuffer, ArrayBufferCodec>.DecodeToDictionary(source.Ref, runtime, CodecTestContext.Unused));
      return 0;
    });
    fixture.WaitUntilIdle();
    Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersReleased);

    fixture.ResetCounters();
    fixture.Runtime.Execute(runtime =>
    {
      Assert.Throws<InvalidOperationException>(() =>
          JavaScriptDictionaryCodec<ArrayBuffer, ArrayBufferCodec>.DecodeToDictionary(source, runtime, CodecTestContext.Unused));
      return 0;
    });
    fixture.WaitUntilIdle();
    Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void ListEncodeLeavesAuthorValuesUsable()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var first = fixture.Evaluate("({})", "codec-release-list-encode-first.js");
    using var second = fixture.Evaluate("({})", "codec-release-list-encode-second.js");
    IReadOnlyList<JavaScriptValue> authored = [first, second];

    fixture.Runtime.Execute(runtime =>
    {
      using var encoded = JavaScriptArrayCodec<JavaScriptValue, JavaScriptValueCodec>.Encode(authored, runtime, CodecTestContext.Unused);
      using var array = encoded.AsArray();
      using var encodedFirst = array.GetValue(0);
      using var encodedSecond = array.GetValue(1);
      Assert.True(runtime.StrictEquals(first, encodedFirst));
      Assert.True(runtime.StrictEquals(second, encodedSecond));
      return 0;
    });

    // Disposing the encoded array released only its retained copies.
    Assert.Equal(JavaScriptValueKind.Object, fixture.Runtime.Execute(_ => first.Kind));
    Assert.Equal(JavaScriptValueKind.Object, fixture.Runtime.Execute(_ => second.Kind));
  }

  [Fact]
  public void ListEncodesOneAliasTwice()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var decoded = DecodeFromScript<ArrayBuffer, ArrayBufferCodec>(fixture, "new ArrayBuffer(4)");
    IReadOnlyList<ArrayBuffer> aliased = [decoded, decoded];

    fixture.Runtime.Execute(runtime =>
    {
      using var encoded = JavaScriptArrayCodec<ArrayBuffer, ArrayBufferCodec>.Encode(aliased, runtime, CodecTestContext.Unused);
      using var array = encoded.AsArray();
      using var left = array.GetValue(0);
      using var right = array.GetValue(1);
      Assert.True(runtime.StrictEquals(left, right));
      return 0;
    });

    Assert.Equal(1u, ReleaseAndCountBuffers<IReadOnlyList<ArrayBuffer>, JavaScriptArrayCodec<ArrayBuffer, ArrayBufferCodec>>(fixture, aliased));
  }

  private static T DecodeFromScript<T, TCodec>(HermesRuntimeFixture fixture, string script)
      where TCodec : IJavaScriptCodec<T> =>
      fixture.Runtime.Execute(runtime =>
      {
        using var source = fixture.Evaluate(script, "codec-release-decode.js");
        return TCodec.Decode(source.Ref, runtime, CodecTestContext.Unused);
      });

  private static uint ReleaseAndCountBuffers<T, TCodec>(HermesRuntimeFixture fixture, T value)
      where TCodec : IJavaScriptCodec<T>
  {
    fixture.WaitUntilIdle();
    fixture.ResetCounters();
    ReleaseVia<T, TCodec>(value);
    fixture.WaitUntilIdle();
    return fixture.Counters.LongLivedArrayBuffersReleased;
  }

  private static void ReleaseVia<T, TCodec>(T value)
      where TCodec : IJavaScriptCodec<T> => TCodec.Release(value);

  private sealed class RecordingLeaf(Exception? failure = null)
  {
    public int DisposeCount { get; private set; }

    public void Dispose()
    {
      DisposeCount++;
      if (failure is not null)
      {
        throw failure;
      }
    }
  }

  private readonly struct RecordingLeafCodec : IJavaScriptCodec<RecordingLeaf>
  {
    public static RecordingLeaf Decode(JavaScriptValueRef value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
        throw new NotSupportedException();

    public static RecordingLeaf Decode(JavaScriptValue value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
        throw new NotSupportedException();

    public static JavaScriptValue Encode(RecordingLeaf value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
        throw new NotSupportedException();

    public static void Release(RecordingLeaf value) => value?.Dispose();
  }

  private readonly struct ThrowingStringCodec : IJavaScriptCodec<string>
  {
    public static string Decode(JavaScriptValueRef value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
        throw new NotSupportedException();

    public static string Decode(JavaScriptValue value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
        throw new NotSupportedException();

    public static JavaScriptValue Encode(string value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
        throw new InvalidOperationException("encode failed");
  }

  private readonly record struct BufferSlot(ArrayBuffer Buffer);

  private readonly struct BufferSlotCodec : IJavaScriptCodec<BufferSlot>
  {
    public static BufferSlot Decode(JavaScriptValueRef value, JavaScriptRuntime runtime, DotnetRuntimeContext context)
    {
      var obj = value.AsObject();
      var property = obj.GetProperty("buffer");
      return new BufferSlot(ArrayBufferCodec.Decode(property, runtime, CodecTestContext.Unused));
    }

    public static BufferSlot Decode(JavaScriptValue value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
        Decode(value.Ref, runtime, CodecTestContext.Unused);

    public static JavaScriptValue Encode(BufferSlot value, JavaScriptRuntime runtime, DotnetRuntimeContext context)
    {
      using var obj = runtime.CreateObject();
      using var buffer = ArrayBufferCodec.Encode(value.Buffer, runtime, CodecTestContext.Unused);
      obj.SetProperty("buffer", buffer);
      return obj.AsValue();
    }

    public static void Release(BufferSlot value) => ArrayBufferCodec.Release(value.Buffer);
  }
}
