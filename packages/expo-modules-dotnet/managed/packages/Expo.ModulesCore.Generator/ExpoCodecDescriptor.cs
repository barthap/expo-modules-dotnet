namespace Expo.ModulesCore.Generator;

/// <summary>Whether a codec can be a generic type argument or generated glue calls it directly.</summary>
internal enum ExpoCodecShape
{
  JavaScriptCodec,
  StaticHelper,
}

/// <summary>Whether a codec has an <c>Encode</c> method.</summary>
internal enum ExpoCodecCapabilities
{
  DecodeOnly,
  DecodeAndEncode,
}

/// <summary>The decode method that boundary glue calls.</summary>
internal enum ExpoDecodeOperation
{
  Decode,
  DecodeToArray,
  DecodeToDictionary,
}

/// <summary>Whether decode and encode take the current runtime context.</summary>
internal enum ExpoContextRequirement
{
  None,
  DotnetRuntimeContext,
}

/// <summary>Who releases a decoded value. Generated glue disposes only invocation-owned values.</summary>
internal enum ExpoDecodedOwnership
{
  Borrowed,
  InvocationOwned,
  ContextRetained,
}

/// <summary>How a returned value is encoded.</summary>
internal enum ExpoEncodeOperation
{
  Standard,
  TransferJavaScriptValue,
  EncodeThenDisposeArrayBuffer,
}

/// <summary>
/// A resolved codec: the emitted C# type plus the conversion policy facts. Each resolver branch
/// sets the facts where it selects the codec, so policy never depends on the expression text.
/// </summary>
internal sealed record ExpoCodecDescriptor(
    string Expression,
    ExpoCodecShape Shape,
    ExpoCodecCapabilities Capabilities,
    ExpoDecodeOperation DecodeOperation,
    ExpoContextRequirement ContextRequirement,
    ExpoDecodedOwnership DecodedOwnership,
    ExpoEncodeOperation EncodeOperation);
