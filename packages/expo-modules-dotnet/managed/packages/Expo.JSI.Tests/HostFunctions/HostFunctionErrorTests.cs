using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.HostFunctions;

public sealed class HostFunctionErrorTests
{
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
}
