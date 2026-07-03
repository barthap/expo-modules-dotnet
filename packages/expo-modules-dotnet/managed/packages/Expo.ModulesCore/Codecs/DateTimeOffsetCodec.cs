using System.Globalization;
using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public readonly struct DateTimeOffsetCodec : IJavaScriptCodec<DateTimeOffset>
{
  public static DateTimeOffset Decode(JavaScriptValueRef value, JavaScriptRuntime runtime) =>
      DateTimeOffset.Parse(value.AsString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

  public static DateTimeOffset Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
      DateTimeOffset.Parse(value.AsString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

  public static JavaScriptValue Encode(DateTimeOffset value, JavaScriptRuntime runtime) =>
      runtime.CreateString(value.ToString("O", CultureInfo.InvariantCulture));
}
