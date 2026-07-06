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

    fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Evaluate(source, "value-kind.js");
      Assert.Equal(expected, value.Kind);
      return true;
    });
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

    fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Evaluate(source, "wrong-type.js");
      var exception = Assert.Throws<InvalidOperationException>(
          () => ReadWithConversion(value, conversion)
      );
      Assert.Contains(expectedMessage, exception.Message);
      return true;
    });
  }

  [Fact]
  public void NativeErrorMessageBufferIsReleasedAfterManagedException()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();

    fixture.Runtime.Execute(runtime =>
    {
      using var value = runtime.CreateNumber(1);
      var exception = Assert.Throws<InvalidOperationException>(() => value.AsBool());

      Assert.Contains("Value is not a boolean.", exception.Message);
      Assert.True(fixture.Counters.ReleasedErrors >= 1);
      return true;
    });
  }

  [Fact]
  public void UsingDisposedValueThrowsObjectDisposedException()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      var value = runtime.CreateNumber(1);
      value.Dispose();

      Assert.Throws<ObjectDisposedException>(() => value.Kind);
      return true;
    });
  }

  [Fact]
  public void StrictEqualsUsesJavaScriptIdentity()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var left = fixture.Evaluate("globalThis.__same = {}; globalThis.__same", "strict-equals-left.js");
      using var same = fixture.Evaluate("globalThis.__same", "strict-equals-same.js");
      using var different = fixture.Evaluate("({})", "strict-equals-different.js");

      Assert.True(runtime.StrictEquals(left, same));
      Assert.False(runtime.StrictEquals(left, different));
      return true;
    });
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
