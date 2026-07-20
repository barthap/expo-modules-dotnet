using System.Collections.Generic;
using System.Linq;
using Expo.JSI;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Codecs;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Generated;

public sealed class GeneratedCodecExpansionModuleTests
{
  [Fact]
  public void GeneratedLookingCodeDecodesAndEncodesDictionary()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      GeneratedCodecExpansionModuleProvider.Register(context, modules);
      using var result = fixture.Evaluate(
          "globalThis._expoDotnet.modules.CodecExpansion.total({ first: 2, second: 3.5 })",
          "dictionary-total.js"
      );

      Assert.Equal(5.5, result.AsDouble());
      return true;
    });
  }

  [Fact]
  public void GeneratedLookingCodeEncodesDictionaryAsPlainObject()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      GeneratedCodecExpansionModuleProvider.Register(context, modules);
      using var result = fixture.Evaluate(
          "const value = globalThis._expoDotnet.modules.CodecExpansion.labels(); value.one + ',' + value.two",
          "dictionary-labels.js"
      );

      Assert.Equal("first,second", result.AsString());
      return true;
    });
  }

  [Fact]
  public void GeneratedLookingCodeUsesStringEnumByDefault()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      GeneratedCodecExpansionModuleProvider.Register(context, modules);
      using var result = fixture.Evaluate(
          "globalThis._expoDotnet.modules.CodecExpansion.describeMode('Fast')",
          "enum-mode.js"
      );

      Assert.Equal("Fast", result.AsString());
      return true;
    });
  }

  [Fact]
  public void GeneratedProviderDecodesAndEncodesPositionalRecord()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      using var result = fixture.Evaluate(
          "globalThis._expoDotnet.modules.GeneratedRecords.rename({ name: 'Ada', age: 37 }).name",
          "record-user.js"
      );

      Assert.Equal("Ada!", result.AsString());
      return true;
    });
  }

  [Fact]
  public void GeneratedProviderDecodesAndEncodesRecordClassAndStruct()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      using var classResult = fixture.Evaluate(
          "globalThis._expoDotnet.modules.GeneratedRecords.renameClass({ name: 'Grace', age: 40 }).name",
          "record-class.js"
      );
      using var structResult = fixture.Evaluate(
          "globalThis._expoDotnet.modules.GeneratedRecords.renameStruct({ name: 'Katherine', age: 42 }).name",
          "record-struct.js"
      );

      Assert.Equal("Grace!", classResult.AsString());
      Assert.Equal("Katherine!", structResult.AsString());
      return true;
    });
  }

  [Fact]
  public void GeneratedProviderDecodesAndEncodesNestedPositionalRecord()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      using var result = fixture.Evaluate(
          "const user = globalThis._expoDotnet.modules.GeneratedRecords.moveNested({ name: 'Ada', address: { city: 'London' }, status: 'Draft' }); user.address.city + ':' + user.status",
          "record-nested.js"
      );

      Assert.Equal("London!:Published", result.AsString());
      return true;
    });
  }

  [Fact]
  public void GeneratedProviderRejectsStalePascalCaseForRequiredRecordField()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      using var result = fixture.Evaluate(
          """
          try {
            globalThis._expoDotnet.modules.GeneratedRecords.rename({ Name: 'Ada', Age: 37 });
            false;
          } catch (error) {
            error instanceof Error;
          }
          """,
          "record-stale-pascal-required.js"
      );

      Assert.True(result.AsBool());
      return true;
    });
  }

  [Fact]
  public void GeneratedProviderDoesNotReadStalePascalCaseForNullableRecordField()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      using var result = fixture.Evaluate(
          "globalThis._expoDotnet.modules.GeneratedRecords.renameNullable({ name: 'Ada', LuckyNumber: 42 }).luckyNumber === null",
          "record-stale-pascal-nullable.js"
      );

      Assert.True(result.AsBool());
      return true;
    });
  }

  private enum Mode
  {
    Slow,
    Fast,
  }

  private sealed class CodecExpansionModule
  {
    public double Total(Dictionary<string, double> values) => values.Values.Sum();

    public IReadOnlyDictionary<string, string> Labels() =>
        new Dictionary<string, string>
        {
          ["one"] = "first",
          ["two"] = "second",
        };

    public Mode DescribeMode(Mode mode) => mode;
  }

  private static class GeneratedCodecExpansionModuleProvider
  {
    public static void Register(DotnetRuntimeContext context, JavaScriptObject modules)
    {
      using var module = context.ModuleRegistry.DefineModule(modules, "CodecExpansion");
      var instance = new CodecExpansionModule();
      GeneratedFunction.DefineSync(context, module, "total", 1, TotalHostFunction, instance);
      GeneratedFunction.DefineSync(context, module, "labels", 0, LabelsHostFunction, instance);
      GeneratedFunction.DefineSync(context, module, "describeMode", 1, DescribeModeHostFunction, instance);
    }

    private static JavaScriptValue TotalHostFunction(
        JavaScriptRuntime runtime,
        JavaScriptValueRef thisValue,
        JavaScriptArguments arguments,
        object context)
    {
      GeneratedFunction.RequireArgumentCount("CodecExpansion.total", arguments, 1);
      var module = (CodecExpansionModule)context;
      var values = JavaScriptDictionaryCodec<double, DoubleCodec>.DecodeToDictionary(
          arguments.GetValue(0),
          runtime
      );
      return DoubleCodec.Encode(module.Total(values), runtime);
    }

    private static JavaScriptValue LabelsHostFunction(
        JavaScriptRuntime runtime,
        JavaScriptValueRef thisValue,
        JavaScriptArguments arguments,
        object context)
    {
      GeneratedFunction.RequireArgumentCount("CodecExpansion.labels", arguments, 0);
      var module = (CodecExpansionModule)context;
      return JavaScriptDictionaryCodec<string, StringCodec>.Encode(module.Labels(), runtime);
    }

    private static JavaScriptValue DescribeModeHostFunction(
        JavaScriptRuntime runtime,
        JavaScriptValueRef thisValue,
        JavaScriptArguments arguments,
        object context)
    {
      GeneratedFunction.RequireArgumentCount("CodecExpansion.describeMode", arguments, 1);
      var module = (CodecExpansionModule)context;
      var mode = StringEnumCodec<Mode>.Decode(arguments.GetValue(0), runtime);
      return StringEnumCodec<Mode>.Encode(module.DescribeMode(mode), runtime);
    }
  }
}
