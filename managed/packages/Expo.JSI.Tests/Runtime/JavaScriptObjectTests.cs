using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

public sealed class JavaScriptObjectTests
{
  [Fact]
  public void GetPropertyReadsValueSetFromManagedObject()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var target = fixture.Runtime.CreateObject();
    using var expected = fixture.Runtime.CreateNumber(42.5);

    target.SetProperty("answer", expected);

    using var actual = target.GetProperty("answer");
    Assert.Equal(JavaScriptValueKind.Number, actual.Kind);
    Assert.Equal(42.5, actual.AsDouble());
  }

  [Fact]
  public void SetPropertyIsVisibleToJavaScript()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var global = fixture.Runtime.Global();
    using var target = fixture.Runtime.CreateObject();
    using var expected = fixture.Runtime.CreateString("Zoë\0JS");

    target.SetProperty("message", expected);
    using var targetValue = target.AsValue();
    global.SetProperty("managedObject", targetValue);

    using var actual = fixture.Evaluate("globalThis.managedObject.message", "object-property.js");
    Assert.Equal(JavaScriptValueKind.String, actual.Kind);
    Assert.Equal("Zoë\0JS", actual.AsString());
  }

  [Fact]
  public void GetPropertyReadsValueCreatedByJavaScript()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var value = fixture.Evaluate("({ message: 'hello from JS' })", "object-literal.js");
    using var target = value.AsObject();

    using var actual = target.GetProperty("message");
    Assert.Equal(JavaScriptValueKind.String, actual.Kind);
    Assert.Equal("hello from JS", actual.AsString());
  }
}
