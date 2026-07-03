using System.Globalization;
using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public readonly struct NumberEnumCodec<TEnum> : IJavaScriptCodec<TEnum>
    where TEnum : struct, Enum
{
  public static TEnum Decode(JavaScriptValueRef value, JavaScriptRuntime runtime) =>
      FromNumber(value.AsDouble());

  public static TEnum Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
      FromNumber(value.AsDouble());

  public static JavaScriptValue Encode(TEnum value, JavaScriptRuntime runtime) =>
      runtime.CreateNumber(Convert.ToDouble(value, CultureInfo.InvariantCulture));

  private static TEnum FromNumber(double value)
  {
    var underlyingType = Enum.GetUnderlyingType(typeof(TEnum));
    var underlyingValue = Convert.ChangeType(value, underlyingType, CultureInfo.InvariantCulture);
    var converted = (TEnum)Enum.ToObject(typeof(TEnum), underlyingValue);
    if (Enum.IsDefined(typeof(TEnum), converted))
    {
      return converted;
    }

    throw new ArgumentException($"'{value}' is not a valid {typeof(TEnum).FullName} value.");
  }
}
