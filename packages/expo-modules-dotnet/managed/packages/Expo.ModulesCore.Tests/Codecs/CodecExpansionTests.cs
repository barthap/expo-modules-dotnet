using System.Collections.Generic;
using System.Linq;
using Expo.JSI;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Codecs;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Codecs;

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

  // The defaults reach authored code with their exact value: the enum member, TimeSpan.Zero, NaN,
  // -Infinity, negative zero (which `-0` used to lose), a byte, and an escaped string.
  [Fact]
  public void GeneratedProviderAppliesEnumValueTypeAndEdgeParameterDefaults()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      using var result = fixture.Evaluate(
          """
          const m = globalThis._expoDotnet.modules.GeneratedParameterDefaults;
          [
            m.pick(),
            m.pick(undefined),
            m.pickNullable(),
            m.pickNullable(null),
            m.wait(),
            Number.isNaN(m.edge(0)),
            m.edge(1) === -Infinity,
            Object.is(m.edge(2), -0),
            m.small(),
            m.escaped() === 'q"b\\n\n\t\u2028\u00e9\u{1F600}',
            new m.ParameterDefaultPicker().picked,
          ].join(':')
          """,
          "parameter-defaults.js"
      );

      Assert.Equal("Fast:Fast:Slow:null:0:true:true:true:5:true:Fast", result.AsString());
      return true;
    });
  }

  private static string EvaluateRecordDefaults(string expression, string sourceUrl)
  {
    using var fixture = HermesRuntimeFixture.Create();
    return fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      using var result = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.GeneratedRecordDefaults;\n" + expression,
          sourceUrl
      );
      return result.AsString();
    });
  }

  // An absent key and an explicit undefined both mean "not supplied", as on iOS and for C#
  // function parameters, so both get the authored default.
  [Fact]
  public void GeneratedProviderAppliesOmittedOrUndefinedRecordFieldDefaults()
  {
    Assert.Equal(
        "a:5:x|a:5:x",
        EvaluateRecordDefaults(
            "[m.describe({ name: 'a' }), m.describe({ name: 'a', count: undefined, label: undefined })].join('|')",
            "record-default-omitted.js"
        )
    );
  }

  [Fact]
  public void GeneratedProviderPrefersSuppliedRecordFieldValueOverDefault()
  {
    Assert.Equal(
        "a:41:y",
        EvaluateRecordDefaults("m.describe({ name: 'a', count: 41, label: 'y' })", "record-default-supplied.js")
    );
  }

  // Decided: explicit null on a nullable field keeps null (Android and our parameter rule).
  [Fact]
  public void GeneratedProviderKeepsExplicitNullOnNullableDefaultedRecordField()
  {
    Assert.Equal(
        "a:5:null|a:5:x",
        EvaluateRecordDefaults(
            "[m.describe({ name: 'a', label: null }), m.describe({ name: 'a' })].join('|')",
            "record-default-null-nullable.js"
        )
    );
  }

  // Null is a value, not "not supplied": it goes to NumberCodec, which rejects it before the
  // authored method runs.
  [Fact]
  public void GeneratedProviderRejectsExplicitNullOnNonNullableDefaultedRecordField()
  {
    Assert.Equal(
        "true:0",
        EvaluateRecordDefaults(
            """
            const before = m.calls;
            let failed;
            try { m.describe({ name: 'a', count: null }); failed = 'no error'; }
            catch (error) { failed = String(error instanceof Error); }
            failed + ':' + (m.calls - before)
            """,
            "record-default-null-required.js"
        )
    );
  }

  [Fact]
  public void GeneratedProviderAppliesEnumAndValueTypeRecordDefaults()
  {
    Assert.Equal(
        "Fast:0:Slow|Slow:0:null",
        EvaluateRecordDefaults(
            "[m.describeSettings({}), m.describeSettings({ mode: 'Slow', fallback: null })].join('|')",
            "record-default-settings.js"
        )
    );
  }

  // The rule holds at every depth: a nested record, list elements, dictionary values. An omitted
  // record-struct field with `= default` is default(T), whose Mode is Slow, while `settings: {}`
  // runs the struct's own field defaults and gives Fast.
  [Fact]
  public void GeneratedProviderAppliesRecordDefaultsInNestedRecordsAndCollections()
  {
    Assert.Equal(
        "a:5:x|b:5:x,c:2:x|k=d:5:null|Slow;a:5:x|||Fast",
        EvaluateRecordDefaults(
            """
            [
              m.describeHolder({ inner: { name: 'a' }, items: [{ name: 'b' }, { name: 'c', count: 2 }], byKey: { k: { name: 'd', label: null } } }),
              m.describeHolder({ inner: { name: 'a' }, items: [], byKey: {}, settings: {} }),
            ].join(';')
            """,
            "record-default-nested.js"
        )
    );
  }

  // A callback result decodes through the owned Decode overload, which must apply the same rule.
  [Fact]
  public void GeneratedProviderAppliesRecordDefaultsOnTheOwnedDecodePath()
  {
    Assert.Equal(
        "a:5:x",
        EvaluateRecordDefaults("m.takeOptions(() => ({ name: 'a' }))", "record-default-callback.js")
    );
  }

  // Defaults are inbound only: encode writes every field, also one equal to its default.
  [Fact]
  public void GeneratedProviderEncodesEveryRecordFieldIncludingDefaults()
  {
    Assert.Equal(
        "name,count,label:5:x|name,count,label:5:x",
        EvaluateRecordDefaults(
            """
            const echoed = m.echo({ name: 'a' });
            const made = m.makeDefault('z');
            [echoed, made].map(r => Object.keys(r).join(',') + ':' + r.count + ':' + r.label).join('|')
            """,
            "record-default-encode.js"
        )
    );
  }

  // Record field defaults use the same renderer as parameters, so the edge constants survive
  // decode with the default and encode back to JavaScript.
  [Fact]
  public void GeneratedProviderAppliesEdgeConstantRecordDefaults()
  {
    Assert.Equal(
        "true,true,true,true,true,true,true",
        EvaluateRecordDefaults(
            """
            const e = m.echoEdges({});
            [
              Number.isNaN(e.nan),
              e.low === -Infinity,
              Object.is(e.zero, -0),
              e.small === 7,
              e.big === 9007199254740991,
              e.ratio === 0.5,
              e.text === 'q"b\\n\n\u2028\u00e9\u{1F600}',
            ].join(',')
            """,
            "record-default-edges.js"
        )
    );
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
