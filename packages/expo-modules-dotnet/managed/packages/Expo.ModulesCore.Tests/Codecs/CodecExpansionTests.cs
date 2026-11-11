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

  // `string Name = null` is accepted: an omitted or undefined field decodes as null, a supplied
  // one wins.
  [Fact]
  public void GeneratedProviderDecodesOmittedNullDefaultOnNonNullableReferenceRecordField()
  {
    Assert.Equal(
        "null:5|null:5|z:5",
        EvaluateRecordDefaults(
            "[m.describeNullName({}), m.describeNullName({ name: undefined }), m.describeNullName({ name: 'z' })].join('|')",
            "record-default-null-reference.js"
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
            error instanceof Error && error.message.includes("Value for property 'name' is required, got undefined");
          }
          """,
          "record-stale-pascal-required.js"
      );

      Assert.True(result.AsBool());
      return true;
    });
  }

  // The old PascalCase key is a different key, so it is never read. A defaulted field keeps its
  // default instead of taking the PascalCase value, and a required field fails with the message.
  [Fact]
  public void GeneratedProviderIgnoresStalePascalCaseForDefaultedRecordFields()
  {
    Assert.Equal(
        "a:5:x|true:0",
        EvaluateRecordDefaults(
            $$"""
            const kept = m.describe({ name: 'a', Count: 41, Label: 'y' });
            const before = m.calls;
            let stale;
            try { m.describe({ Name: 'a', Count: 41 }); stale = 'no error'; }
            catch (error) { stale = String(error instanceof Error && error.message.includes("{{RequiredNameMessage}}")); }
            kept + '|' + stale + ':' + (m.calls - before)
            """,
            "record-stale-pascal-defaulted.js"
        )
    );
  }

  private const string RequiredNameMessage = "Value for property 'name' is required, got undefined";

  // Before this item the error came from StringCodec and did not name the field.
  [Fact]
  public void GeneratedProviderNamesMissingRequiredRecordField()
  {
    Assert.Equal(
        "true:true:0",
        EvaluateRecordDefaults(
            $$"""
            const records = globalThis._expoDotnet.modules.GeneratedRecords;
            let renamed;
            try { records.rename({ age: 37 }); renamed = 'no error'; }
            catch (error) { renamed = String(error instanceof Error && error.message.includes("{{RequiredNameMessage}}")); }
            const before = m.calls;
            let described;
            try { m.describe({ count: 1 }); described = 'no error'; }
            catch (error) { described = String(error instanceof Error && error.message.includes("{{RequiredNameMessage}}")); }
            [renamed, described, m.calls - before].join(':')
            """,
            "record-required-missing.js"
        )
    );
  }

  // Null is a value: it reaches StringCodec, which fails with its own error, not the
  // required-field error.
  [Fact]
  public void ExplicitNullOnRequiredRecordFieldFailsThroughItsCodec()
  {
    Assert.Equal(
        "true:false",
        EvaluateRecordDefaults(
            """
            let outcome;
            try { m.describe({ name: null }); outcome = 'no error'; }
            catch (error) { outcome = (error instanceof Error) + ':' + error.message.includes('is required'); }
            outcome
            """,
            "record-required-null.js"
        )
    );
  }

  // The owned Decode overload (callback result) applies the same check; Invoke() throws and the
  // failure reaches JavaScript through the sync host function.
  [Fact]
  public void CallbackResultMissingRequiredRecordFieldFailsInvoke()
  {
    Assert.Equal(
        "true",
        EvaluateRecordDefaults(
            $$"""
            let outcome;
            try { m.takeOptions(() => ({ count: 1 })); outcome = 'no error'; }
            catch (error) { outcome = String(error instanceof Error && error.message.includes("{{RequiredNameMessage}}")); }
            outcome
            """,
            "record-required-callback.js"
        )
    );
  }

  // Guard: fields that are optional by type do not get the required check.
  [Fact]
  public void NullableAndJavaScriptValueRecordFieldsStayOptional()
  {
    Assert.Equal(
        "true:Undefined",
        EvaluateRecordDefaults(
            """
            const records = globalThis._expoDotnet.modules.GeneratedRecords;
            [records.renameNullable({ name: 'a' }).luckyNumber === null, m.valueKind({ name: 'a' })].join(':')
            """,
            "record-optional-by-type.js"
        )
    );
  }

  // An async method decodes arguments inside its outer try, so the failure rejects the Promise
  // and the call does not throw synchronously.
  [Fact]
  public async Task AsyncMethodRejectsWhenRequiredRecordFieldIsMissing()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      using var setup = fixture.Evaluate(
          $$"""
          const m = globalThis._expoDotnet.modules.GeneratedRecordDefaults;
          globalThis.__requiredOutcome = 'pending';
          const before = m.calls;
          try {
            m.describeAsync({ count: 1 }).then(
              () => { globalThis.__requiredOutcome = 'fulfilled'; },
              error => {
                globalThis.__requiredOutcome = 'rejected:' +
                  (error instanceof Error && error.message.includes("{{RequiredNameMessage}}"));
              }
            );
          } catch (error) {
            globalThis.__requiredOutcome = 'sync-thrown';
          }
          // Read now: the runtime context is disposed when this Execute returns, and the decode
          // failure happens before authored code runs, so a count change here would mean it ran.
          globalThis.__requiredCalls = m.calls - before;
          true
          """,
          "record-required-async.js"
      );
      return true;
    });

    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
    string outcome;
    while (true)
    {
      fixture.DrainTasks();
      outcome = fixture.Runtime.Execute(_ =>
      {
        using var value = fixture.Evaluate(
            "globalThis.__requiredOutcome + ':' + globalThis.__requiredCalls",
            "record-required-async-read.js"
        );
        return value.AsString();
      });
      if (!outcome.StartsWith("pending", StringComparison.Ordinal) || DateTime.UtcNow > deadline)
      {
        break;
      }

      await Task.Delay(10, TestContext.Current.CancellationToken);
    }

    Assert.Equal("rejected:true:0", outcome);
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
