using Expo.ModulesCore;

namespace ExpoMobileV2Module;

[ExpoModule("ExpoCSharpV2")]
public sealed partial class MobileV2MathModule
{
  [JS("add")]
  public double Add(double a, double b)
  {
    return a + b;
  }
}
