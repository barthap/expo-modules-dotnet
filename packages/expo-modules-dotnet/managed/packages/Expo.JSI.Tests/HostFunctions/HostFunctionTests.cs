using System.Globalization;
using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.HostFunctions;

public sealed class HostFunctionTests
{
  [Fact]
  public void HostFunctionReceivesArgumentRefAndReturnsOwnedValue()
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
            var input = arguments.GetValue(0);
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
  public void HostFunctionReadsBoolStringAndObjectArgumentRefs()
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
            var enabled = arguments.GetValue(0);
            var label = arguments.GetValue(1);
            var options = arguments.GetValue(2);

            Assert.Equal(JavaScriptValueKind.Bool, enabled.Kind);
            Assert.Equal(JavaScriptValueKind.String, label.Kind);
            Assert.Equal(JavaScriptValueKind.Object, options.Kind);

            var name = options.AsObject().GetProperty("name");
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
            var offsetValue = thisValue.AsObject().GetProperty("offset");
            var input = arguments.GetValue(0);
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
    Assert.True(counters.ReleasedValues >= 1);
  }

  [Fact]
  public void HostFunctionReceivesScopedThisAndArgumentRefs()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var global = runtime.Global();
      using var target = runtime.CreateObject();
      using var offset = runtime.CreateNumber(1.5);
      target.SetProperty("offset", offset);

      using var function = runtime.CreateHostFunction(
          "describe",
          1,
          (callbackRuntime, thisValue, arguments, context) =>
          {
            var offsetValue = thisValue.AsObject().GetProperty("offset");
            var name = arguments.GetValue(0).AsObject().GetProperty("name");
            return callbackRuntime.CreateString(
                $"{name.AsString()}:{offsetValue.AsDouble().ToString(CultureInfo.InvariantCulture)}"
            );
          },
          new object()
      );
      using var functionValue = function.AsValue();
      target.SetProperty("describe", functionValue);
      using var targetValue = target.AsValue();
      global.SetProperty("target", targetValue);

      using var result = fixture.Evaluate(
          "globalThis.target.describe({ name: 'expo' })",
          "host-function-scoped-refs.js"
      );

      Assert.Equal("expo:1.5", result.AsString());
      return true;
    });
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
