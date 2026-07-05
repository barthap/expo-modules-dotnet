using System;
using System.Threading.Tasks;
using Expo.JSI;
using Expo.ModulesCore;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Generated;

public sealed class GeneratedAttributeModuleTests
{
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
          "globalThis._expoDotnet.modules.GeneratedMath.AddOneWhen(41.5, true)",
          "generated-attribute-math-default-name.js"
      );

      Assert.Equal(JavaScriptValueKind.Number, result.Kind);
      Assert.Equal(42.5, result.AsDouble());
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
          "globalThis._expoDotnet.modules.GeneratedMath.StoreNullable(7)",
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
          $"globalThis._expoDotnet.modules.GeneratedMath.StoreNullable({argument}); " +
          "globalThis._expoDotnet.modules.GeneratedMath.ReadNullable()",
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
          $"globalThis._expoDotnet.modules.GeneratedMath.StoreNullableWithDefault({argument}); " +
          "globalThis._expoDotnet.modules.GeneratedMath.ReadNullable()",
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
          "globalThis._expoDotnet.modules.GeneratedMath.StoreNullableWithDefault(null); " +
          "globalThis._expoDotnet.modules.GeneratedMath.ReadNullable()",
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
          "[math.RoundTripInt(41.8), math.RoundTripUInt(42.2), math.RoundTripFloat(42.5)].join(':')",
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
          "math.StoreNullableInt(41.8); const value = math.ReadNullableInt(); " +
          "math.StoreNullableInt(null); `${value}:${math.ReadNullableInt() === null}`",
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
          "const guid = text.RoundTripGuid('46b59d07-31d0-4e6e-90fd-0f2979f2f5e7'); " +
          "const uri = text.RoundTripUri('https://example.com/path?x=1'); " +
          "const instant = text.RoundTripDateTimeOffset('2026-07-03T12:34:56.7890000+02:00'); " +
          "const span = text.RoundTripTimeSpan('01:02:03'); " +
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
          "globalThis._expoDotnet.modules.GeneratedValues.ReadKind('hello')",
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
          globalThis._expoDotnet.modules.GeneratedValues.ReadKindAsync('hello').then(
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
          "globalThis._expoDotnet.modules.GeneratedValues.CreateString()",
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
          "values.StoreString(); values.ReadStoredString()",
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
          "  globalThis._expoDotnet.modules.GeneratedText.RoundTripUri('not a url'); " +
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
