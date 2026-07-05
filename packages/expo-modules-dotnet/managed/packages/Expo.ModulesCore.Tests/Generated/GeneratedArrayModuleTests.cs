using System.Collections.Generic;
using System.Linq;
using Expo.JSI;
using Expo.ModulesCore;
using Expo.ModulesCore.Codecs;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Generated;

public sealed class GeneratedArrayModuleTests
{
  [Fact]
  public void GeneratedLookingCodeDecodesJavaScriptArrayIntoReadOnlyListParameter()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      GeneratedArrayModuleProvider.Register(context, modules);

      using var result = fixture.Evaluate(
          "globalThis._expoDotnet.modules.Array.sum([1, 2, 3.5])",
          "array-sum.js"
      );

      Assert.Equal(JavaScriptValueKind.Number, result.Kind);
      Assert.Equal(6.5, result.AsDouble());
      return true;
    });
  }

  [Fact]
  public void GeneratedLookingCodeEncodesReadOnlyListReturnAsJavaScriptArray()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      GeneratedArrayModuleProvider.Register(context, modules);

      using var result = fixture.Evaluate(
          "const labels = globalThis._expoDotnet.modules.Array.labels(); Array.isArray(labels) && labels.join(',')",
          "array-labels.js"
      );

      Assert.Equal(JavaScriptValueKind.String, result.Kind);
      Assert.Equal("one,two", result.AsString());
      return true;
    });
  }

  private sealed class ArrayModule
  {
    public double Sum(IReadOnlyList<double> values) => values.Sum();

    public IReadOnlyList<string> Labels() => ["one", "two"];
  }

  private static class GeneratedArrayModuleProvider
  {
    public static void Register(DotnetRuntimeContext context, JavaScriptObject modules)
    {
      using var array = context.ModuleRegistry.DefineModule(modules, "Array");

      GeneratedFunction.DefineSync(
          context,
          array,
          "sum",
          1,
          SumHostFunction,
          new ArrayModule()
      );
      GeneratedFunction.DefineSync(
          context,
          array,
          "labels",
          0,
          LabelsHostFunction,
          new ArrayModule()
      );
    }

    private static JavaScriptValue SumHostFunction(
        JavaScriptRuntime runtime,
        JavaScriptValueRef thisValue,
        JavaScriptArguments arguments,
        object context)
    {
      GeneratedFunction.RequireArgumentCount("Array.sum", arguments, 1);

      var module = (ArrayModule)context;
      var values = JavaScriptArrayCodec<double, DoubleCodec>.DecodeToArray(
          arguments.GetValue(0),
          runtime
      );
      return DoubleCodec.Encode(module.Sum(values), runtime);
    }

    private static JavaScriptValue LabelsHostFunction(
        JavaScriptRuntime runtime,
        JavaScriptValueRef thisValue,
        JavaScriptArguments arguments,
        object context)
    {
      GeneratedFunction.RequireArgumentCount("Array.labels", arguments, 0);

      var module = (ArrayModule)context;
      return JavaScriptArrayCodec<string, StringCodec>.Encode(module.Labels(), runtime);
    }
  }
}
