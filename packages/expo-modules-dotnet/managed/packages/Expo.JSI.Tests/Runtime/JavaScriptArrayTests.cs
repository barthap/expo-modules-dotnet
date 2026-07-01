using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

public sealed class JavaScriptArrayTests
{
  [Fact]
  public void CreateArrayCreatesJavaScriptVisibleArrayWithRequestedLength()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var global = runtime.Global();
      using var array = runtime.CreateArray(3);
      using var arrayValue = array.AsValue();
      global.SetProperty("managedArray", arrayValue);

      using var isArray = fixture.Evaluate("Array.isArray(globalThis.managedArray)", "array-create.js");
      using var length = fixture.Evaluate("globalThis.managedArray.length", "array-create.js");

      Assert.True(isArray.AsBool());
      Assert.Equal(3, length.AsDouble());
      return true;
    });
  }

  [Fact]
  public void GetAndSetValueRoundTripIndexedElements()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var array = runtime.CreateArray(2);
      using var first = runtime.CreateNumber(41.5);
      using var second = runtime.CreateString("expo");

      array.SetValue(0, first);
      array.SetValue(1, second);

      using var actualFirst = array.GetValue(0);
      using var actualSecond = array.GetValue(1);

      Assert.Equal(JavaScriptValueKind.Number, actualFirst.Kind);
      Assert.Equal(41.5, actualFirst.AsDouble());
      Assert.Equal(JavaScriptValueKind.String, actualSecond.Kind);
      Assert.Equal("expo", actualSecond.AsString());
      return true;
    });
  }

  [Fact]
  public void LengthObservesJavaScriptSideMutations()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Evaluate("const xs = [1, 2]; xs.push(3); xs", "array-length.js");
      using var array = value.AsArray();

      Assert.Equal(3u, array.Length);
      return true;
    });
  }

  [Fact]
  public void JavaScriptValueAsArrayConvertsEvaluatedArray()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Evaluate("['a', 'b']", "array-as-array.js");
      using var array = value.AsArray();

      Assert.Equal(2u, array.Length);
      using var element = array.GetValue(1);
      Assert.Equal("b", element.AsString());
      return true;
    });
  }

  [Fact]
  public void JavaScriptValueAsArrayRetainsAfterValidation()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Evaluate("[10, 20, 30]", "array-retain-as-array.js");
      using var array = value.AsArray();
      value.Dispose();

      Assert.Equal(3u, array.Length);
      using var element = array.GetValue(2);
      Assert.Equal(30, element.AsDouble());
      return true;
    });
  }

  [Fact]
  public void JavaScriptValueAsArrayRejectsNonArrayBeforeReturningWrapper()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Evaluate("({ length: 3 })", "array-wrong-type.js");
      var error = Assert.Throws<InvalidOperationException>(() =>
      {
        using var _ = value.AsArray();
      });

      Assert.Contains("array", error.Message, StringComparison.OrdinalIgnoreCase);
      return true;
    });
  }

  [Fact]
  public void JavaScriptValueRefAsArrayWorksInsideHostFunction()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var global = runtime.Global();
      using var function = runtime.CreateHostFunction(
          "readArrayLength",
          1,
          (callbackRuntime, thisValue, arguments, context) =>
          {
            var array = arguments.GetValue(0).AsArray();
            return callbackRuntime.CreateNumber(array.Length);
          },
          new object()
      );
      using var functionValue = function.AsValue();
      global.SetProperty("readArrayLength", functionValue);

      using var result = fixture.Evaluate(
          "globalThis.readArrayLength([1, 2, 3, 4])",
          "borrowed-array.js"
      );

      Assert.Equal(4, result.AsDouble());
      return true;
    });
  }

  [Fact]
  public void DisposingArrayIncrementsReleaseCounter()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();

    fixture.Runtime.Execute(runtime =>
    {
      using (runtime.CreateArray(0))
      {
      }

      return true;
    });

    Assert.True(fixture.Counters.ReleasedValues >= 1);
  }
}
