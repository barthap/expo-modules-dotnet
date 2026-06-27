namespace HostFxrJSIProof;

internal sealed class MathModule
{
  public double Add(double value, bool shouldAddOne)
  {
    return shouldAddOne ? value + 1.0 : value;
  }
}
