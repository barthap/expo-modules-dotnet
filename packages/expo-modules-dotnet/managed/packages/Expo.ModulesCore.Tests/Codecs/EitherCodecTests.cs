using System.Globalization;
using Expo.JSI;
using Expo.ModulesCore.Codecs;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;
using A = Expo.ModulesCore.Codecs.GeneratedScopedOrdinaryCodec<string, Expo.ModulesCore.Codecs.StringCodec>;
using O = Expo.ModulesCore.Codecs.EitherCodec<
    string,
    Expo.ModulesCore.Codecs.StringCodec,
    double,
    Expo.ModulesCore.Codecs.NumberCodec<double>>;
using S = Expo.ModulesCore.Codecs.GeneratedScopedEitherCodec<
    string,
    Expo.ModulesCore.Codecs.GeneratedScopedOrdinaryCodec<string, Expo.ModulesCore.Codecs.StringCodec>,
    Expo.ModulesCore.ArrayBuffer,
    Expo.ModulesCore.Codecs.ArrayBufferCodec>;

namespace Expo.ModulesCore.Tests.Codecs;

public sealed class EitherCodecTests
{
  [Theory]
  [InlineData("'x'", "first:x")]
  [InlineData("''", "first:")]
  [InlineData("2.5", "second:2.5")]
  [InlineData("0", "second:0")]
  public void OrdinaryUnionDecodesEachMemberByKind(string input, string expected)
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var source = fixture.Evaluate(input, "either-ordinary-decode.js");

      Assert.Equal(expected, Describe(O.Decode(source.Ref, runtime)));
      Assert.Equal(expected, Describe(O.Decode(source, runtime)));
      return 0;
    });
  }

  [Theory]
  [InlineData("true", "Bool")]
  [InlineData("({})", "Object")]
  [InlineData("null", "Null")]
  [InlineData("undefined", "Undefined")]
  [InlineData("new ArrayBuffer(1)", "ArrayBuffer")]
  public void OrdinaryUnionRejectsOtherKinds(string input, string kind)
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var source = fixture.Evaluate(input, "either-ordinary-reject.js");
      var expected = $"Value kind '{kind}' is not one of 'String' or 'Number'.";

      var fromRef = Assert.Throws<InvalidOperationException>(() => O.Decode(source.Ref, runtime));
      var fromValue = Assert.Throws<InvalidOperationException>(() => O.Decode(source, runtime));

      Assert.Equal(expected, fromRef.Message);
      Assert.Equal(expected, fromValue.Message);
      return 0;
    });
  }

  [Fact]
  public void OrdinaryUnionEncodesTheActiveMember()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var text = O.Encode(Either<string, double>.FromFirst("a"), runtime);
      using var number = O.Encode(Either<string, double>.FromSecond(2.5), runtime);

      Assert.Equal("a", text.AsString());
      Assert.Equal(2.5, number.AsDouble());
      return 0;
    });
  }

  [Fact]
  public void ScopedUnionDecodesStringWithoutRegistering()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var scope = new GeneratedConversionScope();
    var disposedScope = new GeneratedConversionScope();
    disposedScope.Dispose();

    fixture.Runtime.Execute(runtime =>
    {
      using var source = fixture.Evaluate("'x'", "either-scoped-string.js");
      var decoded = S.Decode(source.Ref, runtime, scope);
      Assert.True(decoded.IsFirst);
      Assert.Equal("x", decoded.First);

      // A disposed scope throws on Register, so success here proves the string path never registers.
      var again = S.Decode(source.Ref, runtime, disposedScope);
      Assert.True(again.IsFirst);
      Assert.Equal("x", again.First);
      return 0;
    });

    fixture.ResetCounters();
    scope.Dispose();
    fixture.WaitUntilIdle();

    Assert.Equal(0u, fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void ScopedUnionDecodesArrayBufferIntoOneLease()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var scope = new GeneratedConversionScope();

    var decoded = fixture.Runtime.Execute(runtime =>
    {
      using var source = fixture.Evaluate("new Uint8Array([1, 2, 3]).buffer", "either-scoped-buffer.js");
      var decoded = S.Decode(source.Ref, runtime, scope);
      Assert.False(decoded.IsFirst);
      Assert.Equal(new byte[] { 1, 2, 3 }, decoded.Second.ToArray());
      return decoded;
    });

    fixture.ResetCounters();
    scope.Dispose();
    fixture.WaitUntilIdle();

    Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Throws<ObjectDisposedException>(() => _ = decoded.Second.ByteLength);
  }

  [Theory]
  [InlineData("5", "Number")]
  [InlineData("({})", "Object")]
  [InlineData("null", "Null")]
  [InlineData("undefined", "Undefined")]
  [InlineData("new Uint8Array(2)", "Object")]
  public void ScopedUnionRejectsOtherKindsWithoutRegistering(string input, string kind)
  {
    using var fixture = HermesRuntimeFixture.Create();
    var scope = new GeneratedConversionScope();

    fixture.Runtime.Execute(runtime =>
    {
      using var source = fixture.Evaluate(input, "either-scoped-reject.js");

      var error = Assert.Throws<InvalidOperationException>(() => S.Decode(source.Ref, runtime, scope));

      Assert.Equal($"Value kind '{kind}' is not one of 'String' or 'ArrayBuffer'.", error.Message);
      return 0;
    });

    fixture.ResetCounters();
    scope.Dispose();
    fixture.WaitUntilIdle();

    Assert.Equal(0u, fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void ScopedUnionTransferAndEncodeFollowTheActiveMember()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var scope = new GeneratedConversionScope();

    fixture.Runtime.Execute(runtime =>
    {
      using var source = fixture.Evaluate("new ArrayBuffer(4)", "either-scoped-transfer.js");
      using var global = runtime.Global();
      global.SetProperty("b", source);
      var buffer = ArrayBufferCodec.Decode(source.Ref, runtime);

      S.Transfer(Either<string, Expo.ModulesCore.ArrayBuffer>.FromFirst("x"), scope);
      S.Transfer(Either<string, Expo.ModulesCore.ArrayBuffer>.FromSecond(buffer), scope);

      using var text = S.EncodeBorrowed(Either<string, Expo.ModulesCore.ArrayBuffer>.FromFirst("x"), runtime);
      Assert.Equal("x", text.AsString());

      using var encoded = S.EncodeBorrowed(
          Either<string, Expo.ModulesCore.ArrayBuffer>.FromSecond(buffer),
          runtime);
      global.SetProperty("r", encoded);
      using var same = fixture.Evaluate("b === r", "either-scoped-transfer-check.js");
      Assert.True(same.AsBool());
      return 0;
    });

    fixture.ResetCounters();
    scope.Dispose();
    fixture.WaitUntilIdle();

    // The counter proves one native entry was released. "Registered once, not twice" rests on reading
    // GeneratedScopedEitherCodec.Transfer, because a repeated registration releases once by design.
    Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void OrdinaryAdapterNeverTouchesTheScope()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var disposedScope = new GeneratedConversionScope();
    disposedScope.Dispose();

    fixture.Runtime.Execute(runtime =>
    {
      using var source = fixture.Evaluate("'x'", "either-adapter.js");

      Assert.Equal("x", A.Decode(source.Ref, runtime, disposedScope));
      A.Transfer("x", disposedScope);
      using var encoded = A.EncodeBorrowed("x", runtime);
      Assert.Equal("x", encoded.AsString());
      Assert.Equal(JavaScriptValueKind.String, A.Kind);
      return 0;
    });
  }

  [Fact]
  public void CarrierExposesOnlyTheActiveMember()
  {
    var first = Either<string, int>.FromFirst("a");
    Assert.True(first.IsFirst);
    Assert.Equal("a", first.First);
    Assert.Equal(
        "The union holds its first member.",
        Assert.Throws<InvalidOperationException>(() => _ = first.Second).Message);
    Assert.Equal(1, first.Match(s => s.Length, n => -n));

    var second = Either<string, int>.FromSecond(7);
    Assert.False(second.IsFirst);
    Assert.Equal(7, second.Second);
    Assert.Equal(
        "The union holds its second member.",
        Assert.Throws<InvalidOperationException>(() => _ = second.First).Message);
    Assert.Equal(-7, second.Match(s => s.Length, n => -n));

    var empty = default(Either<string, int>);
    Assert.False(empty.IsFirst);
    Assert.Equal(0, empty.Second);

    Assert.Throws<ArgumentNullException>(() => first.Match(null!, n => n));
    Assert.Throws<ArgumentNullException>(() => first.Match(s => s.Length, null!));
  }

  [Fact]
  public void LeafCodecsReportTheirValueKind()
  {
    Assert.Equal(JavaScriptValueKind.String, StringCodec.Kind);
    Assert.Equal(JavaScriptValueKind.Bool, BoolCodec.Kind);
    Assert.Equal(JavaScriptValueKind.Number, NumberCodec<int>.Kind);
    Assert.Equal(JavaScriptValueKind.Number, NumberCodec<double>.Kind);
    Assert.Equal(JavaScriptValueKind.ArrayBuffer, ArrayBufferCodec.Kind);
    Assert.Equal(JavaScriptValueKind.ArrayBuffer, ByteArrayCodec.Kind);
    Assert.Equal(JavaScriptValueKind.ArrayBuffer, MemoryByteCodec.Kind);
    Assert.Equal(JavaScriptValueKind.ArrayBuffer, ReadOnlyMemoryByteCodec.Kind);
  }

  private static string Describe(Either<string, double> value) =>
      value.IsFirst
          ? $"first:{value.First}"
          : $"second:{value.Second.ToString(CultureInfo.InvariantCulture)}";
}
