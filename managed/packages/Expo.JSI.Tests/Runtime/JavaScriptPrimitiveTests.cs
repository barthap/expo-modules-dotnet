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

  [Theory]
  [InlineData("hello")]
  [InlineData("Zoë")]
  [InlineData("a\0b")]
  public void CreateStringRoundTripsStrictUtf8(string expected)
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var value = runtime.CreateString(expected);
      AssertJavaScriptString(value, expected);
      return true;
    });
  }

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
