using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

public sealed class JavaScriptValueTests
{
  [Theory]
  [InlineData("undefined", JavaScriptValueKind.Undefined)]
  [InlineData("null", JavaScriptValueKind.Null)]
  [InlineData("true", JavaScriptValueKind.Bool)]
  [InlineData("42.5", JavaScriptValueKind.Number)]
  [InlineData("'hello'", JavaScriptValueKind.String)]
  [InlineData("({})", JavaScriptValueKind.Object)]
  [InlineData("(function () {})", JavaScriptValueKind.Function)]
  [InlineData("new ArrayBuffer(4)", JavaScriptValueKind.ArrayBuffer)]
  public void EvaluatedJavaScriptValuesReportKind(string source, JavaScriptValueKind expected)
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var value = fixture.Evaluate(source, "value-kind.js");

    Assert.Equal(expected, value.Kind);
  }

  [Theory]
  [InlineData("42", "bool", "Value is not a boolean.")]
  [InlineData("'hello'", "number", "Value is not a number.")]
  [InlineData("true", "string", "Value is not a string.")]
  [InlineData("42", "object", "Value is not an object.")]
  [InlineData("42", "array", "Value is not an array.")]
  public void WrongTypeConversionThrowsNativeJsiError(
      string source,
      string conversion,
      string expectedMessage
  )
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var value = fixture.Evaluate(source, "wrong-type.js");

    var exception = Assert.Throws<InvalidOperationException>(
        () => ReadWithConversion(value, conversion)
    );
    Assert.Contains(expectedMessage, exception.Message);
  }

  [Fact]
  public void UsingDisposedValueThrowsObjectDisposedException()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var value = fixture.Runtime.CreateNumber(1);

    value.Dispose();

    Assert.Throws<ObjectDisposedException>(() => value.Kind);
  }

  private static void ReadWithConversion(JavaScriptValue value, string conversion)
  {
    switch (conversion)
    {
      case "bool":
        value.AsBool();
        break;
      case "number":
        value.AsDouble();
        break;
      case "string":
        value.AsString();
        break;
      case "object":
        using (value.AsObject())
        {
        }
        break;
      case "array":
        using (value.AsArray())
        {
        }
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(conversion));
    }
  }
}
