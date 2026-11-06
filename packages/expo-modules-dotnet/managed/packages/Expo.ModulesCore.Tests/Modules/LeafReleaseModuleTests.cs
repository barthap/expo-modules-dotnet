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
    // The returned leaves are the decoded ones: Release disposes each once.
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

    // The returned record holds the decoded leaves, so abandon releases each once.
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

    // The decoded value, which the returned slot holds again and Release disposes once.
    Assert.Equal(twinReleases + 1, leafReleases);
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
    // The decoded value field; encode makes a new JavaScript value and leaves the author value usable.
    Assert.Equal(twinReleases + 1, leafReleases);
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

    // Each decoded element releases once.
    Assert.Equal(twinReleases + 2, leafReleases);
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

    // Encoding the null element throws ArgumentNullException. Release skips the null element and
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

    var result = leaf.ReadString("m.forwardValue(o, p => p.first === o && p.second === o)");

    Assert.Equal("true:Object", result);
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
  public void NestedEventPayloadStaysWithTheAuthor()
  {
    using var leaf = LeafReleaseFixture.Create();
    leaf.Run("globalThis.b = new ArrayBuffer(4); true");

    leaf.Fixture.ResetCounters();
    var result = leaf.ReadString(
        "let seen; m.addListener('onPair', p => { seen = p.first === b && p.second === b; }); " +
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
        "m.addListener('onPair', p => { globalThis.seen = p.first === b && p.second === b; }); true"
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
        "e.addListener('onBuffers', l => { seen = l.length === 1 && l[0] === b; }); " +
        "`${e.emitBuffers(b)}:${seen}`"
    );
    leaf.Fixture.WaitUntilIdle();

    Assert.Equal("4:true", result);
    // The constructor lease and the member lease are separate decodes of the same buffer.
    Assert.Equal(2u, leaf.Fixture.Counters.LongLivedArrayBuffersReleased);
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
