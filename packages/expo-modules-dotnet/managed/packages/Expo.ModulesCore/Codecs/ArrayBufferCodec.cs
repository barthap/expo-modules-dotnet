using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public readonly struct ArrayBufferCodec : IJavaScriptCodec<ArrayBuffer>, IGeneratedScopedCodec<ArrayBuffer>
{
  public static ArrayBuffer Decode(JavaScriptValueRef value, JavaScriptRuntime runtime)
  {
    var arrayBuffer = value.AsArrayBuffer();
    if (arrayBuffer.TryGetMutableBuffer() is { } native)
    {
      return new ArrayBuffer(native);
    }
    return new ArrayBuffer(arrayBuffer.Retain());
  }

  public static ArrayBuffer Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
      Decode(value.Ref, runtime);

  public static JavaScriptValue Encode(ArrayBuffer value, JavaScriptRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(value);
    return value.Encode(runtime);
  }

  /// <summary>Disposes <paramref name="value"/>. A null buffer does nothing.</summary>
  public static void Release(ArrayBuffer value) => value?.Dispose();

  public static JavaScriptValue EncodeCopy(ReadOnlySpan<byte> bytes, JavaScriptRuntime runtime)
  {
    using var buffer = ArrayBuffer.CopyFrom(bytes);
    return Encode(buffer, runtime);
  }

  /// <summary>Decodes a scoped buffer and registers the new lease in <paramref name="scope"/>.</summary>
  /// <remarks>This helper is for generated bindings, not ordinary module author code.</remarks>
  public static ArrayBuffer Decode(
      JavaScriptValueRef value,
      JavaScriptRuntime runtime,
      GeneratedConversionScope scope)
  {
    var result = Decode(value, runtime);
    try
    {
      scope.Register(result);
    }
    catch
    {
      result.Dispose();
      throw;
    }

    return result;
  }

  /// <summary>Registers <paramref name="value"/> in <paramref name="scope"/>. A null buffer registers nothing.</summary>
  /// <remarks>This helper is for generated bindings, not ordinary module author code.</remarks>
  public static void Transfer(ArrayBuffer value, GeneratedConversionScope scope)
  {
    if (value is not null)
    {
      scope.Register(value);
    }
  }

  /// <summary>Encodes a buffer as a new JavaScript value without a byte copy and leaves <paramref name="value"/> usable.</summary>
  /// <remarks>This helper is for generated bindings, not ordinary module author code.</remarks>
  public static JavaScriptValue EncodeBorrowed(ArrayBuffer value, JavaScriptRuntime runtime) =>
      Encode(value, runtime);
}
