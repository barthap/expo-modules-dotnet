using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

/// <summary>Decodes and encodes a non-null JavaScript object as a string-keyed dictionary of scoped leaves.</summary>
/// <remarks>
/// This helper is for generated bindings, not ordinary module author code. Both dictionary
/// interfaces decode to <see cref="Dictionary{TKey, TValue}"/>.
/// </remarks>
/// <typeparam name="T">The property value type.</typeparam>
/// <typeparam name="TCodec">The scoped codec for each property value.</typeparam>
public readonly struct GeneratedScopedDictionaryCodec<T, TCodec>
    : IGeneratedScopedCodec<Dictionary<string, T>>,
      IGeneratedScopedCodec<IReadOnlyDictionary<string, T>>
    where TCodec : IGeneratedScopedCodec<T>
{
  /// <summary>Decodes a scoped JavaScript object into a new dictionary and registers each property's leaves.</summary>
  public static Dictionary<string, T> Decode(
      JavaScriptValueRef value,
      JavaScriptRuntime runtime,
      GeneratedConversionScope scope)
  {
    var obj = value.AsObject();
    var result = new Dictionary<string, T>(StringComparer.Ordinal);
    foreach (var name in obj.GetOwnPropertyNames())
    {
      var property = obj.GetProperty(name);
      result[name] = TCodec.Decode(property, runtime, scope);
    }

    return result;
  }

  /// <summary>Registers the leaves of every property value. A null dictionary registers nothing.</summary>
  public static void Transfer(IReadOnlyDictionary<string, T> value, GeneratedConversionScope scope)
  {
    if (value is null)
    {
      return;
    }

    foreach (var pair in value)
    {
      TCodec.Transfer(pair.Value, scope);
    }
  }

  /// <summary>Encodes a dictionary as a new JavaScript object without consuming any source leaf.</summary>
  public static JavaScriptValue EncodeBorrowed(
      IReadOnlyDictionary<string, T> value,
      JavaScriptRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(value);

    using var obj = runtime.CreateObject();
    foreach (var pair in value)
    {
      using var property = TCodec.EncodeBorrowed(pair.Value, runtime);
      obj.SetProperty(pair.Key, property);
    }

    return obj.AsValue();
  }

  static IReadOnlyDictionary<string, T> IGeneratedScopedCodec<IReadOnlyDictionary<string, T>>.Decode(
      JavaScriptValueRef value,
      JavaScriptRuntime runtime,
      GeneratedConversionScope scope) =>
      Decode(value, runtime, scope);

  static void IGeneratedScopedCodec<Dictionary<string, T>>.Transfer(
      Dictionary<string, T> value,
      GeneratedConversionScope scope) =>
      Transfer(value, scope);

  static JavaScriptValue IGeneratedScopedCodec<Dictionary<string, T>>.EncodeBorrowed(
      Dictionary<string, T> value,
      JavaScriptRuntime runtime) =>
      EncodeBorrowed(value, runtime);
}
