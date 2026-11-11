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

  // A usage-level [JSEnum] wins over the enum type's. A record field has no usage-level attribute,
  // so it keeps the type's Number.
  [Fact]
  public async Task UsageLevelRepresentationWinsOverTheEnumType()
  {
    using var host = CreateHost();

    Assert.Equal("\"Fast\"", await ReadAsync(host, "m.describeNumberModeAsString('Fast')"));
    Assert.Equal("{\"mode\":1}", await ReadAsync(host, "m.echoNumberModeHolder({ mode: 1 })"));
  }

  [Fact]
  public async Task UsageLevelNumberReachesANullableEnum()
  {
    using var host = CreateHost();

    Assert.Equal("[1,null]", await ReadAsync(host, "[m.echoPlainAsNullableNumber(1), m.echoPlainAsNullableNumber(null)]"));
  }

  // Enum.TryParse accepted the string of an underlying number. Only declared strings decode now.
  [Theory]
  [InlineData("0")]
  [InlineData("1")]
  public async Task PlainEnumRejectsNumberStrings(string input)
  {
    using var host = CreateHost();

    var outcome = await CatchAsync(host, $"m.echoPlain('{input}')");

    AssertUncodedError(outcome, "sync");
    Assert.Equal($"'{input}' is not a valid {Ns}.PlainMode value. It must be one of: 'Slow', 'Fast'", outcome.Message);
  }

  // Enum.ToString encoded 42 as '42'. A value that names no member has no JavaScript string now.
  [Fact]
  public async Task PlainEnumRejectsAnUndefinedValueOnEncode()
  {
    using var host = CreateHost();

    var outcome = await CatchAsync(host, "m.plainOutOfRange()");

    AssertUncodedError(outcome, "sync");
    Assert.Equal($"42 is not a member of {Ns}.PlainMode, so it has no JavaScript value.", outcome.Message);
  }

  // 'SHA-1' reaches authored code as SHA1, and SHA1 goes back as 'SHA-1'. MD5 has no value, so its
  // C# name crosses both ways.
  [Fact]
  public async Task DeclaredStringsCrossBothWays()
  {
    using var host = CreateHost();

    Assert.Equal(
        "[\"SHA1\",\"MD5\",\"SHA256\"]",
        await ReadAsync(host, "[m.describeAlgorithm('SHA-1'), m.describeAlgorithm('MD5'), m.describeAlgorithm('SHA-256')]"));
    Assert.Equal("[\"SHA-1\",\"MD5\",\"SHA-256\"]", await ReadAsync(host, "[m.echoAlgorithm('SHA-1'), m.echoAlgorithm('MD5'), m.echoAlgorithm('SHA-256')]"));
  }

  // Matching is exact and ordinal: the C# name of a member that has a value, another case, extra
  // whitespace, a number string, and the empty string all fail before authored code runs, and the
  // message lists every accepted string so a JavaScript caller can fix the call.
  [Theory]
  [InlineData("'SHA1'", "SHA1")]
  [InlineData("'sha-1'", "sha-1")]
  [InlineData("' SHA-1'", " SHA-1")]
  [InlineData("'SHA-1 '", "SHA-1 ")]
  [InlineData("'1'", "1")]
  [InlineData("''", "")]
  public async Task OnlyDeclaredStringsDecode(string input, string received)
  {
    using var host = CreateHost();

    var outcome = await CatchAsync(host, $"m.describeAlgorithm({input})");

    AssertUncodedError(outcome, "sync");
    Assert.Equal($"'{received}' is not a valid {Ns}.DigestAlgorithm value. It must be one of: 'MD5', 'SHA-1', 'SHA-256'", outcome.Message);
  }

  // A value that is not a string fails exactly as it does for a string parameter.
  [Theory]
  [InlineData("1")]
  [InlineData("null")]
  [InlineData("({})")]
  public async Task NonStringValuesFailLikeAStringParameter(string input)
  {
    using var host = CreateHost();

    var expected = await CatchAsync(host, $"m.echoText({input})");
    var outcome = await CatchAsync(host, $"m.describeAlgorithm({input})");

    Assert.True(outcome.IsError);
    Assert.Equal(expected, outcome);
  }

  [Fact]
  public async Task UndefinedValuesDoNotEncode()
  {
    using var host = CreateHost();

    var outcome = await CatchAsync(host, "m.undefinedAlgorithm()");

    AssertUncodedError(outcome, "sync");
    Assert.Equal($"42 is not a member of {Ns}.DigestAlgorithm, so it has no JavaScript value.", outcome.Message);
  }

  // An async function decodes its arguments before it starts, and a bad string rejects the promise
  // instead of throwing synchronously.
  [Fact]
  public async Task AsyncFunctionsRejectForAnUndeclaredString()
  {
    using var host = CreateHost();

    var outcome = await CatchAsync(host, "m.hashAsync('MD2')");

    AssertUncodedError(outcome, "async");
    Assert.Equal($"'MD2' is not a valid {Ns}.DigestAlgorithm value. It must be one of: 'MD5', 'SHA-1', 'SHA-256'", outcome.Message);
    Assert.Equal("\"SHA1\"", await ReadAsync(host, "m.hashAsync('SHA-1')"));
  }

  // A record field, a list element, a nullable value, and a dictionary value decode and encode
  // through the same codec, and a bad string inside them fails with the same message.
  [Fact]
  public async Task DeclaredStringsInCompositePositions()
  {
    using var host = CreateHost();

    Assert.Equal("{\"algorithm\":\"SHA-1\"}", await ReadAsync(host, "m.echoOptions({ algorithm: 'SHA-1' })"));
    Assert.Equal("[\"SHA-1\",\"MD5\"]", await ReadAsync(host, "m.echoList(['SHA-1', 'MD5'])"));
    Assert.Equal("[\"SHA-1\",null,null]", await ReadAsync(host, "[m.echoNullable('SHA-1'), m.echoNullable(null), m.echoNullable(undefined)]"));
    Assert.Equal("{\"a\":\"SHA-1\",\"b\":\"SHA-256\"}", await ReadAsync(host, "m.echoMap({ a: 'SHA-1', b: 'SHA-256' })"));
    var expected = $"'sha-1' is not a valid {Ns}.DigestAlgorithm value. It must be one of: 'MD5', 'SHA-1', 'SHA-256'";
    Assert.Equal(expected, (await CatchAsync(host, "m.echoList(['SHA-1', 'sha-1'])")).Message);
    Assert.Equal(expected, (await CatchAsync(host, "m.echoOptions({ algorithm: 'sha-1' })")).Message);
  }

  [Fact]
  public async Task DeclaredStringsInOtherPositions()
  {
    using var host = CreateHost();

    Assert.Equal(
        "[\"SHA-1\",\"MD5\",\"SHA-256\"]",
        await ReadAsync(host, "[m.preferred, (m.preferred = 'MD5', m.preferred), m.strongest]"));
    Assert.Equal("\"SHA-1\"", await ReadAsync(host, "m.echoLater('SHA-1')"));
    // The callback receives SHA1 as 'SHA-1' with its tuple partner, and its 'SHA-256' result decodes.
    Assert.Equal(
        "\"SHA-256\"",
        await ReadAsync(host, "m.callWith((algorithm, count) => algorithm === 'SHA-1' && count === 2 ? 'SHA-256' : 'MD5')"));
    Assert.Equal(
        "[\"SHA-1\",\"SHA-1\",\"SHA-256\",\"SHA-256\",\"MD5\"]",
        await ReadAsync(
            host,
            "(() => { const h = new m.DigestHasher('SHA-1'); const first = h.algorithm; const previous = h.swap('SHA-256'); " +
            "return [first, previous, h.algorithm, new m.DigestHasher().algorithm, (h.algorithm = 'MD5', h.algorithm)]; })()"));
  }

  [Fact]
  public async Task EventPayloadsCarryDeclaredStrings()
  {
    using var host = CreateHost();

    Assert.Equal(
        "[\"SHA-1\",\"SHA-1\"]",
        await ReadAsync(
            host,
            "(() => { const seen = []; m.addListener('onAlgorithm', value => seen.push(value)); " +
            "m.addListener('onOptions', value => seen.push(value.algorithm)); return m.emitDigest('SHA-1').then(() => seen); })()"));
  }

  // A default is the member itself (a cast of its number), so an omitted argument or field is the
  // default member and goes back through its declared string.
  [Fact]
  public async Task DefaultsAreMembersNotStrings()
  {
    using var host = CreateHost();

    Assert.Equal("[\"SHA-1\",\"SHA-1\",\"MD5\"]", await ReadAsync(host, "[m.pickDefault(), m.pickDefault(undefined), m.pickDefault('MD5')]"));
    Assert.Equal("{\"label\":\"a\",\"algorithm\":\"SHA-1\"}", await ReadAsync(host, "m.echoSettings({ label: 'a' })"));
    Assert.Equal("{\"label\":\"b\",\"algorithm\":\"SHA-256\"}", await ReadAsync(host, "m.echoSettings({ label: 'b', algorithm: 'SHA-256' })"));
  }

  // Each [Flags] member crosses as its name; a combination names no member, so it fails to encode.
  [Fact]
  public async Task FlagsMembersCrossButCombinationsDoNotEncode()
  {
    using var host = CreateHost();

    Assert.Equal("\"Spaced\"", await ReadAsync(host, "m.echoFlags('Spaced')"));
    var outcome = await CatchAsync(host, "m.combinedFlags()");
    AssertUncodedError(outcome, "sync");
    Assert.Equal($"3 is not a member of {Ns}.DigestFlags, so it has no JavaScript value.", outcome.Message);
  }

  // Number enums do not change, also when a member carries [EnumMember] without Value.
  [Fact]
  public async Task NumberEnumsKeepCrossingAsNumbers()
  {
    using var host = CreateHost();

    Assert.Equal("[1,2]", await ReadAsync(host, "[m.echoNumberMode(1), m.echoQuiet(2)]"));
    Assert.True((await CatchAsync(host, "m.echoQuiet('High')")).IsError);
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
