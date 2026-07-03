using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public static class JavaScriptDictionaryCodec<T, TCodec>
    where TCodec : IJavaScriptCodec<T>
{
  public static Dictionary<string, T> DecodeToDictionary(
      JavaScriptValueRef value,
      JavaScriptRuntime runtime)
  {
    var obj = value.AsObject();
    var result = new Dictionary<string, T>(StringComparer.Ordinal);
    foreach (var name in obj.GetOwnPropertyNames())
    {
      var property = obj.GetProperty(name);
      result[name] = TCodec.Decode(property, runtime);
    }

    return result;
  }

  public static Dictionary<string, T> DecodeToDictionary(
      JavaScriptValue value,
      JavaScriptRuntime runtime)
  {
    using var obj = value.AsObject();
    var result = new Dictionary<string, T>(StringComparer.Ordinal);
    foreach (var name in obj.GetOwnPropertyNames())
    {
      using var property = obj.GetProperty(name);
      result[name] = TCodec.Decode(property, runtime);
    }

    return result;
  }

  public static JavaScriptValue Encode(
      IReadOnlyDictionary<string, T> values,
      JavaScriptRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(values);

    using var obj = runtime.CreateObject();
    foreach (var pair in values)
    {
      using var value = TCodec.Encode(pair.Value, runtime);
      obj.SetProperty(pair.Key, value);
    }

    return obj.AsValue();
  }
}
