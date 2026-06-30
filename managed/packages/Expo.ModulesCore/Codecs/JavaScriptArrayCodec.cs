using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public static class JavaScriptArrayCodec<T, TCodec>
    where TCodec : IJavaScriptCodec<T>
{
  public static T[] DecodeToArray(JavaScriptValueRef value, JavaScriptRuntime runtime)
  {
    var array = value.AsArray();
    var length = checked((int)array.Length);
    var result = new T[length];

    for (var index = 0; index < length; index++)
    {
      var element = array.GetValue((uint)index);
      result[index] = TCodec.Decode(element, runtime);
    }

    return result;
  }

  public static JavaScriptValue Encode(IReadOnlyList<T> values, JavaScriptRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(values);

    using var array = runtime.CreateArray((uint)values.Count);
    for (var index = 0; index < values.Count; index++)
    {
      using var element = TCodec.Encode(values[index], runtime);
      array.SetValue((uint)index, element);
    }
    return array.AsValue();
  }
}
