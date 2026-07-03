using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

public sealed class JavaScriptObjectTests
{
  [Fact]
  public void GetPropertyReadsValueSetFromManagedObject()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var target = runtime.CreateObject();
      using var expected = runtime.CreateNumber(42.5);

      target.SetProperty("answer", expected);

      using var actual = target.GetProperty("answer");
      Assert.Equal(JavaScriptValueKind.Number, actual.Kind);
      Assert.Equal(42.5, actual.AsDouble());
      return true;
    });
  }

  [Fact]
  public void SetPropertyIsVisibleToJavaScript()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var global = runtime.Global();
      using var target = runtime.CreateObject();
      using var expected = runtime.CreateString("Zoë\0JS");

      target.SetProperty("message", expected);
      using var targetValue = target.AsValue();
      global.SetProperty("managedObject", targetValue);

      using var actual = fixture.Evaluate("globalThis.managedObject.message", "object-property.js");
      Assert.Equal(JavaScriptValueKind.String, actual.Kind);
      Assert.Equal("Zoë\0JS", actual.AsString());
      return true;
    });
  }

  [Fact]
  public void GetPropertyReadsValueCreatedByJavaScript()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Evaluate("({ message: 'hello from JS' })", "object-literal.js");
      using var target = value.AsObject();

      using var actual = target.GetProperty("message");
      Assert.Equal(JavaScriptValueKind.String, actual.Kind);
      Assert.Equal("hello from JS", actual.AsString());
      return true;
    });
  }

  [Fact]
  public void GetOwnPropertyNamesReturnsOwnEnumerableAndNonEnumerableNames()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Evaluate(
          "(() => { const obj = { alpha: 1, zażółć: 2 }; Object.defineProperty(obj, 'hidden', { value: 3, enumerable: false }); return obj; })()",
          "object-property-names.js"
      );
      using var target = value.AsObject();

      var names = target.GetOwnPropertyNames();

      Assert.Contains("alpha", names);
      Assert.Contains("zażółć", names);
      Assert.Contains("hidden", names);
      return true;
    });
  }

  [Fact]
  public void GetOwnPropertyNamesExcludesPrototypeProperties()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Evaluate(
          "(() => { const proto = { inherited: 1 }; const obj = Object.create(proto); obj.own = 2; return obj; })()",
          "own-property-names.js"
      );
      using var target = value.AsObject();

      var names = target.GetOwnPropertyNames();

      Assert.Contains("own", names);
      Assert.DoesNotContain("inherited", names);
      return true;
    });
  }

  [Fact]
  public void JavaScriptValueAsObjectRetainsAfterValidation()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Evaluate("({ answer: 42 })", "object-retain-as-object.js");
      using var target = value.AsObject();
      value.Dispose();

      using var actual = target.GetProperty("answer");
      Assert.Equal(42, actual.AsDouble());
      return true;
    });
  }

  [Fact]
  public void JavaScriptValueAsObjectRejectsNonObjectBeforeReturningWrapper()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var value = runtime.CreateNumber(7);
      var error = Assert.Throws<InvalidOperationException>(() =>
      {
        using var _ = value.AsObject();
      });

      Assert.Contains("object", error.Message, StringComparison.OrdinalIgnoreCase);
      return true;
    });
  }

  [Fact]
  public void Utf8PropertyNameRoundTrips()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var target = runtime.CreateObject();
      using var expected = runtime.CreateString("ok");

      target.SetProperty("zażółć", expected);

      using var actual = target.GetProperty("zażółć");
      Assert.Equal(JavaScriptValueKind.String, actual.Kind);
      Assert.Equal("ok", actual.AsString());
      return true;
    });
  }

  [Fact]
  public void DisposingObjectIncrementsReleaseCounter()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();

    fixture.Runtime.Execute(runtime =>
    {
      using (runtime.CreateObject())
      {
      }

      return true;
    });

    var counters = fixture.Counters;
    Assert.True(counters.ReleasedValues >= 1);
  }
}
