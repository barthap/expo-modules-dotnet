using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

/// <summary>Converts JavaScript binary values to and from <c>Memory&lt;byte&gt;</c> slices.</summary>
/// <remarks>
/// Decoding creates independently owned managed storage. Encoding copies exactly the bytes in the
/// supplied memory slice.
/// </remarks>
public readonly struct MemoryByteCodec : IJavaScriptCodec<Memory<byte>>
{
  /// <summary>Decodes a JavaScript binary value into independently owned managed storage.</summary>
  public static Memory<byte> Decode(JavaScriptValueRef value, JavaScriptRuntime runtime) =>
      ByteArrayCodec.Decode(value, runtime);

  /// <summary>Decodes a JavaScript binary value into independently owned managed storage.</summary>
  public static Memory<byte> Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
      ByteArrayCodec.Decode(value, runtime);

  /// <summary>Encodes an exact copy of the supplied memory slice as a JavaScript binary value.</summary>
  public static JavaScriptValue Encode(Memory<byte> value, JavaScriptRuntime runtime) =>
      ArrayBufferCodec.EncodeCopy(value.Span, runtime);
}
