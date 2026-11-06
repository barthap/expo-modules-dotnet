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

    var encoded = fixture.Runtime.Execute(runtime => JavaScriptValueCodec.Encode(source, runtime));
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
      return ArrayBufferCodec.Decode(source.Ref, runtime);
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
              .Encode((value, "x"), runtime));
      return 0;
    });

    // The failed encode disposed only its retained copy of the author's value.
    Assert.Equal(JavaScriptValueKind.Object, fixture.Runtime.Execute(_ => value.Kind));
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
    public static RecordingLeaf Decode(JavaScriptValueRef value, JavaScriptRuntime runtime) =>
        throw new NotSupportedException();

    public static RecordingLeaf Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
        throw new NotSupportedException();

    public static JavaScriptValue Encode(RecordingLeaf value, JavaScriptRuntime runtime) =>
        throw new NotSupportedException();

    public static void Release(RecordingLeaf value) => value?.Dispose();
  }

  private readonly struct ThrowingStringCodec : IJavaScriptCodec<string>
  {
    public static string Decode(JavaScriptValueRef value, JavaScriptRuntime runtime) =>
        throw new NotSupportedException();

    public static string Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
        throw new NotSupportedException();

    public static JavaScriptValue Encode(string value, JavaScriptRuntime runtime) =>
        throw new InvalidOperationException("encode failed");
  }
}
