using Expo.JSI;
using Expo.ModulesCore;
using Expo.ModulesCore.Codecs;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Generated;

public sealed class GeneratedMathAndTextModuleTests
{
  [Fact]
  public void GeneratedLookingCodeDispatchesSyncFunction()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      GeneratedMathAndTextModuleProvider.Register(runtime);

      using var result = fixture.Evaluate(
          "globalThis.expo.modules.Math.add(41.5, true)",
          "modules-core-math-add.js"
      );

      Assert.Equal(JavaScriptValueKind.Number, result.Kind);
      Assert.Equal(42.5, result.AsDouble());
      return true;
    });
  }

  [Fact]
  public void GeneratedLookingCodePreservesStringValuesThroughModuleDispatch()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      GeneratedMathAndTextModuleProvider.Register(runtime);

      using var result = fixture.Evaluate(
          "globalThis.expo.modules.Text.greet('Zoë\\u0000JS')",
          "modules-core-text-greet.js"
      );

      Assert.Equal(JavaScriptValueKind.String, result.Kind);
      Assert.Equal("Hello, Zoë\0JS", result.AsString());
      return true;
    });
  }

  [Fact]
  public void GeneratedLookingTypeFailureIsCatchableInJavaScript()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      GeneratedMathAndTextModuleProvider.Register(runtime);

      using var result = fixture.Evaluate(
          "try { globalThis.expo.modules.Text.greet(42); 'no error'; } catch (e) { e.message; }",
          "modules-core-text-error.js"
      );

      Assert.Equal(JavaScriptValueKind.String, result.Kind);
      Assert.Contains("string", result.AsString(), StringComparison.OrdinalIgnoreCase);
      return true;
    });
  }

  private sealed class MathModule
  {
    public double Add(double value, bool shouldAddOne) =>
        shouldAddOne ? value + 1.0 : value;
  }

  private sealed class TextModule
  {
    public string Greet(string name) => $"Hello, {name}";
  }

  private static class GeneratedMathAndTextModuleProvider
  {
    public static void Register(JavaScriptRuntime runtime)
    {
      using var math = ModuleRegistry.DefineModule(runtime, "Math");
      using var text = ModuleRegistry.DefineModule(runtime, "Text");

      GeneratedFunction.DefineSync(
          runtime,
          math,
          "add",
          2,
          MathAddHostFunction,
          new MathModule()
      );
      GeneratedFunction.DefineSync(
          runtime,
          text,
          "greet",
          1,
          TextGreetHostFunction,
          new TextModule()
      );
    }

    private static JavaScriptValue MathAddHostFunction(
        JavaScriptRuntime runtime,
        JavaScriptValueRef thisValue,
        JavaScriptArguments arguments,
        object context)
    {
      GeneratedFunction.RequireArgumentCount("Math.add", arguments, 2);

      var module = (MathModule)context;
      var value = DoubleCodec.Decode(arguments.GetValue(0), runtime);
      var shouldAddOne = BoolCodec.Decode(arguments.GetValue(1), runtime);
      return DoubleCodec.Encode(module.Add(value, shouldAddOne), runtime);
    }

    private static JavaScriptValue TextGreetHostFunction(
        JavaScriptRuntime runtime,
        JavaScriptValueRef thisValue,
        JavaScriptArguments arguments,
        object context)
    {
      GeneratedFunction.RequireArgumentCount("Text.greet", arguments, 1);

      var module = (TextModule)context;
      var name = StringCodec.Decode(arguments.GetValue(0), runtime);
      return StringCodec.Encode(module.Greet(name), runtime);
    }
  }
}
