using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public static class ArrayBufferCodec
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

  public static JavaScriptValue EncodeCopy(ReadOnlySpan<byte> bytes, JavaScriptRuntime runtime)
  {
    using var buffer = ArrayBuffer.CopyFrom(bytes);
    return Encode(buffer, runtime);
  }
}
