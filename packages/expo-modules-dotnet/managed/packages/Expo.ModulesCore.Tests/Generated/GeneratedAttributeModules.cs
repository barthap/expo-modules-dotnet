using System.Collections.Generic;
using System.Linq;

namespace Expo.ModulesCore.Tests.Generated;

[ExpoModule("GeneratedMath")]
public sealed partial class GeneratedMathModule
{
  public double? LastNullable { get; private set; }

  [JS("add")]
  public double Add(double a, double b) => a + b;

  [JS]
  public double AddOneWhen(double value, bool shouldAddOne) =>
      shouldAddOne ? value + 1.0 : value;

  [JS]
  public void StoreNullable(double? value)
  {
    LastNullable = value;
  }

  [JS]
  public void StoreNullableWithDefault(double? value = 42.0)
  {
    LastNullable = value;
  }

  [JS]
  public double? ReadNullable() => LastNullable;
}

[ExpoModule]
public sealed partial class GeneratedTextModule
{
  [JS("greet")]
  public string Greet(string name) => $"Hello, {name}";
}

[ExpoModule("GeneratedArray")]
public sealed partial class GeneratedArrayModule
{
  [JS("sum")]
  public double Sum(IReadOnlyList<double> values) => values.Sum();

  [JS("labels")]
  public IReadOnlyList<string> Labels() => ["one", "two"];
}
