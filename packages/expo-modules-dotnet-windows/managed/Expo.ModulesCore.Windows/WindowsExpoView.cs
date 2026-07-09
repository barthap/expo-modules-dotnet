using System.Numerics;
using Microsoft.UI.Composition;
using WinRT;

namespace Expo.ModulesCore.Windows;

public abstract class WindowsExpoView
{
  public Compositor? Compositor { get; private set; }

  public Visual? CompositionVisual { get; private set; }

  protected abstract Visual CreateCompositionVisual(Compositor compositor);

  protected virtual void OnLayout(float width, float height)
  {
    if (CompositionVisual is not null)
    {
      CompositionVisual.Size = new Vector2(width, height);
    }
  }

  protected virtual void OnDisposeComposition()
  {
  }

  public nint InitializeComposition(nint compositorPtr)
  {
    Compositor = MarshalInterface<Compositor>.FromAbi(compositorPtr);
    CompositionVisual = CreateCompositionVisual(Compositor);
    return MarshalInspectable<object>.FromManaged(CompositionVisual);
  }

  public void UpdateLayout(float width, float height)
  {
    OnLayout(width, height);
  }

  public void DisposeComposition()
  {
    OnDisposeComposition();
    CompositionVisual = null;
    Compositor = null;
  }
}
