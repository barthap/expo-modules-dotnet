using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

public sealed class JavaScriptPrimitiveTests
{
  [Fact]
  public void CreateNumberRoundTrips()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var value = runtime.CreateNumber(42.5);
      Assert.Equal(JavaScriptValueKind.Number, value.Kind);
      Assert.Equal(42.5, value.AsDouble());
      return true;
    });
  }

  [Fact]
  public void CreateBoolTrueRoundTrips()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var value = runtime.CreateBool(true);
      Assert.Equal(JavaScriptValueKind.Bool, value.Kind);
      Assert.True(value.AsBool());
      return true;
    });
  }

  [Fact]
  public void CreateBoolFalseRoundTrips()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var value = runtime.CreateBool(false);
      Assert.Equal(JavaScriptValueKind.Bool, value.Kind);
      Assert.False(value.AsBool());
      return true;
    });
  }

  [Fact]
  public void NumberAndBoolCreationUsePrimitiveValueAbi()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.ResetCounters();
    fixture.Runtime.Execute(runtime =>
    {
      using var number = runtime.CreateNumber(42.5);
      using var trueValue = runtime.CreateBool(true);
      using var falseValue = runtime.CreateBool(false);

      Assert.Equal(JavaScriptValueKind.Number, number.Kind);
      Assert.Equal(JavaScriptValueKind.Bool, trueValue.Kind);
      Assert.Equal(JavaScriptValueKind.Bool, falseValue.Kind);
      return true;
    });

    var counters = fixture.Counters;
    Assert.Equal(3u, counters.PrimitiveValueCreates);
    Assert.Equal(0u, counters.DeprecatedNumberCreates);
    Assert.Equal(0u, counters.DeprecatedBoolCreates);
  }

  [Fact]
  public void CreateUndefinedRoundTrips()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var value = runtime.CreateUndefined();
      Assert.Equal(JavaScriptValueKind.Undefined, value.Kind);
      Assert.True(value.IsNullish);
      return true;
    });
  }

  [Fact]
  public void CreateNullRoundTrips()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var value = runtime.CreateNull();
      Assert.Equal(JavaScriptValueKind.Null, value.Kind);
      Assert.True(value.IsNullish);
      return true;
    });
  }

  [Theory]
  [InlineData("")]
  [InlineData("hello")]
  [InlineData("Zoë")]
  [InlineData("a\0b")]
  [InlineData("\0")]
  [InlineData("\u007F")]
  [InlineData("\u0080")]
  [InlineData("\u07FF")]
  [InlineData("\u0800")]
  [InlineData("\uD7FF")]
  [InlineData("\uE000")]
  [InlineData("\uFFFD")]
  [InlineData("\uFFFF")]
  [InlineData("\U00010000")]
  [InlineData("\U0010FFFF")]
  [InlineData("ASCII \u0080 \u0800 \U00010000 \U0010FFFF")]
  public void CreateStringRoundTripsUtf8Boundaries(string expected)
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var value = runtime.CreateString(expected);
      AssertJavaScriptString(value, expected);
      return true;
    });
  }

  // Invalid UTF-8 byte sequences cannot be covered through the managed wrapper today:
  // JavaScriptRuntime.CreateString and ExpoJsiApi.CreateStringValue both accept only string,
  // and the raw create-string ABI function pointer is private to ExpoJsiApi. Exercising invalid
  // bytes would require new managed or ABI testability surface, which this test suite avoids.

  [Fact]
  public void DisposingOwnedValueIncrementsReleaseCounter()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();

    fixture.Runtime.Execute(runtime =>
    {
      using (runtime.CreateNumber(1))
      {
      }

      return true;
    });

    var counters = fixture.Counters;
    Assert.True(counters.ReleasedValues >= 1);
  }

  [Fact]
  public void ReadingStringReleasesNativeStringResultBuffer()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();

    fixture.Runtime.Execute(runtime =>
    {
      using var value = runtime.CreateString("hello");
      Assert.Equal("hello", value.AsString());
      return true;
    });

    var counters = fixture.Counters;
    Assert.True(counters.ReleasedStrings >= 1);
  }

  private static void AssertJavaScriptString(JavaScriptValue value, string expected)
  {
    Assert.Equal(JavaScriptValueKind.String, value.Kind);
    Assert.Equal(expected, value.AsString());
  }
}
