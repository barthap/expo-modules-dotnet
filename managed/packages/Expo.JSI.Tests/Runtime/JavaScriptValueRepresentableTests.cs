using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

public sealed class JavaScriptValueRepresentableTests
{
  [Fact]
  public void JavaScriptValueAsValueReturnsDisposableClone()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var original = runtime.CreateString("alive");
      IJavaScriptValueRepresentable representable = original;

      using (var clone = representable.AsValue())
      {
        Assert.Equal("alive", clone.AsString());
      }

      Assert.Equal("alive", original.AsString());
      return true;
    });
  }
}
