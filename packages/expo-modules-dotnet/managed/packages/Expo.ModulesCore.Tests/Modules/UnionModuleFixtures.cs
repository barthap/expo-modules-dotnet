using System.Globalization;
using Expo.JSI;
using Expo.ModulesCore.Tests.ExternalTypes;

namespace Expo.ModulesCore.Tests.Modules;

[Union]
public abstract record NonceInput
{
  public sealed record Base64(string Value) : NonceInput;
  public sealed record Bytes(byte[] Value) : NonceInput;
  public sealed record Length(int Value) : NonceInput;
}

// The integer case comes first, so it takes only whole numbers that int holds.
[Union]
public abstract record WholeOrFraction
{
  public sealed record Whole(int Value) : WholeOrFraction;
  public sealed record Fraction(double Value) : WholeOrFraction;
}

// The int case is the last number case, so it decodes like NumberCodec<int>.
[Union]
public abstract record TextOrCount
{
  public sealed record Text(string Value) : TextOrCount;
  public sealed record Count(int Value) : TextOrCount;
}

[Union]
public abstract record AddressOrText
{
  public sealed record Address(Uri Value) : AddressOrText;
  public sealed record Text(string Value) : AddressOrText;
}

public sealed record UnionCircle(double X, double Y, double Radius);

public sealed record UnionPoint(double X, double Y);

[Union]
public abstract record UnionShape
{
  public sealed record Round(UnionCircle Value) : UnionShape;
  public sealed record Dot(UnionPoint Value) : UnionShape;
}

public sealed record UnionEntry(string Name);

[Union]
public abstract record ItemsOrNamed
{
  public sealed record Items(IReadOnlyList<int> Value) : ItemsOrNamed;
  public sealed record Named(UnionEntry Value) : ItemsOrNamed;
}

[Union]
public abstract record InnerChoice
{
  public sealed record Text(string Value) : InnerChoice;
  public sealed record Count(int Value) : InnerChoice;
}

[Union]
public abstract record OuterChoice
{
  public sealed record Flag(bool Value) : OuterChoice;
  public sealed record Nested(InnerChoice Value) : OuterChoice;
}

public sealed record UnionSourceOptions(string Path);

[Union]
public abstract record UnionSource
{
  public sealed record Text(string Value) : UnionSource;
  public sealed record Options(UnionSourceOptions Value) : UnionSource;
}

[Union]
public abstract record ItemsOrFlag
{
  public sealed record Items(IReadOnlyList<int> Value) : ItemsOrFlag;
  public sealed record Flag(bool Value) : ItemsOrFlag;
}

[Union]
public abstract record AnyObject
{
  public sealed record Bag(Dictionary<string, JavaScriptValue> Value) : AnyObject;
  public sealed record Text(string Value) : AnyObject;
}

[Union]
public abstract record ExternalChoice
{
  public sealed record Foo(ExternalRecord Bar) : ExternalChoice;
  public sealed record Text(string Value) : ExternalChoice;
}

[ExpoModule("UnionScenarios")]
public sealed partial class UnionScenariosModule
{
  public static int Calls;

  [JS]
  public string DescribeNonce(NonceInput value)
  {
    Calls++;
    return value switch
    {
      NonceInput.Base64(var text) => $"base64:{text}",
      NonceInput.Bytes(var bytes) => $"bytes:{string.Join(",", bytes)}",
      NonceInput.Length(var length) => $"length:{length}",
      _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
  }

  [JS]
  public NonceInput MakeBase64() => new NonceInput.Base64("a");

  [JS]
  public NonceInput MakeBytes() => new NonceInput.Bytes(new byte[] { 1, 2 });

  [JS]
  public NonceInput MakeLength() => new NonceInput.Length(3);

  [JS]
  public NonceInput MakeNullNonce() => null!;

  [JS]
  public string DescribeNumber(WholeOrFraction value)
  {
    Calls++;
    return value switch
    {
      WholeOrFraction.Whole(var whole) => $"whole:{whole}",
      WholeOrFraction.Fraction(var fraction) => $"fraction:{fraction.ToString(CultureInfo.InvariantCulture)}",
      _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
  }

  [JS]
  public string DescribeLone(TextOrCount value)
  {
    Calls++;
    return value switch
    {
      TextOrCount.Text(var text) => $"text:{text}",
      TextOrCount.Count(var count) => $"count:{count}",
      _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
  }

  [JS]
  public string DescribeAddress(AddressOrText value)
  {
    Calls++;
    return value switch
    {
      AddressOrText.Address(var address) => $"address:{address.AbsoluteUri}",
      AddressOrText.Text(var text) => $"text:{text}",
      _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
  }

  [JS]
  public string DescribeShape(UnionShape value)
  {
    Calls++;
    return value switch
    {
      UnionShape.Round(var circle) => $"round:{circle.Radius.ToString(CultureInfo.InvariantCulture)}",
      UnionShape.Dot(var point) =>
          $"dot:{point.X.ToString(CultureInfo.InvariantCulture)},{point.Y.ToString(CultureInfo.InvariantCulture)}",
      _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
  }

  [JS]
  public string DescribeItems(ItemsOrNamed value)
  {
    Calls++;
    return value switch
    {
      ItemsOrNamed.Items(var items) => $"items:{string.Join(",", items)}",
      ItemsOrNamed.Named(var entry) => $"named:{entry.Name}",
      _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
  }

  [JS]
  public string DescribeNested(OuterChoice value)
  {
    Calls++;
    return value switch
    {
      OuterChoice.Flag(var flag) => $"flag:{(flag ? "true" : "false")}",
      OuterChoice.Nested(InnerChoice.Text(var text)) => $"text:{text}",
      OuterChoice.Nested(InnerChoice.Count(var count)) => $"count:{count}",
      _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
  }

  [JS]
  public string DescribeSource(UnionSource value)
  {
    Calls++;
    return value switch
    {
      UnionSource.Text(var text) => $"text:{text}",
      UnionSource.Options(var options) => $"options:{options.Path}",
      _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
  }

  [JS]
  public string DescribeOptionalSource(UnionSource? value) => value is null ? "null" : DescribeSource(value);

  [JS]
  public UnionSource? MaybeSource() => null;

  [JS]
  public string DescribeFlags(ItemsOrFlag value)
  {
    Calls++;
    return value switch
    {
      ItemsOrFlag.Items(var items) => $"items:{string.Join(",", items)}",
      ItemsOrFlag.Flag(var flag) => $"flag:{(flag ? "true" : "false")}",
      _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
  }

  [JS]
  public string DescribeObject(AnyObject value)
  {
    Calls++;
    return value switch
    {
      AnyObject.Bag(var bag) => $"bag:{bag.Count}",
      AnyObject.Text(var text) => $"text:{text}",
      _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
  }

  [JS]
  public string DescribeExternal(ExternalChoice value)
  {
    Calls++;
    return value switch
    {
      ExternalChoice.Foo(var foo) => $"foo:{foo.Name}:{foo.Count}",
      ExternalChoice.Text(var text) => $"text:{text}",
      _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
  }

  [JS]
  public ExternalChoice MakeExternal() => new ExternalChoice.Foo(new ExternalRecord("a", 2));
}
