namespace HermesConsoleApp;

internal sealed class MathModule
{
  public double Add(double value, bool shouldAddOne)
  {
    return shouldAddOne ? value + 1.0 : value;
  }
}

internal sealed class TextModule
{
  public string Greet(string name)
  {
    return $"Hello, {name}";
  }
}
