using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

public sealed class JavaScriptPrimitiveTests
{
  [Fact]
  public void CreateNumberRoundTrips()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var value = fixture.Runtime.CreateNumber(42.5);

    Assert.Equal(JavaScriptValueKind.Number, value.Kind);
    Assert.Equal(42.5, value.AsDouble());
  }

  [Fact]
  public void CreateBoolTrueRoundTrips()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var value = fixture.Runtime.CreateBool(true);

    Assert.Equal(JavaScriptValueKind.Bool, value.Kind);
    Assert.True(value.AsBool());
  }

  [Fact]
  public void CreateBoolFalseRoundTrips()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var value = fixture.Runtime.CreateBool(false);

    Assert.Equal(JavaScriptValueKind.Bool, value.Kind);
    Assert.False(value.AsBool());
  }

  [Theory]
  [InlineData("hello")]
  [InlineData("Zoë")]
  [InlineData("a\0b")]
  public void CreateStringRoundTripsStrictUtf8(string expected)
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var value = fixture.Runtime.CreateString(expected);

    AssertJavaScriptString(value, expected);
  }

  [Fact]
  public void DisposingOwnedValueIncrementsReleaseCounter()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();

    using (fixture.Runtime.CreateNumber(1))
    {
    }

    var counters = fixture.Counters;
    Assert.True(counters.ReleasedValues >= 1);
  }

  [Fact]
  public void ReadingStringReleasesNativeStringResultBuffer()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var value = fixture.Runtime.CreateString("hello");
    fixture.ResetCounters();

    Assert.Equal("hello", value.AsString());

    var counters = fixture.Counters;
    Assert.True(counters.ReleasedStrings >= 1);
  }

  private static void AssertJavaScriptString(JavaScriptValue value, string expected)
  {
    Assert.Equal(JavaScriptValueKind.String, value.Kind);
    Assert.Equal(expected, value.AsString());
  }
}
