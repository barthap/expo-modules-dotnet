using System;
using System.Threading.Tasks;
using Expo.JSI;
using Expo.ModulesCore;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Tests.Fixtures;
using Expo.ModulesCore.Tests.Modules;
using Xunit;

namespace Expo.ModulesCore.Tests.SharedObjects;

public sealed class GeneratedSharedObjectTests
{
  [Fact]
  public void LazyClassInstallationHappensOnceAndIsReused()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.SharedThings; " +
          "const first = m.CounterEntry; " +
          "const second = globalThis._expoDotnet.modules.SharedThings.CounterEntry; " +
          "const a = new first(1); const b = new second(2); " +
          "[first === second, Object.getPrototypeOf(a) === Object.getPrototypeOf(b)].join(':')",
          "generated-shared-object-lazy-install.js"
      );

      Assert.Equal("true:true", result.AsString());
      return true;
    });
  }

  [Fact]
  public void GeneratedConstructorDecodesArgumentsAndBindsExactPrototype()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.SharedThings; " +
          "const c = new m.CounterEntry(5); " +
          "[c.current, Object.getPrototypeOf(c) === m.CounterEntry.prototype, " +
          "c instanceof m.CounterEntry, m.CounterEntry.prototype.constructor === m.CounterEntry]" +
          ".join(':')",
          "generated-shared-object-constructor.js"
      );

      Assert.Equal("5:true:true:true", result.AsString());
      return true;
    });
  }

  [Fact]
  public void ImplicitAndExplicitClassNamesAreExposedOnlyForConstructibleClasses()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.SharedThings; " +
          "[typeof m.CounterEntry, typeof m.Sibling, typeof m.NativeSnapshot, " +
          "typeof m.SnapshotEntry, new m.Sibling().kind].join(':')",
          "generated-shared-object-class-names.js"
      );

      Assert.Equal("function:function:undefined:undefined:1", result.AsString());
      return true;
    });
  }

  [Fact]
  public async Task SyncAndAsyncPrototypeMethodsRunOnDecodedReceiver()
  {
    using var fixture = HermesRuntimeFixture.Create();
    DotnetRuntimeContext? context = null;

    fixture.Runtime.Execute(runtime =>
    {
      context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var setup = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.SharedThings; " +
          "globalThis.__c = new m.CounterEntry(10); " +
          "globalThis.__sync = globalThis.__c.increment(4); " +
          "globalThis.__c.incrementLater(6).then(value => { globalThis.__async = value; }); " +
          "undefined",
          "generated-shared-object-methods-setup.js"
      );
      return true;
    });

    await WaitForConditionAsync(
        fixture,
        "typeof globalThis.__async !== 'undefined'"
    );

    fixture.Runtime.Execute(runtime =>
    {
      using var result = fixture.Evaluate(
          "[globalThis.__sync, globalThis.__async, globalThis.__c.current].join(':')",
          "generated-shared-object-methods-check.js"
      );
      Assert.Equal("14:20:20", result.AsString());
      context!.Dispose();
      return true;
    });
  }

  [Fact]
  public void ReadOnlyAndReadWritePropertiesWorkOnThePrototype()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.SharedThings; " +
          "const c = new m.CounterEntry(1); c.current = 42; " +
          "const snapshot = m.makeSnapshot(); " +
          "const readOnly = (() => { 'use strict'; try { snapshot.stamp = 0; return 'no error'; } " +
          "catch (error) { return error instanceof TypeError; } })(); " +
          "[c.current, snapshot.stamp, readOnly, " +
          "Object.getOwnPropertyNames(c).length, Object.getOwnPropertyNames(snapshot).length]" +
          ".join(':')",
          "generated-shared-object-properties.js"
      );

      Assert.Equal("42:7:true:0:0", result.AsString());
      return true;
    });
  }

  [Fact]
  public void NativeCreatedReturnAndRepeatEncodeKeepStrictIdentity()
  {
    using var fixture = HermesRuntimeFixture.Create();
    SharedThingsModule.LastSeenCounter = null;

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.SharedThings; " +
          "const a = m.makeCounter(3); " +
          "const b = m.echoCounter(a); " +
          "const c = m.echoCounter(a); " +
          "[a === b, b === c, m.readCounter(a)].join(':')",
          "generated-shared-object-identity.js"
      );

      Assert.Equal("true:true:3", result.AsString());
      return true;
    });
  }

  [Fact]
  public void JsConstructedInstanceReachesManagedCodeAsTheOriginalInstance()
  {
    using var fixture = HermesRuntimeFixture.Create();
    SharedThingsModule.LastSeenCounter = null;

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var first = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.SharedThings; " +
          "globalThis.__c = new m.CounterEntry(8); " +
          "m.echoCounter(globalThis.__c); 'done'",
          "generated-shared-object-original-first.js"
      );
      var firstSeen = SharedThingsModule.LastSeenCounter;
      Assert.NotNull(firstSeen);
      Assert.Equal(8, firstSeen!.Current);

      using var second = fixture.Evaluate(
          "globalThis._expoDotnet.modules.SharedThings.echoCounter(globalThis.__c); 'done'",
          "generated-shared-object-original-second.js"
      );
      Assert.Same(firstSeen, SharedThingsModule.LastSeenCounter);
      return true;
    });
  }

  [Fact]
  public void ForeignAndWrongClassReceiversAreRejectedBeforeAuthoredCode()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.SharedThings; " +
          "const increment = m.CounterEntry.prototype.increment; " +
          "const plain = (() => { try { increment.call({}, 1); return 'no error'; } " +
          "catch (error) { return 'rejected'; } })(); " +
          "const wrongClass = (() => { try { increment.call(new m.Sibling(), 1); return 'no error'; } " +
          "catch (error) { return error.message.includes('CounterEntry'); } })(); " +
          "const released = (() => { const c = new m.CounterEntry(1); c.release(); " +
          "try { increment.call(c, 1); return 'no error'; } " +
          "catch (error) { return error.message.includes('not an active shared object'); } })(); " +
          "[plain, wrongClass, released].join(':')",
          "generated-shared-object-receiver-rejection.js"
      );

      Assert.Equal("rejected:true:true", result.AsString());
      return true;
    });
  }

  [Fact]
  public void RepeatedReleaseIsIdempotentAndReleasesExactlyOnce()
  {
    using var fixture = HermesRuntimeFixture.Create();
    CounterEntry.ReleaseCount = 0;

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.SharedThings; " +
          "const c = new m.CounterEntry(1); " +
          "c.release(); c.release(); 'released'",
          "generated-shared-object-repeat-release.js"
      );

      Assert.Equal("released", result.AsString());
      Assert.Equal(1, CounterEntry.ReleaseCount);
      return true;
    });
  }

  [Fact]
  public void DeterministicCollectionReleasesTheConstructedInstance()
  {
    using var fixture = HermesRuntimeFixture.Create();
    CounterEntry.ReleaseCount = 0;
    DotnetRuntimeContext? context = null;

    fixture.Runtime.Execute(runtime =>
    {
      context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var setup = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.SharedThings; " +
          "globalThis.__temp = new m.CounterEntry(1); " +
          "globalThis.__temp = undefined; 'dropped'",
          "generated-shared-object-collect.js"
      );
      return true;
    });

    fixture.CollectGarbageForTesting();
    fixture.WaitUntilIdle();

    Assert.Equal(1, CounterEntry.ReleaseCount);

    fixture.Runtime.Execute(runtime =>
    {
      context!.Dispose();
      return true;
    });
  }

  [Fact]
  public async Task GeneratedSharedObjectEventsAreIsolatedPerInstance()
  {
    using var fixture = HermesRuntimeFixture.Create();
    DotnetRuntimeContext? context = null;
    fixture.Runtime.Execute(runtime =>
    {
      context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      using var setup = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.SharedThings; " +
          "globalThis.__eventLog = []; " +
          "globalThis.__eventA = new m.CounterEntry(1); " +
          "globalThis.__eventB = new m.CounterEntry(10); " +
          "globalThis.__eventA.addListener('onChange', value => __eventLog.push('a:' + value)); " +
          "globalThis.__eventB.addListener('onChange', value => __eventLog.push('b:' + value)); " +
          "globalThis.__eventA.incrementAndEmitAsync(2).then(() => { globalThis.__eventDone = true; });",
          "generated-shared-object-events-isolation.js"
      );
      return true;
    });

    await WaitForConditionAsync(fixture, "globalThis.__eventDone === true");
    fixture.Runtime.Execute(_ =>
    {
      using var result = fixture.Evaluate("globalThis.__eventLog.join(',')", "generated-shared-object-events-result.js");
      Assert.Equal("a:3", result.AsString());
      context!.Dispose();
      return true;
    });
  }

  [Fact]
  public void SharedObjectEventSubscriptionRemovalIsIdempotentAndCompactsStorage()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      using var result = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.SharedThings; " +
          "const c = new m.CounterEntry(1); const fn = () => {}; " +
          "const first = c.addListener('onChange', fn); c.addListener('onChange', () => {}); " +
          "const before = c.listenerCount('onChange'); first.remove(); first.remove(); " +
          "const names = Object.getOwnPropertyNames(c); " +
          "[before, c.listenerCount('onChange'), names.length, names.includes('addListener')].join(':')",
          "generated-shared-object-events-remove.js"
      );
      Assert.Equal("2:1:1:false", result.AsString());
      return true;
    });
  }

  [Fact]
  public void SharedObjectEventPrototypeMatchesListenerMutationSemantics()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      using var result = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.SharedThings; const c = new m.CounterEntry(1); " +
          "const log = []; let added = false; " +
          "c.addListener('onChange', () => { log.push('first'); if (!added) { added = true; c.addListener('onChange', () => log.push('late')); } }); " +
          "c.addListener('onChange', () => { log.push('second'); throw new Error('ignored'); }); " +
          "c.emit('onChange', 1); const first = log.join(','); c.emit('onChange', 2); " +
          "let rejected = false; try { c.removeListener('onChange', 42); } catch { rejected = true; } " +
          "[first, log.join(','), rejected].join(':')",
          "generated-shared-object-events-mutation.js"
      );
      Assert.Equal("first,second:first,second,first,second,late:true", result.AsString());
      return true;
    });
  }

  [Fact]
  public void CollectedSubscriptionDisposesOwnedWeakStateExactlyOnce()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var disposed = 0;
    SharedObjectEventPrototype.SubscriptionDisposedForTesting = () => Interlocked.Increment(ref disposed);
    DotnetRuntimeContext? context = null;
    try
    {
      fixture.Runtime.Execute(runtime =>
      {
        context = new DotnetRuntimeContext(runtime);
        using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
        ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
        using var setup = fixture.Evaluate(
            "const m = globalThis._expoDotnet.modules.SharedThings; globalThis.__subscriptionTarget = new m.CounterEntry(1); " +
            "globalThis.__subscriptionTarget.addListener('onChange', () => {}); 'dropped'",
            "generated-shared-object-events-subscription-gc.js"
        );
        return true;
      });
      fixture.CollectGarbageForTesting();
      fixture.WaitUntilIdle();
      Assert.Equal(1, Volatile.Read(ref disposed));
      fixture.Runtime.Execute(_ => { context!.Dispose(); return true; });
    }
    finally
    {
      SharedObjectEventPrototype.SubscriptionDisposedForTesting = null;
    }
  }

  [Fact]
  public void RetainedSubscriptionDoesNotKeepDisposedRuntimeContextAlive()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var disposed = 0;
    SharedObjectEventPrototype.SubscriptionDisposedForTesting = () => Interlocked.Increment(ref disposed);
    try
    {
      var contextReference = CreateDisposedContextWithRetainedSubscription(fixture);

      fixture.CollectGarbageForTesting();
      fixture.WaitUntilIdle();
      CollectManagedGarbage();

      Assert.False(contextReference.IsAlive);
      fixture.Runtime.Execute(_ =>
      {
        using var removed = fixture.Evaluate(
            "globalThis.__retainedSubscription.remove(); " +
            "globalThis.__retainedSubscription.remove(); 'removed'",
            "generated-shared-object-events-remove-after-teardown.js"
        );
        Assert.Equal("removed", removed.AsString());
        return true;
      });
      Assert.Equal(1, Volatile.Read(ref disposed));
    }
    finally
    {
      SharedObjectEventPrototype.SubscriptionDisposedForTesting = null;
    }
  }

  [Theory]
  [InlineData((int)SharedObjectEventSubscriptionSetupStep.BeforeCreateHostFunction)]
  [InlineData((int)SharedObjectEventSubscriptionSetupStep.AfterRemovePropertyDefined)]
  public void AddListenerFailureDoesNotCommitStorageAndDisposesWeakState(
      int failureStepValue)
  {
    var failureStep = (SharedObjectEventSubscriptionSetupStep)failureStepValue;
    using var fixture = HermesRuntimeFixture.Create();
    var disposed = 0;
    SharedObjectEventPrototype.SubscriptionDisposedForTesting = () => Interlocked.Increment(ref disposed);
    SharedObjectEventPrototype.SubscriptionSetupStepForTesting = step =>
    {
      if (step == failureStep)
      {
        throw new InvalidOperationException($"injected {step}");
      }
    };
    try
    {
      fixture.Runtime.Execute(runtime =>
      {
        using var context = new DotnetRuntimeContext(runtime);
        using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
        ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
        using var result = fixture.Evaluate(
            "const m = globalThis._expoDotnet.modules.SharedThings; " +
            "const c = new m.CounterEntry(1); let failed = false; " +
            "try { c.addListener('onChange', () => {}); } catch { failed = true; } " +
            "[failed, c.listenerCount('onChange')].join(':')",
            "generated-shared-object-events-add-failure.js"
        );
        Assert.Equal("true:0", result.AsString());
        return true;
      });
      Assert.Equal(1, Volatile.Read(ref disposed));
    }
    finally
    {
      SharedObjectEventPrototype.SubscriptionSetupStepForTesting = null;
      SharedObjectEventPrototype.SubscriptionDisposedForTesting = null;
    }
  }

  [Fact]
  public async Task SharedObjectEventDispatchFailsAfterReleaseAndTeardown()
  {
    using var fixture = HermesRuntimeFixture.Create();
    DotnetRuntimeContext? context = null;
    CounterEntry? instance = null;
    fixture.Runtime.Execute(runtime =>
    {
      context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      using var setup = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.SharedThings; globalThis.__lifetimeCounter = new m.CounterEntry(1); " +
          "m.echoCounter(globalThis.__lifetimeCounter); 'ready'",
          "generated-shared-object-events-lifetime.js"
      );
      instance = SharedThingsModule.LastSeenCounter;
      using var release = fixture.Evaluate("globalThis.__lifetimeCounter.release(); 'released'", "generated-shared-object-events-release.js");
      return true;
    });
    await Assert.ThrowsAnyAsync<Exception>(() => instance!.EmitReadyAsync());
    fixture.Runtime.Execute(_ => { context!.Dispose(); return true; });
    await Assert.ThrowsAnyAsync<Exception>(() => instance!.EmitReadyAsync());
  }

  [Fact]
  public async Task SharedObjectEventDispatchReleaseRaceHasDefinedWinners()
  {
    using var fixture = HermesRuntimeFixture.Create();
    CounterEntry.ReleaseCount = 0;
    DotnetRuntimeContext? context = null;
    CounterEntry? first = null;
    fixture.Runtime.Execute(runtime =>
    {
      context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      using var setup = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.SharedThings; globalThis.__raceLog = []; " +
          "globalThis.__raceA = new m.CounterEntry(1); __raceA.addListener('onReady', () => __raceLog.push('a')); " +
          "m.echoCounter(__raceA); 'ready'",
          "generated-shared-object-events-race-a.js"
      );
      first = SharedThingsModule.LastSeenCounter;
      return true;
    });

    context!.SharedObjects.EventDispatchStepForTesting = (step, entryId) =>
    {
      if (step == SharedObjectEventDispatchStep.WeakObjectLocked)
      {
        context.SharedObjects.Release(entryId);
      }
    };
    await Assert.ThrowsAnyAsync<Exception>(() => first!.EmitReadyAsync());
    Assert.Equal(1, CounterEntry.ReleaseCount);

    CounterEntry? second = null;
    fixture.Runtime.Execute(_ =>
    {
      context.SharedObjects.EventDispatchStepForTesting = null;
      using var setup = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.SharedThings; " +
          "globalThis.__raceB = new m.CounterEntry(2); __raceB.addListener('onReady', () => __raceLog.push('b')); " +
          "m.echoCounter(__raceB); 'ready'",
          "generated-shared-object-events-race-b.js"
      );
      second = SharedThingsModule.LastSeenCounter;
      return true;
    });
    context.SharedObjects.EventDispatchStepForTesting = (step, entryId) =>
    {
      if (step == SharedObjectEventDispatchStep.EntryRevalidated)
      {
        context.SharedObjects.Release(entryId);
      }
    };
    await second!.EmitReadyAsync();
    fixture.Runtime.Execute(_ =>
    {
      using var result = fixture.Evaluate("globalThis.__raceLog.join(',')", "generated-shared-object-events-race-result.js");
      Assert.Equal("b", result.AsString());
      context.SharedObjects.EventDispatchStepForTesting = null;
      context.Dispose();
      return true;
    });
    Assert.Equal(2, CounterEntry.ReleaseCount);
  }

  [Fact]
  public async Task ContextTeardownWinsBeforeSharedObjectEventDispatchRevalidation()
  {
    using var fixture = HermesRuntimeFixture.Create();
    CounterEntry.ReleaseCount = 0;
    DotnetRuntimeContext? context = null;
    SharedObjectRegistry? registry = null;
    CounterEntry? instance = null;
    fixture.Runtime.Execute(runtime =>
    {
      context = new DotnetRuntimeContext(runtime);
      registry = context.SharedObjects;
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      using var setup = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.SharedThings; " +
          "globalThis.__teardownBefore = new m.CounterEntry(1); " +
          "__teardownBefore.addListener('onReady', () => { globalThis.__teardownBeforeCalled = true; }); " +
          "m.echoCounter(__teardownBefore); 'ready'",
          "generated-shared-object-events-teardown-before.js"
      );
      instance = SharedThingsModule.LastSeenCounter;
      return true;
    });

    using var weakLocked = new ManualResetEventSlim();
    using var allowRevalidation = new ManualResetEventSlim();
    registry!.EventDispatchStepForTesting = (step, _) =>
    {
      if (step == SharedObjectEventDispatchStep.WeakObjectLocked)
      {
        weakLocked.Set();
        allowRevalidation.Wait(TestContext.Current.CancellationToken);
      }
    };
    Task? dispatch = null;
    Task? teardown = null;
    try
    {
      dispatch = Task.Run(() => instance!.EmitReadyAsync(), TestContext.Current.CancellationToken);
      Assert.True(weakLocked.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
      teardown = Task.Run(context!.Dispose, TestContext.Current.CancellationToken);
      await teardown.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
      allowRevalidation.Set();

      await Assert.ThrowsAnyAsync<Exception>(() => dispatch);
      Assert.Equal(1, CounterEntry.ReleaseCount);
      fixture.Runtime.Execute(_ =>
      {
        using var called = fixture.Evaluate(
            "String(globalThis.__teardownBeforeCalled === true)",
            "generated-shared-object-events-teardown-before-result.js"
        );
        Assert.Equal("false", called.AsString());
        return true;
      });
      fixture.CollectGarbageForTesting();
      fixture.WaitUntilIdle();
      Assert.Equal(0u, fixture.Counters.LongLivedObjectsRemaining);
    }
    finally
    {
      registry.EventDispatchStepForTesting = null;
      allowRevalidation.Set();
      if (dispatch is not null)
      {
        try { await dispatch.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); } catch { }
      }
      if (teardown is not null)
      {
        try { await teardown.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); } catch { }
      }
      else
      {
        context!.Dispose();
      }
    }
  }

  [Fact]
  public async Task SharedObjectEventDispatchCompletesWhenContextTeardownStartsAfterRevalidation()
  {
    using var fixture = HermesRuntimeFixture.Create();
    CounterEntry.ReleaseCount = 0;
    DotnetRuntimeContext? context = null;
    SharedObjectRegistry? registry = null;
    CounterEntry? instance = null;
    fixture.Runtime.Execute(runtime =>
    {
      context = new DotnetRuntimeContext(runtime);
      registry = context.SharedObjects;
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      using var setup = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.SharedThings; globalThis.__teardownAfterLog = []; " +
          "globalThis.__teardownAfter = new m.CounterEntry(1); " +
          "__teardownAfter.addListener('onReady', () => __teardownAfterLog.push('called')); " +
          "m.echoCounter(__teardownAfter); 'ready'",
          "generated-shared-object-events-teardown-after.js"
      );
      instance = SharedThingsModule.LastSeenCounter;
      return true;
    });

    using var revalidated = new ManualResetEventSlim();
    using var allowDispatch = new ManualResetEventSlim();
    registry!.EventDispatchStepForTesting = (step, _) =>
    {
      if (step == SharedObjectEventDispatchStep.EntryRevalidated)
      {
        revalidated.Set();
        allowDispatch.Wait(TestContext.Current.CancellationToken);
      }
    };
    Task? dispatch = null;
    Task? teardown = null;
    try
    {
      dispatch = Task.Run(() => instance!.EmitReadyAsync(), TestContext.Current.CancellationToken);
      Assert.True(revalidated.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
      teardown = Task.Run(context!.Dispose, TestContext.Current.CancellationToken);
      await teardown.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
      allowDispatch.Set();

      await dispatch;
      Assert.Equal(1, CounterEntry.ReleaseCount);
      fixture.Runtime.Execute(_ =>
      {
        using var result = fixture.Evaluate(
            "globalThis.__teardownAfterLog.join(',')",
            "generated-shared-object-events-teardown-after-result.js"
        );
        Assert.Equal("called", result.AsString());
        return true;
      });
      fixture.CollectGarbageForTesting();
      fixture.WaitUntilIdle();
      Assert.Equal(0u, fixture.Counters.LongLivedObjectsRemaining);
    }
    finally
    {
      registry.EventDispatchStepForTesting = null;
      allowDispatch.Set();
      if (dispatch is not null)
      {
        try { await dispatch.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); } catch { }
      }
      if (teardown is not null)
      {
        try { await teardown.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); } catch { }
      }
      else
      {
        context!.Dispose();
      }
    }
  }

  [Fact]
  public async Task SharedObjectEventDispatchAwaitsOffRuntimeAndEncodesRecordPayload()
  {
    using var fixture = HermesRuntimeFixture.Create();
    DotnetRuntimeContext? context = null;
    CounterEntry? instance = null;
    fixture.Runtime.Execute(runtime =>
    {
      context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      using var setup = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.SharedThings; globalThis.__payloadValue = -1; " +
          "globalThis.__payloadCounter = new m.CounterEntry(1); " +
          "__payloadCounter.addListener('onPayload', payload => { __payloadValue = payload.value; }); " +
          "m.echoCounter(__payloadCounter); 'ready'",
          "generated-shared-object-events-record.js"
      );
      instance = SharedThingsModule.LastSeenCounter;
      return true;
    });
    await Task.Run(
        () => instance!.EmitPayloadAsync(73),
        TestContext.Current.CancellationToken
    );
    fixture.Runtime.Execute(_ =>
    {
      using var result = fixture.Evaluate("String(globalThis.__payloadValue)", "generated-shared-object-events-record-result.js");
      Assert.Equal("73", result.AsString());
      context!.Dispose();
      return true;
    });
  }

  [Fact]
  public async Task SharedObjectEventDispatchSurfacesPayloadEncodeFailure()
  {
    using var fixture = HermesRuntimeFixture.Create();
    DotnetRuntimeContext? context = null;
    CounterEntry? instance = null;
    fixture.Runtime.Execute(runtime =>
    {
      context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      using var setup = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.SharedThings; globalThis.__encodeCounter = new m.CounterEntry(1); " +
          "m.echoCounter(__encodeCounter); 'ready'",
          "generated-shared-object-events-encode-failure.js"
      );
      instance = SharedThingsModule.LastSeenCounter;
      return true;
    });
    var disposedPayload = fixture.Runtime.Execute(runtime => runtime.CreateString("disposed"));
    disposedPayload.Dispose();
    await Assert.ThrowsAsync<ObjectDisposedException>(() => instance!.EmitValueAsync(disposedPayload));
    fixture.Runtime.Execute(_ => { context!.Dispose(); return true; });
  }

  [Fact]
  public void SharedObjectEventPrototypeCoversRemovalAndDisposedRegistration()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.Runtime.Execute(runtime =>
    {
      var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      using var result = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.SharedThings; const c = new m.CounterEntry(1); " +
          "const a = () => {}; const b = () => {}; const sa = c.addListener('onChange', a); c.addListener('onChange', b); " +
          "c.removeListener('onChange', a); const afterOne = c.listenerCount('onChange'); " +
          "c.removeAllListeners('onChange'); const afterAll = c.listenerCount('onChange'); " +
          "const sc = c.addListener('onChange', a); c.removeSubscription(sc); const afterSubscription = c.listenerCount('onChange'); " +
          "const invalid = ['addListener', 'removeListener', 'removeAllListeners', 'emit', 'listenerCount', 'removeSubscription']" +
          ".every(name => { try { c[name](); return false; } catch { return true; } }); " +
          "globalThis.__retainedAddListener = c.addListener; [afterOne, afterAll, afterSubscription, invalid].join(':')",
          "generated-shared-object-events-removal-methods.js"
      );
      Assert.Equal("1:0:0:true", result.AsString());
      context.Dispose();
      using var disposed = fixture.Evaluate(
          "try { globalThis.__retainedAddListener.call({}, 'x', () => {}); 'no error'; } " +
          "catch (error) { error.message.includes('DotnetRuntimeContext').toString(); }",
          "generated-shared-object-events-disposed-method.js"
      );
      Assert.Equal("true", disposed.AsString());
      return true;
    });
  }

  [Fact]
  public void SelfCapturingSharedObjectListenerDoesNotPreventCollection()
  {
    using var fixture = HermesRuntimeFixture.Create();
    CounterEntry.ReleaseCount = 0;
    DotnetRuntimeContext? context = null;
    fixture.Runtime.Execute(runtime =>
    {
      context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      using var setup = fixture.Evaluate(
          "(() => { const m = globalThis._expoDotnet.modules.SharedThings; " +
          "const counter = new m.CounterEntry(1); " +
          "counter.addListener('onChange', () => counter.current); })(); 'dropped'",
          "generated-shared-object-events-self-capture.js"
      );
      return true;
    });

    fixture.CollectGarbageForTesting();
    fixture.WaitUntilIdle();
    Assert.Equal(1, CounterEntry.ReleaseCount);
    fixture.Runtime.Execute(_ => { context!.Dispose(); return true; });
  }

  [Fact]
  public void ContextTeardownReleasesInstancesAndClassInstallations()
  {
    using var fixture = HermesRuntimeFixture.Create();
    CounterEntry.ReleaseCount = 0;

    fixture.Runtime.Execute(runtime =>
    {
      var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var setup = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.SharedThings; " +
          "globalThis.__kept = new m.CounterEntry(1); 'created'",
          "generated-shared-object-teardown-setup.js"
      );

      context.Dispose();

      Assert.Equal(1, CounterEntry.ReleaseCount);

      using var result = fixture.Evaluate(
          "try { new globalThis._expoDotnet.modules.SharedThings.CounterEntry(1); 'no error'; } " +
          "catch (error) { error.message; }",
          "generated-shared-object-teardown-construct.js"
      );
      Assert.Contains("DotnetRuntimeContext", result.AsString());
      return true;
    });

    fixture.WaitUntilIdle();
    Assert.Equal(0u, fixture.Counters.LongLivedObjectsRemaining);
  }

  [Fact]
  public void UseAfterReleaseFailsLoudly()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.SharedThings; " +
          "const c = new m.CounterEntry(1); c.release(); " +
          "const method = (() => { try { c.increment(1); return 'no error'; } " +
          "catch (error) { return error.message.includes('not an active shared object'); } })(); " +
          "const property = (() => { try { return String(c.current); } " +
          "catch (error) { return error.message.includes('not an active shared object'); } })(); " +
          "[method, property].join(':')",
          "generated-shared-object-use-after-release.js"
      );

      Assert.Equal("true:true", result.AsString());
      return true;
    });
  }

  [Fact]
  public void SharedRefReleaseDoesNotDisposeTheReference()
  {
    using var fixture = HermesRuntimeFixture.Create();
    TrackedRef.LastCreated = null;

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.SharedThings; " +
          "const tracked = m.wrapTracked(); " +
          "const before = tracked.isDisposed; " +
          "tracked.release(); " +
          "[before].join(':')",
          "generated-shared-object-shared-ref.js"
      );

      Assert.Equal("false", result.AsString());
      Assert.NotNull(TrackedRef.LastCreated);
      Assert.False(TrackedRef.LastCreated!.Ref.Disposed);
      return true;
    });
  }

  [Fact]
  public async Task AsyncSharedResultCompletedOnAnotherThreadPreservesIdentity()
  {
    using var fixture = HermesRuntimeFixture.Create();
    SharedThingsModule.LastSeenCounter = null;
    SharedThingsModule.PendingCounter = new TaskCompletionSource<CounterEntry>(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    DotnetRuntimeContext? context = null;

    fixture.Runtime.Execute(runtime =>
    {
      context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var setup = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.SharedThings; " +
          "globalThis.__a = m.makeCounter(9); " +
          "m.echoCounter(globalThis.__a); " +
          "m.awaitPendingCounter().then(value => { globalThis.__resolved = value; }); " +
          "'pending'",
          "generated-shared-object-async-setup.js"
      );
      return true;
    });

    var existing = SharedThingsModule.LastSeenCounter;
    Assert.NotNull(existing);

    // Complete the pending task on a different managed thread after the host-function frame has
    // exited; settlement must use the captured runtime context, not the thread-static accessor.
    await Task.Run(() => SharedThingsModule.PendingCounter!.TrySetResult(existing!));

    await WaitForConditionAsync(
        fixture,
        "typeof globalThis.__resolved !== 'undefined'"
    );

    fixture.Runtime.Execute(runtime =>
    {
      using var result = fixture.Evaluate(
          "(globalThis.__resolved === globalThis.__a).toString()",
          "generated-shared-object-async-check.js"
      );
      Assert.Equal("true", result.AsString());
      context!.Dispose();
      return true;
    });
  }

  [System.Runtime.CompilerServices.MethodImpl(
      System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
  private static WeakReference CreateDisposedContextWithRetainedSubscription(
      HermesRuntimeFixture fixture)
  {
    WeakReference? contextReference = null;
    fixture.Runtime.Execute(runtime =>
    {
      var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      using var setup = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.SharedThings; " +
          "const c = new m.CounterEntry(1); " +
          "globalThis.__retainedSubscription = c.addListener('onChange', () => {}); 'retained'",
          "generated-shared-object-events-retained-subscription.js"
      );
      contextReference = new WeakReference(context);
      context.Dispose();
      using var dropRoots = fixture.Evaluate(
          "globalThis._expoDotnet = undefined; 'dropped'",
          "generated-shared-object-events-drop-context-roots.js"
      );
      return true;
    });
    return contextReference!;
  }

  private static void CollectManagedGarbage()
  {
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
  }

  private static async Task WaitForConditionAsync(
      HermesRuntimeFixture fixture,
      string conditionExpression
  )
  {
    for (var attempt = 0; attempt < 200; attempt++)
    {
      fixture.DrainTasks();
      var done = fixture.Runtime.Execute(_ =>
      {
        using var value = fixture.Evaluate(
            $"({conditionExpression}) ? 'yes' : 'no'",
            "generated-shared-object-wait.js"
        );
        return value.AsString();
      });
      if (done == "yes")
      {
        return;
      }
      await Task.Delay(10, TestContext.Current.CancellationToken);
    }

    Assert.Fail($"Condition '{conditionExpression}' did not become true.");
  }
}
