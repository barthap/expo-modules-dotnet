using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

/// <summary>Reports the one JavaScript value kind that a codec decodes.</summary>
/// <remarks>
/// Union codecs read the kind of a JavaScript value once and pick the member codec that reports
/// the same kind. This helper is for generated bindings, not ordinary module author code.
/// </remarks>
public interface IJavaScriptValueKindCodec
{
  /// <summary>Gets the JavaScript value kind that this codec decodes.</summary>
  static abstract JavaScriptValueKind Kind { get; }
}
