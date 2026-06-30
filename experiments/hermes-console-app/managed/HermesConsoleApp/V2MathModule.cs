using Expo.ModulesCore;

namespace HermesConsoleApp;

[ExpoModule("V2Math")]
internal sealed partial class V2MathModule
{
  [JS("add")]
  public double Add(double a, double b)
  {
    return a + b;
  }
}
