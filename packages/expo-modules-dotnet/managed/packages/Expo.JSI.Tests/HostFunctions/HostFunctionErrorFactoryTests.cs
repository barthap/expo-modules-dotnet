using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.HostFunctions;

// A host function created with a JavaScriptErrorFactory throws the factory's value to JavaScript.
// This is how Expo.ModulesCore gives module errors a code; raw host functions keep ToString().
public sealed class HostFunctionErrorFactoryTests
{
  [Fact]
  public void FactoryValueIsThrownToJavaScriptAndReleased()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var thrown = new InvalidOperationException("callback failed");
    var calls = 0;
    Exception? received = null;

    fixture.Runtime.Execute(runtime =>
    {
      using (fixture.Evaluate("globalThis.marker = { tag: 'factory' }; true", "factory-marker.js"))
      {
      }
      Install(runtime, "fail", (_, _, _, _) => throw thrown, (factoryRuntime, exception) =>
      {
        calls++;
        received = exception;
        using var global = factoryRuntime.Global();
        return global.GetProperty("marker");
      });
      using var caught = fixture.Evaluate(
          "try { fail(); 'no error'; } catch (e) { e === globalThis.marker; }",
          "factory-identity.js"
      );
      Assert.True(caught.AsBool());
      return true;
    });

    // Called once, with the original exception, and the detached value was released by native.
    Assert.Equal(1, calls);
    Assert.Same(thrown, received);
    Assert.Equal(0u, fixture.Counters.OwnedValuesTracked);
  }

  [Fact]
  public void ThrowingFactoryFallsBackToTheMessageResult()
  {
    using var fixture = HermesRuntimeFixture.Create();

    var message = fixture.Runtime.Execute(runtime =>
    {
      Install(
          runtime,
          "fail",
          (_, _, _, _) => throw new InvalidOperationException("callback failed"),
          (factoryRuntime, _) =>
          {
            using var partial = factoryRuntime.CreateObject();
            throw new InvalidOperationException("factory failed");
          }
      );
      using var caught = fixture.Evaluate(
          "try { fail(); 'no error'; } catch (e) { e.message; }",
          "factory-fallback.js"
      );
      return caught.AsString();
    });

    // The message result is built from the original exception, as for a function without a factory.
    Assert.Contains("System.InvalidOperationException: callback failed", message);
    Assert.DoesNotContain("factory failed", message);
    Assert.Equal(0u, fixture.Counters.OwnedValuesTracked);
  }

  [Fact]
  public void NullFactoryResultFallsBackToTheMessageResult()
  {
    using var fixture = HermesRuntimeFixture.Create();

    var message = fixture.Runtime.Execute(runtime =>
    {
      Install(
          runtime,
          "fail",
          (_, _, _, _) => throw new InvalidOperationException("callback failed"),
          (_, _) => null!
      );
      using var caught = fixture.Evaluate(
          "try { fail(); 'no error'; } catch (e) { e.message; }",
          "factory-null.js"
      );
      return caught.AsString();
    });

    Assert.Contains("callback failed", message);
  }

  [Fact]
  public void CallbackThatCatchesItsOwnExceptionNeverCallsTheFactory()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var calls = 0;

    fixture.Runtime.Execute(runtime =>
    {
      Install(
          runtime,
          "recover",
          (callbackRuntime, _, _, _) =>
          {
            try
            {
              throw new InvalidOperationException("handled");
            }
            catch (InvalidOperationException)
            {
            }
            return callbackRuntime.CreateUndefined();
          },
          (_, _) =>
          {
            calls++;
            throw new InvalidOperationException("factory must not run");
          }
      );
      using var result = fixture.Evaluate("recover() === undefined", "factory-not-called.js");
      Assert.True(result.AsBool());
      return true;
    });

    Assert.Equal(0, calls);
    Assert.Equal(0u, fixture.Counters.OwnedValuesTracked);
  }

  // Reentrancy: the inner host function runs inside a JavaScript callback that the outer host
  // function called. Each frame converts its own failure; the inner value crosses the outer
  // managed Call as a message, because nested thrown values are not relayed (spec, out of scope).
  [Fact]
  public void NestedFactoryValueCrossesTheOuterCallAsAMessage()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var innerCalls = 0;
    var outerCalls = 0;
    Exception? outerReceived = null;

    fixture.Runtime.Execute(runtime =>
    {
      using (fixture.Evaluate("globalThis.outerMarker = { tag: 'outer' }; true", "nested-marker.js"))
      {
      }
      Install(
          runtime,
          "inner",
          (_, _, _, _) => throw new InvalidOperationException("inner failure"),
          (factoryRuntime, _) =>
          {
            innerCalls++;
            using var error = factoryRuntime.CreateErrorObject("inner value");
            return error.AsValue();
          }
      );
      Install(
          runtime,
          "outer",
          (callbackRuntime, _, arguments, _) =>
          {
            using var callback = arguments.GetValue(0).AsFunction();
            using var result = callback.Call();
            return callbackRuntime.CreateUndefined();
          },
          (factoryRuntime, exception) =>
          {
            outerCalls++;
            outerReceived = exception;
            using var global = factoryRuntime.Global();
            return global.GetProperty("outerMarker");
          }
      );
      using var uncaught = fixture.Evaluate(
          "try { outer(() => inner()); 'no error'; } catch (e) { e === globalThis.outerMarker; }",
          "nested-uncaught.js"
      );
      Assert.True(uncaught.AsBool());
      using var caughtInside = fixture.Evaluate(
          "outer(() => { try { inner(); } catch (e) { globalThis.innerMessage = e.message; } }); globalThis.innerMessage",
          "nested-caught.js"
      );
      // Inside the callback the inner factory value is caught as is.
      Assert.Equal("inner value", caughtInside.AsString());
      return true;
    });

    Assert.Equal(2, innerCalls);
    Assert.Equal(1, outerCalls);
    var outerException = Assert.IsType<InvalidOperationException>(outerReceived);
    Assert.StartsWith("Native JSI error ", outerException.Message);
    Assert.Contains("inner value", outerException.Message);
    Assert.Equal(0u, fixture.Counters.OwnedValuesTracked);
  }

  private static void Install(
      JavaScriptRuntime runtime,
      string name,
      JavaScriptHostFunction callback,
      JavaScriptErrorFactory createError
  )
  {
    using var global = runtime.Global();
    using var function = runtime.CreateHostFunction(name, 1, callback, new object(), null, createError);
    using var functionValue = function.AsValue();
    global.SetProperty(name, functionValue);
  }
}
