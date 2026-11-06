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

/// <summary>
/// Who releases the wrapper leaves of a value. A value that holds leaves has at least one, and
/// generated glue releases it through its codec's <c>Release</c>.
/// </summary>
internal enum ExpoResourceOwnership
{
  Borrowed,
  HoldsLeaves,
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
    ExpoResourceOwnership ResourceOwnership)
{
  /// <summary>
  /// The codec's value type with its nullable annotation, which generated glue uses to declare
  /// leaf-bearing locals and as the type argument of <c>CodecRelease.Release</c>.
  /// </summary>
  public string ValueTypeName { get; init; } = string.Empty;
}
