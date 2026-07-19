using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public static class ByteArrayCodec
{
  public static byte[] Decode(JavaScriptValueRef value, JavaScriptRuntime runtime)
  {
    using var buffer = ArrayBufferCodec.Decode(value, runtime);
    return buffer.ToArray();
  }

  public static byte[] Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
      Decode(value.Ref, runtime);

  public static JavaScriptValue Encode(byte[] value, JavaScriptRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(value);
    using var buffer = ArrayBuffer.CopyFrom(value);
    return ArrayBufferCodec.Encode(buffer, runtime);
  }
}
