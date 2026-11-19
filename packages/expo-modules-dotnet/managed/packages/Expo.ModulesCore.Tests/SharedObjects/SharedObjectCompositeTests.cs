using System;
using System.Collections.Generic;
using Expo.JSI;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.SharedObjects;

// A concrete shared object is an ordinary codec element: each list element, dictionary value, and
// record field decodes to the registry's managed instance and encodes to its paired object, with
// the context of the binding that runs.
public sealed partial class SharedObjectCompositeTests
{
  private const string ErrHelper =
      "const err = f => { try { f(); return 'no error'; } " +
      "catch (error) { return error instanceof Error ? error.message.split('\\n')[0] : typeof error; } }; ";

  private static T Run<T>(Func<HermesRuntimeFixture, T> body)
  {
    using var fixture = HermesRuntimeFixture.Create();
    return fixture.Runtime.Execute(runtime =>
    {
      CompositeEntriesModule.Reset();
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      using var setup = fixture.Evaluate(
          "globalThis.m = globalThis._expoDotnet.modules.CompositeEntries; " +
          "globalThis.a = new m.Entry('a'); globalThis.b = new m.Entry('b'); 'ready'",
          "shared-object-composite-setup.js"
      );
      return body(fixture);
    });
  }

  private static string Evaluate(HermesRuntimeFixture fixture, string source)
  {
    using var result = fixture.Evaluate(source, "shared-object-composite.js");
    return result.AsString();
  }

  [Fact]
  public void ListOfNullableSharedObjectsRoundTripsWithIdentity()
  {
    Run(fixture =>
    {
      var result = Evaluate(
          fixture,
          "const r = m.echoList([a, null, b, a]); String(r[0] === a && r[1] === null && r[2] === b && r[3] === a && r.length === 4)"
      );

      Assert.Equal("true", result);
      var seen = CompositeEntriesModule.LastList!;
      Assert.Equal(4, seen.Count);
      Assert.Same(Entry.Constructed[0], seen[0]);
      Assert.Null(seen[1]);
      Assert.Same(Entry.Constructed[1], seen[2]);
      Assert.Same(Entry.Constructed[0], seen[3]);
      return true;
    });
  }

  // A record field reaches the shared-object codec only through the context that the generated
  // record codec forwards, so identity on both sides proves the right context arrives.
  [Fact]
  public void DictionaryAndRecordHoldSharedObjects()
  {
    Run(fixture =>
    {
      var result = Evaluate(
          fixture,
          "const d = m.echoDictionary({ x: a, y: b }); " +
          "const p = m.echoPair({ first: a, second: b }); " +
          "const q = m.echoPair({ first: b, second: null }); " +
          "[d.x === a, d.y === b, p.first === a, p.second === b, q.first === b, q.second === null].join(':')"
      );

      Assert.Equal("true:true:true:true:true:true", result);
      var a = Entry.Constructed[0];
      var b = Entry.Constructed[1];
      Assert.Same(a, CompositeEntriesModule.LastDictionary!["x"]);
      Assert.Same(b, CompositeEntriesModule.LastDictionary!["y"]);
      Assert.Same(b, CompositeEntriesModule.LastPair!.First);
      Assert.Null(CompositeEntriesModule.LastPair!.Second);
      return true;
    });
  }

  [Fact]
  public void RecordFieldsDecodeToTheManagedInstances()
  {
    Run(fixture =>
    {
      Evaluate(fixture, "m.echoPair({ first: a, second: b }); 'done'");

      Assert.Same(Entry.Constructed[0], CompositeEntriesModule.LastPair!.First);
      Assert.Same(Entry.Constructed[1], CompositeEntriesModule.LastPair!.Second);
      return true;
    });
  }

  [Fact]
  public void FirstPairingHappensInsideAComposite()
  {
    Run(fixture =>
    {
      var result = Evaluate(
          fixture,
          "const first = m.makeEntries(); const second = m.makeEntries(); " +
          "[first.length, first.every(e => Object.getPrototypeOf(e) === m.Entry.prototype), " +
          "first.every((e, i) => e === second[i]), first.map(e => e.label).join('')].join(':')"
      );

      Assert.Equal("3:true:true:012", result);
      return true;
    });
  }

  [Theory]
  [InlineData("({})")]
  [InlineData("null")]
  [InlineData("released")]
  [InlineData("other")]
  public void WrongOrReleasedElementFailsBeforeAuthoredCode(string element)
  {
    Run(fixture =>
    {
      Evaluate(fixture, "globalThis.released = new m.Entry('r'); released.release(); globalThis.other = new m.OtherEntry(); 'ready'");
      CompositeEntriesModule.CallCount = 0;

      var parts = Evaluate(
          fixture,
          ErrHelper +
          $"const v = {element}; " +
          "[err(() => m.echoStrict([a, v])), err(() => m.takeOne(v))].join('|')"
      ).Split('|');

      Assert.Equal(0, CompositeEntriesModule.CallCount);
      Assert.NotEqual("no error", parts[0]);
      // The element fails exactly as the same value does at a direct parameter.
      Assert.Equal(parts[1], parts[0]);
      return true;
    });
  }

  [Fact]
  public void EncodeFailureAtLaterElementLeavesEarlierElementPaired()
  {
    Run(fixture =>
    {
      var result = Evaluate(
          fixture,
          ErrHelper +
          "const second = new m.Entry('second'); m.stashForEncode(second); second.release(); " +
          "const failure = err(() => m.returnStashed()); " +
          "const first = m.returnNativeFirst(); " +
          "[failure, Object.getPrototypeOf(first) === m.Entry.prototype, first.label, m.returnNativeFirst() === first].join('|')"
      );

      var parts = result.Split('|');
      Assert.Contains("released", parts[0], StringComparison.Ordinal);
      Assert.Equal("true", parts[1]);
      Assert.Equal("native", parts[2]);
      Assert.Equal("true", parts[3]);
      // The failed encode neither released nor disposed the first instance.
      Assert.Equal(1, Entry.ReleaseCount);
      return true;
    });
  }

  [Fact]
  public void InstancesStayActiveAfterCompositeCall()
  {
    Run(fixture =>
    {
      var result = Evaluate(
          fixture,
          "m.echoList([a, b]); m.echoDictionary({ x: a }); m.echoPair({ first: a, second: b }); " +
          "const again = m.echoList([a, b]); " +
          "[again[0] === a, a.label, b.label, m.takeOne(a) === a].join(':')"
      );

      Assert.Equal("true:a:b:true", result);
      Assert.Equal(0, Entry.ReleaseCount);
      return true;
    });
  }

  // A record or list case decodes and encodes its Entry fields through the registry, and the union
  // still picks the case by the shape of the value.
  [Fact]
  public void UnionPayloadRecordFieldRoundTripsSharedObject()
  {
    Run(fixture =>
    {
      var result = Evaluate(
          fixture,
          "const t = m.echoChoice('text'); " +
          "const r = m.echoChoice({ first: a, second: b }); " +
          "const l = m.echoChoice([b, a]); " +
          "[r.first === a, r.second === b, l.length, l[0] === b, l[1] === a, t].join(':')"
      );

      Assert.Equal("true:true:2:true:true:text", result);
      // The last call decoded the list case.
      var a = Entry.Constructed[0];
      var b = Entry.Constructed[1];
      var listed = Assert.IsType<EntryChoice.Listed>(CompositeEntriesModule.LastChoice);
      Assert.Same(b, listed.Value[0]);
      Assert.Same(a, listed.Value[1]);
      return true;
    });
  }

  [Fact]
  public void UnionRecordCaseDecodesToTheManagedInstances()
  {
    Run(fixture =>
    {
      Evaluate(fixture, "m.echoChoice({ first: a, second: null }); 'done'");

      var held = Assert.IsType<EntryChoice.Held>(CompositeEntriesModule.LastChoice);
      Assert.Same(Entry.Constructed[0], held.Value.First);
      Assert.Null(held.Value.Second);
      return true;
    });
  }
}
