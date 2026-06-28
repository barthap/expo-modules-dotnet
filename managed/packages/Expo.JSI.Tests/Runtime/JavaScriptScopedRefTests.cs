using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

public sealed class JavaScriptScopedRefTests
{
  [Fact]
  public void OwnedValueRefReadsNestedPropertyWithoutDisposableIntermediates()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Evaluate(
          "({ user: { name: 'expo' } })",
          "scoped-ref-nested-property.js"
      );

      var property = value.Ref.AsObject()
          .GetProperty("user")
          .AsObject()
          .GetProperty("name");

      Assert.Equal(JavaScriptValueKind.String, property.Kind);
      Assert.Equal("expo", property.AsString());
      return true;
    });
  }

  [Fact]
  public void RefRetainReturnsOwnedValueThatSurvivesScope()
  {
    using var fixture = HermesRuntimeFixture.Create();

    using var retained = fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Evaluate(
          "({ message: 'retained' })",
          "scoped-ref-retain.js"
      );

      return value.Ref.AsObject().GetProperty("message").Retain();
    });

    fixture.Runtime.Execute(_ =>
    {
      Assert.Equal("retained", retained.AsString());
      return true;
    });
  }

  [Fact]
  public void RefTraversalReleasesTemporaryHandlesThroughExistingCounters()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();

    fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Evaluate(
          "({ user: { name: 'expo' } })",
          "scoped-ref-release.js"
      );

      var name = value.Ref.AsObject()
          .GetProperty("user")
          .AsObject()
          .GetProperty("name");

      Assert.Equal("expo", name.AsString());
      return true;
    });

    Assert.True(fixture.Counters.ReleasedObjects >= 1);
    Assert.True(fixture.Counters.ReleasedValues >= 1);
  }

  [Fact]
  public void RefOutsideRuntimeAccessThrows()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var value = fixture.Runtime.Execute(_ =>
      fixture.Evaluate("'outside'", "scoped-ref-outside.js"));

    var error = Assert.Throws<InvalidOperationException>(() => _ = value.Ref);
    Assert.Contains("Scoped JavaScript refs require active runtime access", error.Message);
  }

  [Fact]
  public void DefaultRefFailsBeforeTouchingNative()
  {
    var error = Assert.Throws<ObjectDisposedException>(ReadDefaultRefString);
    Assert.Equal("JavaScriptHandleScope", error.ObjectName);
  }

  private static void ReadDefaultRefString()
  {
    JavaScriptValueRef value = default;
    _ = value.AsString();
  }

  [Fact]
  public void RefFromDisposedOwnedValueThrows()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var value = runtime.CreateString("disposed");
      value.Dispose();

      Assert.Throws<ObjectDisposedException>(() => _ = value.Ref);
      return true;
    });
  }
}
