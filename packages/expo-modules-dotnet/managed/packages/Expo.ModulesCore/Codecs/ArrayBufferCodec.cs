using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public readonly struct ArrayBufferCodec : IJavaScriptCodec<ArrayBuffer>
{
  public static ArrayBuffer Decode(JavaScriptValueRef value, JavaScriptRuntime runtime, DotnetRuntimeContext context)
  {
    var arrayBuffer = value.AsArrayBuffer();
    if (arrayBuffer.TryGetMutableBuffer() is { } native)
    {
      return new ArrayBuffer(native);
    }
    return new ArrayBuffer(arrayBuffer.Retain());
  }

  public static ArrayBuffer Decode(JavaScriptValue value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
      Decode(value.Ref, runtime, context);

  public static JavaScriptValue Encode(ArrayBuffer value, JavaScriptRuntime runtime, DotnetRuntimeContext context)
  {
    ArgumentNullException.ThrowIfNull(value);
    return value.Encode(runtime);
  }

  /// <summary>Disposes <paramref name="value"/>. A null buffer does nothing.</summary>
  public static void Release(ArrayBuffer value) => value?.Dispose();

  public static JavaScriptValue EncodeCopy(ReadOnlySpan<byte> bytes, JavaScriptRuntime runtime)
  {
    using var buffer = ArrayBuffer.CopyFrom(bytes);
    return buffer.Encode(runtime);
  }
}
