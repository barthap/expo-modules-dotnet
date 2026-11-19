using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public readonly struct ByteArrayCodec : IJavaScriptCodec<byte[]>
{
  public static byte[] Decode(JavaScriptValueRef value, JavaScriptRuntime runtime, DotnetRuntimeContext context)
  {
    using var buffer = ArrayBufferCodec.Decode(value, runtime, context);
    return buffer.ToArray();
  }

  public static byte[] Decode(JavaScriptValue value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
      Decode(value.Ref, runtime, context);

  public static JavaScriptValue Encode(byte[] value, JavaScriptRuntime runtime, DotnetRuntimeContext context)
  {
    ArgumentNullException.ThrowIfNull(value);
    using var buffer = ArrayBuffer.CopyFrom(value);
    return ArrayBufferCodec.Encode(buffer, runtime, context);
  }
}
