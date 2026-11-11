using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Testing;
using Expo.ModulesCore.Tests.Errors;
using Xunit;

namespace Expo.ModulesCore.Tests.Modules;

// Enums cross through generated codecs. JavaScript ported from upstream passes the raw strings of
// Swift and Kotlin enums, so these tests pin the exact strings and error messages JavaScript sees.
// The fixtures live in EnumModuleFixtures.cs.
public sealed class EnumModuleTests
{
  private const string Ns = "Expo.ModulesCore.Tests.Modules";

  // Plain enums keep crossing as their C# member names both ways, case-sensitively. This must hold
  // before and after string enums move to generated codecs.
  [Fact]
  public async Task PlainEnumMembersCrossAsTheirNames()
  {
    using var host = CreateHost();

    Assert.Equal("[\"Fast\",\"Slow\"]", await ReadAsync(host, "[m.echoPlain('Fast'), m.echoPlain('Slow')]"));
    var outcome = await CatchAsync(host, "m.echoPlain('fast')");
    AssertUncodedError(outcome, "sync");
    Assert.StartsWith($"'fast' is not a valid {Ns}.PlainMode value.", outcome.Message, StringComparison.Ordinal);
  }

  // Pins the StringEnumCodec behavior that the generated enum codec changes: Enum.TryParse also
  // accepts the string of an underlying number.
  [Fact]
  public async Task PlainEnumDecodesANumberStringThroughEnumTryParse()
  {
    using var host = CreateHost();

    Assert.Equal("[\"Slow\",\"Fast\"]", await ReadAsync(host, "[m.echoPlain('0'), m.echoPlain('1')]"));
  }

  // Pins the StringEnumCodec behavior that the generated enum codec changes: a value that names no
  // member encodes as its number.
  [Fact]
  public async Task PlainEnumEncodesAnUndefinedValueAsItsNumber()
  {
    using var host = CreateHost();

    Assert.Equal("\"42\"", await ReadAsync(host, "m.plainOutOfRange()"));
  }

  private static ExpoModuleTestHost CreateHost() =>
      ExpoModuleTestHost.Create(ExpoModulesProvider_Expo_ModulesCore_Tests.Register);

  // Runs the expression with `m` bound to the EnumScenarios module, waits for it when it is a
  // promise, and returns JSON.stringify of the result, so one string pins strings, numbers, null,
  // arrays, and objects exactly.
  private static async Task<string> ReadAsync(ExpoModuleTestHost host, string expression)
  {
    var value = await host.EvaluatePromiseAsync(
        $$"""
        (() => {
          const m = globalThis._expoDotnet.modules.EnumScenarios;
          return Promise.resolve({{expression}}).then(result => JSON.stringify(result));
        })()
        """,
        TestContext.Current.CancellationToken
    );
    return CodedErrorModuleTests.ReadString(host, value);
  }

  // Runs the expression and describes the error it throws ("sync") or rejects with ("async").
  private static async Task<ErrorOutcome> CatchAsync(ExpoModuleTestHost host, string expression)
  {
    var value = await host.EvaluatePromiseAsync(
        $$"""
        (() => {
          const m = globalThis._expoDotnet.modules.EnumScenarios;
          const describe = (e, mode) => [
            mode,
            e instanceof Error,
            e && e.name,
            e !== null && typeof e === 'object' && 'code' in e,
            String(e && e.code),
            e && e.message
          ].join('\u001f');
          let result;
          try { result = {{expression}}; } catch (e) { return Promise.resolve(describe(e, 'sync')); }
          return Promise.resolve(result).then(() => 'no error', e => describe(e, 'async'));
        })()
        """,
        TestContext.Current.CancellationToken
    );
    return ErrorOutcome.Parse(CodedErrorModuleTests.ReadString(host, value));
  }

  // Enum codec errors are uncoded, as upstream codec errors are.
  private static void AssertUncodedError(ErrorOutcome outcome, string mode)
  {
    Assert.Equal(mode, outcome.Mode);
    Assert.True(outcome.IsError);
    Assert.Equal("Error", outcome.Name);
    Assert.False(outcome.HasCode);
  }
}
