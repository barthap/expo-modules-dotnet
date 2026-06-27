using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

public sealed class JavaScriptArrayTests
{
  [Fact]
  public void CreateArrayCreatesJavaScriptVisibleArrayWithRequestedLength()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var global = fixture.Runtime.Global();
    using var array = fixture.Runtime.CreateArray(3);
    using var arrayValue = array.AsValue();
    global.SetProperty("managedArray", arrayValue);

    using var isArray = fixture.Evaluate("Array.isArray(globalThis.managedArray)", "array-create.js");
    using var length = fixture.Evaluate("globalThis.managedArray.length", "array-create.js");

    Assert.True(isArray.AsBool());
    Assert.Equal(3, length.AsDouble());
  }

  [Fact]
  public void GetAndSetValueRoundTripIndexedElements()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var array = fixture.Runtime.CreateArray(2);
    using var first = fixture.Runtime.CreateNumber(41.5);
    using var second = fixture.Runtime.CreateString("expo");

    array.SetValue(0, first);
    array.SetValue(1, second);

    using var actualFirst = array.GetValue(0);
    using var actualSecond = array.GetValue(1);

    Assert.Equal(JavaScriptValueKind.Number, actualFirst.Kind);
    Assert.Equal(41.5, actualFirst.AsDouble());
    Assert.Equal(JavaScriptValueKind.String, actualSecond.Kind);
    Assert.Equal("expo", actualSecond.AsString());
  }

  [Fact]
  public void LengthObservesJavaScriptSideMutations()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var value = fixture.Evaluate("const xs = [1, 2]; xs.push(3); xs", "array-length.js");
    using var array = value.AsArray();

    Assert.Equal(3u, array.Length);
  }

  [Fact]
  public void JavaScriptValueAsArrayConvertsEvaluatedArray()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var value = fixture.Evaluate("['a', 'b']", "array-as-array.js");
    using var array = value.AsArray();

    Assert.Equal(2u, array.Length);
    using var element = array.GetValue(1);
    Assert.Equal("b", element.AsString());
  }

  [Fact]
  public void JavaScriptBorrowedValueAsArrayWorksInsideHostFunction()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var global = fixture.Runtime.Global();
    using var function = fixture.Runtime.CreateHostFunction(
        "readArrayLength",
        1,
        (runtime, thisValue, arguments, context) =>
        {
          using var array = arguments.GetBorrowedValue(0).AsArray();
          return runtime.CreateNumber(array.Length);
        },
        new object()
    );
    using var functionValue = function.AsValue();
    global.SetProperty("readArrayLength", functionValue);

    using var result = fixture.Evaluate("globalThis.readArrayLength([1, 2, 3, 4])", "borrowed-array.js");

    Assert.Equal(4, result.AsDouble());
  }

  [Fact]
  public void DisposingArrayIncrementsReleaseCounter()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();

    using (fixture.Runtime.CreateArray(0))
    {
    }

    Assert.True(fixture.Counters.ReleasedObjects >= 1);
  }
}
