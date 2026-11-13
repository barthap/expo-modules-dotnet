using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

/// <summary>Decodes and encodes a non-null JavaScript object as a string-keyed dictionary.</summary>
/// <remarks>
/// This codec does not handle nullish input. A nullable dictionary container uses
/// <see cref="NullableDictionaryCodec{T, TCodec}"/> or
/// <see cref="NullableReadOnlyDictionaryCodec{T, TCodec}"/>. Generated boundary glue calls
/// <c>DecodeToDictionary</c>, while composed positions use the <see cref="IJavaScriptCodec{T}"/>
/// implementations. Both implementations decode to <see cref="Dictionary{TKey, TValue}"/>.
/// </remarks>
/// <typeparam name="T">The property value type.</typeparam>
/// <typeparam name="TCodec">The codec for each property value.</typeparam>
public readonly struct JavaScriptDictionaryCodec<T, TCodec>
    : IJavaScriptCodec<Dictionary<string, T>>,
      IJavaScriptCodec<IReadOnlyDictionary<string, T>>
    where TCodec : IJavaScriptCodec<T>
{
  /// <summary>Decodes a scoped JavaScript object into a new dictionary.</summary>
  public static Dictionary<string, T> DecodeToDictionary(
      JavaScriptValueRef value,
      JavaScriptRuntime runtime,
      DotnetRuntimeContext context)
  {
    var obj = value.AsObject();
    var result = new Dictionary<string, T>(StringComparer.Ordinal);

    try
    {
      foreach (var name in obj.GetOwnPropertyNames())
      {
        var property = obj.GetProperty(name);
        result[name] = TCodec.Decode(property, runtime, context);
      }
    }
    catch
    {
      Release(result);
      throw;
    }

    return result;
  }

  /// <summary>
  /// Decodes an owned JavaScript object into a new dictionary. The method disposes the object
  /// wrapper and each property wrapper it creates, and leaves <paramref name="value"/> to the caller.
  /// </summary>
  public static Dictionary<string, T> DecodeToDictionary(
      JavaScriptValue value,
      JavaScriptRuntime runtime,
      DotnetRuntimeContext context)
  {
    using var obj = value.AsObject();
    var result = new Dictionary<string, T>(StringComparer.Ordinal);

    try
    {
      foreach (var name in obj.GetOwnPropertyNames())
      {
        using var property = obj.GetProperty(name);
        result[name] = TCodec.Decode(property, runtime, context);
      }
    }
    catch
    {
      Release(result);
      throw;
    }

    return result;
  }

  /// <summary>
  /// Releases every value of <paramref name="values"/> through <typeparamref name="TCodec"/> and
  /// attempts all of them before it throws one <see cref="AggregateException"/>. A null dictionary
  /// releases nothing.
  /// </summary>
  public static void Release(IReadOnlyDictionary<string, T> values)
  {
    if (values is null)
    {
      return;
    }

    List<Exception>? failures = null;
    foreach (var pair in values)
    {
      CodecRelease.Release<T, TCodec>(pair.Value, ref failures);
    }

    CodecRelease.ThrowIfFailed(failures);
  }

  /// <summary>Decodes a scoped JavaScript object into a new dictionary.</summary>
  public static Dictionary<string, T> Decode(JavaScriptValueRef value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
      DecodeToDictionary(value, runtime, context);

  /// <summary>Decodes an owned JavaScript object into a new dictionary.</summary>
  public static Dictionary<string, T> Decode(JavaScriptValue value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
      DecodeToDictionary(value, runtime, context);

  /// <summary>Encodes a string-keyed dictionary as a new JavaScript object.</summary>
  public static JavaScriptValue Encode(
      IReadOnlyDictionary<string, T> values,
      JavaScriptRuntime runtime,
      DotnetRuntimeContext context)
  {
    ArgumentNullException.ThrowIfNull(values);

    using var obj = runtime.CreateObject();
    foreach (var pair in values)
    {
      using var value = TCodec.Encode(pair.Value, runtime, context);
      obj.SetProperty(pair.Key, value);
    }

    return obj.AsValue();
  }

  static IReadOnlyDictionary<string, T> IJavaScriptCodec<IReadOnlyDictionary<string, T>>.Decode(
      JavaScriptValueRef value,
      JavaScriptRuntime runtime,
      DotnetRuntimeContext context) =>
      Decode(value, runtime, context);

  static IReadOnlyDictionary<string, T> IJavaScriptCodec<IReadOnlyDictionary<string, T>>.Decode(
      JavaScriptValue value,
      JavaScriptRuntime runtime,
      DotnetRuntimeContext context) =>
      Decode(value, runtime, context);

  static JavaScriptValue IJavaScriptCodec<Dictionary<string, T>>.Encode(
      Dictionary<string, T> value,
      JavaScriptRuntime runtime,
      DotnetRuntimeContext context) =>
      Encode(value, runtime, context);

  static void IJavaScriptCodec<Dictionary<string, T>>.Release(Dictionary<string, T> value) =>
      Release(value);
}
