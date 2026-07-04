using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

public sealed class JavaScriptFunctionTests
{
  [Fact]
  public void CallInvokesJavaScriptFunctionWithRepresentableArguments()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var value = fixture.Evaluate("(a, b) => a + b", "function-call.js");
      using var function = value.AsFunction();
      using var a = runtime.CreateNumber(20);
      using var b = runtime.CreateNumber(22);

      using var result = function.Call(a, b);

      Assert.Equal(42, result.AsDouble());
      Assert.Equal(20, a.AsDouble());
      Assert.Equal(22, b.AsDouble());
      return true;
    });
  }

  [Fact]
  public void CallWithThisUsesExplicitObjectReceiver()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var value = fixture.Evaluate(
          "(function (suffix) { return this.prefix + suffix; })",
          "function-call-this.js"
      );
      using var function = value.AsFunction();
      using var receiverValue = fixture.Evaluate(
          "({ prefix: 'hello ' })",
          "function-this-object.js"
      );
      using var receiver = receiverValue.AsObject();
      using var suffix = runtime.CreateString("JS");

      using var result = function.CallWithThis(receiver, suffix);

      Assert.Equal("hello JS", result.AsString());
      return true;
    });
  }

  [Fact]
  public void ScopedValueRefAsFunctionRetainsCallableWrapper()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var value = fixture.Evaluate("(value) => value + 1", "function-ref-retain.js");
      using var function = value.Ref.AsFunction();
      value.Dispose();
      using var argument = runtime.CreateNumber(41);

      using var result = function.Call(argument);

      Assert.Equal(42, result.AsDouble());
      return true;
    });
  }

  [Fact]
  public void CallAsConstructorCreatesObject()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var value = fixture.Evaluate(
          "(function Box(value) { this.value = value; })",
          "function-constructor.js"
      );
      using var function = value.AsFunction();
      using var argument = runtime.CreateString("boxed");

      using var constructed = function.CallAsConstructor(argument);
      using var constructedObject = constructed.AsObject();
      using var result = constructedObject.GetProperty("value");

      Assert.Equal("boxed", result.AsString());
      return true;
    });
  }

  [Fact]
  public void JavaScriptCallErrorsDisposeTemporaryArguments()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      fixture.ResetCounters();
      using var value = fixture.Evaluate(
          "(() => { throw new Error('call failed'); })",
          "function-call-error.js"
      );
      using var function = value.AsFunction();
      using var argument = runtime.CreateObject();
      var beforeCallReleases = fixture.Counters.ReleasedValues;

      var error = Assert.IsType<InvalidOperationException>(
          Record.Exception(() => function.Call(argument))
      );

      Assert.Contains("call failed", error.Message);
      Assert.True(fixture.Counters.ReleasedValues > beforeCallReleases);
      return true;
    });
  }
}
