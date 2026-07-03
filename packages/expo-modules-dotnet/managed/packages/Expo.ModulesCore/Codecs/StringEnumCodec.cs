using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public readonly struct StringEnumCodec<TEnum> : IJavaScriptCodec<TEnum>
    where TEnum : struct, Enum
{
  public static TEnum Decode(JavaScriptValueRef value, JavaScriptRuntime runtime) =>
      Parse(value.AsString());

  public static TEnum Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
      Parse(value.AsString());

  public static JavaScriptValue Encode(TEnum value, JavaScriptRuntime runtime) =>
      runtime.CreateString(value.ToString());

  private static TEnum Parse(string value)
  {
    if (Enum.TryParse<TEnum>(value, ignoreCase: false, out var result) &&
        Enum.IsDefined(typeof(TEnum), result))
    {
      return result;
    }

    throw new ArgumentException($"'{value}' is not a valid {typeof(TEnum).FullName} value.");
  }
}
