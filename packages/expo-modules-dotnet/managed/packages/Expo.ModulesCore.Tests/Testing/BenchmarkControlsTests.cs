using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Testing;

// The benchmark controls do the same traversal or construction as a managed call in plain JSI C++.
// A control that created a value handle, or that silently read nothing, would make the benchmark
// compare a managed call against the wrong baseline.
public sealed class BenchmarkControlsTests
{
  private static string EvaluateString(HermesRuntimeFixture fixture, string source)
  {
    return fixture.Runtime.Execute(_ =>
    {
      using var result = fixture.Evaluate(source, "benchmark-controls.js");
      return result.AsString();
    });
  }

  private static void Run(HermesRuntimeFixture fixture, string source)
  {
    fixture.Runtime.Execute(_ =>
    {
      using var result = fixture.Evaluate(source, "benchmark-controls.js");
      return true;
    });
  }

  [Fact]
  public void ControlsAreAbsentUntilInstalled()
  {
    using var fixture = HermesRuntimeFixture.Create();

    Assert.Equal("undefined", EvaluateString(fixture, "typeof __expoBenchNative"));

    fixture.InstallBenchmarkControls();

    Assert.Equal("object", EvaluateString(fixture, "typeof __expoBenchNative"));
  }

  [Theory]
  [InlineData("JSON.stringify(__expoBenchNative.makeRecord4())", "{\"a\":1,\"b\":2,\"c\":3,\"d\":4}")]
  [InlineData("__expoBenchNative.makeString()", "abcdefghijklmnop")]
  [InlineData("String(Object.keys(__expoBenchNative.makeNested48()).length)", "4")]
  [InlineData("String(Object.keys(__expoBenchNative.makeNested48().r4).length)", "12")]
  [InlineData("JSON.stringify(__expoBenchNative.makeRecord12())", "{\"n1\":1,\"n2\":2,\"n3\":3,\"n4\":4,\"n5\":5,\"n6\":6,\"s1\":\"s1\",\"s2\":\"s2\",\"s3\":\"s3\",\"s4\":\"s4\",\"s5\":\"s5\",\"s6\":\"s6\"}")]
  [InlineData("JSON.stringify(__expoBenchNative.makeDictionary(3))", "{\"k0\":0,\"k1\":1,\"k2\":2}")]
  [InlineData("JSON.stringify(__expoBenchNative.makeNumbers(3))", "[0,1,2]")]
  [InlineData("JSON.stringify(__expoBenchNative.makeStrings(2))", "[\"s0\",\"s1\"]")]
  [InlineData("JSON.stringify(__expoBenchNative.makeRecords4(1))", "[{\"a\":1,\"b\":2,\"c\":3,\"d\":4}]")]
  [InlineData("JSON.stringify(__expoBenchNative.makeMixed(3))", "[0,\"s1\",true]")]
  [InlineData("String(__expoBenchNative.makeNumbers(1000).length)", "1000")]
  [InlineData("String(__expoBenchNative.makeUint8Array(1024).byteLength)", "1024")]
  [InlineData("String(__expoBenchNative.makeUint8Array(8) instanceof Uint8Array)", "true")]
  [InlineData("String(__expoBenchNative.makeArrayBuffer(16) instanceof ArrayBuffer)", "true")]
  [InlineData("String(__expoBenchNative.addDoubles(1.5, 2))", "3.5")]
  [InlineData("String(__expoBenchNative.noop())", "undefined")]
  [InlineData("String(__expoBenchNative.echoInt(7)) + __expoBenchNative.echoBoolean(true) + __expoBenchNative.echoDouble(0.5)", "7true0.5")]
  public void MakeControlsBuildTheDocumentedValues(string script, string expected)
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.InstallBenchmarkControls();

    Assert.Equal(expected, EvaluateString(fixture, script));
  }

  [Fact]
  public void TakeControlsAcceptWhatMakeControlsBuild()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.InstallBenchmarkControls();

    var script = @"
      var c = __expoBenchNative;
      var results = [
        c.takeString(c.makeString()),
        c.takeRecord4(c.makeRecord4()),
        c.takeRecord12(c.makeRecord12()),
        c.takeNested48(c.makeNested48()),
        c.takeDictionary(c.makeDictionary(50)),
        c.takeNumbers(c.makeNumbers(50)),
        c.takeStrings(c.makeStrings(50)),
        c.takeRecords4(c.makeRecords4(50)),
        c.takeMixed(c.makeMixed(50)),
        c.takeArrayBuffer(c.makeArrayBuffer(64)),
        c.takeUint8Array(c.makeUint8Array(64)),
        c.takeArrayBuffer(c.makeArrayBuffer(0)),
        c.takeUint8Array(c.makeUint8Array(0)),
        c.takeNumbers(c.makeNumbers(0)),
        c.takeDictionary(c.makeDictionary(0))
      ];
      results.every(function (r) { return r === undefined; }) ? 'ok' : 'not undefined'";

    Assert.Equal("ok", EvaluateString(fixture, script));
  }

  [Theory]
  [InlineData("takeRecord4(42)")]
  [InlineData("takeRecord4({ a: 1 })")]
  [InlineData("takeRecord12({ n1: 1 })")]
  [InlineData("takeNested48({ r1: 1 })")]
  [InlineData("takeString(1)")]
  [InlineData("takeDictionary({ k: 'x' })")]
  [InlineData("takeNumbers({})")]
  [InlineData("takeNumbers(['x'])")]
  [InlineData("takeStrings([1])")]
  [InlineData("takeRecords4([1])")]
  [InlineData("takeMixed([null])")]
  [InlineData("takeArrayBuffer({})")]
  [InlineData("takeUint8Array({})")]
  [InlineData("takeString()")]
  public void TakeControlRejectsAWrongShape(string call)
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.InstallBenchmarkControls();

    var control = call.Substring(0, call.IndexOf('('));
    var script = $"try {{ __expoBenchNative.{call}; 'no throw' }} catch (e) {{ (e instanceof Error) + ':' + e.message }}";
    var result = EvaluateString(fixture, script);

    Assert.StartsWith("true:" + control + " expects ", result);
  }

  [Fact]
  public void ControlsCreateNoValueHandles()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.InstallBenchmarkControls();
    Run(fixture, @"
      var c = __expoBenchNative;
      globalThis.args = {
        s: c.makeString(), r4: c.makeRecord4(), r12: c.makeRecord12(), n48: c.makeNested48(),
        d: c.makeDictionary(100), nums: c.makeNumbers(100), strs: c.makeStrings(100),
        recs: c.makeRecords4(100), mixed: c.makeMixed(100), ab: c.makeArrayBuffer(256),
        u8: c.makeUint8Array(256)
      };
      undefined");

    var allControls = @"
      var c = __expoBenchNative, a = globalThis.args;
      c.noop(); c.addDoubles(1, 2); c.echoBoolean(true); c.echoInt(1); c.echoDouble(1.5);
      c.takeString(a.s); c.makeString();
      c.takeRecord4(a.r4); c.makeRecord4();
      c.takeRecord12(a.r12); c.makeRecord12();
      c.takeNested48(a.n48); c.makeNested48();
      c.takeDictionary(a.d); c.makeDictionary(100);
      c.takeNumbers(a.nums); c.makeNumbers(100);
      c.takeStrings(a.strs); c.makeStrings(100);
      c.takeRecords4(a.recs); c.makeRecords4(100);
      c.takeMixed(a.mixed); c.makeMixed(100);
      c.takeArrayBuffer(a.ab); c.makeArrayBuffer(256);
      c.takeUint8Array(a.u8); c.makeUint8Array(256);
      undefined";
    var twin = @"
      var c = __expoBenchNative, a = globalThis.args;
      undefined";

    fixture.ResetCounters();
    Run(fixture, twin);
    var baseline = fixture.Counters;

    fixture.ResetCounters();
    Run(fixture, allControls);
    var withControls = fixture.Counters;

    Assert.Equal(baseline.OwnedValuesCreatedLinked, withControls.OwnedValuesCreatedLinked);
    Assert.Equal(baseline.OwnedValuesCreatedUnlinked, withControls.OwnedValuesCreatedUnlinked);
    Assert.Equal(baseline.BorrowedValuesCreated, withControls.BorrowedValuesCreated);
  }
}
