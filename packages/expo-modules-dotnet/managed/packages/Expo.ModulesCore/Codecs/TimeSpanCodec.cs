using System.Globalization;
using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public readonly struct TimeSpanCodec : IJavaScriptCodec<TimeSpan>
{
  public static TimeSpan Decode(JavaScriptValueRef value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
      TimeSpan.Parse(value.AsString(), CultureInfo.InvariantCulture);

  public static TimeSpan Decode(JavaScriptValue value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
      TimeSpan.Parse(value.AsString(), CultureInfo.InvariantCulture);

  public static JavaScriptValue Encode(TimeSpan value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
      runtime.CreateString(value.ToString("c", CultureInfo.InvariantCulture));
}
