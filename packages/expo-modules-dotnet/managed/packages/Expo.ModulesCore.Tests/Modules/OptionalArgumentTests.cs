using System;
using System.Threading.Tasks;
using Expo.JSI;
using Expo.ModulesCore;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Modules;

public sealed class OptionalArgumentTests
{
  private const string Helpers =
      "const m = globalThis._expoDotnet.modules.OptionalArguments; " +
      "const t = (f) => { try { return String(f()); } catch (e) { return 'ERR:' + e.message; } }; ";

  [Fact]
  public void SyncFunctionOmitsOneAndTwoTrailingNullableArguments()
  {
    Assert.Equal(
        "a|b|null;a|null|null;a|b|3",
        Run("[t(() => m.describe('a', 'b')), t(() => m.describe('a')), t(() => m.describe('a', 'b', 3))].join(';')")
    );
  }

  [Fact]
  public void SyncFunctionStillRejectsAMissingNonNullableArgument()
  {
    var result = Run("[t(() => m.describe()), t(() => m.requiredOnly())].join(';')");

    Assert.Equal(
        "ERR:OptionalArguments.describe expects between 1 and 3 arguments, got 0.;" +
        "ERR:OptionalArguments.requiredOnly expects 1 arguments, got 0.",
        result
    );
  }

  [Fact]
  public void SyncFunctionStillRejectsExtraArguments()
  {
    var result = Run("[t(() => m.describe('a', 'b', 3, 4)), t(() => m.requiredOnly('a', 'b'))].join(';')");

    Assert.Equal(
        "ERR:OptionalArguments.describe expects between 1 and 3 arguments, got 4.;" +
        "ERR:OptionalArguments.requiredOnly expects 1 arguments, got 2.",
        result
    );
  }

  [Fact]
  public void NullableParameterBeforeARequiredParameterStaysRequired()
  {
    var result = Run(
        "[t(() => m.nullableBeforeRequired()), t(() => m.nullableBeforeRequired('a')), " +
        "t(() => m.nullableBeforeRequired(undefined, 'b')), t(() => m.nullableBeforeRequired(null, 'b'))].join(';')"
    );

    Assert.Equal(
        "ERR:OptionalArguments.nullableBeforeRequired expects 2 arguments, got 0.;" +
        "ERR:OptionalArguments.nullableBeforeRequired expects 2 arguments, got 1.;" +
        "null|b;null|b",
        result
    );
  }

  [Fact]
  public void NullableParameterBeforeADefaultedParameterIsOptional()
  {
    Assert.Equal(
        "null|fallback;a|fallback;a|b",
        Run("[t(() => m.nullableThenDefaulted()), t(() => m.nullableThenDefaulted('a')), t(() => m.nullableThenDefaulted('a', 'b'))].join(';')")
    );
  }

  [Fact]
  public void FunctionLengthIsTheRequiredCount()
  {
    Assert.Equal(
        "1,1,2,0,1",
        Run("[m.describe.length, m.requiredOnly.length, m.nullableBeforeRequired.length, m.nullableThenDefaulted.length, m.sumThenLabel.length].join(',')")
    );
  }

  [Fact]
  public void SpanFunctionOmitsATrailingNullableArgument()
  {
    Assert.Equal(
        "6;9;ERR:OptionalArguments.sumThenLabel expects between 1 and 2 arguments, got 0.",
        Run("const b = new Uint8Array([1, 2, 3]).buffer; [t(() => m.sumThenLabel(b)), t(() => m.sumThenLabel(b, 'abc')), t(() => m.sumThenLabel())].join(';')")
    );
  }

  [Fact]
  public void SharedObjectMethodOmitsTrailingNullableArguments()
  {
    Assert.Equal(
        "x|null|null/a|null|null;x|null|null/a|b|null",
        Run("const o = new m.OptionalBox('x'); [t(() => o.describe('a')), t(() => o.describe('a', 'b'))].join(';')")
    );
  }

  [Fact]
  public void SharedObjectMethodStillRejectsMissingAndExtraArguments()
  {
    Assert.Equal(
        "ERR:OptionalBox.describe expects between 1 and 3 arguments, got 0.;" +
        "ERR:OptionalBox.describe expects between 1 and 3 arguments, got 4.",
        Run("const o = new m.OptionalBox('x'); [t(() => o.describe()), t(() => o.describe('a', 'b', 3, 4))].join(';')")
    );
  }

  [Fact]
  public void SharedObjectConstructorOmitsOneAndTwoTrailingNullableArguments()
  {
    Assert.Equal(
        "x|null|null/z|null|null;x|y|null/z|null|null;x|y|3/z|null|null",
        Run("[t(() => new m.OptionalBox('x').describe('z')), t(() => new m.OptionalBox('x', 'y').describe('z')), t(() => new m.OptionalBox('x', 'y', 3).describe('z'))].join(';')")
    );
  }

  [Fact]
  public void SharedObjectConstructorStillRejectsMissingAndExtraArguments()
  {
    Assert.Equal(
        "ERR:new OptionalBox expects between 1 and 3 arguments, got 0.;" +
        "ERR:new OptionalBox expects between 1 and 3 arguments, got 4.",
        Run("[t(() => new m.OptionalBox()), t(() => new m.OptionalBox('x', 'y', 3, 4))].join(';')")
    );
  }

  [Fact]
  public async Task AsyncFunctionResolvesWithOmittedTrailingNullableArguments()
  {
    var result = await RunAsync(
        "Promise.all([m.describeAsync('a'), m.describeAsync('a', 'b'), m.describeAsync('a', 'b', 3)]).then(v => v.join(';'))"
    );

    Assert.Equal("a|null|null;a|b|null;a|b|3", result);
  }

  [Fact]
  public async Task AsyncFunctionRejectsMissingAndExtraArguments()
  {
    var result = await RunAsync(
        "Promise.all([m.describeAsync(), m.describeAsync('a', 'b', 3, 4)].map(p => p.then(() => 'resolved', e => 'ERR:' + e.message))).then(v => v.join(';'))"
    );

    Assert.Equal(
        "ERR:OptionalArguments.describeAsync expects between 1 and 3 arguments, got 0.;" +
        "ERR:OptionalArguments.describeAsync expects between 1 and 3 arguments, got 4.",
        result
    );
  }

  [Fact]
  public async Task SharedObjectAsyncMethodResolvesWithOmittedTrailingNullableArgument()
  {
    var result = await RunAsync(
        "(() => { const o = new m.OptionalBox('x'); return Promise.all([o.describeAsync('a'), o.describeAsync('a', 'b')]).then(v => v.join(';')); })()"
    );

    Assert.Equal("x|null|null/a|null;x|null|null/a|b", result);
  }

  private static string Run(string expression)
  {
    using var fixture = HermesRuntimeFixture.Create();
    return fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(Helpers + expression, "optional-arguments.js");
      return result.AsString();
    });
  }

  private static async Task<string> RunAsync(string expression)
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var started = fixture.Evaluate(
          Helpers +
          "globalThis.__optionalResult = undefined; " +
          $"{expression}.then(v => {{ globalThis.__optionalResult = v; }}, e => {{ globalThis.__optionalResult = 'REJECTED:' + e.message; }}); true;",
          "optional-arguments-async.js"
      );
      return true;
    });

    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
    while (DateTime.UtcNow < deadline)
    {
      fixture.DrainTasks();
      var settled = fixture.Runtime.Execute(_ =>
      {
        using var value = fixture.Evaluate("globalThis.__optionalResult", "optional-arguments-read.js");
        return value.IsNullish ? null : value.AsString();
      });
      if (settled is not null)
      {
        return settled;
      }

      await Task.Delay(10, TestContext.Current.CancellationToken);
    }

    Assert.Fail("Timed out waiting for the Promise to settle.");
    return string.Empty;
  }
}
