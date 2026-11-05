namespace Expo.ModulesCore.Generator;

/// <summary>
/// Whether a codec can be a generic type argument or generated glue calls it directly. A
/// <c>ScopedOnly</c> codec implements only the scoped codec protocol.
/// </summary>
internal enum ExpoCodecShape
{
  JavaScriptCodec,
  ScopedOnly,
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

/// <summary>
/// Who releases the wrapper leaves of a value. A scoped resource holds at least one leaf, and
/// generated glue releases it through the invocation's conversion scope.
/// </summary>
internal enum ExpoResourceOwnership
{
  Borrowed,
  ScopedResource,
  ContextRetained,
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
    ExpoResourceOwnership ResourceOwnership);
