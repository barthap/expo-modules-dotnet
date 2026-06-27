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

  [Fact]
  public void CreateAsciiStringRoundTrips()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var value = fixture.Runtime.CreateString("hello");

    Assert.Equal(JavaScriptValueKind.String, value.Kind);
    Assert.Equal("hello", value.AsString());
  }

  [Fact]
  public void CreateNonAsciiStringRoundTrips()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var value = fixture.Runtime.CreateString("Zoë");

    Assert.Equal(JavaScriptValueKind.String, value.Kind);
    Assert.Equal("Zoë", value.AsString());
  }

  [Fact]
  public void CreateEmbeddedNulStringRoundTrips()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var value = fixture.Runtime.CreateString("a\0b");

    Assert.Equal(JavaScriptValueKind.String, value.Kind);
    Assert.Equal("a\0b", value.AsString());
  }
}
