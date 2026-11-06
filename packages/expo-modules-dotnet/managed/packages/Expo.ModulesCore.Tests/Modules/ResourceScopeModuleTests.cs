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
// one conversion scope per invocation. Lease counts use JS-created buffers, whose release reaches
// LongLivedArrayBuffersReleased after the runtime goes idle. ReleasedValues also counts traversal
// temporaries, so JavaScriptValue assertions compare against a leaf-free twin call.
public sealed class ResourceScopeModuleTests
{
  [Fact]
  public void SyncLeafParameterReleasesOnceOnSuccessAndThrow()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b = new ArrayBuffer(4); true");

    scope.Fixture.ResetCounters();
    var result = scope.ReadString(
        "const measured = m.measure(b); " +
        "let failed; try { m.measureOrThrow(b); failed = 'no error'; } " +
        "catch (error) { failed = error instanceof Error && error.message.includes('measure failed'); } " +
        "`${measured}:${failed}`"
    );
    scope.Fixture.WaitUntilIdle();

    Assert.Equal("4:true", result);
    Assert.Equal(2u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public async Task LaterDecodeFailureReleasesEarlierLeafSyncAndAsync()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b1 = new ArrayBuffer(4); globalThis.b2 = new ArrayBuffer(4); true");

    // The buffer decodes into the scope before the count decode throws; the scope still releases it.
    scope.Fixture.ResetCounters();
    var result = scope.ReadString(
        "let failed; try { m.measureWithCount(b1, 'x'); failed = 'no error'; } " +
        "catch (error) { failed = error instanceof Error; } String(failed)"
    );
    scope.Fixture.WaitUntilIdle();
    Assert.Equal("true", result);
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);

    // Async: the outer catch releases the scope before it returns the rejected Promise.
    scope.Fixture.ResetCounters();
    scope.StartOutcome("m.measureWithCountAsync(b2, 'x')");
    await scope.WaitForOutcomeAsync();
    scope.Fixture.WaitUntilIdle();
    Assert.Equal("rejected:Error", scope.ReadString("`${__outcome.status}:${__outcome.name}`"));
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void SyncMethodReturningItsInputBufferKeepsIdentityAndReleasesOnce()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b = new ArrayBuffer(4); true");

    scope.Fixture.ResetCounters();
    var result = scope.ReadString("String(m.same(b) === b)");
    scope.Fixture.WaitUntilIdle();

    Assert.Equal("true", result);
    // The decoded lease and the returned lease are one object: the scope registers it once.
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void SyncMethodReturningItsInputValueResolvesIt()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.o = {}; true");

    scope.Fixture.ResetCounters();
    Assert.Equal("true", scope.ReadString("String(m.text('x') === 'x')"));
    var twinReleases = scope.Fixture.Counters.ReleasedValues;

    scope.Fixture.ResetCounters();
    Assert.Equal("true", scope.ReadString("String(m.sameValue(o) === o)"));
    var leafReleases = scope.Fixture.Counters.ReleasedValues;

    // The returned input is one wrapper registered once; the bridge receives an independent copy.
    Assert.Equal(twinReleases + 1, leafReleases);
  }

  [Fact]
  public void ReturnedRetainedCopyOfInputReleasesBothLeases()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b = new ArrayBuffer(4); true");

    scope.Fixture.ResetCounters();
    var result = scope.ReadString("String(m.retained(b) === b)");
    scope.Fixture.WaitUntilIdle();

    Assert.Equal("true", result);
    // The decoded lease and the Retain() copy share one entry, which releases only when both leases drop.
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void SameBufferInTwoParametersReleasesBothLeases()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b = new ArrayBuffer(4); true");

    scope.Fixture.ResetCounters();
    var result = scope.ReadString("String(m.measureTwo(b, b))");
    scope.Fixture.WaitUntilIdle();

    Assert.Equal("8", result);
    // Each decode creates its own lease over the same JS buffer; equal leaves are not aliases.
    Assert.Equal(2u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void ReturnedDisposedValueFailsThroughCatchableError()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b = new ArrayBuffer(4); true");

    var result = scope.ReadString(
        "let failed; try { m.disposedValue(); failed = 'no error'; } " +
        "catch (error) { failed = error instanceof Error; } " +
        "`${failed}:${m.measure(b)}`"
    );

    Assert.Equal("true:4", result);
  }

  [Fact]
  public async Task AsyncMethodReturningItsInputValueResolvesWithIt()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.o = {}; true");

    scope.StartOutcome("m.sameValueAsync(o)");
    await scope.WaitForOutcomeAsync();

    Assert.Equal("fulfilled:true", scope.ReadString("`${__outcome.status}:${__outcome.value === o}`"));
  }

  [Fact]
  public async Task AsyncLeafBufferIsReadableAfterAwait()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b = new ArrayBuffer(4); true");

    scope.Fixture.ResetCounters();
    scope.StartOutcome("m.lengthAfterAwaitAsync(b)");
    await scope.WaitForOutcomeAsync();
    scope.Fixture.WaitUntilIdle();

    Assert.Equal("fulfilled:4", scope.ReadString("`${__outcome.status}:${__outcome.value}`"));
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public async Task AsyncLeafFaultRejectsAndReleasesInputOnce()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b = new ArrayBuffer(4); true");

    scope.Fixture.ResetCounters();
    scope.StartOutcome("m.failAfterAwaitAsync(b)");
    await scope.WaitForOutcomeAsync();
    scope.Fixture.WaitUntilIdle();

    Assert.Equal(
        "rejected:Error:true",
        scope.ReadString("`${__outcome.status}:${__outcome.name}:${__outcome.message.includes('async failed')}`")
    );
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void AsyncInputLeafLivesUntilSettlement()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.o = {}; true");

    // Leaf-free twin: the same paused sequence with a string result.
    scope.StartOutcome("m.holdTextAsync('x')");
    scope.Fixture.DisableSyncExecutionForTesting();
    scope.Fixture.PauseRuntimeExecutor();
    scope.Fixture.ResetCounters();
    scope.Module.Gate.SetResult();
    scope.Fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate);
    scope.Fixture.ResumeRuntimeExecutor();
    scope.Fixture.WaitUntilIdle();
    scope.Fixture.SetSyncExecutionSupportedForTesting(true);
    var twinReleases = scope.Fixture.Counters.ReleasedValues;
    Assert.Equal("fulfilled:x", scope.ReadString("`${__outcome.status}:${__outcome.value}`"));

    scope.Module.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    scope.StartOutcome("m.holdValueAsync(o)");
    scope.Fixture.DisableSyncExecutionForTesting();
    scope.Fixture.PauseRuntimeExecutor();
    scope.Fixture.ResetCounters();
    scope.Module.Gate.SetResult();
    scope.Fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate);
    // A JavaScriptValue release deletes the jsi::Value on the calling thread with no executor hop,
    // so an input released at task completion would show here while the executor is paused.
    Assert.Equal(0u, scope.Fixture.Counters.ReleasedValues);
    scope.Fixture.ResumeRuntimeExecutor();
    scope.Fixture.WaitUntilIdle();
    scope.Fixture.SetSyncExecutionSupportedForTesting(true);
    var leafReleases = scope.Fixture.Counters.ReleasedValues;

    Assert.Equal("fulfilled:true", scope.ReadString("`${__outcome.status}:${__outcome.value === o}`"));
    Assert.Equal(twinReleases + 1, leafReleases);
  }

  [Fact]
  public void AbandonedAsyncLeafResultReleasesInputAndResultOnce()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b = new ArrayBuffer(4); true");

    scope.StartOutcome("m.holdRetainedAsync(b)");
    scope.Fixture.DisableSyncExecutionForTesting();
    scope.Fixture.PauseRuntimeExecutor();
    scope.Fixture.ResetCounters();
    scope.Fixture.DropNextRuntimeTask(JavaScriptTaskPriority.Immediate);
    scope.Module.Gate.SetResult();
    scope.Fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate);
    scope.Fixture.ResumeRuntimeExecutor();
    scope.Fixture.WaitUntilIdle();
    scope.Fixture.SetSyncExecutionSupportedForTesting(true);
    // The scheduler abandons the result on a thread-pool continuation after the dropped task
    // faults, and the entry release then needs another runtime turn.
    scope.WaitForArrayBufferReleases(1u);

    // The dropped settlement abandons the result, and the Promise never settles. The decoded lease
    // and the Retain() copy share one entry, which releases only when both leases drop.
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal("pending", scope.ReadString("__outcome.status"));
  }

  [Fact]
  public async Task AsyncLeafMemberArgumentCountFailureRejects()
  {
    using var scope = ResourceScopeFixture.Create();

    scope.Fixture.ResetCounters();
    scope.StartOutcome("m.holdAsync()");
    await scope.WaitForOutcomeAsync();
    scope.Fixture.WaitUntilIdle();

    Assert.Equal(
        "rejected:Error:true",
        scope.ReadString("`${__outcome.status}:${__outcome.name}:${__outcome.message.includes('expects between 1 and 1 arguments')}`")
    );
    Assert.Equal(0u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void PropertyBufferLeasesReleaseWhenStoredCopyDrops()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b = new ArrayBuffer(4); globalThis.props = globalThis._expoDotnet.modules.GeneratedProperties; true");

    scope.Fixture.ResetCounters();
    var result = scope.ReadString("props.buffer = b; [props.buffer === b, props.buffer === b].join(':')");
    scope.Fixture.WaitUntilIdle();

    Assert.Equal("true:true", result);
    // The stored copy pins the entry; a leaked setter or getter lease would keep it at 0 after the
    // module drops that copy.
    Assert.Equal(0u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);

    scope.Fixture.Runtime.Execute(_ =>
    {
      var propertyModule = scope.Context.ModuleRegistry.GetOrCreateModule<GeneratedPropertiesModule>(
          "GeneratedProperties",
          static () => throw new InvalidOperationException("The generated module must already exist.")
      );
      propertyModule.Dispose();
      return true;
    });
    scope.Fixture.WaitUntilIdle();

    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void SharedObjectConstructorAndMemberReleaseLeaves()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b = new ArrayBuffer(4); true");

    scope.Fixture.ResetCounters();
    var result = scope.ReadString(
        "const entry = new m.ResourceScopeEntry(b, false); " +
        "const measured = entry.measure(b); " +
        "let failed; try { new m.ResourceScopeEntry(b, true); failed = 'no error'; } " +
        "catch (error) { failed = error instanceof Error && error.message.includes('entry failed'); } " +
        "`${measured}:${failed}`"
    );
    scope.Fixture.WaitUntilIdle();

    Assert.Equal("4:true", result);
    Assert.Equal(3u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void SpanFunctionReleasesSpanLeaseAndLeafArgumentOnce()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b1 = new ArrayBuffer(4); globalThis.b2 = new ArrayBuffer(2); true");

    scope.Fixture.ResetCounters();
    var result = scope.ReadString("String(m.pick(b1, b2) === b2)");
    scope.Fixture.WaitUntilIdle();

    Assert.Equal("true", result);
    // The span lease and the scoped argument; the returned argument is the same registered leaf.
    Assert.Equal(2u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void RecordWithBufferFieldsReleasesEachLeaseOnce()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b1 = new ArrayBuffer(4); globalThis.b2 = new ArrayBuffer(2); true");

    scope.Fixture.ResetCounters();
    var result = scope.ReadString("String(m.measurePair({ first: b1, second: b2 }))");
    scope.Fixture.WaitUntilIdle();

    Assert.Equal("6", result);
    Assert.Equal(2u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void RecordReturnTransfersEveryLeaf()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run(
        "globalThis.b1 = new ArrayBuffer(4); new Uint8Array(b1)[0] = 7; " +
        "globalThis.b2 = new ArrayBuffer(2); true"
    );

    scope.Fixture.ResetCounters();
    var result = scope.ReadString(
        "globalThis.r = m.echoPair({ first: b1, second: b2 }); `${r.first === b1}:${r.second === b2}`"
    );
    scope.Fixture.WaitUntilIdle();

    Assert.Equal("true:true", result);
    // The returned leaves are the decoded ones: each is registered once and released once.
    Assert.Equal(2u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal("7", scope.ReadString("String(new Uint8Array(r.first)[0])"));
  }

  [Fact]
  public void LaterFieldDecodeFailureReleasesEarlierLease()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b = new ArrayBuffer(4); true");

    scope.Fixture.ResetCounters();
    var result = scope.ReadString(
        "let failed; try { m.measureCounted({ buffer: b, count: 'x' }); failed = 'no error'; } " +
        "catch (error) { failed = error instanceof Error; } String(failed)"
    );
    scope.Fixture.WaitUntilIdle();

    Assert.Equal("true", result);
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void AliasInTwoReturnedLeavesReleasesSourceOnce()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.o = {}; true");

    scope.Fixture.ResetCounters();
    Assert.Equal("true", scope.ReadString("const t = m.textTwin('x'); String(t.first === 'x' && t.second === 'x')"));
    scope.Fixture.WaitUntilIdle();
    var twinReleases = scope.Fixture.Counters.ReleasedValues;

    scope.Fixture.ResetCounters();
    Assert.Equal("true", scope.ReadString("const r = m.twin(o); String(r.first === o && r.second === o)"));
    scope.Fixture.WaitUntilIdle();
    var leafReleases = scope.Fixture.Counters.ReleasedValues;

    // The decoded input and the one retained copy, which both fields alias and the scope registers once.
    Assert.Equal(twinReleases + 2, leafReleases);
  }

  [Fact]
  public async Task SyncAndAsyncRecordReturningInputLeavesReleaseOnce()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run(
        "globalThis.o1 = {}; globalThis.o2 = {}; " +
        "globalThis.b1 = new ArrayBuffer(4); globalThis.b2 = new ArrayBuffer(2); true"
    );

    Assert.Equal(
        "true:true",
        scope.ReadString("const v = m.echoValues({ first: o1, second: o2 }); `${v.first === o1}:${v.second === o2}`")
    );

    scope.Fixture.ResetCounters();
    scope.StartOutcome("m.echoPairAsync({ first: b1, second: b2 })");
    await scope.WaitForOutcomeAsync();
    scope.Fixture.WaitUntilIdle();

    Assert.Equal(
        "fulfilled:true:true",
        scope.ReadString("`${__outcome.status}:${__outcome.value.first === b1}:${__outcome.value.second === b2}`")
    );
    Assert.Equal(2u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void EncodeFailureReleasesTransferredLeaves()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b = new ArrayBuffer(4); true");

    scope.Fixture.ResetCounters();
    var result = scope.ReadString(
        "let failed; try { m.brokenPair(b); failed = 'no error'; } " +
        "catch (error) { failed = error instanceof Error; } String(failed)"
    );
    scope.Fixture.WaitUntilIdle();

    Assert.Equal("true", result);
    // The decoded lease and the transferred Retain() copy share one entry, which releases only when
    // both leases drop.
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void ListAndDictionaryOfBuffersRoundTrip()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run(
        "globalThis.b1 = new ArrayBuffer(4); new Uint8Array(b1)[0] = 1; " +
        "globalThis.b2 = new ArrayBuffer(2); true"
    );

    scope.Fixture.ResetCounters();
    var result = scope.ReadString(
        "globalThis.l = m.echoList([b1, b2]); globalThis.d = m.echoMap({ a: b1 }); " +
        "[l.length === 2, l[0] === b1, l[1] === b2, Object.keys(d).join(',') === 'a', d.a === b1].join(':')"
    );
    scope.Fixture.WaitUntilIdle();

    Assert.Equal("true:true:true:true:true", result);
    Assert.Equal(3u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
    // The JavaScript results stay usable after the scope released the leases.
    Assert.Equal("1:2:1", scope.ReadString("`${new Uint8Array(l[0])[0]}:${l[1].byteLength}:${new Uint8Array(d.a)[0]}`"));
  }

  [Fact]
  public void NestedRecordListDictionaryBufferRoundTrips()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b = new ArrayBuffer(4); true");

    scope.Fixture.ResetCounters();
    var result = scope.ReadString("const s = m.echoShelf({ rows: [{ a: b }] }); String(s.rows[0].a === b)");
    scope.Fixture.WaitUntilIdle();

    Assert.Equal("true", result);
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void AbandonedAsyncRecordResultReleasesLeavesOnce()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b1 = new ArrayBuffer(4); globalThis.b2 = new ArrayBuffer(2); true");

    scope.StartOutcome("m.holdPairAsync({ first: b1, second: b2 })");
    scope.Fixture.DisableSyncExecutionForTesting();
    scope.Fixture.PauseRuntimeExecutor();
    scope.Fixture.ResetCounters();
    scope.Fixture.DropNextRuntimeTask(JavaScriptTaskPriority.Immediate);
    scope.Module.Gate.SetResult();
    scope.Fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate);
    scope.Fixture.ResumeRuntimeExecutor();
    scope.Fixture.WaitUntilIdle();
    scope.Fixture.SetSyncExecutionSupportedForTesting(true);
    // The scheduler abandons the result on a thread-pool continuation after the dropped task
    // faults, and the entry releases then need another runtime turn.
    scope.WaitForArrayBufferReleases(2u);

    // The returned record holds the decoded leaves, so abandon releases each once.
    Assert.Equal(2u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal("pending", scope.ReadString("__outcome.status"));
  }

  [Fact]
  public void NullableRecordStructWithLeafRoundTrips()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.o = {}; true");

    Assert.Equal("null:String", scope.ReadString("`${m.slotKind(null)}:${m.slotKind({ value: 'a' })}`"));
    Assert.Equal("true", scope.ReadString("String(m.echoSlot(null) === null)"));

    scope.Fixture.ResetCounters();
    Assert.Equal("true", scope.ReadString("String(m.echoTextSlot({ value: 'x' }).value === 'x')"));
    scope.Fixture.WaitUntilIdle();
    var twinReleases = scope.Fixture.Counters.ReleasedValues;

    scope.Fixture.ResetCounters();
    Assert.Equal("true", scope.ReadString("String(m.echoSlot({ value: o }).value === o)"));
    scope.Fixture.WaitUntilIdle();
    var leafReleases = scope.Fixture.Counters.ReleasedValues;

    // The decoded value, which the returned slot holds again and the scope registers once.
    Assert.Equal(twinReleases + 1, leafReleases);
  }

  [Fact]
  public void MixedRecordRoundTripsBothLeafKinds()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b = new ArrayBuffer(4); globalThis.o = {}; true");

    scope.Fixture.ResetCounters();
    Assert.Equal(
        "true",
        scope.ReadString("const tm = m.echoTextMixed({ buffer: 'x', value: 'y' }); String(tm.buffer === 'x' && tm.value === 'y')")
    );
    scope.Fixture.WaitUntilIdle();
    var twinReleases = scope.Fixture.Counters.ReleasedValues;

    scope.Fixture.ResetCounters();
    var result = scope.ReadString("const mx = m.echoMixed({ buffer: b, value: o }); `${mx.buffer === b}:${mx.value === o}`");
    scope.Fixture.WaitUntilIdle();
    var leafReleases = scope.Fixture.Counters.ReleasedValues;

    Assert.Equal("true:true", result);
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
    // The decoded value field; encode borrows it instead of disposing it.
    Assert.Equal(twinReleases + 1, leafReleases);
  }

  [Fact]
  public void ValueListRoundTripsWithoutLeakOrConsumption()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.o1 = {}; globalThis.o2 = {}; true");

    scope.Fixture.ResetCounters();
    Assert.Equal("true", scope.ReadString("const tl = m.echoTextList(['x', 'y']); String(tl[0] === 'x' && tl[1] === 'y')"));
    scope.Fixture.WaitUntilIdle();
    var twinReleases = scope.Fixture.Counters.ReleasedValues;

    scope.Fixture.ResetCounters();
    Assert.Equal("true", scope.ReadString("const vl = m.echoValueList([o1, o2]); String(vl[0] === o1 && vl[1] === o2)"));
    scope.Fixture.WaitUntilIdle();
    var leafReleases = scope.Fixture.Counters.ReleasedValues;

    // Each decoded element releases once.
    Assert.Equal(twinReleases + 2, leafReleases);
  }

  [Fact]
  public void NullRecordInListDecodeFailureReleasesEarlierLeases()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b1 = new ArrayBuffer(4); globalThis.b2 = new ArrayBuffer(2); true");

    scope.Fixture.ResetCounters();
    var result = scope.ReadString(
        "let failed; try { m.measurePairs([{ first: b1, second: b2 }, null]); failed = 'no error'; } " +
        "catch (error) { failed = error instanceof Error; } String(failed)"
    );
    scope.Fixture.WaitUntilIdle();

    Assert.Equal("true", result);
    Assert.Equal(2u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void NullRecordInReturnedListKeepsTransferTotal()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b1 = new ArrayBuffer(4); globalThis.b2 = new ArrayBuffer(2); true");

    scope.Fixture.ResetCounters();
    var result = scope.ReadString(
        "let failed; try { m.pairsWithNull({ first: b1, second: b2 }); failed = 'no error'; } " +
        "catch (error) { failed = error instanceof Error; } String(failed)"
    );
    scope.Fixture.WaitUntilIdle();

    // The null element registers nothing, the later pair of retained copies still registers, and
    // the record EncodeBorrowed throws ArgumentNullException for the null element. Each entry
    // releases once its decoded lease and its retained copy drop.
    Assert.Equal("true", result);
    Assert.Equal(2u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void StoredRecordPropertyReturnsRetainedCopies()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b1 = new ArrayBuffer(4); globalThis.b2 = new ArrayBuffer(2); true");

    scope.Fixture.ResetCounters();
    var result = scope.ReadString(
        "m.storedPair = { first: b1, second: b2 }; const p1 = m.storedPair; const p2 = m.storedPair; " +
        "[p1.first === b1, p1.second === b2, p2.first === b1, p2.second === b2].join(':')"
    );
    scope.Fixture.WaitUntilIdle();

    Assert.Equal("true:true:true:true", result);
    // The stored copies pin both entries; a leaked setter or getter lease would keep them at 0
    // after the module drops those copies.
    Assert.Equal(0u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal("true", scope.ReadString("String(m.storedPair.first === b1)"));

    scope.Fixture.Runtime.Execute(_ =>
    {
      scope.Module.Dispose();
      return true;
    });
    scope.Fixture.WaitUntilIdle();

    // Each entry releases once, after every setter, getter, and stored lease on it dropped.
    Assert.Equal(2u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void OmittedOptionalBufferListRegistersNothing()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b = new ArrayBuffer(4); true");

    scope.Fixture.ResetCounters();
    Assert.Equal("-1", scope.ReadString("String(m.countOptional())"));
    scope.Fixture.WaitUntilIdle();
    Assert.Equal(0u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);

    scope.Fixture.ResetCounters();
    Assert.Equal("1", scope.ReadString("String(m.countOptional([b]))"));
    scope.Fixture.WaitUntilIdle();
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void NullishBufferParameterRegistersNothing()
  {
    using var scope = ResourceScopeFixture.Create();

    scope.Fixture.ResetCounters();
    var result = scope.ReadString("[m.measureOptional(null), m.measureOptional(undefined)].join(':')");
    scope.Fixture.WaitUntilIdle();

    Assert.Equal("-1:-1", result);
    Assert.Equal(0u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public async Task NonNullNullableBufferParameterReleasesOnceSyncAndAsync()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b = new ArrayBuffer(4); true");

    scope.Fixture.ResetCounters();
    Assert.Equal("4", scope.ReadString("String(m.measureOptional(b))"));
    scope.Fixture.WaitUntilIdle();
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);

    scope.Fixture.ResetCounters();
    scope.StartOutcome("m.measureOptionalAfterAwaitAsync(b)");
    await scope.WaitForOutcomeAsync();
    scope.Fixture.WaitUntilIdle();
    Assert.Equal("fulfilled:4", scope.ReadString("`${__outcome.status}:${__outcome.value}`"));
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);

    scope.Fixture.ResetCounters();
    scope.StartOutcome("m.measureOptionalAfterAwaitAsync(null)");
    await scope.WaitForOutcomeAsync();
    scope.Fixture.WaitUntilIdle();
    Assert.Equal("fulfilled:-1", scope.ReadString("`${__outcome.status}:${__outcome.value}`"));
    Assert.Equal(0u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void OptionalNullableBufferUsesAuthoredDefault()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b = new ArrayBuffer(4); true");

    scope.Fixture.ResetCounters();
    var defaults = scope.ReadString("[m.measureDefaulted(), m.measureDefaulted(undefined), m.measureDefaulted(null)].join(':')");
    scope.Fixture.WaitUntilIdle();
    Assert.Equal("-1:-1:-1", defaults);
    Assert.Equal(0u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);

    scope.Fixture.ResetCounters();
    Assert.Equal("4", scope.ReadString("String(m.measureDefaulted(b))"));
    scope.Fixture.WaitUntilIdle();
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public async Task NullableBufferReturnEncodesNullOrTransfersSyncAndAsync()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b = new ArrayBuffer(4); new Uint8Array(b)[0] = 7; true");

    scope.Fixture.ResetCounters();
    var nulls = scope.ReadString("[m.echoOptional(null) === null, m.echoOptional(undefined) === null].join(':')");
    scope.Fixture.WaitUntilIdle();
    Assert.Equal("true:true", nulls);
    Assert.Equal(0u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);

    scope.Fixture.ResetCounters();
    var echoed = scope.ReadString("globalThis.r = m.echoOptional(b); [r === b, new Uint8Array(r)[0]].join(':')");
    scope.Fixture.WaitUntilIdle();
    Assert.Equal("true:7", echoed);
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);

    scope.Fixture.ResetCounters();
    scope.StartOutcome("m.echoOptionalAsync(null)");
    await scope.WaitForOutcomeAsync();
    scope.Fixture.WaitUntilIdle();
    Assert.Equal("fulfilled:true", scope.ReadString("`${__outcome.status}:${__outcome.value === null}`"));
    Assert.Equal(0u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);

    scope.Fixture.ResetCounters();
    scope.StartOutcome("m.echoOptionalAsync(b)");
    await scope.WaitForOutcomeAsync();
    scope.Fixture.WaitUntilIdle();
    Assert.Equal("fulfilled:true", scope.ReadString("`${__outcome.status}:${__outcome.value === b}`"));
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void NullableBufferPropertyStoresNullOrRetainedCopy()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b = new ArrayBuffer(4); true");

    scope.Fixture.ResetCounters();
    Assert.Equal("null", scope.ReadString("String(m.optionalBuffer)"));
    scope.Fixture.WaitUntilIdle();
    Assert.Equal(0u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);

    scope.Fixture.ResetCounters();
    var stored = scope.ReadString("m.optionalBuffer = b; [m.optionalBuffer === b, m.optionalBuffer === b].join(':')");
    scope.Fixture.WaitUntilIdle();
    Assert.Equal("true:true", stored);
    // The stored copy pins the entry, as in PropertyBufferLeasesReleaseWhenStoredCopyDrops.
    Assert.Equal(0u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);

    var cleared = scope.ReadString("m.optionalBuffer = null; String(m.optionalBuffer)");
    scope.Fixture.WaitUntilIdle();
    Assert.Equal("null", cleared);
    // The setter disposed the stored copy.
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void NullableBufferRecordFieldsRegisterOnlyPresentLeaves()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b1 = new ArrayBuffer(4); globalThis.b2 = new ArrayBuffer(2); true");

    scope.Fixture.ResetCounters();
    Assert.Equal("4", scope.ReadString("String(m.measureOptionalPair({ first: b1, second: null }))"));
    scope.Fixture.WaitUntilIdle();
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);

    // JavaScript omits the field: it decodes to null and registers nothing.
    scope.Fixture.ResetCounters();
    Assert.Equal("2", scope.ReadString("String(m.measureOptionalPair({ second: b2 }))"));
    scope.Fixture.WaitUntilIdle();
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);

    scope.Fixture.ResetCounters();
    var echoed = scope.ReadString(
        "globalThis.r = m.echoOptionalPair({ first: null, second: b2 }); [r.first === null, r.second === b2].join(':')"
    );
    scope.Fixture.WaitUntilIdle();
    Assert.Equal("true:true", echoed);
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void LaterFieldDecodeFailureReleasesEarlierNullableLease()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b = new ArrayBuffer(4); true");

    scope.Fixture.ResetCounters();
    var present = scope.ReadString(
        "let failed; try { m.measureOptionalCounted({ buffer: b, count: 'x' }); failed = 'no error'; } " +
        "catch (error) { failed = error instanceof Error; } String(failed)"
    );
    scope.Fixture.WaitUntilIdle();
    Assert.Equal("true", present);
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);

    scope.Fixture.ResetCounters();
    var absent = scope.ReadString(
        "let failed; try { m.measureOptionalCounted({ buffer: null, count: 'x' }); failed = 'no error'; } " +
        "catch (error) { failed = error instanceof Error; } String(failed)"
    );
    scope.Fixture.WaitUntilIdle();
    Assert.Equal("true", absent);
    Assert.Equal(0u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void NullableBufferCollectionsRegisterOnlyPresentLeaves()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b1 = new ArrayBuffer(4); globalThis.b2 = new ArrayBuffer(2); true");

    scope.Fixture.ResetCounters();
    Assert.Equal("2", scope.ReadString("String(m.countPresent([b1, null, undefined, b2]))"));
    scope.Fixture.WaitUntilIdle();
    Assert.Equal(2u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);

    scope.Fixture.ResetCounters();
    var list = scope.ReadString("globalThis.r = m.echoOptionalList([null, b1]); [r[0] === null, r[1] === b1].join(':')");
    scope.Fixture.WaitUntilIdle();
    Assert.Equal("true:true", list);
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);

    scope.Fixture.ResetCounters();
    var map = scope.ReadString("globalThis.r = m.echoOptionalMap({ a: null, b: b2 }); [r.a === null, r.b === b2].join(':')");
    scope.Fixture.WaitUntilIdle();
    Assert.Equal("true:true", map);
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);

    // The same buffer in two slots decodes to two entries; each releases once.
    scope.Fixture.ResetCounters();
    Assert.Equal("2", scope.ReadString("String(m.countPresent([b1, b1]))"));
    scope.Fixture.WaitUntilIdle();
    Assert.Equal(2u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void NullableBufferParameterRejectsNonBufferValues()
  {
    using var scope = ResourceScopeFixture.Create();

    scope.Fixture.ResetCounters();
    var result = scope.ReadString(
        "const outcomes = []; " +
        "for (const bad of ['x', {}]) { " +
        "try { m.measureOptional(bad); outcomes.push('no error'); } " +
        "catch (error) { outcomes.push(error instanceof Error); } } " +
        "outcomes.join(':')"
    );
    scope.Fixture.WaitUntilIdle();

    Assert.Equal("true:true", result);
    Assert.Equal(0u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void NonNullBufferParameterStaysStrict()
  {
    using var scope = ResourceScopeFixture.Create();

    scope.Fixture.ResetCounters();
    var result = scope.ReadString(
        "const outcomes = []; " +
        "for (const bad of [null, undefined]) { " +
        "try { m.measure(bad); outcomes.push('no error'); } " +
        "catch (error) { outcomes.push(error instanceof Error); } } " +
        "outcomes.join(':')"
    );
    scope.Fixture.WaitUntilIdle();

    Assert.Equal("true:true", result);
    Assert.Equal(0u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void SharedObjectNullableBufferMembersRegisterOnlyPresentLeaves()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run("globalThis.b = new ArrayBuffer(4); true");

    scope.Fixture.ResetCounters();
    var result = scope.ReadString(
        "globalThis.e0 = new m.ResourceScopeOptionalEntry(null); " +
        "globalThis.e1 = new m.ResourceScopeOptionalEntry(b); " +
        "[e0.readSeed(), e1.readSeed(), e1.measure(null), e1.measure(b)].join(':')"
    );
    scope.Fixture.WaitUntilIdle();
    Assert.Equal("-1:4:-1:4", result);
    // The constructor decode and the method decode are two entries.
    Assert.Equal(2u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);

    scope.Fixture.ResetCounters();
    var property = scope.ReadString(
        "e1.buffer = b; const same = e1.buffer === b; e1.buffer = null; [same, e1.buffer === null].join(':')"
    );
    scope.Fixture.WaitUntilIdle();
    Assert.Equal("true:true", property);
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
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
    using var scope = ResourceScopeFixture.Create();
    scope.Run(UnionSetup);
    UnionThingsModule.Calls = 0;

    scope.Fixture.ResetCounters();
    Assert.Equal("first:hello", scope.ReadString("u.write('hello')"));
    scope.Fixture.WaitUntilIdle();
    Assert.Equal(0u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);

    scope.Fixture.ResetCounters();
    Assert.Equal("second:1,2,3", scope.ReadString("u.write(b)"));
    scope.Fixture.WaitUntilIdle();
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);

    scope.Fixture.ResetCounters();
    Assert.Equal("first:", scope.ReadString("u.write('')"));
    scope.Fixture.WaitUntilIdle();
    Assert.Equal(0u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);

    Assert.Equal(3, UnionThingsModule.Calls);
  }

  [Fact]
  public void SharedObjectUnionMembersDecodeEachMemberAndReleaseOnce()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run(UnionSetup);
    UnionThingsModule.Calls = 0;

    scope.Fixture.ResetCounters();
    var strings = scope.ReadString(
        "globalThis.w = new u.UnionWriter('seed'); [w.readSeed(), w.write('x')].join('|')"
    );
    scope.Fixture.WaitUntilIdle();
    Assert.Equal("first:seed|first:x", strings);
    Assert.Equal(0u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);

    scope.Fixture.ResetCounters();
    var buffers = scope.ReadString(
        "globalThis.w2 = new u.UnionWriter(b); [w2.readSeed(), w2.write(b)].join('|')"
    );
    scope.Fixture.WaitUntilIdle();
    Assert.Equal("second:1,2,3|second:1,2,3", buffers);
    // The constructor decode and the method decode are two entries.
    Assert.Equal(2u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
    // One authored read in the constructor and one in the method, for each of the two passes.
    Assert.Equal(4, UnionThingsModule.Calls);
  }

  [Fact]
  public async Task UnionParameterReleasesTheBufferAfterAsyncSettlement()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run(UnionSetup);
    UnionThingsModule.Calls = 0;

    scope.Fixture.ResetCounters();
    scope.StartOutcome("u.writeLater(b)");
    await scope.WaitForOutcomeAsync();
    scope.Fixture.WaitUntilIdle();
    Assert.Equal("fulfilled:second:1,2,3", scope.ReadString("`${__outcome.status}:${__outcome.value}`"));
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);

    scope.Fixture.ResetCounters();
    scope.StartOutcome("u.writeLater('x')");
    await scope.WaitForOutcomeAsync();
    scope.Fixture.WaitUntilIdle();
    Assert.Equal("fulfilled:first:x", scope.ReadString("`${__outcome.status}:${__outcome.value}`"));
    Assert.Equal(0u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
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
    using var scope = ResourceScopeFixture.Create();
    scope.Run(UnionSetup);
    scope.Run("globalThis.w = new u.UnionWriter('seed'); true");
    UnionThingsModule.Calls = 0;
    var before = scope.Fixture.Runtime.Execute(_ => scope.Context.SharedObjects.Count);

    scope.Fixture.ResetCounters();
    var result = scope.ReadString(
        "const v = () => (" + input + "); " +
        "const err = f => { try { f(); return 'no error'; } " +
        "catch (error) { return error instanceof Error ? error.message : 'not an Error'; } }; " +
        "[err(() => u.write(v())), err(() => w.write(v())), err(() => new u.UnionWriter(v()))].join('|')"
    );
    scope.Fixture.WaitUntilIdle();

    var messages = result.Split('|');
    Assert.Equal(3, messages.Length);
    Assert.All(messages, message => Assert.Contains(StringOrBufferKind(kind), message));
    Assert.Equal(0, UnionThingsModule.Calls);
    Assert.Equal(before, scope.Fixture.Runtime.Execute(_ => scope.Context.SharedObjects.Count));
    Assert.Equal(0u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void NullableUnionParameterPassesNullAndRejectsOtherKinds()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run(UnionSetup);
    UnionThingsModule.Calls = 0;

    scope.Fixture.ResetCounters();
    var result = scope.ReadString(
        "[u.writeOptional(null), u.writeOptional(undefined), u.writeOptional('x'), u.writeOptional(b)].join('|')"
    );
    scope.Fixture.WaitUntilIdle();
    Assert.Equal("null|null|first:x|second:1,2,3", result);
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);

    var callsBefore = UnionThingsModule.Calls;
    var number = scope.ReadString(
        "try { u.writeOptional(5); 'no error'; } catch (error) { error.message; }"
    );
    var obj = scope.ReadString(
        "try { u.writeOptional({}); 'no error'; } catch (error) { error.message; }"
    );
    Assert.Contains(StringOrBufferKind("Number"), number);
    Assert.Contains(StringOrBufferKind("Object"), obj);
    Assert.Equal(callsBefore, UnionThingsModule.Calls);
  }

  [Fact]
  public void OrdinaryUnionDecodesByKindNotTruthiness()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run(UnionSetup);
    UnionThingsModule.Calls = 0;

    var result = scope.ReadString(
        "globalThis.w = new u.UnionWriter('seed'); " +
        "[u.pick(0), u.pick(1.5), u.pick(false), u.pick(true), w.pick(0), w.pick(true)].join(':')"
    );
    Assert.Equal("100:115:0:1:100:1", result);

    string CatchMessage(string call) => scope.ReadString(
        $"try {{ {call}; 'no error'; }} catch (error) {{ error instanceof Error ? error.message : 'not an Error'; }}"
    );

    Assert.Contains(NumberOrBoolKind("String"), CatchMessage("u.pick('')"));
    Assert.Contains(NumberOrBoolKind("String"), CatchMessage("w.pick('')"));
    Assert.Contains(NumberOrBoolKind("Null"), CatchMessage("u.pick(null)"));
  }

  [Fact]
  public void LaterParameterFailureReleasesTheEarlierUnionLease()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run(UnionSetup);
    UnionThingsModule.Calls = 0;

    scope.Fixture.ResetCounters();
    var result = scope.ReadString(
        "let failed; try { u.writeWithCount(b, 'x'); failed = 'no error'; } " +
        "catch (error) { failed = error.message; } String(failed)"
    );
    scope.Fixture.WaitUntilIdle();
    // The int codec rejects the string; the same native text as JavaScriptValueTests.
    Assert.Contains("Value is not a number.", result);
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal(0, UnionThingsModule.Calls);

    scope.Fixture.ResetCounters();
    Assert.Equal("second:1,2,3:2", scope.ReadString("u.writeWithCount(b, 2)"));
    scope.Fixture.WaitUntilIdle();
    Assert.Equal(1u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  [Fact]
  public void UnionListReleasesOneLeasePerBuffer()
  {
    using var scope = ResourceScopeFixture.Create();
    scope.Run(UnionSetup);
    UnionThingsModule.Calls = 0;

    scope.Fixture.ResetCounters();
    var result = scope.ReadString("globalThis.b2 = new ArrayBuffer(2); u.writeAll(['a', b, b2])");
    scope.Fixture.WaitUntilIdle();
    Assert.Equal("first:a|second:1,2,3|second:0,0", result);
    Assert.Equal(2u, scope.Fixture.Counters.LongLivedArrayBuffersReleased);
  }

  private sealed class ResourceScopeFixture : IDisposable
  {
    private ResourceScopeFixture(
        HermesRuntimeFixture fixture,
        DotnetRuntimeContext context,
        ResourceScopeModule module)
    {
      Fixture = fixture;
      Context = context;
      Module = module;
    }

    public HermesRuntimeFixture Fixture { get; }

    public DotnetRuntimeContext Context { get; }

    public ResourceScopeModule Module { get; }

    public static ResourceScopeFixture Create()
    {
      var fixture = HermesRuntimeFixture.Create();
      try
      {
        DotnetRuntimeContext? context = null;
        ResourceScopeModule? module = null;
        fixture.Runtime.Execute(runtime =>
        {
          context = new DotnetRuntimeContext(runtime);
          using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
          ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
          module = context.ModuleRegistry.GetOrCreateModule<ResourceScopeModule>(
              "ResourceScope",
              static () => throw new InvalidOperationException("The generated module must already exist.")
          );
          using var setup = fixture.Evaluate(
              "globalThis.m = globalThis._expoDotnet.modules.ResourceScope; true",
              "resource-scope-setup.js"
          );
          return true;
        });
        return new ResourceScopeFixture(fixture, context!, module!);
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
        using var result = Fixture.Evaluate(source, "resource-scope-run.js");
        return true;
      });
    }

    public string ReadString(string expression)
    {
      return Fixture.Runtime.Execute(_ =>
      {
        using var result = Fixture.Evaluate(expression, "resource-scope-read.js");
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
