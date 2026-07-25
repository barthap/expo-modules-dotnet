using System;
using System.Threading.Tasks;
using Expo.JSI;
using Expo.ModulesCore;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Modules;

public sealed class GeneratedAttributeModuleTests
{
  [Fact]
  public void GeneratedProviderInstallsDirectAccessorProperties()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "const module = globalThis._expoDotnet.modules.GeneratedProperties; " +
          "const descriptor = Object.getOwnPropertyDescriptor(module, 'ready'); " +
          "module.ready = true; [module.ready, descriptor.enumerable, descriptor.configurable, " +
          "descriptor.get.length, descriptor.set.length].join(':')",
          "generated-attribute-properties-descriptor.js"
      );

      Assert.Equal("true:true:true:0:1", result.AsString());
      return true;
    });
  }

  [Fact]
  public void GeneratedProviderPropertiesPreserveReadOnlyAndExplicitNameShapes()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "const module = globalThis._expoDotnet.modules.GeneratedProperties; " +
          "const readOnly = (() => { 'use strict'; try { module.isReadOnly = false; return 'no error'; } catch (error) { return error instanceof TypeError; } })(); " +
          "const privateSetter = (() => { 'use strict'; try { module.privateSetter = false; return 'no error'; } catch (error) { return error instanceof TypeError; } })(); " +
          "[readOnly, privateSetter, module.privateSetter, module.privateSetterCallCount, " +
          "module.internalGetter, module.isReady, typeof module.readyWithExplicitName].join(':')",
          "generated-attribute-properties-read-only.js"
      );

      Assert.Equal("true:true:true:0:internal:false:undefined", result.AsString());
      return true;
    });
  }

  [Fact]
  public void GeneratedProviderPropertyErrorsAndCapturedAccessorRespectContextLifetime()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var beforeTeardown = fixture.Evaluate(
          "const module = globalThis._expoDotnet.modules.GeneratedProperties; " +
          "globalThis.__generatedPropertyGetter = Object.getOwnPropertyDescriptor(module, 'ready').get; " +
          "const getterError = (() => { try { module.throwingGetter; } catch (error) { return error.message; } })(); " +
          "const setterError = (() => { try { module.count = 'invalid'; } catch (error) { return error.message; } })(); " +
          "[getterError.includes('getter failed'), setterError.length > 0, module.countSetterCallCount, globalThis.__generatedPropertyGetter.call(module)].join(':')",
          "generated-attribute-properties-errors-before-teardown.js"
      );
      Assert.Equal("true:true:0:false", beforeTeardown.AsString());

      context.Dispose();

      using var afterTeardown = fixture.Evaluate(
          "try { globalThis.__generatedPropertyGetter(); 'no error'; } catch (error) { error.message; }",
          "generated-attribute-properties-errors-after-teardown.js"
      );
      Assert.Contains("DotnetRuntimeContext", afterTeardown.AsString());
      return true;
    });
  }

  [Fact]
  public void GeneratedProviderPropertyJavaScriptValueSetterDisposesInvocationWrapperAndKeepsRetainedCopy()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      var propertyModule = context.ModuleRegistry.GetOrCreateModule<GeneratedPropertiesModule>(
          "GeneratedProperties",
          static () => throw new InvalidOperationException("The generated module must already exist.")
      );

      fixture.ResetCounters();
      using var result = fixture.Evaluate(
          "const module = globalThis._expoDotnet.modules.GeneratedProperties; " +
          "module.value = 'stored'; [module.value, module.value].join(':')",
          "generated-attribute-properties-value-set.js"
      );
      Assert.Equal("stored:stored", result.AsString());

      // Assignment releases its invocation-owned decode wrapper. Both getter reads prove the
      // authored retained copy survived that release and each getter transferred an independent
      // wrapper through the host-function boundary.
      Assert.Equal(1u, fixture.Counters.ReleasedValues);

      propertyModule.Dispose();
      // The module releases exactly its one retained owner; context teardown later calls the
      // idempotent module disposal path again without releasing it a second time.
      Assert.Equal(2u, fixture.Counters.ReleasedValues);
      return true;
    });
  }

  [Fact]
  public void GeneratedProviderPropertyArrayBufferUsesGeneratedCodec()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "const module = globalThis._expoDotnet.modules.GeneratedProperties; " +
          "module.buffer = new Uint8Array([1, 2, 3]).buffer; Array.from(new Uint8Array(module.buffer)).join(',')",
          "generated-attribute-properties-array-buffer.js"
      );
      Assert.Equal("1,2,3", result.AsString());
      return true;
    });
  }

  [Fact]
  public void GeneratedProviderDispatchesExplicitNamedSyncFunction()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "globalThis._expoDotnet.modules.GeneratedMath.add(20.25, 22.25)",
          "generated-attribute-math-add.js"
      );

      Assert.Equal(JavaScriptValueKind.Number, result.Kind);
      Assert.Equal(42.5, result.AsDouble());
      return true;
    });
  }

  [Fact]
  public void GeneratedProviderDispatchesDefaultNamedSyncFunction()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "globalThis._expoDotnet.modules.GeneratedMath.addOneWhen(41.5, true)",
          "generated-attribute-math-default-name.js"
      );

      Assert.Equal(JavaScriptValueKind.Number, result.Kind);
      Assert.Equal(42.5, result.AsDouble());

      using var names = fixture.Evaluate(
          "typeof globalThis._expoDotnet.modules.GeneratedMath.addOneWhen + ':' + " +
          "typeof globalThis._expoDotnet.modules.GeneratedMath.AddOneWhen",
          "generated-attribute-math-default-name-alias.js"
      );

      Assert.Equal("function:undefined", names.AsString());
      return true;
    });
  }

  [Fact]
  public void GeneratedProviderReturnsUndefinedForVoidFunction()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "globalThis._expoDotnet.modules.GeneratedMath.storeNullable(7)",
          "generated-attribute-void-return.js"
      );

      Assert.Equal(JavaScriptValueKind.Undefined, result.Kind);
      return true;
    });
  }

  [Theory]
  [InlineData("null")]
  [InlineData("undefined")]
  public void GeneratedProviderAcceptsNullishNullableArguments(string argument)
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          $"globalThis._expoDotnet.modules.GeneratedMath.storeNullable({argument}); " +
          "globalThis._expoDotnet.modules.GeneratedMath.readNullable()",
          "generated-attribute-nullable-argument.js"
      );

      Assert.Equal(JavaScriptValueKind.Null, result.Kind);
      return true;
    });
  }

  [Theory]
  [InlineData("")]
  [InlineData("undefined")]
  public void GeneratedProviderUsesDefaultForMissingOrUndefinedOptionalNullableArguments(
      string argument
  )
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          $"globalThis._expoDotnet.modules.GeneratedMath.storeNullableWithDefault({argument}); " +
          "globalThis._expoDotnet.modules.GeneratedMath.readNullable()",
          "generated-attribute-nullable-default.js"
      );

      Assert.Equal(JavaScriptValueKind.Number, result.Kind);
      Assert.Equal(42.0, result.AsDouble());
      return true;
    });
  }

  [Fact]
  public void GeneratedProviderPreservesExplicitNullForOptionalNullableArguments()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "globalThis._expoDotnet.modules.GeneratedMath.storeNullableWithDefault(null); " +
          "globalThis._expoDotnet.modules.GeneratedMath.readNullable()",
          "generated-attribute-nullable-default-null.js"
      );

      Assert.Equal(JavaScriptValueKind.Null, result.Kind);
      return true;
    });
  }

  [Fact]
  public void GeneratedProviderSupportsAdditionalNumberPrimitiveConversions()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "const math = globalThis._expoDotnet.modules.GeneratedMath; " +
          "[math.roundTripInt(41.8), math.roundTripUInt(42.2), math.roundTripFloat(42.5)].join(':')",
          "generated-attribute-number-primitives.js"
      );

      Assert.Equal(JavaScriptValueKind.String, result.Kind);
      Assert.Equal("41:42:42.5", result.AsString());
      return true;
    });
  }

  [Fact]
  public void GeneratedProviderComposesNullableAdditionalNumberPrimitiveConversions()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "const math = globalThis._expoDotnet.modules.GeneratedMath; " +
          "math.storeNullableInt(41.8); const value = math.readNullableInt(); " +
          "math.storeNullableInt(null); `${value}:${math.readNullableInt() === null}`",
          "generated-attribute-nullable-number-primitive.js"
      );

      Assert.Equal(JavaScriptValueKind.String, result.Kind);
      Assert.Equal("41:true", result.AsString());
      return true;
    });
  }

  [Fact]
  public void GeneratedProviderPreservesStrings()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "globalThis._expoDotnet.modules.GeneratedText.greet('Zoë\\u0000JS')",
          "generated-attribute-text-greet.js"
      );

      Assert.Equal(JavaScriptValueKind.String, result.Kind);
      Assert.Equal("Hello, Zoë\0JS", result.AsString());
      return true;
    });
  }

  [Fact]
  public void GeneratedProviderSupportsStringBackedConvertiblePrimitives()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "const text = globalThis._expoDotnet.modules.GeneratedText; " +
          "const guid = text.roundTripGuid('46b59d07-31d0-4e6e-90fd-0f2979f2f5e7'); " +
          "const uri = text.roundTripUri('https://example.com/path?x=1'); " +
          "const instant = text.roundTripDateTimeOffset('2026-07-03T12:34:56.7890000+02:00'); " +
          "const span = text.roundTripTimeSpan('01:02:03'); " +
          "[guid, uri, instant, span].join('|')",
          "generated-attribute-convertibles.js"
      );

      Assert.Equal(JavaScriptValueKind.String, result.Kind);
      Assert.Equal(
          "46b59d07-31d0-4e6e-90fd-0f2979f2f5e7|https://example.com/path?x=1|2026-07-03T12:34:56.7890000+02:00|01:02:03",
          result.AsString()
      );
      return true;
    });
  }

  [Fact]
  public void GeneratedProviderPassesJavaScriptValueArgumentForInvocationLifetime()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "globalThis._expoDotnet.modules.GeneratedValues.readKind('hello')",
          "generated-attribute-javascript-value-argument.js"
      );

      Assert.Equal(JavaScriptValueKind.String, result.Kind);
      Assert.Equal("String", result.AsString());
      return true;
    });
  }

  [Fact]
  public async Task GeneratedProviderKeepsJavaScriptValueArgumentUntilAsyncSettles()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var setup = fixture.Evaluate(
          """
          globalThis.__generatedValueOutcome = 'pending';
          globalThis._expoDotnet.modules.GeneratedValues.readKindAsync('hello').then(
            value => { globalThis.__generatedValueOutcome = value; },
            error => { globalThis.__generatedValueOutcome = error && error.message ? error.message : String(error); }
          );
          true;
          """,
          "generated-attribute-javascript-value-async-argument.js"
      );
      return true;
    });

    await WaitForGeneratedValueOutcomeAsync(fixture);

    fixture.Runtime.Execute(_ =>
    {
      using var result = fixture.Evaluate(
          "globalThis.__generatedValueOutcome",
          "generated-attribute-javascript-value-async-argument-result.js"
      );
      Assert.Equal(JavaScriptValueKind.String, result.Kind);
      Assert.Equal("String", result.AsString());
      return true;
    });
  }

  [Fact]
  public void GeneratedProviderReturnsCreatedJavaScriptValue()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "globalThis._expoDotnet.modules.GeneratedValues.createString()",
          "generated-attribute-javascript-value-return-created.js"
      );

      Assert.Equal(JavaScriptValueKind.String, result.Kind);
      Assert.Equal("created", result.AsString());
      return true;
    });
  }

  [Fact]
  public void GeneratedProviderReturnsRetainedStoredJavaScriptValue()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "const values = globalThis._expoDotnet.modules.GeneratedValues; " +
          "values.storeString(); values.readStoredString()",
          "generated-attribute-javascript-value-return-stored.js"
      );

      Assert.Equal(JavaScriptValueKind.String, result.Kind);
      Assert.Equal("stored", result.AsString());
      return true;
    });
  }

  [Fact]
  public void GeneratedProviderReportsInvalidConvertiblePrimitiveAsJavaScriptError()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "try { " +
          "  globalThis._expoDotnet.modules.GeneratedText.roundTripUri('not a url'); " +
          "  'no error'; " +
          "} catch (e) { e.message; }",
          "generated-attribute-invalid-convertible.js"
      );

      Assert.Equal(JavaScriptValueKind.String, result.Kind);
      Assert.Contains("uri", result.AsString(), StringComparison.OrdinalIgnoreCase);
      return true;
    });
  }

  [Fact]
  public void GeneratedProviderSupportsReadOnlyListConversions()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "const labels = globalThis._expoDotnet.modules.GeneratedArray.labels(); " +
          "globalThis._expoDotnet.modules.GeneratedArray.sum([1, 2, 3.5]) + ':' + labels.join(',')",
          "generated-attribute-array.js"
      );

      Assert.Equal(JavaScriptValueKind.String, result.Kind);
      Assert.Equal("6.5:one,two", result.AsString());
      return true;
    });
  }

  [Theory]
  [InlineData("null")]
  [InlineData("undefined")]
  public void GeneratedProviderDecodesNullishIntoRequiredNullableStringParameters(string argument)
  {
    Assert.Equal(
        "null",
        EvaluateNullableModule(
            $"m.storeText({argument}); m.readText() === null ? 'null' : String(m.readText())",
            "generated-nullable-required-argument.js"
        )
    );

    Assert.Equal(
        "kept",
        EvaluateNullableModule(
            "m.storeText('kept'); m.readText()",
            "generated-nullable-required-argument-value.js"
        )
    );
  }

  [Fact]
  public void GeneratedProviderEncodesNullNullableStringReturnsAsJavaScriptNull()
  {
    Assert.Equal(
        "object:null",
        EvaluateNullableModule(
            "m.storeText(null); [typeof m.readText(), m.readText() === null ? 'null' : 'other'].join(':')",
            "generated-nullable-return.js"
        )
    );
  }

  // Without this the change could silently turn every existing `string` parameter into `string?`
  // and move failures from decode time into authored code.
  [Fact]
  public void GeneratedProviderStillRejectsNullForNonNullableStringParameters()
  {
    Assert.Equal(
        "rejected:false",
        EvaluateNullableModule(
            "const outcome = (() => { try { m.requireText(null); return 'no error'; } " +
            "catch (error) { return 'rejected'; } })(); " +
            "[outcome, m.readStrictCallSeen()].join(':')",
            "generated-nullable-strict-parameter.js"
        )
    );
  }

  [Theory]
  [InlineData("", "fallback")]
  [InlineData("undefined", "fallback")]
  [InlineData("null", "null")]
  public void GeneratedProviderHonoursDefaultsForOptionalNullableStringParameters(
      string argument,
      string expected)
  {
    Assert.Equal(
        expected,
        EvaluateNullableModule(
            $"m.storeTextWithDefault({argument}); m.readText() === null ? 'null' : m.readText()",
            "generated-nullable-optional-argument.js"
        )
    );
  }

  [Fact]
  public void GeneratedProviderRoundTripsNullableStringProperties()
  {
    Assert.Equal(
        "kept:true",
        EvaluateNullableModule(
            "m.text = 'kept'; const kept = m.text; m.text = null; [kept, m.text === null].join(':')",
            "generated-nullable-property.js"
        )
    );
  }

  [Fact]
  public void GeneratedProviderRoundTripsNullableRecordsAndNullableRecordFields()
  {
    Assert.Equal(
        "true:kept",
        EvaluateNullableModule(
            "[m.echoLabel(null) === null, m.echoLabel({ text: 'kept' }).text].join(':')",
            "generated-nullable-record.js"
        )
    );

    Assert.Equal(
        "true:nick",
        EvaluateNullableModule(
            "[m.echoProfile({ name: 'a', nickname: null }).nickname === null, " +
            "m.echoProfile({ name: 'a', nickname: 'nick' }).nickname].join(':')",
            "generated-nullable-record-field.js"
        )
    );
  }

  [Fact]
  public void GeneratedProviderRoundTripsNullableCollectionContainers()
  {
    Assert.Equal(
        "true:true:true:one:1:1",
        EvaluateNullableModule(
            "[m.echoList(null) === null, m.echoMap(null) === null, m.echoReadOnlyMap(null) === null, " +
            "m.echoList(['one'])[0], Object.keys(m.echoMap({ a: 'b' })).length, " +
            "Object.keys(m.echoReadOnlyMap({ a: 'b' })).length].join(':')",
            "generated-nullable-containers.js"
        )
    );
  }

  [Fact]
  public void GeneratedProviderPreservesNullsInsideCollectionContents()
  {
    Assert.Equal(
        "one:null:true",
        EvaluateNullableModule(
            "const elements = m.echoElements(['one', null]); " +
            "const values = m.echoValues({ kept: 'one', missing: null }); " +
            "[elements[0], elements[1] === null ? 'null' : 'other', values.missing === null].join(':')",
            "generated-nullable-contents.js"
        )
    );
  }

  [Fact]
  public void GeneratedProviderRoundTripsNullableByteArrays()
  {
    Assert.Equal(
        "true:1,2,3",
        EvaluateNullableModule(
            "[m.echoBytes(null) === null, " +
            "Array.from(new Uint8Array(m.echoBytes(new Uint8Array([1, 2, 3]).buffer))).join(',')].join(':')",
            "generated-nullable-byte-array.js"
        )
    );
  }

  [Fact]
  public async Task GeneratedProviderResolvesNullableStringTaskResultsWithJavaScriptNull()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var started = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.GeneratedNullable; " +
          "m.storeText(null); globalThis.__nullableAsync = 'pending'; " +
          "m.readTextAsync().then(value => { globalThis.__nullableAsync = value === null ? 'null' : 'other'; }); true",
          "generated-nullable-async.js"
      );
      Assert.True(started.AsBool());
      return true;
    });

    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
    while (DateTime.UtcNow < deadline)
    {
      fixture.DrainTasks();
      var settled = fixture.Runtime.Execute(_ =>
      {
        using var value = fixture.Evaluate(
            "globalThis.__nullableAsync",
            "generated-nullable-async-outcome.js"
        );
        return value.AsString();
      });

      if (settled != "pending")
      {
        Assert.Equal("null", settled);
        return;
      }

      await Task.Delay(10, TestContext.Current.CancellationToken);
    }

    Assert.Fail("Timed out waiting for the nullable async result.");
  }

  private static string EvaluateNullableModule(string script, string sourceUrl)
  {
    using var fixture = HermesRuntimeFixture.Create();

    return fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.GeneratedNullable; " + script,
          sourceUrl
      );
      return result.AsString();
    });
  }

  private static async Task WaitForGeneratedValueOutcomeAsync(HermesRuntimeFixture fixture)
  {
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
    while (DateTime.UtcNow < deadline)
    {
      fixture.DrainTasks();
      var settled = fixture.Runtime.Execute(_ =>
      {
        using var value = fixture.Evaluate(
            "globalThis.__generatedValueOutcome !== 'pending'",
            "generated-attribute-javascript-value-outcome-settled.js"
        );
        return value.AsBool();
      });

      if (settled)
      {
        return;
      }

      await Task.Delay(10, TestContext.Current.CancellationToken);
    }

    Assert.Fail("Timed out waiting for generated JavaScriptValue Promise outcome.");
  }
}
