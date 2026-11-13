using System;
using System.Threading;
using System.Threading.Tasks;
using Expo.JSI;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.SharedObjects;

// Async results and callbacks encode after the host-function frame exits, so they use the context
// that the binding captured instead of an ambient one.
public sealed partial class SharedObjectCompositeTests
{
  [Fact]
  public async Task AsyncResultHoldsSharedObjects()
  {
    using var deferred = DeferredFixture.Create();

    // Another host function runs between the call and the settlement, so the ambient context at
    // settlement time is not the one the async call captured.
    deferred.StartOutcome("(() => { const p = m.holdEntriesAsync(a); m.takeOne(b); return p; })()");
    CompositeEntriesModule.Gate.SetResult();
    await deferred.WaitForOutcomeAsync();

    Assert.Equal(
        "fulfilled:2:true:true:late",
        deferred.ReadString(
            "const v = __outcome.value; " +
            "[__outcome.status, v.length, v[0] === a, Object.getPrototypeOf(v[1]) === m.Entry.prototype, v[1].label].join(':')"
        )
    );
  }

  [Fact]
  public async Task AsyncResultWithLeafAndSharedObjectEncodesBothAndReleasesTheLeafOnce()
  {
    using var deferred = DeferredFixture.Create();
    deferred.Run("globalThis.buf = new ArrayBuffer(4); true");

    deferred.Fixture.WaitUntilIdle();
    deferred.Fixture.ResetCounters();
    deferred.StartOutcome("m.holdLeafAndEntryAsync(buf)");
    CompositeEntriesModule.Gate.SetResult();
    await deferred.WaitForOutcomeAsync();
    deferred.Fixture.WaitUntilIdle();

    Assert.Equal(
        "fulfilled:4:held:true",
        deferred.ReadString(
            "const v = __outcome.value; " +
            "[__outcome.status, v.data.byteLength, v.entry.label, Object.getPrototypeOf(v.entry) === m.Entry.prototype].join(':')"
        )
    );
    // The decoded argument lease and the returned Retain() copy share one entry, which releases
    // once after the factory encodes the result.
    Assert.Equal(1u, deferred.Fixture.Counters.LongLivedArrayBuffersReleased);
    // a, b, and the encoded result entry.
    Assert.Equal(3, deferred.Context.SharedObjects.Count);
  }

  [Fact]
  public void AbandonedAsyncResultReleasesTheLeafOnceAndNeverPairsTheSharedObject()
  {
    using var deferred = DeferredFixture.Create();
    deferred.Run("globalThis.buf = new ArrayBuffer(4); true");

    deferred.StartOutcome("m.holdLeafAndEntryAsync(buf)");
    deferred.Fixture.DisableSyncExecutionForTesting();
    deferred.Fixture.PauseRuntimeExecutor();
    deferred.Fixture.ResetCounters();
    deferred.Fixture.DropNextRuntimeTask(JavaScriptTaskPriority.Immediate);
    CompositeEntriesModule.Gate.SetResult();
    deferred.Fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate);
    deferred.Fixture.ResumeRuntimeExecutor();
    deferred.Fixture.WaitUntilIdle();
    deferred.Fixture.SetSyncExecutionSupportedForTesting(true);
    deferred.WaitForArrayBufferReleases(1u);

    Assert.Equal(1u, deferred.Fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal("pending", deferred.ReadString("__outcome.status"));
    Assert.NotNull(CompositeEntriesModule.LastHeld);
    // The result never encoded, so only a and b have JavaScript objects.
    Assert.Equal(2, deferred.Context.SharedObjects.Count);
  }

  [Fact]
  public void CallbackCarriesSharedObjectsDuringTheCall()
  {
    using var deferred = DeferredFixture.Create();

    var result = deferred.ReadString(
        "const seen = []; " +
        "const r1 = m.callWithEntries([a, b], list => { seen.push(list.length, list[0] === a, list[1] === b); return list[1]; }); " +
        "const r2 = m.callWithEntries([a], list => null); " +
        "const r3 = m.callWithNativeEntries(list => { seen.push(list.map(e => e.label).join(''), " +
        "list.every(e => Object.getPrototypeOf(e) === m.Entry.prototype)); return list[0]; }); " +
        "[seen.join(','), r1 === b, r2 === null, r3.label].join('|')"
    );

    Assert.Equal("2,true,true,n0n1,true|true|true|n0", result);
    // The last callback result is the instance JavaScript returned (a, b, n0, n1 in construction order).
    Assert.Same(Entry.Constructed[2], CompositeEntriesModule.LastCallbackResult);
  }

  [Fact]
  public async Task CallbackCarriesSharedObjectsThroughInvokeAsync()
  {
    using var deferred = DeferredFixture.Create();
    deferred.Run(
        "globalThis.seen = []; " +
        "m.storeCallback(list => { seen.push(list.length, list[1] === b); return list[0]; }); true"
    );

    deferred.StartOutcome("m.callStoredAsync([a, b])");
    await deferred.WaitForOutcomeAsync();

    Assert.Equal("fulfilled:true:2,true", deferred.ReadString("`${__outcome.status}:${__outcome.value === a}:${seen}`"));
    Assert.Same(Entry.Constructed[0], CompositeEntriesModule.LastCallbackResult);
  }

  [Fact]
  public async Task ModuleEventPayloadHoldsSharedObject()
  {
    using var deferred = DeferredFixture.Create();

    deferred.StartOutcome(
        "(() => { globalThis.seen = []; m.addListener('note', value => seen.push(value)); return m.emitNoteAsync(a); })()"
    );
    await deferred.WaitForOutcomeAsync();

    Assert.Equal(
        "fulfilled:1:true:native:true:true",
        deferred.ReadString(
            "[__outcome.status, seen.length, seen[0].first === a, seen[0].second.label, " +
            "Object.getPrototypeOf(seen[0].second) === m.Entry.prototype, m.takeOne(a) === a].join(':')"
        )
    );
    // The emit neither released the payload instances nor disposed them.
    Assert.Equal(0, Entry.ReleaseCount);
  }

  [Fact]
  public async Task SharedObjectEventPayloadHoldsSharedObject()
  {
    using var deferred = DeferredFixture.Create();

    deferred.StartOutcome(
        "(() => { globalThis.seen = []; a.addListener('shared', value => seen.push(value)); return a.emitSharedAsync(b); })()"
    );
    await deferred.WaitForOutcomeAsync();

    Assert.Equal(
        "fulfilled:1:true:true:true",
        deferred.ReadString(
            "[__outcome.status, seen.length, seen[0].first === a, seen[0].second === b, m.takeOne(b) === b].join(':')"
        )
    );
    Assert.Equal(0, Entry.ReleaseCount);
  }

  [Fact]
  public async Task UnionEventPayloadWithRecordCaseHoldsSharedObject()
  {
    using var deferred = DeferredFixture.Create();

    deferred.StartOutcome(
        "(() => { globalThis.seen = []; m.addListener('choice', value => seen.push(value)); return m.emitChoiceAsync(a); })()"
    );
    await deferred.WaitForOutcomeAsync();

    Assert.Equal(
        "fulfilled:1:true:true",
        deferred.ReadString(
            "[__outcome.status, seen.length, seen[0].first === a, seen[0].second === null].join(':')"
        )
    );
    Assert.Equal(0, Entry.ReleaseCount);
  }

  private sealed class DeferredFixture : IDisposable
  {
    private DeferredFixture(HermesRuntimeFixture fixture, DotnetRuntimeContext context)
    {
      Fixture = fixture;
      Context = context;
    }

    public HermesRuntimeFixture Fixture { get; }

    public DotnetRuntimeContext Context { get; }

    public static DeferredFixture Create()
    {
      var fixture = HermesRuntimeFixture.Create();
      try
      {
        var context = fixture.Runtime.Execute(runtime =>
        {
          CompositeEntriesModule.Reset();
          var created = new DotnetRuntimeContext(runtime);
          using var modules = created.ModuleRegistry.GetOrCreateDotnetModulesObject();
          ExpoModulesProvider_Expo_ModulesCore_Tests.Register(created, modules);
          using var setup = fixture.Evaluate(
              "globalThis.m = globalThis._expoDotnet.modules.CompositeEntries; " +
              "globalThis.a = new m.Entry('a'); globalThis.b = new m.Entry('b'); true",
              "shared-object-deferred-setup.js"
          );
          return created;
        });
        return new DeferredFixture(fixture, context);
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
        using var result = Fixture.Evaluate(source, "shared-object-deferred-run.js");
        return true;
      });
    }

    public string ReadString(string expression)
    {
      return Fixture.Runtime.Execute(_ =>
      {
        using var result = Fixture.Evaluate(expression, "shared-object-deferred-read.js");
        return result.AsString();
      });
    }

    // Records the settlement of the Promise that the expression returns in globalThis.__outcome.
    public void StartOutcome(string expression)
    {
      Run(
          $$"""
          globalThis.__outcome = { status: 'pending', value: undefined };
          Promise.resolve({{expression}}).then(
            value => { globalThis.__outcome.status = 'fulfilled'; globalThis.__outcome.value = value; },
            error => { globalThis.__outcome.status = 'rejected'; globalThis.__outcome.value = String(error && error.message || error); }
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

    // Gives asynchronous lease releases runtime turns until the counter reaches the expected value
    // or the deadline passes; the caller still asserts the exact count.
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
