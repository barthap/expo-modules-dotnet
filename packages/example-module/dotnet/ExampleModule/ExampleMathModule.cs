using Expo.ModulesCore;

namespace ExampleModule;

[ExpoModule("ExampleModule")]
public sealed partial class ExampleMathModule
{
  [JS("add")]
  public double Add(double a, double b)
  {
    return a + b;
  }
}
