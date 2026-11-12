using Xunit;

namespace Expo.ModulesCore.Tests.Modules;

// Named unions decode through generated codecs that run inside the Hermes-backed test host. The
// fixtures live in UnionModuleFixtures.cs.
public sealed partial class LeafReleaseModuleTests
{
  private const string ScenarioSetup =
      "globalThis.us = globalThis._expoDotnet.modules.UnionScenarios; " +
      "globalThis.err = f => { try { f(); return 'no error'; } catch (e) { return `${e.code}|${e.message}`; } }; true";

  private const string MismatchCode = "ERR_UNION_CASE_MISMATCH|";

  [Fact]
  public void ThreeCasesDecodeByKind()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(ScenarioSetup);

    Assert.Equal(
        "base64:AAEC|bytes:1,2,3|length:12",
        leaf.ReadString("[us.describeNonce('AAEC'), us.describeNonce(new Uint8Array([1, 2, 3]).buffer), us.describeNonce(12)].join('|')"));
  }

  // An integer case before a double case takes a number only when the integer type holds it
  // exactly, so a fraction or an out-of-range number reaches the double case instead of being
  // truncated or overflowing.
  [Theory]
  [InlineData("3", "whole:3")]
  [InlineData("2.5", "fraction:2.5")]
  [InlineData("3e9", "fraction:3000000000")]
  [InlineData("-0", "whole:0")]
  [InlineData("2 ** 31 - 1", "whole:2147483647")]
  [InlineData("-(2 ** 31)", "whole:-2147483648")]
  [InlineData("2 ** 31", "fraction:2147483648")]
  [InlineData("NaN", "fraction:NaN")]
  [InlineData("Infinity", "fraction:Infinity")]
  public void IntegerGateSendsNonExactNumbersToTheLaterNumberCase(string input, string expected)
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(ScenarioSetup);

    Assert.Equal(expected, leaf.ReadString($"us.describeNumber({input})"));
  }

  // The last number case decodes like NumberCodec<int> outside a union.
  [Fact]
  public void LoneIntegerCaseTruncatesLikeItsCodec()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(ScenarioSetup);

    Assert.Equal("count:2|text:x", leaf.ReadString("[us.describeLone(2.5), us.describeLone('x')].join('|')"));
  }

  [Fact]
  public void FailedAttemptMovesToTheNextCase()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(ScenarioSetup);

    Assert.Equal(
        "address:https://expo.dev/x|text:hello",
        leaf.ReadString("[us.describeAddress('https://expo.dev/x'), us.describeAddress('hello')].join('|')"));
  }

  // Records are picked by a decode attempt, so the record with more required fields goes first,
  // and a value that fits neither reports the last attempt's failure as the cause.
  [Fact]
  public void RecordsArePickedByTheirFields()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(ScenarioSetup);

    Assert.Equal(
        "round:2|dot:1,2",
        leaf.ReadString("[us.describeShape({ x: 1, y: 1, radius: 2 }), us.describeShape({ x: 1, y: 2 })].join('|')"));
    Assert.Equal(
        MismatchCode + "'UnionShape' expected UnionCircle or UnionPoint, but received object\n→ Caused by: Value for property 'y' is required, got undefined",
        leaf.ReadString("err(() => us.describeShape({ x: 1 }))"));
  }

  [Fact]
  public void ListAndRecordInOneUnion()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(ScenarioSetup);

    Assert.Equal("items:1,2|named:a", leaf.ReadString("[us.describeItems([1, 2]), us.describeItems({ name: 'a' })].join('|')"));
  }

  [Fact]
  public void NestedUnionDecodesThroughItsOwnCases()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(ScenarioSetup);

    Assert.Equal(
        "flag:true|text:a|count:3",
        leaf.ReadString("[us.describeNested(true), us.describeNested('a'), us.describeNested(3)].join('|')"));
    // No case takes an object, so no case was attempted and there is no cause.
    Assert.Equal(
        MismatchCode + "'OuterChoice' expected bool or InnerChoice, but received object",
        leaf.ReadString("err(() => us.describeNested({}))"));
  }

  // The received name is the JavaScript typeof name, with null and ArrayBuffer named apart. Only the
  // object row attempts a case (the list), so only it has a cause; its text comes from the native
  // array check, so the test pins only that a cause is there.
  [Theory]
  [InlineData("undefined", "undefined", false)]
  [InlineData("null", "null", false)]
  [InlineData("5", "number", false)]
  [InlineData("'x'", "string", false)]
  [InlineData("() => 1", "function", false)]
  [InlineData("new ArrayBuffer(1)", "ArrayBuffer", false)]
  [InlineData("({})", "object", true)]
  public void MismatchNamesTheReceivedKind(string input, string received, bool attempted)
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(ScenarioSetup);
    UnionScenariosModule.Calls = 0;

    var expected = MismatchCode + $"'ItemsOrFlag' expected IReadOnlyList<int> or bool, but received {received}";
    var actual = leaf.ReadString($"err(() => us.describeFlags({input}))");
    if (attempted)
    {
      Assert.StartsWith(expected + "\n→ Caused by: ", actual);
    }
    else
    {
      Assert.Equal(expected, actual);
    }
    Assert.Equal(0, UnionScenariosModule.Calls);
  }

  [Fact]
  public void NonNullableUnionRejectsNullishWithTheMismatchError()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(ScenarioSetup);
    UnionScenariosModule.Calls = 0;

    var result = leaf.ReadString(
        "[err(() => us.describeSource(42)), err(() => us.describeSource(null)), err(() => us.describeSource(undefined)), err(() => us.describeSource(true))].join('#')");

    Assert.Equal(
        string.Join('#',
            MismatchCode + "'UnionSource' expected string or UnionSourceOptions, but received number",
            MismatchCode + "'UnionSource' expected string or UnionSourceOptions, but received null",
            MismatchCode + "'UnionSource' expected string or UnionSourceOptions, but received undefined",
            MismatchCode + "'UnionSource' expected string or UnionSourceOptions, but received boolean"),
        result);
    Assert.Equal(0, UnionScenariosModule.Calls);
  }

  [Fact]
  public void NullableUnionPassesNullAndReturnsNull()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(ScenarioSetup);

    Assert.Equal(
        "null|null|text:x|true",
        leaf.ReadString("[us.describeOptionalSource(null), us.describeOptionalSource(undefined), us.describeOptionalSource('x'), us.maybeSource() === null].join('|')"));
  }

  [Fact]
  public void EncodeWritesThePayloadOfTheCaseItHolds()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(ScenarioSetup);

    Assert.Equal(
        "a|true|1,2|3",
        leaf.ReadString("const b = us.makeBytes(); [us.makeBase64(), b instanceof ArrayBuffer, new Uint8Array(b).join(','), us.makeLength()].join('|')"));
    // A null non-nullable union is a catchable error, not a crash.
    Assert.NotEqual("no error", leaf.ReadString("err(() => us.makeNullNonce())"));
  }

  // Spec rule 6 (settles the unverified point): a JavaScriptValue dictionary decodes every object,
  // so a later object-only case can never be chosen.
  [Theory]
  [InlineData("({ a: 1, b: 2 })")]
  [InlineData("[1, 2]")]
  [InlineData("new Uint8Array(3)")]
  [InlineData("Object.create(null)")]
  [InlineData("new Map([[1, 2]])")]
  [InlineData("new Date(0)")]
  [InlineData("Object.defineProperty({}, 'hidden', { value: 1, enumerable: false })")]
  public void JavaScriptValueDictionaryCaseTakesEveryObject(string input)
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(ScenarioSetup);

    Assert.StartsWith("bag:", leaf.ReadString($"us.describeObject({input})"));
  }

  [Fact]
  public void ReferencedAssemblyPayloadRoundTrips()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(ScenarioSetup);

    Assert.Equal(
        "foo:a:2|text:x|{\"name\":\"a\",\"count\":2}",
        leaf.ReadString("[us.describeExternal({ name: 'a', count: 2 }), us.describeExternal('x'), JSON.stringify(us.makeExternal())].join('|')"));
  }

  private const string FallbackSetup =
      ScenarioSetup + "; globalThis.b = new ArrayBuffer(4); globalThis.b2 = new ArrayBuffer(2); true";

  // The Packed attempt decodes the buffer, then fails on count. The record codec releases that lease
  // before the Raw case is tried, and the Raw payload is released once when the call ends.
  [Fact]
  public void FailedRecordAttemptReleasesItsLeaseAndFallsThrough()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(FallbackSetup);

    leaf.Fixture.ResetCounters();
    Assert.Equal("raw:object", leaf.ReadString("us.fallback({ data: b, count: 'x' })"));
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Throws<ObjectDisposedException>(() => _ = UnionScenariosModule.LastRaw!.Kind);

    leaf.Fixture.ResetCounters();
    Assert.Equal("packed:2", leaf.ReadString("us.fallback({ data: b, count: 2 })"));
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public async Task FailedAttemptReleasesItsLeaseInAnAsyncCall()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(FallbackSetup);

    leaf.Fixture.ResetCounters();
    leaf.StartOutcome("us.fallbackLater({ data: b, count: 'x' })");
    await leaf.WaitForOutcomeAsync();
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("fulfilled:raw:object", leaf.ReadString("`${__outcome.status}:${__outcome.value}`"));
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Throws<ObjectDisposedException>(() => _ = UnionScenariosModule.LastRaw!.Kind);
  }

  [Theory]
  [InlineData("us.fallbackField({ value: { data: b, count: 'x' }, tag: 7 })", "raw:object:7", 1u)]
  [InlineData("us.fallbackAll([{ data: b, count: 'x' }, { data: b2, count: 1 }])", "raw:object|packed:1", 2u)]
  [InlineData("[us.fallbackOptional(null), us.fallbackOptional({ data: b, count: 'x' })].join('|')", "null|raw:object", 1u)]
  [InlineData("us.takeFallback(() => ({ data: b, count: 'x' }))", "raw:object", 1u)]
  public void FailedAttemptReleasesItsLeaseInEveryDecodePosition(string call, string expected, uint releases)
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(FallbackSetup);

    leaf.Fixture.ResetCounters();
    Assert.Equal(expected, leaf.ReadString(call));
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal(releases, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  // An event payload stays with the author: only the parameter lease releases.
  [Fact]
  public void UnionEventPayloadStaysWithTheAuthor()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(FallbackSetup + "; globalThis.seen = false; us.addListener('onFallback', p => { seen = p.data === b && p.count === 1; }); true");

    leaf.Fixture.ResetCounters();
    Assert.Equal("4:true", leaf.ReadString("`${us.emitPacked(b)}:${seen}`"));
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  // A case whose construction throws after its payload decoded releases that payload and stops:
  // the later Raw case, which would take an ArrayBuffer, is not tried.
  [Fact]
  public void CaseConstructionFailureReleasesThePayloadAndStops()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(FallbackSetup);

    leaf.Fixture.ResetCounters();
    Assert.Equal("undefined|rejected length", leaf.ReadString("err(() => us.describeStrict(new ArrayBuffer(3)))"));
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    Assert.Equal("buffer:2", leaf.ReadString("us.describeStrict(new ArrayBuffer(2))"));
  }
}
