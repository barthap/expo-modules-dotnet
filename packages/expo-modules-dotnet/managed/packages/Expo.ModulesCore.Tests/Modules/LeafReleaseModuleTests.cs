using System;
using System.Threading;
using System.Threading.Tasks;
using Expo.JSI;
using Expo.ModulesCore;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Modules;

// Generated bindings release every direct JavaScriptValue and ArrayBuffer leaf exactly once through
// the codec Release member, for the result first and then the arguments. Lease counts use
// JS-created buffers, whose release reaches LongLivedArrayBuffersReleased after the runtime goes
// idle. ReleasedValues also counts traversal temporaries, so JavaScriptValue assertions compare
// against a leaf-free twin call.
public sealed class LeafReleaseModuleTests
{
  [Fact]
  public void SyncLeafParameterReleasesOnceOnSuccessAndThrow()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); true");

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString(
        "const measured = m.measure(b); " +
        "let failed; try { m.measureOrThrow(b); failed = 'no error'; } " +
        "catch (error) { failed = error instanceof Error && error.message.includes('measure failed'); } " +
        "`${measured}:${failed}`"
    );
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("4:true", result);
    Assert.Equal(2u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public async Task LaterDecodeFailureReleasesEarlierLeafSyncAndAsync()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b1 = new ArrayBuffer(4); globalThis.b2 = new ArrayBuffer(4); true");

    // The buffer decodes before the count decode throws; the catch still releases it.
    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString(
        "let failed; try { m.measureWithCount(b1, 'x'); failed = 'no error'; } " +
        "catch (error) { failed = error instanceof Error; } String(failed)"
    );
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("true", result);
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    // Async: the outer catch releases the argument before it returns the rejected Promise.
    leaf.Fixture.ResetCounters();
    leaf.StartOutcome("m.measureWithCountAsync(b2, 'x')");
    await leaf.WaitForOutcomeAsync();
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("rejected:Error", leaf.ReadString("`${__outcome.status}:${__outcome.name}`"));
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void SyncMethodReturningItsInputBufferKeepsIdentityAndReleasesOnce()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); true");

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString("String(m.same(b) === b)");
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("true", result);
    // The decoded lease and the returned lease are one object: Release disposes it once.
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void SyncMethodReturningItsInputValueResolvesIt()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.o = {}; true");

    leaf.Fixture.ResetCounters();
    Assert.Equal("true", leaf.ReadString("String(m.text('x') === 'x')"));
    var twinReleases = leaf.Fixture.Counters.ReleasedValues;

    leaf.Fixture.ResetCounters();
    Assert.Equal("true", leaf.ReadString("String(m.sameValue(o) === o)"));
    var leafReleases = leaf.Fixture.Counters.ReleasedValues;

    // The returned input is one wrapper that Release disposes once; the bridge receives an independent copy.
    Assert.Equal(twinReleases + 1, leafReleases);
  }

  [Fact]
  public void SyncReturnedRetainedValueCopyReleasesOnce()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.o = {}; true");

    // Calibration: the same call shape where the returned wrapper is the argument itself.
    leaf.Fixture.ResetCounters();
    Assert.Equal("true", leaf.ReadString("String(m.sameValue(o) === o)"));
    var aliasReleases = leaf.Fixture.Counters.ReleasedValues;

    leaf.Fixture.ResetCounters();
    Assert.Equal("true", leaf.ReadString("String(m.retainedValue(o) === o)"));
    var copyReleases = leaf.Fixture.Counters.ReleasedValues;

    // The returned Retain() copy is one more wrapper, and the glue releases it after the encode.
    Assert.Equal(aliasReleases + 1, copyReleases);
  }

  [Fact]
  public void ReturnedRetainedCopyOfInputReleasesBothLeases()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); true");

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString("String(m.retained(b) === b)");
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("true", result);
    // The decoded lease and the Retain() copy share one entry, which releases only when both leases drop.
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void SameBufferInTwoParametersReleasesBothLeases()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); true");

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString("String(m.measureTwo(b, b))");
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("8", result);
    // Each decode creates its own lease over the same JS buffer; equal leaves are not aliases.
    Assert.Equal(2u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void ReturnedDisposedValueFailsThroughCatchableError()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); true");

    var result = leaf.ReadString(
        "let failed; try { m.disposedValue(); failed = 'no error'; } " +
        "catch (error) { failed = error instanceof Error; } " +
        "`${failed}:${m.measure(b)}`"
    );

    Assert.Equal("true:4", result);
  }

  [Fact]
  public async Task AsyncMethodReturningItsInputValueResolvesWithIt()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.o = {}; true");

    leaf.StartOutcome("m.sameValueAsync(o)");
    await leaf.WaitForOutcomeAsync();

    Assert.Equal("fulfilled:true", leaf.ReadString("`${__outcome.status}:${__outcome.value === o}`"));
  }

  [Fact]
  public void AsyncReturnedRetainedValueCopyReleasesOnce()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.o = {}; true");

    // Each call is measured over one paused settlement, so no outcome polling lands in the count.
    // Calibration: the same call shape where the returned wrapper is the argument itself.
    var aliasReleases = CountSettlementReleases(leaf, "m.holdValueAsync(o)");
    Assert.Equal("fulfilled:true", leaf.ReadString("`${__outcome.status}:${__outcome.value === o}`"));

    var copyReleases = CountSettlementReleases(leaf, "m.holdRetainedValueAsync(o)");
    Assert.Equal("fulfilled:true", leaf.ReadString("`${__outcome.status}:${__outcome.value === o}`"));

    // The returned Retain() copy is one more wrapper, and the factory releases it after the encode.
    Assert.Equal(aliasReleases + 1, copyReleases);
  }

  // Starts a gated async call, releases the gate while the runtime executor is paused, and counts
  // the values released from the gate to the end of the settlement turn.
  private static uint CountSettlementReleases(LeafReleaseFixture leaf, string expression)
  {
    leaf.Module.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    leaf.StartOutcome(expression);
    leaf.Fixture.DisableSyncExecutionForTesting();
    leaf.Fixture.PauseRuntimeExecutor();
    leaf.Fixture.ResetCounters();
    leaf.Module.Gate.SetResult();
    leaf.Fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate);
    leaf.Fixture.ResumeRuntimeExecutor();
    leaf.Fixture.WaitUntilIdle();
    leaf.Fixture.SetSyncExecutionSupportedForTesting(true);
    return leaf.Fixture.Counters.ReleasedValues;
  }

  [Fact]
  public async Task AsyncLeafBufferIsReadableAfterAwait()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); true");

    leaf.Fixture.ResetCounters();
    leaf.StartOutcome("m.lengthAfterAwaitAsync(b)");
    await leaf.WaitForOutcomeAsync();
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("fulfilled:4", leaf.ReadString("`${__outcome.status}:${__outcome.value}`"));
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public async Task AsyncLeafFaultRejectsAndReleasesInputOnce()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); true");

    leaf.Fixture.ResetCounters();
    leaf.StartOutcome("m.failAfterAwaitAsync(b)");
    await leaf.WaitForOutcomeAsync();
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal(
        "rejected:Error:true",
        leaf.ReadString("`${__outcome.status}:${__outcome.name}:${__outcome.message.includes('async failed')}`")
    );
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public async Task AsyncLeafMethodThrowingBeforeTaskReleasesOnce()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); true");

    leaf.Fixture.WaitUntilIdle();
    leaf.Fixture.ResetCounters();
    leaf.StartOutcome("m.throwBeforeTaskAsync(b)");
    await leaf.WaitForOutcomeAsync();
    leaf.Fixture.WaitUntilIdle();

    // The method throws before it returns a Task, so the outer catch releases the argument.
    Assert.Equal(
        "rejected:Error:true",
        leaf.ReadString("`${__outcome.status}:${__outcome.name}:${__outcome.message.includes('before task')}`")
    );
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public async Task AsyncRetainedResultReleasesInputAndResultOnce()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); true");

    leaf.Fixture.WaitUntilIdle();
    leaf.Fixture.ResetCounters();
    leaf.StartOutcome("m.holdRetainedAsync(b)");
    leaf.Module.Gate.SetResult();
    await leaf.WaitForOutcomeAsync();
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("fulfilled:true", leaf.ReadString("`${__outcome.status}:${__outcome.value === b}`"));
    // The decoded lease and the returned Retain() copy share one entry, which releases only when
    // the factory releases both after it encodes the result.
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public async Task AsyncResultEncodeFailureRejectsAndReleasesOnce()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); true");

    leaf.Fixture.WaitUntilIdle();
    leaf.Fixture.ResetCounters();
    leaf.StartOutcome("m.brokenPairAsync(b)");
    await leaf.WaitForOutcomeAsync();
    leaf.Fixture.WaitUntilIdle();

    // The factory's Encode throws on the null field and still releases the result and the input.
    Assert.Equal("rejected:Error", leaf.ReadString("`${__outcome.status}:${__outcome.name}`"));
    leaf.WaitForArrayBufferReleases(1u);
    // The decoded lease and the Retain() copy share one entry.
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void AsyncInputLeafLivesUntilSettlement()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.o = {}; true");

    // Leaf-free twin: the same paused sequence with a string result.
    leaf.StartOutcome("m.holdTextAsync('x')");
    leaf.Fixture.DisableSyncExecutionForTesting();
    leaf.Fixture.PauseRuntimeExecutor();
    leaf.Fixture.ResetCounters();
    leaf.Module.Gate.SetResult();
    leaf.Fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate);
    leaf.Fixture.ResumeRuntimeExecutor();
    leaf.Fixture.WaitUntilIdle();
    leaf.Fixture.SetSyncExecutionSupportedForTesting(true);
    var twinReleases = leaf.Fixture.Counters.ReleasedValues;
    Assert.Equal("fulfilled:x", leaf.ReadString("`${__outcome.status}:${__outcome.value}`"));

    leaf.Module.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    leaf.StartOutcome("m.holdValueAsync(o)");
    leaf.Fixture.DisableSyncExecutionForTesting();
    leaf.Fixture.PauseRuntimeExecutor();
    leaf.Fixture.ResetCounters();
    leaf.Module.Gate.SetResult();
    leaf.Fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate);
    // A JavaScriptValue release deletes the jsi::Value on the calling thread with no executor hop,
    // so an input released at task completion would show here while the executor is paused.
    Assert.Equal(0u, leaf.Fixture.Counters.ReleasedValues);
    leaf.Fixture.ResumeRuntimeExecutor();
    leaf.Fixture.WaitUntilIdle();
    leaf.Fixture.SetSyncExecutionSupportedForTesting(true);
    var leafReleases = leaf.Fixture.Counters.ReleasedValues;

    Assert.Equal("fulfilled:true", leaf.ReadString("`${__outcome.status}:${__outcome.value === o}`"));
    Assert.Equal(twinReleases + 1, leafReleases);
  }

  [Fact]
  public void AbandonedAsyncLeafResultReleasesInputAndResultOnce()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); true");

    leaf.StartOutcome("m.holdRetainedAsync(b)");
    leaf.Fixture.DisableSyncExecutionForTesting();
    leaf.Fixture.PauseRuntimeExecutor();
    leaf.Fixture.ResetCounters();
    leaf.Fixture.DropNextRuntimeTask(JavaScriptTaskPriority.Immediate);
    leaf.Module.Gate.SetResult();
    leaf.Fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate);
    leaf.Fixture.ResumeRuntimeExecutor();
    leaf.Fixture.WaitUntilIdle();
    leaf.Fixture.SetSyncExecutionSupportedForTesting(true);
    // The scheduler abandons the result on a thread-pool continuation after the dropped task
    // faults, and the entry release then needs another runtime turn.
    leaf.WaitForArrayBufferReleases(1u);

    // The dropped settlement abandons the result, and the Promise never settles. The decoded lease
    // and the Retain() copy share one entry, which releases only when both leases drop.
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal("pending", leaf.ReadString("__outcome.status"));
  }

  [Fact]
  public async Task AsyncLeafMemberArgumentCountFailureRejects()
  {
    using var leaf = LeafReleaseFixture.Create();

    leaf.Fixture.ResetCounters();
    leaf.StartOutcome("m.holdAsync()");
    await leaf.WaitForOutcomeAsync();
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal(
        "rejected:Error:true",
        leaf.ReadString("`${__outcome.status}:${__outcome.name}:${__outcome.message.includes('expects between 1 and 1 arguments')}`")
    );
    Assert.Equal(0u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void PropertyBufferLeasesReleaseWhenStoredCopyDrops()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); globalThis.props = globalThis._expoDotnet.modules.GeneratedProperties; true");

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString("props.buffer = b; [props.buffer === b, props.buffer === b].join(':')");
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("true:true", result);
    // The stored copy pins the entry; a leaked setter or getter lease would keep it at 0 after the
    // module drops that copy.
    Assert.Equal(0u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    leaf.Fixture.Runtime.Execute(_ =>
    {
      var propertyModule = leaf.Context.ModuleRegistry.GetOrCreateModule<GeneratedPropertiesModule>(
          "GeneratedProperties",
          static () => throw new InvalidOperationException("The generated module must already exist.")
      );
      propertyModule.Dispose();
      return true;
    });
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void SharedObjectConstructorAndMemberReleaseLeaves()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); true");

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString(
        "const entry = new m.LeafReleaseEntry(b, false); " +
        "const measured = entry.measure(b); " +
        "let failed; try { new m.LeafReleaseEntry(b, true); failed = 'no error'; } " +
        "catch (error) { failed = error instanceof Error && error.message.includes('entry failed'); } " +
        "`${measured}:${failed}`"
    );
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("4:true", result);
    Assert.Equal(3u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void SpanFunctionReleasesSpanLeaseAndLeafArgumentOnce()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b1 = new ArrayBuffer(4); globalThis.b2 = new ArrayBuffer(2); true");

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString("String(m.pick(b1, b2) === b2)");
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("true", result);
    // The span lease and the argument; the returned argument is the same leaf, which Release disposes once.
    Assert.Equal(2u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void SpanFunctionReleasesReturnedRetainedCopy()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b1 = new ArrayBuffer(4); globalThis.b2 = new ArrayBuffer(2); true");

    leaf.Fixture.WaitUntilIdle();
    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString("String(m.pickRetained(b1, b2) === b2)");
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("true", result);
    // The span lease, and the argument entry, which releases only when the returned Retain() copy
    // drops too.
    Assert.Equal(2u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void RecordWithBufferFieldsReleasesEachLeaseOnce()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b1 = new ArrayBuffer(4); globalThis.b2 = new ArrayBuffer(2); true");

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString("String(m.measurePair({ first: b1, second: b2 }))");
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("6", result);
    Assert.Equal(2u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void RecordReturnReleasesEveryLeafOnce()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(
        "globalThis.b1 = new ArrayBuffer(4); new Uint8Array(b1)[0] = 7; " +
        "globalThis.b2 = new ArrayBuffer(2); true"
    );

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString(
        "globalThis.r = m.echoPair({ first: b1, second: b2 }); `${r.first === b1}:${r.second === b2}`"
    );
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("true:true", result);
    // Each decoded lease shares its entry with a returned Retain() copy, so each entry releases
    // only when the result release drops the copy too.
    Assert.Equal(2u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal("7", leaf.ReadString("String(new Uint8Array(r.first)[0])"));
  }

  [Fact]
  public void LaterFieldDecodeFailureReleasesEarlierLease()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); true");

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString(
        "let failed; try { m.measureCounted({ buffer: b, count: 'x' }); failed = 'no error'; } " +
        "catch (error) { failed = error instanceof Error; } String(failed)"
    );
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("true", result);
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void AliasInTwoReturnedLeavesReleasesSourceOnce()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.o = {}; true");

    leaf.Fixture.ResetCounters();
    Assert.Equal("true", leaf.ReadString("const t = m.textTwin('x'); String(t.first === 'x' && t.second === 'x')"));
    leaf.Fixture.WaitUntilIdle();
    var twinReleases = leaf.Fixture.Counters.ReleasedValues;

    leaf.Fixture.ResetCounters();
    Assert.Equal("true", leaf.ReadString("const r = m.twin(o); String(r.first === o && r.second === o)"));
    leaf.Fixture.WaitUntilIdle();
    var leafReleases = leaf.Fixture.Counters.ReleasedValues;

    // The decoded input and the one retained copy, which both fields alias and Release disposes once.
    Assert.Equal(twinReleases + 2, leafReleases);
  }

  [Fact]
  public async Task SyncAndAsyncRecordReturningInputLeavesReleaseOnce()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(
        "globalThis.o1 = {}; globalThis.o2 = {}; " +
        "globalThis.b1 = new ArrayBuffer(4); globalThis.b2 = new ArrayBuffer(2); true"
    );

    Assert.Equal(
        "true:true",
        leaf.ReadString("const v = m.echoValues({ first: o1, second: o2 }); `${v.first === o1}:${v.second === o2}`")
    );

    leaf.Fixture.ResetCounters();
    leaf.StartOutcome("m.echoPairAsync({ first: b1, second: b2 })");
    await leaf.WaitForOutcomeAsync();
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal(
        "fulfilled:true:true",
        leaf.ReadString("`${__outcome.status}:${__outcome.value.first === b1}:${__outcome.value.second === b2}`")
    );
    Assert.Equal(2u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void EncodeFailureReleasesReturnedLeaves()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); true");

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString(
        "let failed; try { m.brokenPair(b); failed = 'no error'; } " +
        "catch (error) { failed = error instanceof Error; } String(failed)"
    );
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("true", result);
    // The decoded lease and the returned Retain() copy share one entry, which releases only when
    // both leases drop.
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void ListAndDictionaryOfBuffersRoundTrip()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(
        "globalThis.b1 = new ArrayBuffer(4); new Uint8Array(b1)[0] = 1; " +
        "globalThis.b2 = new ArrayBuffer(2); true"
    );

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString(
        "globalThis.l = m.echoList([b1, b2]); globalThis.d = m.echoMap({ a: b1 }); " +
        "[l.length === 2, l[0] === b1, l[1] === b2, Object.keys(d).join(',') === 'a', d.a === b1].join(':')"
    );
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("true:true:true:true:true", result);
    // Three decoded entries; each releases only when the result release drops its Retain() copy too.
    Assert.Equal(3u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
    // The JavaScript results stay usable after Release disposed the leases.
    Assert.Equal("1:2:1", leaf.ReadString("`${new Uint8Array(l[0])[0]}:${l[1].byteLength}:${new Uint8Array(d.a)[0]}`"));
  }

  [Fact]
  public void NestedRecordListDictionaryBufferRoundTrips()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); true");

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString("const s = m.echoShelf({ rows: [{ a: b }] }); String(s.rows[0].a === b)");
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("true", result);
    // The decoded lease and the returned Retain() copy share one entry.
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void AbandonedAsyncRecordResultReleasesLeavesOnce()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b1 = new ArrayBuffer(4); globalThis.b2 = new ArrayBuffer(2); true");

    leaf.StartOutcome("m.holdPairAsync({ first: b1, second: b2 })");
    leaf.Fixture.DisableSyncExecutionForTesting();
    leaf.Fixture.PauseRuntimeExecutor();
    leaf.Fixture.ResetCounters();
    leaf.Fixture.DropNextRuntimeTask(JavaScriptTaskPriority.Immediate);
    leaf.Module.Gate.SetResult();
    leaf.Fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate);
    leaf.Fixture.ResumeRuntimeExecutor();
    leaf.Fixture.WaitUntilIdle();
    leaf.Fixture.SetSyncExecutionSupportedForTesting(true);
    // The scheduler abandons the result on a thread-pool continuation after the dropped task
    // faults, and the entry releases then need another runtime turn.
    leaf.WaitForArrayBufferReleases(2u);

    // The returned record holds Retain() copies, so each entry releases only when abandon drops
    // both the decoded lease and the copy.
    Assert.Equal(2u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal("pending", leaf.ReadString("__outcome.status"));
  }

  [Fact]
  public void NullableRecordStructWithLeafRoundTrips()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.o = {}; true");

    Assert.Equal("null:String", leaf.ReadString("`${m.slotKind(null)}:${m.slotKind({ value: 'a' })}`"));
    Assert.Equal("true", leaf.ReadString("String(m.echoSlot(null) === null)"));

    leaf.Fixture.ResetCounters();
    Assert.Equal("true", leaf.ReadString("String(m.echoTextSlot({ value: 'x' }).value === 'x')"));
    leaf.Fixture.WaitUntilIdle();
    var twinReleases = leaf.Fixture.Counters.ReleasedValues;

    leaf.Fixture.ResetCounters();
    Assert.Equal("true", leaf.ReadString("String(m.echoSlot({ value: o }).value === o)"));
    leaf.Fixture.WaitUntilIdle();
    var leafReleases = leaf.Fixture.Counters.ReleasedValues;

    // The decoded value and the returned slot's Retain() copy, which only the result release disposes.
    Assert.Equal(twinReleases + 2, leafReleases);
  }

  [Fact]
  public void MixedRecordRoundTripsBothLeafKinds()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); globalThis.o = {}; true");

    leaf.Fixture.ResetCounters();
    Assert.Equal(
        "true",
        leaf.ReadString("const tm = m.echoTextMixed({ buffer: 'x', value: 'y' }); String(tm.buffer === 'x' && tm.value === 'y')")
    );
    leaf.Fixture.WaitUntilIdle();
    var twinReleases = leaf.Fixture.Counters.ReleasedValues;

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString("const mx = m.echoMixed({ buffer: b, value: o }); `${mx.buffer === b}:${mx.value === o}`");
    leaf.Fixture.WaitUntilIdle();
    var leafReleases = leaf.Fixture.Counters.ReleasedValues;

    Assert.Equal("true:true", result);
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
    // The decoded value field and its returned Retain() copy; encode makes a new JavaScript value
    // and leaves both wrappers for the release.
    Assert.Equal(twinReleases + 2, leafReleases);
  }

  [Fact]
  public void ValueListRoundTripsWithoutLeakOrConsumption()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.o1 = {}; globalThis.o2 = {}; true");

    leaf.Fixture.ResetCounters();
    Assert.Equal("true", leaf.ReadString("const tl = m.echoTextList(['x', 'y']); String(tl[0] === 'x' && tl[1] === 'y')"));
    leaf.Fixture.WaitUntilIdle();
    var twinReleases = leaf.Fixture.Counters.ReleasedValues;

    leaf.Fixture.ResetCounters();
    Assert.Equal("true", leaf.ReadString("const vl = m.echoValueList([o1, o2]); String(vl[0] === o1 && vl[1] === o2)"));
    leaf.Fixture.WaitUntilIdle();
    var leafReleases = leaf.Fixture.Counters.ReleasedValues;

    // Each decoded element and its returned Retain() copy, which only the result release disposes.
    Assert.Equal(twinReleases + 4, leafReleases);
  }

  [Fact]
  public void NullRecordInListDecodeFailureReleasesEarlierLeases()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b1 = new ArrayBuffer(4); globalThis.b2 = new ArrayBuffer(2); true");

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString(
        "let failed; try { m.measurePairs([{ first: b1, second: b2 }, null]); failed = 'no error'; } " +
        "catch (error) { failed = error instanceof Error; } String(failed)"
    );
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("true", result);
    Assert.Equal(2u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void NullRecordInReturnedListReleasesOtherLeaves()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b1 = new ArrayBuffer(4); globalThis.b2 = new ArrayBuffer(2); true");

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString(
        "let failed; try { m.pairsWithNull({ first: b1, second: b2 }); failed = 'no error'; } " +
        "catch (error) { failed = error instanceof Error; } String(failed)"
    );
    leaf.Fixture.WaitUntilIdle();

    // Encoding the null record throws NullReferenceException. Release skips the null element and
    // still releases the later pair of retained copies. Each entry releases once its decoded lease
    // and its retained copy drop.
    Assert.Equal("true", result);
    Assert.Equal(2u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void StoredRecordPropertyReturnsRetainedCopies()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b1 = new ArrayBuffer(4); globalThis.b2 = new ArrayBuffer(2); true");

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString(
        "m.storedPair = { first: b1, second: b2 }; const p1 = m.storedPair; const p2 = m.storedPair; " +
        "[p1.first === b1, p1.second === b2, p2.first === b1, p2.second === b2].join(':')"
    );
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("true:true:true:true", result);
    // The stored copies pin both entries; a leaked setter or getter lease would keep them at 0
    // after the module drops those copies.
    Assert.Equal(0u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal("true", leaf.ReadString("String(m.storedPair.first === b1)"));

    leaf.Fixture.Runtime.Execute(_ =>
    {
      leaf.Module.Dispose();
      return true;
    });
    leaf.Fixture.WaitUntilIdle();

    // Each entry releases once, after every setter, getter, and stored lease on it dropped.
    Assert.Equal(2u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void OmittedOptionalBufferListReleasesNothing()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); true");

    leaf.Fixture.ResetCounters();
    Assert.Equal("-1", leaf.ReadString("String(m.countOptional())"));
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal(0u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    leaf.Fixture.ResetCounters();
    Assert.Equal("1", leaf.ReadString("String(m.countOptional([b]))"));
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void CallbackArgumentStaysWithTheAuthor()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); true");

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString(
        "String(m.forwardBuffer(b, p => p.first === b && p.second === b ? p.first.byteLength : -1))"
    );
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("8", result);
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void CallbackValueArgumentStaysUsable()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.o = {}; true");

    leaf.Fixture.WaitUntilIdle();
    leaf.Fixture.ResetCounters();
    Assert.Equal("true:x", leaf.ReadString("m.forwardText('x', p => p.first === 'x' && p.second === 'x')"));
    leaf.Fixture.WaitUntilIdle();
    var twinReleases = leaf.Fixture.Counters.ReleasedValues;

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString("m.forwardValue(o, p => p.first === o && p.second === o)");
    leaf.Fixture.WaitUntilIdle();
    var leafReleases = leaf.Fixture.Counters.ReleasedValues;

    Assert.Equal("true:Object", result);
    // The decoded argument releases once, after the callback returns.
    Assert.Equal(twinReleases + 1, leafReleases);
  }

  [Fact]
  public void CallbackResultBelongsToTheAuthor()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b1 = new ArrayBuffer(2); globalThis.b2 = new ArrayBuffer(4); true");

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString("String(m.takeBuffers(() => [b1, b2]))");
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("6", result);
    Assert.Equal(2u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void CallbackResultLaterFieldDecodeFailureReleasesEarlierLease()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); true");

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString(
        "let failed; try { m.takeCounted(() => ({ buffer: b, count: 'x' })); failed = 'no error'; } " +
        "catch (error) { failed = error instanceof Error; } String(failed)"
    );
    leaf.Fixture.WaitUntilIdle();

    // The author never receives the record, so the record decode must release the buffer it
    // already decoded before the count field failed.
    Assert.Equal("true", result);
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void NestedEventPayloadStaysWithTheAuthor()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); true");

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString(
        "let seen; m.addListener('pair', p => { seen = p.first === b && p.second === b; }); " +
        "`${m.emitPair(b)}:${seen}`"
    );
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("4:true", result);
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public async Task NestedEventPayloadEmittedLaterStaysWithTheAuthor()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(
        "globalThis.b = new ArrayBuffer(4); globalThis.seen = false; " +
        "m.addListener('pair', p => { globalThis.seen = p.first === b && p.second === b; }); true"
    );

    leaf.Fixture.ResetCounters();
    leaf.StartOutcome("m.emitPairLater(b)");
    await leaf.WaitForOutcomeAsync();
    leaf.WaitForArrayBufferReleases(1u);
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("fulfilled", leaf.ReadString("__outcome.status"));
    Assert.Equal("true", leaf.ReadString("String(seen)"));
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void SharedObjectNestedEventPayloadStaysWithTheAuthor()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); true");

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString(
        "const e = new m.LeafReleaseEntry(b, false); let seen; " +
        "e.addListener('buffers', l => { seen = l.length === 1 && l[0] === b; }); " +
        "`${e.emitBuffers(b)}:${seen}`"
    );
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("4:true", result);
    // The constructor lease and the member lease are separate decodes of the same buffer.
    Assert.Equal(2u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void NullishBufferParameterHoldsNoLeaf()
  {
    using var leaf = LeafReleaseFixture.Create();

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString("[m.measureOptional(null), m.measureOptional(undefined)].join(':')");
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("-1:-1", result);
    Assert.Equal(0u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public async Task NonNullNullableBufferParameterReleasesOnceSyncAndAsync()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); true");

    leaf.Fixture.ResetCounters();
    Assert.Equal("4", leaf.ReadString("String(m.measureOptional(b))"));
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    leaf.Fixture.ResetCounters();
    leaf.StartOutcome("m.measureOptionalAfterAwaitAsync(b)");
    await leaf.WaitForOutcomeAsync();
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("fulfilled:4", leaf.ReadString("`${__outcome.status}:${__outcome.value}`"));
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    leaf.Fixture.ResetCounters();
    leaf.StartOutcome("m.measureOptionalAfterAwaitAsync(null)");
    await leaf.WaitForOutcomeAsync();
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("fulfilled:-1", leaf.ReadString("`${__outcome.status}:${__outcome.value}`"));
    Assert.Equal(0u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void OptionalNullableBufferUsesAuthoredDefault()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); true");

    leaf.Fixture.ResetCounters();
    var defaults = leaf.ReadString("[m.measureDefaulted(), m.measureDefaulted(undefined), m.measureDefaulted(null)].join(':')");
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("-1:-1:-1", defaults);
    Assert.Equal(0u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    leaf.Fixture.ResetCounters();
    Assert.Equal("4", leaf.ReadString("String(m.measureDefaulted(b))"));
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public async Task NullableBufferReturnEncodesNullOrTheBufferSyncAndAsync()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); new Uint8Array(b)[0] = 7; true");

    leaf.Fixture.ResetCounters();
    var nulls = leaf.ReadString("[m.echoOptional(null) === null, m.echoOptional(undefined) === null].join(':')");
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("true:true", nulls);
    Assert.Equal(0u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    leaf.Fixture.ResetCounters();
    var echoed = leaf.ReadString("globalThis.r = m.echoOptional(b); [r === b, new Uint8Array(r)[0]].join(':')");
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("true:7", echoed);
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    leaf.Fixture.ResetCounters();
    leaf.StartOutcome("m.echoOptionalAsync(null)");
    await leaf.WaitForOutcomeAsync();
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("fulfilled:true", leaf.ReadString("`${__outcome.status}:${__outcome.value === null}`"));
    Assert.Equal(0u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    leaf.Fixture.ResetCounters();
    leaf.StartOutcome("m.echoOptionalAsync(b)");
    await leaf.WaitForOutcomeAsync();
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("fulfilled:true", leaf.ReadString("`${__outcome.status}:${__outcome.value === b}`"));
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void NullableBufferPropertyStoresNullOrRetainedCopy()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); true");

    leaf.Fixture.ResetCounters();
    Assert.Equal("null", leaf.ReadString("String(m.optionalBuffer)"));
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal(0u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    leaf.Fixture.ResetCounters();
    var stored = leaf.ReadString("m.optionalBuffer = b; [m.optionalBuffer === b, m.optionalBuffer === b].join(':')");
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("true:true", stored);
    // The stored copy pins the entry, as in PropertyBufferLeasesReleaseWhenStoredCopyDrops.
    Assert.Equal(0u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    var cleared = leaf.ReadString("m.optionalBuffer = null; String(m.optionalBuffer)");
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("null", cleared);
    // The setter disposed the stored copy.
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void NullableBufferRecordFieldsReleaseOnlyPresentLeaves()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b1 = new ArrayBuffer(4); globalThis.b2 = new ArrayBuffer(2); true");

    leaf.Fixture.ResetCounters();
    Assert.Equal("4", leaf.ReadString("String(m.measureOptionalPair({ first: b1, second: null }))"));
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    // JavaScript omits the field: it decodes to null and releases nothing.
    leaf.Fixture.ResetCounters();
    Assert.Equal("2", leaf.ReadString("String(m.measureOptionalPair({ second: b2 }))"));
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    leaf.Fixture.ResetCounters();
    var echoed = leaf.ReadString(
        "globalThis.r = m.echoOptionalPair({ first: null, second: b2 }); [r.first === null, r.second === b2].join(':')"
    );
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("true:true", echoed);
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void LaterFieldDecodeFailureReleasesEarlierNullableLease()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); true");

    leaf.Fixture.ResetCounters();
    var present = leaf.ReadString(
        "let failed; try { m.measureOptionalCounted({ buffer: b, count: 'x' }); failed = 'no error'; } " +
        "catch (error) { failed = error instanceof Error; } String(failed)"
    );
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("true", present);
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    leaf.Fixture.ResetCounters();
    var absent = leaf.ReadString(
        "let failed; try { m.measureOptionalCounted({ buffer: null, count: 'x' }); failed = 'no error'; } " +
        "catch (error) { failed = error instanceof Error; } String(failed)"
    );
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("true", absent);
    Assert.Equal(0u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void NullableBufferCollectionsReleaseOnlyPresentLeaves()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b1 = new ArrayBuffer(4); globalThis.b2 = new ArrayBuffer(2); true");

    leaf.Fixture.ResetCounters();
    Assert.Equal("2", leaf.ReadString("String(m.countPresent([b1, null, undefined, b2]))"));
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal(2u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    leaf.Fixture.ResetCounters();
    var list = leaf.ReadString("globalThis.r = m.echoOptionalList([null, b1]); [r[0] === null, r[1] === b1].join(':')");
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("true:true", list);
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    leaf.Fixture.ResetCounters();
    var map = leaf.ReadString("globalThis.r = m.echoOptionalMap({ a: null, b: b2 }); [r.a === null, r.b === b2].join(':')");
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("true:true", map);
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    // The same buffer in two slots decodes to two entries; each releases once.
    leaf.Fixture.ResetCounters();
    Assert.Equal("2", leaf.ReadString("String(m.countPresent([b1, b1]))"));
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal(2u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void NullableBufferParameterRejectsNonBufferValues()
  {
    using var leaf = LeafReleaseFixture.Create();

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString(
        "const outcomes = []; " +
        "for (const bad of ['x', {}]) { " +
        "try { m.measureOptional(bad); outcomes.push('no error'); } " +
        "catch (error) { outcomes.push(error instanceof Error); } } " +
        "outcomes.join(':')"
    );
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("true:true", result);
    Assert.Equal(0u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void NonNullBufferParameterStaysStrict()
  {
    using var leaf = LeafReleaseFixture.Create();

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString(
        "const outcomes = []; " +
        "for (const bad of [null, undefined]) { " +
        "try { m.measure(bad); outcomes.push('no error'); } " +
        "catch (error) { outcomes.push(error instanceof Error); } } " +
        "outcomes.join(':')"
    );
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("true:true", result);
    Assert.Equal(0u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void SharedObjectNullableBufferMembersReleaseOnlyPresentLeaves()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); true");

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString(
        "globalThis.e0 = new m.LeafReleaseOptionalEntry(null); " +
        "globalThis.e1 = new m.LeafReleaseOptionalEntry(b); " +
        "[e0.readSeed(), e1.readSeed(), e1.measure(null), e1.measure(b)].join(':')"
    );
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("-1:4:-1:4", result);
    // The constructor decode and the method decode are two entries.
    Assert.Equal(2u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    leaf.Fixture.ResetCounters();
    var property = leaf.ReadString(
        "e1.buffer = b; const same = e1.buffer === b; e1.buffer = null; [same, e1.buffer === null].join(':')"
    );
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("true:true", property);
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void NullableBufferCallbackArgumentStaysWithTheAuthor()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); true");

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString(
        "[m.forwardOptionalBuffer(b, x => x === b ? 10 : -1), m.forwardOptionalBuffer(null, x => x === null ? 10 : -1)].join(':')"
    );
    leaf.Fixture.WaitUntilIdle();

    // The author's buffer stays usable after the callback, and only its own decode releases.
    Assert.Equal("14:10", result);
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void NullableBufferCallbackResultBelongsToTheAuthor()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); true");

    leaf.Fixture.ResetCounters();
    var nulls = leaf.ReadString("[m.takeOptionalBuffer(() => null), m.takeOptionalBuffer(() => undefined)].join(':')");
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("-1:-1", nulls);
    Assert.Equal(0u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    leaf.Fixture.ResetCounters();
    Assert.Equal("4", leaf.ReadString("String(m.takeOptionalBuffer(() => b))"));
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void NullableCallbackWithBufferTypesFollowsTheLeafRules()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); globalThis.c = new ArrayBuffer(8); true");

    // Null and undefined arrive as C# null. Only the buffer parameter releases, once per call.
    leaf.Fixture.ResetCounters();
    var nulls = leaf.ReadString(
        "[m.exchangeThroughOptionalCallback(b, null), m.exchangeThroughOptionalCallback(b, undefined)].join(':')"
    );
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("-1:-1", nulls);
    Assert.Equal(2u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    // The author's buffer stays readable after the callback, and the returned buffer is the
    // author's to dispose: the parameter and the result release once each.
    leaf.Fixture.ResetCounters();
    Assert.Equal("12", leaf.ReadString("String(m.exchangeThroughOptionalCallback(b, x => x === b ? c : null))"));
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal(2u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void NestedNullableBufferEventPayloadStaysWithTheAuthor()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); true");

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString(
        "let seen; m.addListener('optionalBuffers', l => { seen = l.length === 2 && l[0] === null && l[1] === b; }); " +
        "`${m.emitOptionalBuffers(b)}:${seen}`"
    );
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("4:true", result);
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void SharedObjectNestedNullableBufferEventPayloadStaysWithTheAuthor()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(
        "globalThis.b = new ArrayBuffer(4); globalThis.seen = []; " +
        "globalThis.e = new m.LeafReleaseOptionalEntry(null); " +
        "e.addListener('optionalPair', p => { " +
        "seen.push(`${p.first === null}:${p.second === null ? 'null' : p.second === b}`); }); true"
    );

    leaf.Fixture.ResetCounters();
    var empty = leaf.ReadString("`${e.emitOptionalPair(null)}:${seen.join(',')}`");
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("-1:true:null", empty);
    Assert.Equal(0u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    leaf.Fixture.ResetCounters();
    var present = leaf.ReadString("`${e.emitOptionalPair(b)}:${seen.join(',')}`");
    leaf.Fixture.WaitUntilIdle();
    // The author reads the buffer after the emit, and only the member's own decode releases.
    Assert.Equal("4:true:null,true:true", present);
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  private const string UnionSetup =
      "globalThis.u = globalThis._expoDotnet.modules.UnionThings; " +
      "globalThis.b = new Uint8Array([1, 2, 3]).buffer; true";

  private static string StringOrBufferKind(string kind) =>
      $"Value kind '{kind}' is not one of 'String' or 'ArrayBuffer'.";

  private static string NumberOrBoolKind(string kind) =>
      $"Value kind '{kind}' is not one of 'Number' or 'Bool'.";

  [Fact]
  public void UnionParameterDecodesEachMemberAndReleasesTheBufferOnce()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(UnionSetup);
    UnionThingsModule.Calls = 0;

    leaf.Fixture.ResetCounters();
    Assert.Equal("first:hello", leaf.ReadString("u.write('hello')"));
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal(0u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    leaf.Fixture.ResetCounters();
    Assert.Equal("second:1,2,3", leaf.ReadString("u.write(b)"));
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    leaf.Fixture.ResetCounters();
    Assert.Equal("first:", leaf.ReadString("u.write('')"));
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal(0u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    Assert.Equal(3, UnionThingsModule.Calls);
  }

  [Fact]
  public void SharedObjectUnionMembersDecodeEachMemberAndReleaseOnce()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(UnionSetup);
    UnionThingsModule.Calls = 0;

    leaf.Fixture.ResetCounters();
    var strings = leaf.ReadString(
        "globalThis.w = new u.UnionWriter('seed'); [w.readSeed(), w.write('x')].join('|')"
    );
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("first:seed|first:x", strings);
    Assert.Equal(0u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    leaf.Fixture.ResetCounters();
    var buffers = leaf.ReadString(
        "globalThis.w2 = new u.UnionWriter(b); [w2.readSeed(), w2.write(b)].join('|')"
    );
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("second:1,2,3|second:1,2,3", buffers);
    // The constructor decode and the method decode are two entries.
    Assert.Equal(2u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
    // One authored read in the constructor and one in the method, for each of the two passes.
    Assert.Equal(4, UnionThingsModule.Calls);
  }

  [Fact]
  public async Task UnionParameterReleasesTheBufferAfterAsyncSettlement()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(UnionSetup);
    UnionThingsModule.Calls = 0;

    leaf.Fixture.ResetCounters();
    leaf.StartOutcome("u.writeLater(b)");
    await leaf.WaitForOutcomeAsync();
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("fulfilled:second:1,2,3", leaf.ReadString("`${__outcome.status}:${__outcome.value}`"));
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    leaf.Fixture.ResetCounters();
    leaf.StartOutcome("u.writeLater('x')");
    await leaf.WaitForOutcomeAsync();
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("fulfilled:first:x", leaf.ReadString("`${__outcome.status}:${__outcome.value}`"));
    Assert.Equal(0u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
    // One authored read per call.
    Assert.Equal(2, UnionThingsModule.Calls);
  }

  [Theory]
  [InlineData("5", "Number")]
  [InlineData("true", "Bool")]
  [InlineData("({})", "Object")]
  [InlineData("null", "Null")]
  [InlineData("undefined", "Undefined")]
  [InlineData("new Uint8Array([1, 2, 3])", "Object")]
  public void UnionParameterRejectsOtherKindsBeforeAuthoredCode(string input, string kind)
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(UnionSetup);
    leaf.Run("globalThis.w = new u.UnionWriter('seed'); true");
    UnionThingsModule.Calls = 0;
    var before = leaf.Fixture.Runtime.Execute(_ => leaf.Context.SharedObjects.Count);

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString(
        "const v = () => (" + input + "); " +
        "const err = f => { try { f(); return 'no error'; } " +
        "catch (error) { return error instanceof Error ? error.message : 'not an Error'; } }; " +
        "[err(() => u.write(v())), err(() => w.write(v())), err(() => new u.UnionWriter(v()))].join('|')"
    );
    leaf.Fixture.WaitUntilIdle();

    var messages = result.Split('|');
    Assert.Equal(3, messages.Length);
    Assert.All(messages, message => Assert.Contains(StringOrBufferKind(kind), message));
    Assert.Equal(0, UnionThingsModule.Calls);
    Assert.Equal(before, leaf.Fixture.Runtime.Execute(_ => leaf.Context.SharedObjects.Count));
    Assert.Equal(0u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void NullableUnionParameterPassesNullAndRejectsOtherKinds()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(UnionSetup);
    UnionThingsModule.Calls = 0;

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString(
        "[u.writeOptional(null), u.writeOptional(undefined), u.writeOptional('x'), u.writeOptional(b)].join('|')"
    );
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("null|null|first:x|second:1,2,3", result);
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    var callsBefore = UnionThingsModule.Calls;
    var number = leaf.ReadString(
        "try { u.writeOptional(5); 'no error'; } catch (error) { error.message; }"
    );
    var obj = leaf.ReadString(
        "try { u.writeOptional({}); 'no error'; } catch (error) { error.message; }"
    );
    Assert.Contains(StringOrBufferKind("Number"), number);
    Assert.Contains(StringOrBufferKind("Object"), obj);
    Assert.Equal(callsBefore, UnionThingsModule.Calls);
  }

  [Fact]
  public void OrdinaryUnionDecodesByKindNotTruthiness()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(UnionSetup);
    UnionThingsModule.Calls = 0;

    var result = leaf.ReadString(
        "globalThis.w = new u.UnionWriter('seed'); " +
        "[u.pick(0), u.pick(1.5), u.pick(false), u.pick(true), w.pick(0), w.pick(true)].join(':')"
    );
    Assert.Equal("100:115:0:1:100:1", result);

    string CatchMessage(string call) => leaf.ReadString(
        $"try {{ {call}; 'no error'; }} catch (error) {{ error instanceof Error ? error.message : 'not an Error'; }}"
    );

    Assert.Contains(NumberOrBoolKind("String"), CatchMessage("u.pick('')"));
    Assert.Contains(NumberOrBoolKind("String"), CatchMessage("w.pick('')"));
    Assert.Contains(NumberOrBoolKind("Null"), CatchMessage("u.pick(null)"));
  }

  [Fact]
  public void LaterParameterFailureReleasesTheEarlierUnionLease()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(UnionSetup);
    UnionThingsModule.Calls = 0;

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString(
        "let failed; try { u.writeWithCount(b, 'x'); failed = 'no error'; } " +
        "catch (error) { failed = error.message; } String(failed)"
    );
    leaf.Fixture.WaitUntilIdle();
    // The int codec rejects the string; the same native text as JavaScriptValueTests.
    Assert.Contains("Value is not a number.", result);
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal(0, UnionThingsModule.Calls);

    leaf.Fixture.ResetCounters();
    Assert.Equal("second:1,2,3:2", leaf.ReadString("u.writeWithCount(b, 2)"));
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void UnionListReleasesOneLeasePerBuffer()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(UnionSetup);
    UnionThingsModule.Calls = 0;

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString("globalThis.b2 = new ArrayBuffer(2); u.writeAll(['a', b, b2])");
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("first:a|second:1,2,3|second:0,0", result);
    Assert.Equal(2u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void UnionListLaterElementFailureReleasesEarlierLeases()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(UnionSetup);
    UnionThingsModule.Calls = 0;

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString(
        "globalThis.b2 = new ArrayBuffer(2); " +
        "try { u.writeAll([b, b2, 5]); 'no error'; } " +
        "catch (error) { error instanceof Error ? error.message : 'not an Error'; }"
    );
    leaf.Fixture.WaitUntilIdle();

    // The list decode releases the two buffers it already decoded, and authored code never runs.
    Assert.Contains(StringOrBufferKind("Number"), result);
    Assert.Equal(2u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal(0, UnionThingsModule.Calls);
  }

  [Fact]
  public void UnionListReturnReleasesEachBufferOnce()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(UnionSetup);

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString(
        "globalThis.b2 = new ArrayBuffer(2); const r = u.echoAll(['a', b, b2]); " +
        "[r[0], r[1] === b, r[2] === b2].join(':')"
    );
    leaf.Fixture.WaitUntilIdle();

    // Each returned Retain() copy shares its parameter's entry, so each entry releases only when
    // glue releases both the parameter list and the result list.
    Assert.Equal("a:true:true", result);
    Assert.Equal(2u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public async Task UnionReturnKeepsIdentityAndReleasesOnceSyncAndAsync()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(UnionSetup);

    leaf.Fixture.ResetCounters();
    Assert.Equal("true:x", leaf.ReadString("`${u.echo(b) === b}:${u.echo('x')}`"));
    leaf.Fixture.WaitUntilIdle();
    // The parameter and the returned Retain() copy share one entry, which releases only when glue
    // releases both; the string call holds no lease.
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    leaf.Fixture.ResetCounters();
    leaf.StartOutcome("u.echoLater(b)");
    await leaf.WaitForOutcomeAsync();
    leaf.WaitForArrayBufferReleases(1u);
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("fulfilled:true", leaf.ReadString("`${__outcome.status}:${__outcome.value === b}`"));
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    leaf.Fixture.ResetCounters();
    leaf.StartOutcome("u.echoLater('x')");
    await leaf.WaitForOutcomeAsync();
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("fulfilled:x", leaf.ReadString("`${__outcome.status}:${__outcome.value}`"));
    Assert.Equal(0u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void UnionRecordFieldRoundTripsAndReleasesOnce()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(UnionSetup);

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString(
        "const a = u.wrap({ content: b, count: 1 }); const t = u.wrap({ content: 'x', count: 2 }); " +
        "`${a.content === b}:${a.count}:${t.content}:${t.count}`"
    );
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("true:1:x:2", result);
    // The returned record holds a Retain() copy, so the entry releases only when glue releases both
    // the parameter record and the returned record.
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void UnionPropertyStoresRetainedCopies()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(UnionSetup);

    leaf.Fixture.ResetCounters();
    var stored = leaf.ReadString("u.stored = b; [u.stored === b, u.stored === b].join(':')");
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("true:true", stored);
    // The stored copy pins the entry; a leaked setter or getter lease would keep it pinned after
    // the module drops that copy.
    Assert.Equal(0u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    var text = leaf.ReadString("u.stored = 'x'; u.stored");
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("x", text);
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void UnionEventPayloadAndCallbackArgumentStayWithTheAuthor()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(UnionSetup);
    leaf.Run("globalThis.events = []; u.addListener('content', c => { events.push(c === b || c === 'x'); }); true");
    UnionThingsModule.Calls = 0;

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString(
        "[u.forward(b, c => c === b), u.forward('x', c => c === 'x'), events.join(',')].join('|')"
    );
    leaf.Fixture.WaitUntilIdle();

    // The author reads each union after the emit and the callback, and only the parameter decode
    // releases a lease.
    Assert.Equal("true:second:1,2,3|true:first:x|true,true", result);
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal(2, UnionThingsModule.Calls);
  }

  [Fact]
  public void UnionCallbackResultBelongsToTheAuthor()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(UnionSetup);

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString("[u.take(() => b), u.take(() => 'x')].join('|')");
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("second:1,2,3|first:x", result);
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    var error = leaf.ReadString(
        "try { u.take(() => 5); 'no error'; } catch (error) { error instanceof Error ? error.message : 'not an Error'; }"
    );
    Assert.Contains(StringOrBufferKind("Number"), error);
  }

  private const string TypedArraySetup =
      "globalThis.t = globalThis._expoDotnet.modules.TypedArrayThings; " +
      "globalThis.buf = new Uint8Array(16).map((_, i) => i).buffer; " +
      "globalThis.s = new Uint8Array(buf, 4, 8); true";

  // Hex of bytes 4 to 11 of the setup buffer, the bytes that the sub-view `s` covers.
  private const string S8 = "0405060708090A0B";

  private const string NotU8 = "Value is not a Uint8Array.";

  // A Uint8Array whose prototype is a subclass prototype, accepted by prototype-chain membership.
  private const string Sub =
      "(() => { function Sub() {} Sub.prototype = Object.create(Uint8Array.prototype); "
      + "Sub.prototype.constructor = Sub; const v = new Uint8Array(4); "
      + "Object.setPrototypeOf(v, Sub.prototype); return v; })()";

  // Evaluates the expression with a reset counter, lets lease releases land, and asserts the count.
  private static string ReadCounted(LeafReleaseFixture leaf, string expression, uint expectedReleases)
  {
    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString(expression);
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal(expectedReleases, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
    return result;
  }

  [Fact]
  public void Uint8ArrayFillWritesInPlaceAndReleasesOnce()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(TypedArraySetup);
    TypedArrayThingsModule.Calls = 0;

    Assert.Equal(
        "66,66,66,66",
        ReadCounted(leaf, "globalThis.v = new Uint8Array(4); t.fill(v); Array.from(v).join(',')", 1)
    );
    Assert.Equal(
        "0,1,2,3,66,66,66,66,66,66,66,66,12,13,14,15",
        ReadCounted(leaf, "t.fill(s); Array.from(new Uint8Array(buf)).join(',')", 1)
    );
    Assert.Equal(
        "66,66,66,66",
        ReadCounted(leaf, "globalThis.w = " + Sub + "; t.fill(w); Array.from(w).join(',')", 1)
    );
    Assert.Equal(3, TypedArrayThingsModule.Calls);
  }

  [Fact]
  public async Task Uint8ArraySubViewSeesOnlyItsSliceSyncAndAsync()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(TypedArraySetup);
    TypedArrayThingsModule.Calls = 0;

    Assert.Equal("4:8:" + S8, ReadCounted(leaf, "t.hex(s)", 1));

    leaf.Fixture.ResetCounters();
    leaf.StartOutcome("t.hexLater(s)");
    await leaf.WaitForOutcomeAsync();
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal("fulfilled:4:8:" + S8, leaf.ReadString("`${__outcome.status}:${__outcome.value}`"));
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public async Task Uint8ArrayReturnsAndPropertiesEncodeViews()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(TypedArraySetup);
    TypedArrayThingsModule.Calls = 0;

    // On the Hermes testhost, jsi::Runtime::tryGetMutableBuffer is the base implementation and
    // returns null. A native-backed buffer that comes back from JavaScript therefore decodes as a
    // JavaScript-backed lease and counts one entry. The bytes still alias. Another engine can
    // count differently, so these two calls assert no count.
    Assert.Equal(
        "true:0:3:1,2,3",
        leaf.ReadString(
            "globalThis.r = t.make(); [r instanceof Uint8Array, r.byteOffset, r.length, Array.from(r).join(',')].join(':')"
        )
    );
    Assert.Equal("0:3:010203", leaf.ReadString("t.hex(t.make())"));
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal(
        "true:false:true:4:8",
        ReadCounted(
            leaf,
            "globalThis.e = t.echo(s); [e instanceof Uint8Array, e === s, e.buffer === buf, e.byteOffset, e.length].join(':')",
            1
        )
    );

    leaf.Fixture.ResetCounters();
    leaf.StartOutcome("t.echoLater(s)");
    await leaf.WaitForOutcomeAsync();
    leaf.Fixture.WaitUntilIdle();
    Assert.Equal(
        "fulfilled:true:true:4:8",
        leaf.ReadString(
            "`${__outcome.status}:${__outcome.value instanceof Uint8Array}:${__outcome.value.buffer === buf}:"
            + "${__outcome.value.byteOffset}:${__outcome.value.length}`"
        )
    );
    Assert.Equal(1u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);

    Assert.Equal("null", ReadCounted(leaf, "String(t.stored)", 0));
    // The stored copy pins the entry, as in NullableBufferPropertyStoresNullOrRetainedCopy.
    Assert.Equal(
        "true:true:4:8",
        ReadCounted(
            leaf,
            "t.stored = s; const p = t.stored; [p instanceof Uint8Array, p.buffer === buf, p.byteOffset, p.length].join(':')",
            0
        )
    );
    // The setter disposed the stored copy.
    Assert.Equal("null", ReadCounted(leaf, "t.stored = null; String(t.stored)", 1));
  }

  [Theory]
  [InlineData("new ArrayBuffer(4)", false)]
  [InlineData("new Int32Array(2)", false)]
  [InlineData("new DataView(new ArrayBuffer(4))", false)]
  [InlineData("({})", false)]
  [InlineData("5", false)]
  [InlineData("null", true)]
  [InlineData("undefined", true)]
  public void Uint8ArrayParametersRejectWrongShapes(string input, bool nullish)
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(TypedArraySetup);
    leaf.Run("globalThis.e = new t.TypedArrayEntry(s); true");
    // The constructor decoded a view; let its lease release land before the counter reset.
    leaf.Fixture.WaitUntilIdle();
    TypedArrayThingsModule.Calls = 0;
    var before = leaf.Fixture.Runtime.Execute(_ => leaf.Context.SharedObjects.Count);

    var result = ReadCounted(
        leaf,
        "const v = () => (" + input + "); "
            + "const err = f => { try { return 'ok:' + f(); } "
            + "catch (error) { return error instanceof Error ? error.message : 'not an Error'; } }; "
            + "[err(() => t.fill(v())), err(() => e.describe(v())), "
            + "err(() => new t.TypedArrayEntry(v())), err(() => t.fillOptional(v()))].join('|')",
        0
    );

    var parts = result.Split('|');
    Assert.Equal(4, parts.Length);
    Assert.Contains(NotU8, parts[0]);
    Assert.Contains(NotU8, parts[1]);
    Assert.Contains(NotU8, parts[2]);
    if (nullish)
    {
      Assert.Equal("ok:null", parts[3]);
    }
    else
    {
      Assert.Contains(NotU8, parts[3]);
    }
    Assert.Equal(0, TypedArrayThingsModule.Calls);
    Assert.Equal(before, leaf.Fixture.Runtime.Execute(_ => leaf.Context.SharedObjects.Count));
  }

  [Fact]
  public void Uint8ArrayCompositionReleasesEachLeafOnce()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(TypedArraySetup);
    TypedArrayThingsModule.Calls = 0;

    Assert.Equal(
        "0:2:0001|0:1:00",
        ReadCounted(leaf, "t.hexAll([new Uint8Array(buf, 0, 2), new Uint8Array(new ArrayBuffer(1))])", 2)
    );

    // Two decodes of one buffer are two entries.
    Assert.Equal(
        "2:true:4:8",
        ReadCounted(
            leaf,
            "const l = t.echoList([s, s]); [l.length, l[0].buffer === buf, l[1].byteOffset, l[1].length].join(':')",
            2
        )
    );

    Assert.Equal(
        "true:4:2",
        ReadCounted(
            leaf,
            "const m = t.echoMap({ a: s, b: new Uint8Array(2) }); [m.a.buffer === buf, m.a.byteOffset, m.b.length].join(':')",
            2
        )
    );

    Assert.Equal(
        "x:true:4:8",
        ReadCounted(
            leaf,
            "const h = t.echoHolder({ bytes: s, label: 'x' }); "
                + "[h.label, h.bytes.buffer === buf, h.bytes.byteOffset, h.bytes.length].join(':')",
            1
        )
    );

    // The constructor decode and the method decode are two entries.
    Assert.Equal(
        "4:8:" + S8 + "|4:8:" + S8,
        ReadCounted(
            leaf,
            "globalThis.e = new t.TypedArrayEntry(s); [e.readSeed(), e.describe(s)].join('|')",
            2
        )
    );
  }

  [Fact]
  public void RetainedUint8ArrayOutlivesTheCall()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(TypedArraySetup);
    TypedArrayThingsModule.Calls = 0;

    Assert.Equal("held", ReadCounted(leaf, "t.hold(s); 'held'", 0));
    // The held copy shares the JavaScript bytes.
    Assert.Equal("4:8:6305060708090A0B", ReadCounted(leaf, "s[0] = 99; t.readHeld()", 0));
    Assert.Equal("none", ReadCounted(leaf, "t.releaseHeld(); t.readHeld()", 1));
  }

  [Fact]
  public void ExactBinaryParametersStillRejectUint8Array()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(TypedArraySetup);
    TypedArrayThingsModule.Calls = 0;

    var result = ReadCounted(
        leaf,
        "let a, b; "
            + "try { t.takeBuffer(new Uint8Array(4)); a = 'no error'; } catch (error) { a = error.message; } "
            + "try { t.takeBytes(new Uint8Array(4)); b = 'no error'; } catch (error) { b = error.message; } "
            + "[a, b].join('|')",
        0
    );
    var parts = result.Split('|');
    Assert.Equal(2, parts.Length);
    Assert.Contains("Value is not a JavaScript ArrayBuffer.", parts[0]);
    Assert.Contains("Value is not a JavaScript ArrayBuffer.", parts[1]);

    // Control: an ArrayBuffer still decodes.
    Assert.Equal("4", leaf.ReadString("String(t.takeBuffer(new Uint8Array(4).buffer))"));
  }

  [Fact]
  public void Uint8ArrayLeaseReleasesWhenAuthoredCodeThrows()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(TypedArraySetup);
    TypedArrayThingsModule.Calls = 0;

    var result = ReadCounted(
        leaf,
        "let failed; try { t.failAfterRead(s); failed = 'no error'; } catch (error) { failed = error.message; } String(failed)",
        1
    );
    Assert.Contains("view failed", result);
    Assert.Equal(1, TypedArrayThingsModule.Calls);
  }

  [Fact]
  public void Uint8ArrayUnionDecodesByKind()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(TypedArraySetup);
    TypedArrayThingsModule.Calls = 0;

    Assert.Equal("text:x|bytes:4:8:" + S8, ReadCounted(leaf, "[t.write('x'), t.write(s)].join('|')", 1));

    var result = ReadCounted(
        leaf,
        "const err = f => { try { return 'ok:' + f(); } "
            + "catch (error) { return error instanceof Error ? error.message : 'not an Error'; } }; "
            + "[err(() => t.write(buf)), err(() => t.write({})), err(() => t.write(new Int32Array(2)))].join('|')",
        0
    );
    var parts = result.Split('|');
    Assert.Equal(3, parts.Length);
    Assert.Contains("Value kind 'ArrayBuffer' is not one of 'String' or 'Object'.", parts[0]);
    Assert.Contains(NotU8, parts[1]);
    Assert.Contains(NotU8, parts[2]);
    Assert.Equal(1, TypedArrayThingsModule.Calls);
  }

  [Fact]
  public void NullableUint8ArrayInEventAndCallbackStaysWithTheAuthor()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(TypedArraySetup);
    leaf.Run(
        "globalThis.events = []; t.addListener('bytes', v => { "
            + "events.push(v === null ? 'null' : `${v instanceof Uint8Array}:${v.buffer === buf}:${v.byteOffset}:${v.length}`); }); true"
    );
    TypedArrayThingsModule.Calls = 0;

    // The author reads the view after the event and the callback, so only the parameter decode
    // releases a lease.
    Assert.Equal(
        "true:4:8:" + S8 + "|true:true:4:8",
        ReadCounted(
            leaf,
            "const r = t.forward(s, v => v instanceof Uint8Array && v.buffer === buf && v.byteOffset === 4); "
                + "[r, events.join(',')].join('|')",
            1
        )
    );

    // A null view reaches the event and the callback as JavaScript null and holds no lease.
    Assert.Equal(
        "true:null|true:true:4:8,null",
        ReadCounted(leaf, "const r = t.forward(null, v => v === null); [r, events.join(',')].join('|')", 0)
    );
    Assert.Equal(1, TypedArrayThingsModule.Calls);
  }

  [Fact]
  public void NullableUint8ArrayCallbackResultBelongsToTheAuthor()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run(TypedArraySetup);

    // The author disposes the returned view, which releases its lease once.
    Assert.Equal("4:8:" + S8, ReadCounted(leaf, "t.take(() => s)", 1));
    Assert.Equal("null", ReadCounted(leaf, "t.take(() => null)", 0));
    Assert.Equal("null", ReadCounted(leaf, "t.take(() => undefined)", 0));

    var error = ReadCounted(
        leaf,
        "try { t.take(() => buf); 'no error'; } catch (error) { error instanceof Error ? error.message : 'not an Error'; }",
        0
    );
    Assert.Contains(NotU8, error);
  }

  private sealed class LeafReleaseFixture : IDisposable
  {
    private LeafReleaseFixture(
        HermesRuntimeFixture fixture,
        DotnetRuntimeContext context,
        LeafReleaseModule module)
    {
      Fixture = fixture;
      Context = context;
      Module = module;
    }

    public HermesRuntimeFixture Fixture { get; }

    public DotnetRuntimeContext Context { get; }

    public LeafReleaseModule Module { get; }

    public static LeafReleaseFixture Create()
    {
      var fixture = HermesRuntimeFixture.Create();
      try
      {
        DotnetRuntimeContext? context = null;
        LeafReleaseModule? module = null;
        fixture.Runtime.Execute(runtime =>
        {
          context = new DotnetRuntimeContext(runtime);
          using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
          ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
          module = context.ModuleRegistry.GetOrCreateModule<LeafReleaseModule>(
              "LeafRelease",
              static () => throw new InvalidOperationException("The generated module must already exist.")
          );
          using var setup = fixture.Evaluate(
              "globalThis.m = globalThis._expoDotnet.modules.LeafRelease; true",
              "leaf-release-setup.js"
          );
          return true;
        });
        return new LeafReleaseFixture(fixture, context!, module!);
      }
      catch
      {
        fixture.Dispose();
        throw;
      }
    }

    public void Run(string source)
    {
      Fixture.Runtime.Execute(_ =>
      {
        using var result = Fixture.Evaluate(source, "leaf-release-run.js");
        return true;
      });
    }

    public string ReadString(string expression)
    {
      return Fixture.Runtime.Execute(_ =>
      {
        using var result = Fixture.Evaluate(expression, "leaf-release-read.js");
        return result.AsString();
      });
    }

    // Records the settlement of the Promise that the expression returns in globalThis.__outcome.
    public void StartOutcome(string expression)
    {
      Run(
          $$"""
          globalThis.__outcome = { status: 'pending', value: undefined, name: null, message: null };
          {{expression}}.then(
            value => {
              globalThis.__outcome.status = 'fulfilled';
              globalThis.__outcome.value = value;
            },
            error => {
              globalThis.__outcome.status = 'rejected';
              globalThis.__outcome.name = error && error.name;
              globalThis.__outcome.message = error && error.message ? error.message : String(error);
            }
          );
          true;
          """
      );
    }

    public async Task WaitForOutcomeAsync()
    {
      var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
      while (DateTime.UtcNow < deadline)
      {
        Fixture.DrainTasks();
        if (ReadString("__outcome.status") != "pending")
        {
          return;
        }

        await Task.Delay(10, TestContext.Current.CancellationToken);
      }

      Assert.Fail("Timed out waiting for the Promise outcome.");
    }

    // Gives asynchronous lease releases runtime turns until the counter reaches the expected
    // value or the deadline passes; the caller still asserts the exact count.
    public void WaitForArrayBufferReleases(uint expected)
    {
      var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
      while (Fixture.Counters.LongLivedArrayBuffersReleased < expected && DateTime.UtcNow < deadline)
      {
        Fixture.Runtime.Execute(runtime =>
        {
          using var value = runtime.CreateUndefined();
          return true;
        });
        Fixture.WaitUntilIdle();
        Thread.Sleep(10);
      }
    }

    public void Dispose()
    {
      Fixture.SetSyncExecutionSupportedForTesting(true);
      Fixture.ResumeRuntimeExecutor();
      Fixture.Runtime.Execute(_ =>
      {
        Context.Dispose();
        return true;
      });
      Fixture.Dispose();
    }
  }
}
