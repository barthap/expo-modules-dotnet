using Expo.JSI;
using Expo.ModulesCore.Codecs;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;
using NullableList = Expo.ModulesCore.Codecs.GeneratedScopedNullableReferenceCodec<
    System.Collections.Generic.IReadOnlyList<Expo.ModulesCore.ArrayBuffer>,
    Expo.ModulesCore.Codecs.GeneratedScopedListCodec<Expo.ModulesCore.ArrayBuffer, Expo.ModulesCore.Codecs.ArrayBufferCodec>>;

namespace Expo.ModulesCore.Tests.Codecs;

public sealed class ScopedCodecTests
{
  [Fact]
  public void JavaScriptValueScopedDecodeRegistersOneRetainedWrapper()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var scope = new GeneratedConversionScope();

    var (source, decoded) = fixture.Runtime.Execute(runtime =>
    {
      var source = fixture.Evaluate("'x'", "scoped-value-decode.js");
      var decoded = JavaScriptValueCodec.Decode(source.Ref, runtime, scope);
      Assert.Equal("x", decoded.AsString());
      return (source, decoded);
    });
    using (source)
    {
      fixture.ResetCounters();
      scope.Dispose();
      fixture.WaitUntilIdle();

      Assert.Equal(1u, fixture.Counters.ReleasedValues);
      Assert.Throws<ObjectDisposedException>(() => _ = decoded.Kind);
      Assert.Equal("x", fixture.Runtime.Execute(_ => source.AsString()));
    }
  }

  [Fact]
  public void ScopedDecodeIntoDisposedScopeReleasesTheRetainedWrapper()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var scope = new GeneratedConversionScope();
    scope.Dispose();

    fixture.Runtime.Execute(runtime =>
    {
      using var source = fixture.Evaluate("'x'", "scoped-value-decode-disposed.js");

      fixture.ResetCounters();
      Assert.Throws<ObjectDisposedException>(() => JavaScriptValueCodec.Decode(source.Ref, runtime, scope));

      // The scope refused the retained wrapper, so Decode must release it.
      Assert.Equal(1u, fixture.Counters.ReleasedValues);
      return 0;
    });
  }

  [Fact]
  public void JavaScriptValueTransferReleasesSourceOnceAndEncodedResultSurvives()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var scope = new GeneratedConversionScope();

    fixture.Runtime.Execute(runtime =>
    {
      var source = fixture.Evaluate("'x'", "scoped-value-transfer.js");
      JavaScriptValueCodec.Transfer(source, scope);
      JavaScriptValueCodec.Transfer(source, scope);
      using var encoded = JavaScriptValueCodec.EncodeBorrowed(source, runtime);

      fixture.ResetCounters();
      scope.Dispose();

      Assert.Equal(1u, fixture.Counters.ReleasedValues);
      Assert.Equal("x", encoded.AsString());
      return 0;
    });
  }

  [Fact]
  public void JavaScriptValueEncodeBorrowedLeavesSourceUsable()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var source = fixture.Evaluate("'x'", "scoped-value-borrow.js");
      var encoded = JavaScriptValueCodec.EncodeBorrowed(source, runtime);
      encoded.Dispose();

      Assert.Equal("x", source.AsString());
      return 0;
    });
  }

  [Fact]
  public void ArrayBufferScopedDecodeRegistersOneLease()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var scope = new GeneratedConversionScope();

    fixture.Runtime.Execute(runtime =>
    {
      using var source = fixture.Evaluate("new ArrayBuffer(4)", "scoped-buffer-decode.js");
      var decoded = ArrayBufferCodec.Decode(source.Ref, runtime, scope);
      Assert.Equal(4, decoded.ByteLength);
      return 0;
    });

    fixture.ResetCounters();
    scope.Dispose();
    fixture.WaitUntilIdle();

    Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void ArrayBufferEncodeBorrowedKeepsSourceAndIdentity()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var scope = new GeneratedConversionScope();

    fixture.Runtime.Execute(runtime =>
    {
      using var source = fixture.Evaluate("new ArrayBuffer(4)", "scoped-buffer-identity.js");
      using var global = runtime.Global();
      global.SetProperty("b", source);
      var buffer = ArrayBufferCodec.Decode(source.Ref, runtime, scope);

      using var encoded = ArrayBufferCodec.EncodeBorrowed(buffer, runtime);
      global.SetProperty("r", encoded);
      using var same = fixture.Evaluate("b === r", "scoped-buffer-identity-check.js");

      Assert.True(same.AsBool());
      Assert.Equal(4, buffer.ByteLength);
      return 0;
    });
  }

  [Fact]
  public void ArrayBufferTransferReleasesLeaseOnce()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var scope = new GeneratedConversionScope();

    fixture.Runtime.Execute(runtime =>
    {
      using var source = fixture.Evaluate("new ArrayBuffer(4)", "scoped-buffer-transfer.js");
      var buffer = ArrayBufferCodec.Decode(source.Ref, runtime);
      ArrayBufferCodec.Transfer(buffer, scope);
      ArrayBufferCodec.Transfer(buffer, scope);
      return 0;
    });

    fixture.ResetCounters();
    scope.Dispose();
    fixture.WaitUntilIdle();

    Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void ScopedListDecodesEachBufferIntoTheScope()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var scope = new GeneratedConversionScope();

    fixture.Runtime.Execute(runtime =>
    {
      using var source = fixture.Evaluate(
          "[new ArrayBuffer(1), new ArrayBuffer(2)]",
          "scoped-list-decode.js");
      var decoded = GeneratedScopedListCodec<ArrayBuffer, ArrayBufferCodec>.Decode(
          source.Ref,
          runtime,
          scope);

      Assert.Equal([1, 2], decoded.Select(buffer => buffer.ByteLength));
      return 0;
    });

    fixture.ResetCounters();
    scope.Dispose();
    fixture.WaitUntilIdle();

    Assert.Equal(2u, fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void ScopedListEncodesOneAliasTwice()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var scope = new GeneratedConversionScope();

    fixture.Runtime.Execute(runtime =>
    {
      var value = fixture.Evaluate("({})", "scoped-list-alias.js");
      IReadOnlyList<JavaScriptValue> list = [value, value];

      GeneratedScopedListCodec<JavaScriptValue, JavaScriptValueCodec>.Transfer(list, scope);
      using var encoded = GeneratedScopedListCodec<JavaScriptValue, JavaScriptValueCodec>
          .EncodeBorrowed(list, runtime);

      fixture.ResetCounters();
      scope.Dispose();
      Assert.Equal(1u, fixture.Counters.ReleasedValues);

      using var global = runtime.Global();
      global.SetProperty("encodedList", encoded);
      using var same = fixture.Evaluate(
          "encodedList.length === 2 && encodedList[0] === encodedList[1]",
          "scoped-list-alias-check.js");
      Assert.True(same.AsBool());
      return 0;
    });
  }

  [Fact]
  public void ScopedListNullElementFailsEncodeAndKeepsOtherLeavesOwned()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var scope = new GeneratedConversionScope();

    fixture.Runtime.Execute(runtime =>
    {
      var first = fixture.Evaluate("({})", "scoped-list-null-first.js");
      var second = fixture.Evaluate("({})", "scoped-list-null-second.js");
      IReadOnlyList<JavaScriptValue> list = [first, null!, second];

      GeneratedScopedListCodec<JavaScriptValue, JavaScriptValueCodec>.Transfer(list, scope);
      Assert.Throws<ArgumentNullException>(() =>
          GeneratedScopedListCodec<JavaScriptValue, JavaScriptValueCodec>
              .EncodeBorrowed(list, runtime));

      fixture.ResetCounters();
      scope.Dispose();

      Assert.Equal(2u, fixture.Counters.ReleasedValues);
      return 0;
    });
  }

  [Fact]
  public void ScopedListEncodeReleasesElementTemporaries()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var scope = new GeneratedConversionScope();

    fixture.Runtime.Execute(runtime =>
    {
      IReadOnlyList<string> strings = ["a", "b"];
      fixture.ResetCounters();
      JavaScriptArrayCodec<string, StringCodec>.Encode(strings, runtime).Dispose();
      var baseline = fixture.Counters.ReleasedValues;

      var first = fixture.Evaluate("({})", "scoped-list-temporaries-first.js");
      var second = fixture.Evaluate("({})", "scoped-list-temporaries-second.js");
      IReadOnlyList<JavaScriptValue> list = [first, second];
      GeneratedScopedListCodec<JavaScriptValue, JavaScriptValueCodec>.Transfer(list, scope);

      fixture.ResetCounters();
      GeneratedScopedListCodec<JavaScriptValue, JavaScriptValueCodec>
          .EncodeBorrowed(list, runtime)
          .Dispose();

      Assert.Equal(baseline, fixture.Counters.ReleasedValues);
      return 0;
    });
  }

  [Fact]
  public void ScopedDictionaryEncodeReleasesPropertyTemporaries()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var scope = new GeneratedConversionScope();

    fixture.Runtime.Execute(runtime =>
    {
      IReadOnlyDictionary<string, string> strings = new Dictionary<string, string>
      {
        ["a"] = "a",
        ["b"] = "b",
      };
      fixture.ResetCounters();
      JavaScriptDictionaryCodec<string, StringCodec>.Encode(strings, runtime).Dispose();
      var baseline = fixture.Counters.ReleasedValues;

      var first = fixture.Evaluate("({})", "scoped-dictionary-temporaries-first.js");
      var second = fixture.Evaluate("({})", "scoped-dictionary-temporaries-second.js");
      IReadOnlyDictionary<string, JavaScriptValue> dictionary = new Dictionary<string, JavaScriptValue>
      {
        ["a"] = first,
        ["b"] = second,
      };
      GeneratedScopedDictionaryCodec<JavaScriptValue, JavaScriptValueCodec>.Transfer(dictionary, scope);

      fixture.ResetCounters();
      GeneratedScopedDictionaryCodec<JavaScriptValue, JavaScriptValueCodec>
          .EncodeBorrowed(dictionary, runtime)
          .Dispose();

      Assert.Equal(baseline, fixture.Counters.ReleasedValues);
      return 0;
    });
  }

  [Fact]
  public void ScopedDictionaryImplementsBothDictionaryInterfaces()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var mutableScope = new GeneratedConversionScope();
    var readOnlyScope = new GeneratedConversionScope();

    fixture.Runtime.Execute(runtime =>
    {
      using var source = fixture.Evaluate("new ArrayBuffer(1)", "scoped-dictionary.js");
      var mutable = new Dictionary<string, ArrayBuffer>
      {
        ["a"] = ArrayBufferCodec.Decode(source.Ref, runtime),
      };
      var readOnly = new Dictionary<string, ArrayBuffer>
      {
        ["a"] = ArrayBufferCodec.Decode(source.Ref, runtime),
      };

      using var mutableEncoded = TransferAndEncode<
          Dictionary<string, ArrayBuffer>,
          GeneratedScopedDictionaryCodec<ArrayBuffer, ArrayBufferCodec>>(mutable, runtime, mutableScope);
      using var readOnlyEncoded = TransferAndEncode<
          IReadOnlyDictionary<string, ArrayBuffer>,
          GeneratedScopedDictionaryCodec<ArrayBuffer, ArrayBufferCodec>>(readOnly, runtime, readOnlyScope);

      using var global = runtime.Global();
      global.SetProperty("mutableEncoded", mutableEncoded);
      global.SetProperty("readOnlyEncoded", readOnlyEncoded);
      using var shape = fixture.Evaluate(
          "Object.keys(mutableEncoded).join() === 'a' && mutableEncoded.a instanceof ArrayBuffer"
          + " && Object.keys(readOnlyEncoded).join() === 'a' && readOnlyEncoded.a instanceof ArrayBuffer",
          "scoped-dictionary-check.js");
      Assert.True(shape.AsBool());
      return 0;
    });

    fixture.ResetCounters();
    mutableScope.Dispose();
    readOnlyScope.Dispose();
    fixture.WaitUntilIdle();

    Assert.Equal(2u, fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void ScopedDictionaryDecodesEachPropertyIntoTheScope()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var scope = new GeneratedConversionScope();

    fixture.Runtime.Execute(runtime =>
    {
      using var source = fixture.Evaluate(
          "({ a: new ArrayBuffer(1), b: new ArrayBuffer(2) })",
          "scoped-dictionary-decode.js");
      var decoded = GeneratedScopedDictionaryCodec<ArrayBuffer, ArrayBufferCodec>.Decode(
          source.Ref,
          runtime,
          scope);

      Assert.Equal(1, decoded["a"].ByteLength);
      Assert.Equal(2, decoded["b"].ByteLength);
      Assert.Same(StringComparer.Ordinal, decoded.Comparer);
      return 0;
    });

    fixture.ResetCounters();
    scope.Dispose();
    fixture.WaitUntilIdle();

    Assert.Equal(2u, fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void ScopedNullableReferenceCodecRegistersNothingForNull()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var scope = new GeneratedConversionScope();
    var nullScope = new GeneratedConversionScope();

    fixture.Runtime.Execute(runtime =>
    {
      using var nullSource = fixture.Evaluate("null", "scoped-nullable-ref-null.js");
      using var listSource = fixture.Evaluate("[new ArrayBuffer(3)]", "scoped-nullable-ref-list.js");

      Assert.Null(NullableList.Decode(nullSource.Ref, runtime, nullScope));
      NullableList.Transfer(null, nullScope);
      using var encodedNull = NullableList.EncodeBorrowed(null, runtime);
      Assert.Equal(JavaScriptValueKind.Null, encodedNull.Kind);

      var decoded = NullableList.Decode(listSource.Ref, runtime, scope);
      Assert.NotNull(decoded);
      Assert.Equal(3, Assert.Single(decoded).ByteLength);
      NullableList.Transfer(decoded, scope);
      using var encoded = NullableList.EncodeBorrowed(decoded, runtime);
      using var global = runtime.Global();
      global.SetProperty("encodedNullableList", encoded);
      using var shape = fixture.Evaluate(
          "Array.isArray(encodedNullableList) && encodedNullableList[0].byteLength === 3",
          "scoped-nullable-ref-check.js");
      Assert.True(shape.AsBool());
      return 0;
    });

    // The null path registered nothing, so nothing was registered into the scope it used.
    nullScope.Dispose();
    fixture.ResetCounters();
    scope.Dispose();
    fixture.WaitUntilIdle();

    Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void ScopedNullableCodecRoundTripsValueStruct()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var scope = new GeneratedConversionScope();

    var slot = fixture.Runtime.Execute(runtime =>
    {
      using var nullSource = fixture.Evaluate("null", "scoped-nullable-null.js");
      using var slotSource = fixture.Evaluate("({ value: 'a' })", "scoped-nullable-slot.js");

      Assert.Null(GeneratedScopedNullableCodec<ValueSlot, ValueSlotCodec>.Decode(
          nullSource.Ref,
          runtime,
          scope));
      var decoded = GeneratedScopedNullableCodec<ValueSlot, ValueSlotCodec>.Decode(
          slotSource.Ref,
          runtime,
          scope);
      Assert.NotNull(decoded);
      Assert.Equal("a", decoded.Value.Value.AsString());

      GeneratedScopedNullableCodec<ValueSlot, ValueSlotCodec>.Transfer(decoded, scope);
      using var encodedSlot = GeneratedScopedNullableCodec<ValueSlot, ValueSlotCodec>
          .EncodeBorrowed(decoded, runtime);
      using var global = runtime.Global();
      global.SetProperty("encodedSlot", encodedSlot);
      using var slotValue = fixture.Evaluate("encodedSlot.value === 'a'", "scoped-nullable-slot-check.js");
      Assert.True(slotValue.AsBool());

      using var encodedNull = GeneratedScopedNullableCodec<ValueSlot, ValueSlotCodec>
          .EncodeBorrowed(null, runtime);
      Assert.Equal(JavaScriptValueKind.Null, encodedNull.Kind);
      GeneratedScopedNullableCodec<ValueSlot, ValueSlotCodec>.Transfer(null, scope);
      return decoded;
    });

    fixture.ResetCounters();
    scope.Dispose();
    fixture.WaitUntilIdle();

    Assert.NotNull(slot);
    Assert.Equal(1u, fixture.Counters.ReleasedValues);
  }

  [Fact]
  public void TransferredResultFactoryEncodesThenDisposesScope()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var scope = new GeneratedConversionScope();

    fixture.Runtime.Execute(runtime =>
    {
      using var source = fixture.Evaluate("'x'", "transferred-result-success.js");
      var leaf = JavaScriptValueCodec.Decode(source.Ref, runtime, scope);
      var result = GeneratedTransferredResult<JavaScriptValue, JavaScriptValueCodec>.Resolve(leaf, scope);

      fixture.ResetCounters();
      using var js = result.CreateValue(runtime);

      Assert.Equal("x", js.AsString());
      Assert.Equal(1u, fixture.Counters.ReleasedValues);

      result.Abandon();
      Assert.Equal(1u, fixture.Counters.ReleasedValues);
      return 0;
    });
  }

  [Fact]
  public void TransferredResultFactoryFailureStillDisposesScope()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var scope = new GeneratedConversionScope();

    fixture.Runtime.Execute(runtime =>
    {
      using var source = fixture.Evaluate("'x'", "transferred-result-failure-source.js");
      _ = JavaScriptValueCodec.Decode(source.Ref, runtime, scope);
      var alreadyDisposed = fixture.Evaluate("'y'", "transferred-result-failure-disposed.js");
      alreadyDisposed.Dispose();
      var result = GeneratedTransferredResult<JavaScriptValue, JavaScriptValueCodec>
          .Resolve(alreadyDisposed, scope);

      fixture.ResetCounters();
      Assert.Throws<ObjectDisposedException>(() => result.CreateValue(runtime));

      Assert.Equal(1u, fixture.Counters.ReleasedValues);
      result.Abandon();
      Assert.Equal(1u, fixture.Counters.ReleasedValues);
      return 0;
    });
  }

  [Fact]
  public void TransferredResultAbandonDisposesScope()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var scope = new GeneratedConversionScope();

    var result = fixture.Runtime.Execute(runtime =>
    {
      using var first = fixture.Evaluate("new ArrayBuffer(1)", "transferred-result-abandon-first.js");
      using var second = fixture.Evaluate("new ArrayBuffer(2)", "transferred-result-abandon-second.js");
      var firstBuffer = ArrayBufferCodec.Decode(first.Ref, runtime, scope);
      _ = ArrayBufferCodec.Decode(second.Ref, runtime, scope);
      return GeneratedTransferredResult<ArrayBuffer, ArrayBufferCodec>.Resolve(firstBuffer, scope);
    });

    fixture.ResetCounters();
    result.Abandon();
    fixture.WaitUntilIdle();
    Assert.Equal(2u, fixture.Counters.LongLivedArrayBuffersReleased);

    result.Abandon();
    fixture.WaitUntilIdle();
    Assert.Equal(2u, fixture.Counters.LongLivedArrayBuffersReleased);
  }

  private static JavaScriptValue TransferAndEncode<T, TCodec>(
      T value,
      JavaScriptRuntime runtime,
      GeneratedConversionScope scope)
      where TCodec : IGeneratedScopedCodec<T>
  {
    TCodec.Transfer(value, scope);
    return TCodec.EncodeBorrowed(value, runtime);
  }

  private readonly record struct ValueSlot(JavaScriptValue Value);

  private readonly struct ValueSlotCodec : IGeneratedScopedCodec<ValueSlot>
  {
    public static ValueSlot Decode(
        JavaScriptValueRef value,
        JavaScriptRuntime runtime,
        GeneratedConversionScope scope)
    {
      var obj = value.AsObject();
      return new ValueSlot(JavaScriptValueCodec.Decode(obj.GetProperty("value"), runtime, scope));
    }

    public static void Transfer(ValueSlot value, GeneratedConversionScope scope) =>
        JavaScriptValueCodec.Transfer(value.Value, scope);

    public static JavaScriptValue EncodeBorrowed(ValueSlot value, JavaScriptRuntime runtime)
    {
      using var obj = runtime.CreateObject();
      using var encoded = JavaScriptValueCodec.EncodeBorrowed(value.Value, runtime);
      obj.SetProperty("value", encoded);
      return obj.AsValue();
    }
  }
}
