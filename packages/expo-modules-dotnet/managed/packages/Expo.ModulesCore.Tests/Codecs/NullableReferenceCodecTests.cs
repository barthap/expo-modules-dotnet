using System;
using System.Collections.Generic;
using Expo.JSI;
using Expo.ModulesCore.Codecs;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Codecs;

public sealed class NullableReferenceCodecTests
{
  [Theory]
  [InlineData("null")]
  [InlineData("undefined")]
  public void DecodeReturnsNullForNullishInputWithoutCallingInnerCodec(string expression)
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var nullish = fixture.Evaluate(expression, "nullable-reference-decode-nullish.js");

      // ThrowingStringCodec fails on every call, so a decode that reached the inner codec would
      // throw instead of returning null. That is what keeps the non-nullable codec strict.
      Assert.Null(NullableReferenceCodec<string, ThrowingStringCodec>.Decode(nullish.Ref, runtime));
      Assert.Null(NullableReferenceCodec<string, ThrowingStringCodec>.Decode(nullish, runtime));
      return true;
    });
  }

  [Fact]
  public void DecodeDelegatesNonNullValueToInnerCodec()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var value = fixture.Evaluate("'kept'", "nullable-reference-decode-value.js");

      Assert.Equal("kept", NullableReferenceCodec<string, StringCodec>.Decode(value.Ref, runtime));
      Assert.Equal("kept", NullableReferenceCodec<string, StringCodec>.Decode(value, runtime));
      return true;
    });
  }

  [Fact]
  public void EncodeProducesJavaScriptNullForManagedNull()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var encoded = NullableReferenceCodec<string, ThrowingStringCodec>.Encode(null, runtime);

      Assert.Equal(JavaScriptValueKind.Null, encoded.Kind);
      return true;
    });
  }

  [Fact]
  public void EncodeDelegatesNonNullValueToInnerCodec()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var encoded = NullableReferenceCodec<string, StringCodec>.Encode("kept", runtime);

      Assert.Equal(JavaScriptValueKind.String, encoded.Kind);
      Assert.Equal("kept", encoded.AsString());
      return true;
    });
  }

  [Fact]
  public void ByteArrayCodecComposesThroughTheNullableReferenceWrapper()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var nullish = fixture.Evaluate("null", "nullable-byte-array-null.js");
      using var buffer = fixture.Evaluate(
          "new Uint8Array([1, 2, 3]).buffer",
          "nullable-byte-array-value.js"
      );

      Assert.Null(NullableReferenceCodec<byte[], ByteArrayCodec>.Decode(nullish.Ref, runtime));
      Assert.Equal(
          new byte[] { 1, 2, 3 },
          NullableReferenceCodec<byte[], ByteArrayCodec>.Decode(buffer.Ref, runtime)
      );

      using var encodedNull = NullableReferenceCodec<byte[], ByteArrayCodec>.Encode(null, runtime);
      Assert.Equal(JavaScriptValueKind.Null, encodedNull.Kind);

      using var encodedValue = NullableReferenceCodec<byte[], ByteArrayCodec>.Encode(
          new byte[] { 4, 5 },
          runtime
      );
      Assert.Equal(new byte[] { 4, 5 }, ByteArrayCodec.Decode(encodedValue, runtime));
      return true;
    });
  }

  [Theory]
  [InlineData("null")]
  [InlineData("undefined")]
  public void NullableReadOnlyListCodecDecodesNullishContainerAsNull(string expression)
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var nullish = fixture.Evaluate(expression, "nullable-list-container-nullish.js");

      Assert.Null(NullableReadOnlyListCodec<string, StringCodec>.Decode(nullish.Ref, runtime));
      Assert.Null(NullableReadOnlyListCodec<string, StringCodec>.Decode(nullish, runtime));
      return true;
    });
  }

  [Fact]
  public void NullableReadOnlyListCodecRoundTripsContainersAndNullableElements()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var values = fixture.Evaluate(
          "['one', null, 'two']",
          "nullable-list-elements.js"
      );

      var decoded = NullableReadOnlyListCodec<string?, NullableReferenceCodec<string, StringCodec>>
          .Decode(values.Ref, runtime);
      Assert.Equal(new[] { "one", null, "two" }, decoded);

      using var encodedNull =
          NullableReadOnlyListCodec<string?, NullableReferenceCodec<string, StringCodec>>
              .Encode(null, runtime);
      Assert.Equal(JavaScriptValueKind.Null, encodedNull.Kind);

      using var encoded =
          NullableReadOnlyListCodec<string?, NullableReferenceCodec<string, StringCodec>>
              .Encode(new[] { "one", null }, runtime);
      using var global = runtime.Global();
      global.SetProperty("nullableListRoundTrip", encoded);
      using var shape = fixture.Evaluate(
          "nullableListRoundTrip.map((item) => item === null ? 'null' : item).join(',')",
          "nullable-list-encoded.js"
      );
      Assert.Equal("one,null", shape.AsString());
      return true;
    });
  }

  [Theory]
  [InlineData("null")]
  [InlineData("undefined")]
  public void NullableDictionaryCodecsDecodeNullishContainerAsNull(string expression)
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var nullish = fixture.Evaluate(expression, "nullable-dictionary-container-nullish.js");

      Assert.Null(NullableDictionaryCodec<string, StringCodec>.Decode(nullish.Ref, runtime));
      Assert.Null(NullableDictionaryCodec<string, StringCodec>.Decode(nullish, runtime));
      Assert.Null(NullableReadOnlyDictionaryCodec<string, StringCodec>.Decode(nullish.Ref, runtime));
      Assert.Null(NullableReadOnlyDictionaryCodec<string, StringCodec>.Decode(nullish, runtime));
      return true;
    });
  }

  [Fact]
  public void NullableDictionaryCodecRoundTripsContainersAndNullableValues()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var values = fixture.Evaluate(
          "({ kept: 'one', missing: null })",
          "nullable-dictionary-values.js"
      );

      var decoded = NullableDictionaryCodec<string?, NullableReferenceCodec<string, StringCodec>>
          .Decode(values.Ref, runtime);
      Assert.NotNull(decoded);
      Assert.Equal("one", decoded["kept"]);
      Assert.Null(decoded["missing"]);

      using var encodedNull =
          NullableDictionaryCodec<string?, NullableReferenceCodec<string, StringCodec>>
              .Encode(null, runtime);
      Assert.Equal(JavaScriptValueKind.Null, encodedNull.Kind);

      using var encoded =
          NullableDictionaryCodec<string?, NullableReferenceCodec<string, StringCodec>>
              .Encode(
                  new Dictionary<string, string?>(StringComparer.Ordinal)
                  {
                    ["kept"] = "one",
                    ["missing"] = null,
                  },
                  runtime
              );
      using var global = runtime.Global();
      global.SetProperty("nullableDictionaryRoundTrip", encoded);
      using var shape = fixture.Evaluate(
          "[nullableDictionaryRoundTrip.kept, nullableDictionaryRoundTrip.missing === null].join(':')",
          "nullable-dictionary-encoded.js"
      );
      Assert.Equal("one:true", shape.AsString());
      return true;
    });
  }

  [Fact]
  public void NullableReadOnlyDictionaryCodecRoundTripsContainersAndNullableValues()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var values = fixture.Evaluate(
          "({ kept: 'one', missing: undefined })",
          "nullable-read-only-dictionary-values.js"
      );

      var decoded =
          NullableReadOnlyDictionaryCodec<string?, NullableReferenceCodec<string, StringCodec>>
              .Decode(values.Ref, runtime);
      Assert.NotNull(decoded);
      Assert.Equal("one", decoded["kept"]);
      Assert.Null(decoded["missing"]);

      using var encodedNull =
          NullableReadOnlyDictionaryCodec<string?, NullableReferenceCodec<string, StringCodec>>
              .Encode(null, runtime);
      Assert.Equal(JavaScriptValueKind.Null, encodedNull.Kind);

      using var encoded =
          NullableReadOnlyDictionaryCodec<string?, NullableReferenceCodec<string, StringCodec>>
              .Encode(
                  new Dictionary<string, string?>(StringComparer.Ordinal) { ["missing"] = null },
                  runtime
              );
      using var global = runtime.Global();
      global.SetProperty("nullableReadOnlyDictionaryRoundTrip", encoded);
      using var shape = fixture.Evaluate(
          "nullableReadOnlyDictionaryRoundTrip.missing === null",
          "nullable-read-only-dictionary-encoded.js"
      );
      Assert.True(shape.AsBool());
      return true;
    });
  }

  [Fact]
  public void NestedNullableContainersComposeWithoutLosingNulls()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var values = fixture.Evaluate(
          "[['one'], null]",
          "nullable-nested-container.js"
      );

      var decoded = NullableReadOnlyListCodec<
          IReadOnlyList<string>?,
          NullableReadOnlyListCodec<string, StringCodec>>.Decode(values.Ref, runtime);
      Assert.NotNull(decoded);
      Assert.Equal(2, decoded.Count);
      Assert.Equal(new[] { "one" }, decoded[0]);
      Assert.Null(decoded[1]);
      return true;
    });
  }

  /// <summary>Fails on every conversion so a delegating call is impossible to miss.</summary>
  private readonly struct ThrowingStringCodec : IJavaScriptCodec<string>
  {
    public static string Decode(JavaScriptValueRef value, JavaScriptRuntime runtime) =>
        throw new InvalidOperationException("The inner codec must not run for nullish input.");

    public static string Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
        throw new InvalidOperationException("The inner codec must not run for nullish input.");

    public static JavaScriptValue Encode(string value, JavaScriptRuntime runtime) =>
        throw new InvalidOperationException("The inner codec must not run for a null value.");
  }
}
