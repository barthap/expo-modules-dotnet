using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.HostFunctions;

public sealed class HostFunctionErrorTests
{
  [Fact]
  public void HostFunctionManagedExceptionIsCatchableInJavaScript()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var global = fixture.Runtime.Global();
    using var function = fixture.Runtime.CreateHostFunction(
        "throwFromManaged",
        0,
        static (runtime, thisValue, arguments, context) =>
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
  }
}
