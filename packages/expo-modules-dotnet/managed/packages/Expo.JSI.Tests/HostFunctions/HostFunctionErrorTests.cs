using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.HostFunctions;

public sealed class HostFunctionErrorTests
{
  private static JavaScriptValue ThrowFromNamedManagedHelper(
      JavaScriptRuntime runtime,
      JavaScriptValueRef thisValue,
      JavaScriptArguments arguments,
      object context
  )
  {
    throw new InvalidOperationException("managed helper boom");
  }

  [Fact]
  public void HostFunctionManagedExceptionIsCatchableInJavaScript()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var global = runtime.Global();
      using var function = runtime.CreateHostFunction(
          "throwFromManaged",
          0,
          static (callbackRuntime, thisValue, arguments, context) =>
          {
            throw new InvalidOperationException("managed boom");
          },
          new object()
      );
      using var functionValue = function.AsValue();
      global.SetProperty("throwFromManaged", functionValue);

      using var result = fixture.Evaluate(
          "try { globalThis.throwFromManaged(); 'no error'; } catch (e) { e.message; }",
          "host-function-error.js"
      );

      Assert.Equal(JavaScriptValueKind.String, result.Kind);
      Assert.Contains("managed boom", result.AsString());
      return true;
    });
  }

  [Fact]
  public void HostFunctionManagedExceptionMessageIncludesStackTrace()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.Runtime.Execute(runtime =>
    {
      using var global = runtime.Global();
      using var function = runtime.CreateHostFunction(
          "throwFromNamedManagedHelper",
          0,
          ThrowFromNamedManagedHelper,
          new object()
      );
      using var functionValue = function.AsValue();
      global.SetProperty("throwFromNamedManagedHelper", functionValue);

      using var result = fixture.Evaluate(
          "try { globalThis.throwFromNamedManagedHelper(); 'no error'; } catch (e) { e.message; }",
          "host-function-stack-trace.js"
      );

      Assert.Equal(JavaScriptValueKind.String, result.Kind);
      var message = result.AsString();
      Assert.Contains("System.InvalidOperationException", message);
      Assert.Contains("managed helper boom", message);
      Assert.Contains(nameof(ThrowFromNamedManagedHelper), message);
      return true;
    });
  }
}
