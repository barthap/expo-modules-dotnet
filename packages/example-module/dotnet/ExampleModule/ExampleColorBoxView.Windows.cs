using System.Numerics;
using Expo.ModulesCore.Windows;
using Microsoft.UI.Composition;
using UiColor = Windows.UI.Color;

namespace ExampleModule;

public sealed class ExampleColorBoxView : WindowsExpoView
{
  private CompositionColorBrush? brush;
  private SpriteVisual? visual;

  public string? Color { get; set; }

  public void CommitProps()
  {
    if (brush is not null)
    {
      brush.Color = ParseColor(Color);
    }
  }

  protected override Visual CreateCompositionVisual(Compositor compositor)
  {
    brush = compositor.CreateColorBrush(ParseColor(Color));
    visual = compositor.CreateSpriteVisual();
    visual.Brush = brush;
    return visual;
  }

  protected override void OnLayout(float width, float height)
  {
    if (visual is not null)
    {
      visual.Size = new Vector2(width, height);
    }
  }

  protected override void OnDisposeComposition()
  {
    if (visual is not null)
    {
      visual.Brush = null;
    }

    visual = null;
    brush = null;
  }

  private static UiColor ParseColor(string? value) =>
    value?.ToLowerInvariant() switch
    {
      "blue" => UiColor.FromArgb(255, 37, 99, 235),
      "green" => UiColor.FromArgb(255, 22, 163, 74),
      "red" => UiColor.FromArgb(255, 220, 38, 38),
      "yellow" => UiColor.FromArgb(255, 234, 179, 8),
      "purple" => UiColor.FromArgb(255, 147, 51, 234),
      _ => UiColor.FromArgb(255, 14, 165, 233),
    };
}
