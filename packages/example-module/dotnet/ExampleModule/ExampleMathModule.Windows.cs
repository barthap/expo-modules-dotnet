using Expo.ModulesCore;

namespace ExampleModule;

[View("ExampleColorBox", typeof(ExampleColorBoxView))]
public sealed partial class ExampleMathModule
{
  [Prop("color")]
  public void SetColor(ExampleColorBoxView view, string? color)
  {
    view.Color = color;
    view.CommitProps();
  }
}
