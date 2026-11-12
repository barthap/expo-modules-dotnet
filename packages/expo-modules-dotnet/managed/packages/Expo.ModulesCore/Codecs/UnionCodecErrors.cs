namespace Expo.ModulesCore.Codecs;

/// <summary>Errors that generated union codecs throw.</summary>
/// <remarks>
/// This helper is for generated bindings, not ordinary module author code. It keeps the runtime type
/// lookup out of generated text, which uses no reflection.
/// </remarks>
public static class UnionCodecErrors
{
  /// <summary>
  /// Creates the error for a union instance whose type is not one of the cases, which only a type
  /// from another assembly can be.
  /// </summary>
  public static InvalidOperationException NotACase<TUnion>(TUnion value, string unionTypeName)
      where TUnion : class
  {
    ArgumentNullException.ThrowIfNull(value);
    ArgumentNullException.ThrowIfNull(unionTypeName);
    var type = value.GetType();
    return new InvalidOperationException(
        $"Type '{type.FullName ?? type.Name}' is not a case of union '{unionTypeName}'.");
  }
}
