using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.HostFunctions;

public sealed class HostFunctionTests
{
  [Fact]
  public void HostFunctionReceivesBorrowedArgumentAndReturnsOwnedValue()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var global = runtime.Global();
      using var function = runtime.CreateHostFunction(
          "addOne",
          1,
          (callbackRuntime, thisValue, arguments, context) =>
          {
            Assert.Equal(1u, arguments.Count);
            var input = arguments.GetBorrowedValue(0);
            Assert.Equal(JavaScriptValueKind.Number, input.Kind);
            return callbackRuntime.CreateNumber(input.AsDouble() + 1);
          },
          new object()
      );
      using var functionValue = function.AsValue();
      global.SetProperty("addOne", functionValue);

      using var result = fixture.Evaluate("globalThis.addOne(41.5)", "host-function-success.js");

      Assert.Equal(JavaScriptValueKind.Number, result.Kind);
      Assert.Equal(42.5, result.AsDouble());
      return true;
    });
  }

  [Fact]
  public void HostFunctionReadsBorrowedBoolStringAndObjectArguments()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var global = runtime.Global();
      using var function = runtime.CreateHostFunction(
          "describeArguments",
          3,
          (callbackRuntime, thisValue, arguments, context) =>
          {
            Assert.Equal(3u, arguments.Count);
            var enabled = arguments.GetBorrowedValue(0);
            var label = arguments.GetBorrowedValue(1);
            var options = arguments.GetBorrowedValue(2);

            Assert.Equal(JavaScriptValueKind.Bool, enabled.Kind);
            Assert.Equal(JavaScriptValueKind.String, label.Kind);
            Assert.Equal(JavaScriptValueKind.Object, options.Kind);

            using var optionsObject = options.AsObject();
            using var name = optionsObject.GetProperty("name");
            return callbackRuntime.CreateString(
                $"{enabled.AsBool()}:{label.AsString()}:{name.AsString()}"
            );
          },
          new object()
      );
      using var functionValue = function.AsValue();
      global.SetProperty("describeArguments", functionValue);

      using var result = fixture.Evaluate(
          "globalThis.describeArguments(true, 'Zoë', { name: 'expo' })",
          "host-function-arguments.js"
      );

      Assert.Equal(JavaScriptValueKind.String, result.Kind);
      Assert.Equal("True:Zoë:expo", result.AsString());
      return true;
    });
  }

  [Fact]
  public void HostFunctionReceivesThisValue()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var global = runtime.Global();
      using var target = runtime.CreateObject();
      using var offset = runtime.CreateNumber(1.5);
      target.SetProperty("offset", offset);
      using var function = runtime.CreateHostFunction(
          "addOffset",
          1,
          (callbackRuntime, thisValue, arguments, context) =>
          {
            using var self = thisValue.AsObject();
            using var offsetValue = self.GetProperty("offset");
            var input = arguments.GetBorrowedValue(0);
            return callbackRuntime.CreateNumber(input.AsDouble() + offsetValue.AsDouble());
          },
          new object()
      );
      using var functionValue = function.AsValue();
      target.SetProperty("addOffset", functionValue);
      using var targetValue = target.AsValue();
      global.SetProperty("target", targetValue);

      using var result = fixture.Evaluate("globalThis.target.addOffset(41)", "host-function-this.js");

      Assert.Equal(JavaScriptValueKind.Number, result.Kind);
      Assert.Equal(42.5, result.AsDouble());
      return true;
    });
  }

  [Fact]
  public void DisposingHostFunctionIncrementsReleaseCounter()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();

    fixture.Runtime.Execute(runtime =>
    {
      using (runtime.CreateHostFunction(
          "noop",
          0,
          (callbackRuntime, thisValue, arguments, context) => callbackRuntime.CreateBool(true),
          new object()
      ))
      {
      }

      return true;
    });

    var counters = fixture.Counters;
    Assert.True(counters.ReleasedFunctions >= 1);
  }

  [Fact]
  public void DisposingEvaluatedResultIncrementsReleaseCounter()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();

    fixture.Runtime.Execute(_ =>
    {
      using (fixture.Evaluate("21 + 21", "counter-evaluate.js"))
      {
      }

      return true;
    });

    var counters = fixture.Counters;
    Assert.True(counters.ReleasedValues >= 1);
  }
}
