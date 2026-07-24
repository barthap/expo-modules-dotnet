using Expo.JSI;
using Expo.ModulesCore;
using Expo.ModulesCore.Codecs;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Modules;

public sealed class GeneratedMathAndTextModuleTests
{
  [Fact]
  public void DefaultGeneratedRegistrationCreatesModulesLazily()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      MathModule.CreateCount = 0;
      TextModule.CreateCount = 0;

      GeneratedMathAndTextModuleProvider.Register(context);

      using var beforeAccess = fixture.Evaluate(
          "Object.keys(globalThis._expoDotnet.modules).join(',') + ':' + " +
          "String(globalThis._expoDotnet.modules.Unknown)",
          "generated-lazy-before-access.js"
      );
      Assert.Equal("Math,Text:undefined", beforeAccess.AsString());
      Assert.Equal(0, MathModule.CreateCount);
      Assert.Equal(0, TextModule.CreateCount);

      using var result = fixture.Evaluate(
          "globalThis._expoDotnet.modules.Math.add(41.5, true)",
          "generated-lazy-call.js"
      );

      Assert.Equal(42.5, result.AsDouble());
      Assert.Equal(1, MathModule.CreateCount);
      Assert.Equal(0, TextModule.CreateCount);
      return true;
    });
  }

  [Fact]
  public void GeneratedLookingCodeDispatchesSyncFunction()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      GeneratedMathAndTextModuleProvider.Register(context, modules);

      using var result = fixture.Evaluate(
          "globalThis._expoDotnet.modules.Math.add(41.5, true)",
          "modules-core-math-add.js"
      );

      Assert.Equal(JavaScriptValueKind.Number, result.Kind);
      Assert.Equal(42.5, result.AsDouble());
      return true;
    });
  }

  [Fact]
  public void GeneratedLookingCodeAugmentsExistingNativeModuleObject()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var _ = fixture.Evaluate(
          "globalThis.expo = { modules: { Math: { nativeValue: 7 } } }; true",
          "modules-core-existing-module-setup.js"
      );

      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateExpoModulesObject();
      GeneratedMathAndTextModuleProvider.Register(context, modules);

      using var result = fixture.Evaluate(
          "globalThis.expo.modules.Math.nativeValue + globalThis.expo.modules.Math.add(41.5, true)",
          "modules-core-existing-module-add.js"
      );

      Assert.Equal(JavaScriptValueKind.Number, result.Kind);
      Assert.Equal(49.5, result.AsDouble());
      return true;
    });
  }

  [Fact]
  public void GeneratedLookingCodePreservesStringValuesThroughModuleDispatch()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      GeneratedMathAndTextModuleProvider.Register(context, modules);

      using var result = fixture.Evaluate(
          "globalThis._expoDotnet.modules.Text.greet('Zoë\\u0000JS')",
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
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      GeneratedMathAndTextModuleProvider.Register(context, modules);

      using var result = fixture.Evaluate(
          "try { globalThis._expoDotnet.modules.Text.greet(42); 'no error'; } catch (e) { e.message; }",
          "modules-core-text-error.js"
      );

      Assert.Equal(JavaScriptValueKind.String, result.Kind);
      Assert.Contains("string", result.AsString(), StringComparison.OrdinalIgnoreCase);
      return true;
    });
  }

  private sealed class MathModule
  {
    public static int CreateCount { get; set; }

    public MathModule()
    {
      CreateCount++;
    }

    public double Add(double value, bool shouldAddOne) =>
        shouldAddOne ? value + 1.0 : value;
  }

  private sealed class TextModule
  {
    public static int CreateCount { get; set; }

    public TextModule()
    {
      CreateCount++;
    }

    public string Greet(string name) => $"Hello, {name}";
  }

  private static class GeneratedMathAndTextModuleProvider
  {
    public static void Register(DotnetRuntimeContext context)
    {
      ArgumentNullException.ThrowIfNull(context);
      context.ModuleRegistry.RegisterLazyModule(
          new LazyModuleDefinition(
              "Math",
              static (context, modules) => RegisterMath(context, modules)
          )
      );
      context.ModuleRegistry.RegisterLazyModule(
          new LazyModuleDefinition(
              "Text",
              static (context, modules) => RegisterText(context, modules)
          )
      );
    }

    public static void Register(DotnetRuntimeContext context, JavaScriptObject modules)
    {
      ArgumentNullException.ThrowIfNull(context);
      ArgumentNullException.ThrowIfNull(modules);
      using var math = RegisterMath(context, modules);
      using var text = RegisterText(context, modules);
    }

    private static JavaScriptObject RegisterMath(DotnetRuntimeContext context, JavaScriptObject modules)
    {
      var math = context.ModuleRegistry.DefineModule(modules, "Math");
      try
      {
        var mathModule = context.ModuleRegistry.GetOrCreateModule("Math", static () => new MathModule());
        GeneratedFunction.DefineSync(
            context,
            math,
            "add",
            2,
            MathAddHostFunction,
            mathModule
        );
        return math;
      }
      catch
      {
        math.Dispose();
        throw;
      }
    }

    private static JavaScriptObject RegisterText(DotnetRuntimeContext context, JavaScriptObject modules)
    {
      var text = context.ModuleRegistry.DefineModule(modules, "Text");
      try
      {
        var textModule = context.ModuleRegistry.GetOrCreateModule("Text", static () => new TextModule());
        GeneratedFunction.DefineSync(
            context,
            text,
            "greet",
            1,
            TextGreetHostFunction,
            textModule
        );
        return text;
      }
      catch
      {
        text.Dispose();
        throw;
      }
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
