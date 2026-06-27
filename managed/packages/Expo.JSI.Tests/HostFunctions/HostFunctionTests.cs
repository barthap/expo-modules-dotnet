using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.HostFunctions;

public sealed class HostFunctionTests
{
  [Fact]
  public void HostFunctionReceivesBorrowedArgumentAndReturnsOwnedValue()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var global = fixture.Runtime.Global();
    using var function = fixture.Runtime.CreateHostFunction(
        "addOne",
        1,
        (runtime, thisValue, arguments, context) =>
        {
          Assert.Equal(1u, arguments.Count);
          var input = arguments.GetBorrowedValue(0);
          Assert.Equal(JavaScriptValueKind.Number, input.Kind);
          return runtime.CreateNumber(input.AsDouble() + 1);
        },
        new object()
    );
    using var functionValue = function.AsValue();
    global.SetProperty("addOne", functionValue);

    using var result = fixture.Evaluate("globalThis.addOne(41.5)", "host-function-success.js");

    Assert.Equal(JavaScriptValueKind.Number, result.Kind);
    Assert.Equal(42.5, result.AsDouble());
  }

  [Fact]
  public void DisposingEvaluatedResultIncrementsReleaseCounter()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();

    using (fixture.Evaluate("21 + 21", "counter-evaluate.js"))
    {
    }

    var counters = fixture.Counters;
    Assert.True(counters.ReleasedValues >= 1);
  }
}
