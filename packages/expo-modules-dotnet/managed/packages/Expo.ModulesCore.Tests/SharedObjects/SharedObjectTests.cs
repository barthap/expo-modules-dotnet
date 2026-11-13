using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Expo.JSI;
using Expo.ModulesCore;
using Expo.ModulesCore.Codecs;
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
  public void SharedObjectMembersUseTheSameNullableReferenceCodecPathAsModuleMembers()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "const entry = new globalThis._expoDotnet.modules.SharedThings.Sibling(); " +
          "entry.label = 'kept'; const kept = entry.label; entry.label = null; " +
          "[kept, entry.label === null, entry.echoLabel(null) === null, entry.echoLabel('back')].join(':')",
          "generated-shared-object-nullable-members.js"
      );

      Assert.Equal("kept:true:true:back", result.AsString());
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
          "globalThis.__eventA.addListener('change', value => __eventLog.push('a:' + value)); " +
          "globalThis.__eventB.addListener('change', value => __eventLog.push('b:' + value)); " +
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

  // A shared-object listener on the old prefixed name is accepted and never called; CounterEntry's
  // OnChange dispatches change.
  [Fact]
  public async Task GeneratedSharedObjectEventDispatchesUnprefixedNameAndNotOldPrefixedName()
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
          "globalThis.__prefixLog = []; " +
          "globalThis.__prefixCounter = new m.CounterEntry(1); " +
          "__prefixCounter.addListener('change', value => __prefixLog.push('change:' + value)); " +
          "__prefixCounter.addListener('onChange', value => __prefixLog.push('old:' + value)); " +
          "__prefixCounter.incrementAndEmitAsync(2).then(() => { globalThis.__prefixDone = true; });",
          "generated-shared-object-events-prefix.js"
      );
      return true;
    });

    await WaitForConditionAsync(fixture, "globalThis.__prefixDone === true");
    fixture.Runtime.Execute(_ =>
    {
      using var result = fixture.Evaluate(
          "globalThis.__prefixLog.join(',')",
          "generated-shared-object-events-prefix-result.js"
      );
      Assert.Equal("change:3", result.AsString());
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
          "const first = c.addListener('change', fn); c.addListener('change', () => {}); " +
          "const before = c.listenerCount('change'); first.remove(); first.remove(); " +
          "const names = Object.getOwnPropertyNames(c); " +
          "[before, c.listenerCount('change'), names.length, names.includes('addListener')].join(':')",
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
          "c.addListener('change', () => { log.push('first'); if (!added) { added = true; c.addListener('change', () => log.push('late')); } }); " +
          "c.addListener('change', () => { log.push('second'); throw new Error('ignored'); }); " +
          "c.emit('change', 1); const first = log.join(','); c.emit('change', 2); " +
          "let rejected = false; try { c.removeListener('change', 42); } catch { rejected = true; } " +
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
            "globalThis.__subscriptionTarget.addListener('change', () => {}); 'dropped'",
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
            "try { c.addListener('change', () => {}); } catch { failed = true; } " +
            "[failed, c.listenerCount('change')].join(':')",
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
          "globalThis.__raceA = new m.CounterEntry(1); __raceA.addListener('ready', () => __raceLog.push('a')); " +
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
          "globalThis.__raceB = new m.CounterEntry(2); __raceB.addListener('ready', () => __raceLog.push('b')); " +
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
          "__teardownBefore.addListener('ready', () => { globalThis.__teardownBeforeCalled = true; }); " +
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
          "__teardownAfter.addListener('ready', () => __teardownAfterLog.push('called')); " +
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
          "__payloadCounter.addListener('payload', payload => { __payloadValue = payload.value; }); " +
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
  public async Task SharedObjectArrayBufferEventRetainsLeaseBeforeScheduling()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var (context, instance, payload) = SetUpBufferEvent(fixture, "generated-shared-object-events-buffer.js");

    fixture.ResetCounters();
    fixture.DisableSyncExecutionForTesting();
    fixture.PauseRuntimeExecutor();
    var emitted = instance.EmitBufferAsync(payload);
    payload.Dispose();
    fixture.ResumeRuntimeExecutor();
    await emitted;
    fixture.WaitUntilIdle();
    fixture.SetSyncExecutionSupportedForTesting(true);

    Assert.Equal("1:7,8,9", ReadSharedBuffer(fixture));
    Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal(0u, fixture.Counters.LongLivedArrayBuffersAbandoned);
    fixture.Runtime.Execute(_ => { context.Dispose(); return true; });
  }

  [Fact]
  public async Task SharedObjectNullArrayBufferEventDispatchesOneNullArgument()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var (context, instance, payload) = SetUpBufferEvent(fixture, "generated-shared-object-events-null-buffer.js");
    payload.Dispose();
    fixture.WaitUntilIdle();

    fixture.ResetCounters();
    await GeneratedSharedObjectEvents.EmitAsync(context, instance, "buffer", (ArrayBuffer?)null);
    fixture.WaitUntilIdle();

    Assert.Equal("1:null", ReadSharedBuffer(fixture));
    Assert.Equal(0u, fixture.Counters.LongLivedArrayBuffersReleased);
    fixture.Runtime.Execute(_ => { context.Dispose(); return true; });
  }

  [Fact]
  public async Task GeneratedSharedObjectNullableBufferEventDeliversOneNullArgument()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var (context, instance, payload) = SetUpBufferEvent(
        fixture,
        "generated-shared-object-events-optional-null-buffer.js",
        "optionalBuffer"
    );
    payload.Dispose();
    fixture.WaitUntilIdle();

    fixture.ResetCounters();
    await instance.EmitOptionalBufferAsync(null);
    fixture.WaitUntilIdle();

    Assert.Equal("1:null", ReadSharedBuffer(fixture));
    Assert.Equal(0u, fixture.Counters.LongLivedArrayBuffersReleased);
    fixture.Runtime.Execute(_ => { context.Dispose(); return true; });
  }

  [Fact]
  public async Task GeneratedSharedObjectNullableBufferEventKeepsItsLeaseAfterCallerDisposes()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var (context, instance, payload) = SetUpBufferEvent(
        fixture,
        "generated-shared-object-events-optional-buffer.js",
        "optionalBuffer"
    );

    fixture.ResetCounters();
    fixture.DisableSyncExecutionForTesting();
    fixture.PauseRuntimeExecutor();
    var emitted = instance.EmitOptionalBufferAsync(payload);
    payload.Dispose();
    fixture.ResumeRuntimeExecutor();
    await emitted;
    fixture.WaitUntilIdle();
    fixture.SetSyncExecutionSupportedForTesting(true);

    Assert.Equal("1:7,8,9", ReadSharedBuffer(fixture));
    Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal(0u, fixture.Counters.LongLivedArrayBuffersAbandoned);
    fixture.Runtime.Execute(_ => { context.Dispose(); return true; });
  }

  private static (DotnetRuntimeContext Context, CounterEntry Instance, ArrayBuffer Payload) SetUpBufferEvent(
      HermesRuntimeFixture fixture,
      string sourceName,
      string eventName = "buffer")
  {
    DotnetRuntimeContext? context = null;
    CounterEntry? instance = null;
    ArrayBuffer? payload = null;
    fixture.Runtime.Execute(runtime =>
    {
      context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      using var setup = fixture.Evaluate(
          "const m = globalThis._expoDotnet.modules.SharedThings; " +
          "globalThis.__bufferCounter = new m.CounterEntry(1); " +
          $"__bufferCounter.addListener('{eventName}', (...args) => {{ " +
          "globalThis.__sharedBuffer = args.length + ':' + " +
          "(args[0] === null ? 'null' : Array.from(new Uint8Array(args[0])).join(',')); }); " +
          "m.echoCounter(__bufferCounter); 'ready'",
          sourceName
      );
      instance = SharedThingsModule.LastSeenCounter;
      using var value = fixture.Evaluate("Uint8Array.from([7, 8, 9]).buffer", sourceName);
      payload = ArrayBufferCodec.Decode(value.Ref, runtime, CodecTestContext.Unused);
      return true;
    });
    return (context!, instance!, payload!);
  }

  private static string ReadSharedBuffer(HermesRuntimeFixture fixture) =>
      fixture.Runtime.Execute(_ =>
      {
        using var value = fixture.Evaluate("globalThis.__sharedBuffer", "generated-shared-object-events-buffer-result.js");
        return value.AsString();
      });

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
          "const a = () => {}; const b = () => {}; const sa = c.addListener('change', a); c.addListener('change', b); " +
          "c.removeListener('change', a); const afterOne = c.listenerCount('change'); " +
          "c.removeAllListeners('change'); const afterAll = c.listenerCount('change'); " +
          "const sc = c.addListener('change', a); c.removeSubscription(sc); const afterSubscription = c.listenerCount('change'); " +
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
          "counter.addListener('change', () => counter.current); })(); 'dropped'",
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

  private const string JsErrHelper =
      "const err = f => { try { f(); return 'no error'; } " +
      "catch (error) { return error instanceof Error ? error.message : typeof error; } }; ";

  private static DotnetRuntimeContext CreateNullableThingsContext(
      HermesRuntimeFixture fixture,
      JavaScriptRuntime runtime)
  {
    NullableSharedThingsModule.LastEntry = null;
    OptionalTrackedRef.LastCreated = null;
    var context = new DotnetRuntimeContext(runtime);
    using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
    ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
    using var setup = fixture.Evaluate(
        "globalThis.m = globalThis._expoDotnet.modules.NullableSharedThings; 'ready'",
        "nullable-shared-object-module-setup.js"
    );
    return context;
  }

  [Fact]
  public async Task NullableSharedObjectNullCrossesEveryBoundaryWithoutPairing()
  {
    using var fixture = HermesRuntimeFixture.Create();
    DotnetRuntimeContext? context = null;
    var steps = new List<SharedObjectPairingStep>();
    var before = 0;

    fixture.Runtime.Execute(runtime =>
    {
      context = CreateNullableThingsContext(fixture, runtime);
      using var setup = fixture.Evaluate(
          "globalThis.e = new m.OptionalEntry('e'); globalThis.e2 = new m.OptionalEntry('e2'); " +
          "globalThis.r0 = m.makeRef(); m.stored = e; e.parent = e2; e.tracked = r0; m.storedRef = r0; m.echo(e); 'ready'",
          "nullable-shared-object-null-setup.js"
      );

      // Every instance is created and paired, and every setter holds a live value, before the hook
      // is set and the registry count is read. The null assignments below then change real state,
      // and only decodes run after this point, so a pairing step would mean a null reached the
      // registry.
      context.SharedObjects.PairingStepForTesting = steps.Add;
      before = context.SharedObjects.Count;

      using var result = fixture.Evaluate(
          "[m.echo(null) === null, m.echo(undefined) === null, " +
          "(m.stored = null, m.stored === null), (m.stored = e, m.stored = undefined, m.stored === null), " +
          "m.echoRef(null) === null, m.echoRef(undefined) === null, " +
          "(m.storedRef = null, m.storedRef === null), " +
          "(e.parent = null, e.parent === null), (e.parent = e2, e.parent = undefined, e.parent === null), " +
          "e.echoParent(null) === null, e.echoParent(undefined) === null, " +
          "(e.tracked = null, e.tracked === null)].join(':')",
          "nullable-shared-object-null-sync.js"
      );
      Assert.Equal(
          "true:true:true:true:true:true:true:true:true:true:true:true",
          result.AsString()
      );
      Assert.Null(NullableSharedThingsModule.LastEntry);

      using var pending = fixture.Evaluate(
          "m.echoLater(null).then(v => { globalThis.__later = String(v === null); }, " +
          "error => { globalThis.__later = 'error:' + error.message; }); " +
          "m.echoRefLater(undefined).then(v => { globalThis.__refLater = String(v === null); }, " +
          "error => { globalThis.__refLater = 'error:' + error.message; }); 'pending'",
          "nullable-shared-object-null-async.js"
      );
      return true;
    });

    await WaitForConditionAsync(
        fixture,
        "typeof globalThis.__later !== 'undefined' && typeof globalThis.__refLater !== 'undefined'"
    );

    fixture.Runtime.Execute(runtime =>
    {
      using var later = fixture.Evaluate(
          "[__later, __refLater].join(':')",
          "nullable-shared-object-null-async-check.js"
      );
      Assert.Equal("true:true", later.AsString());
      Assert.Empty(steps);
      Assert.Equal(before, context!.SharedObjects.Count);

      // Control: a non-null native-created return records the pairing steps, so the empty list
      // above is meaningful.
      using var control = fixture.Evaluate("m.makeEntry('x'); 'made'", "nullable-shared-object-null-control.js");
      Assert.Equal(
          new[]
          {
            SharedObjectPairingStep.InstanceObjectCreated,
            SharedObjectPairingStep.NativeStateAttached,
            SharedObjectPairingStep.WeakObjectCreated,
          },
          steps
      );
      context!.Dispose();
      return true;
    });
  }

  [Fact]
  public async Task NullableSharedObjectNonNullKeepsIdentity()
  {
    using var fixture = HermesRuntimeFixture.Create();
    DotnetRuntimeContext? context = null;
    var steps = new List<SharedObjectPairingStep>();
    var before = 0;

    fixture.Runtime.Execute(runtime =>
    {
      context = CreateNullableThingsContext(fixture, runtime);
      using var setup = fixture.Evaluate(
          "globalThis.a = m.makeEntry('a'); globalThis.b = new m.OptionalEntry('b'); " +
          "globalThis.r = m.makeRef(); 'ready'",
          "nullable-shared-object-identity-setup.js"
      );

      using var echoA = fixture.Evaluate("m.echo(globalThis.a); 'once'", "nullable-shared-object-identity-a1.js");
      var first = NullableSharedThingsModule.LastEntry;
      Assert.Equal("a", first!.Label);
      using var echoAAgain = fixture.Evaluate("m.echo(globalThis.a); 'twice'", "nullable-shared-object-identity-a2.js");
      Assert.Same(first, NullableSharedThingsModule.LastEntry);

      using var echoB = fixture.Evaluate("m.echo(globalThis.b); 'once'", "nullable-shared-object-identity-b1.js");
      var firstB = NullableSharedThingsModule.LastEntry;
      Assert.Equal("b", firstB!.Label);
      using var echoBAgain = fixture.Evaluate("m.echo(globalThis.b); 'twice'", "nullable-shared-object-identity-b2.js");
      Assert.Same(firstB, NullableSharedThingsModule.LastEntry);

      context.SharedObjects.PairingStepForTesting = steps.Add;
      before = context.SharedObjects.Count;

      using var result = fixture.Evaluate(
          "[m.echo(a) === a, m.echo(a) === a, m.echo(b) === b, " +
          "(m.stored = a, m.stored === a), m.stored === a, m.describe(b), " +
          "m.echoRef(r) === r, m.echoRef(r) === r, (m.storedRef = r, m.storedRef === r), " +
          "b.echoParent(a) === a, (b.parent = a, b.parent === a), " +
          "new m.OptionalEntry('c', a).parent === a, (b.tracked = r, b.tracked === r)].join(':')",
          "nullable-shared-object-identity-sync.js"
      );
      Assert.Equal("true:true:true:true:true:b:true:true:true:true:true:true:true", result.AsString());
      // Only 'c' is new, so exactly one instance object is created.
      Assert.Equal(1, steps.Count(s => s == SharedObjectPairingStep.InstanceObjectCreated));
      Assert.Equal(before + 1, context.SharedObjects.Count);

      using var pending = fixture.Evaluate(
          "m.echoLater(globalThis.a).then(v => { globalThis.__later = String(v === globalThis.a); }, " +
          "error => { globalThis.__later = 'error:' + error.message; }); " +
          "m.echoRefLater(globalThis.r).then(v => { globalThis.__refLater = String(v === globalThis.r); }, " +
          "error => { globalThis.__refLater = 'error:' + error.message; }); 'pending'",
          "nullable-shared-object-identity-async.js"
      );
      return true;
    });

    await WaitForConditionAsync(
        fixture,
        "typeof globalThis.__later !== 'undefined' && typeof globalThis.__refLater !== 'undefined'"
    );

    fixture.Runtime.Execute(runtime =>
    {
      using var later = fixture.Evaluate(
          "[__later, __refLater].join(':')",
          "nullable-shared-object-identity-async-check.js"
      );
      Assert.Equal("true:true", later.AsString());
      context!.Dispose();
      return true;
    });
  }

  [Theory]
  [InlineData("({})", "NativeState entry for SharedObjectNativeState is missing.")]
  [InlineData("m.makeRef()", "is not a 'Expo.ModulesCore.Tests.Modules.OptionalEntry' instance.")]
  [InlineData("globalThis.released", "not an active shared object")]
  [InlineData("globalThis.foreign", "not an active shared object")]
  [InlineData("0", "Value is not an object.")]
  [InlineData("false", "Value is not an object.")]
  [InlineData("''", "Value is not an object.")]
  [InlineData("5", "Value is not an object.")]
  public void NullableSharedObjectFailuresMatchTheExactParameter(string input, string expected)
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = CreateNullableThingsContext(fixture, runtime);
      using var setup = fixture.Evaluate(
          "globalThis.kept = m.makeEntry('kept'); m.stored = kept; " +
          "globalThis.e = new m.OptionalEntry('e', kept); " +
          "globalThis.released = m.makeEntry('released'); released.release(); 'ready'",
          "nullable-shared-object-failures-setup.js"
      );

      // An entry installed by another context in the same runtime is not active in this one.
      var contextB = new DotnetRuntimeContext(runtime);
      try
      {
        using var holder = runtime.CreateObject();
        GeneratedSharedObjectClass.Install(contextB, holder, typeof(OptionalEntry), "OptionalEntry", 0, null, null);
        using var foreign = SharedObjectCodec<OptionalEntry>.Encode(new OptionalEntry("foreign"), runtime, contextB);
        using var global = runtime.Global();
        global.SetProperty("foreign", foreign);

        using var result = fixture.Evaluate(
            JsErrHelper +
            $"const v = () => ({input}); " +
            "[err(() => m.echo(v())), err(() => m.echoExact(v())), " +
            "err(() => { m.stored = v(); }), err(() => e.echoParent(v())), " +
            "err(() => { e.parent = v(); }), String(m.stored === kept && e.parent === kept)].join('|')",
            "nullable-shared-object-failures.js"
        );

        var parts = result.AsString().Split('|');
        Assert.Equal(6, parts.Length);
        for (var i = 0; i < 5; i++)
        {
          Assert.Contains(expected, parts[i]);
        }
        // The nullable parameter fails exactly as the non-null one does. The JavaScript message is
        // the full exception text with its stack trace, and the two functions have different
        // frames, so compare the first line (exception type and message).
        Assert.Equal(parts[0].Split('\n')[0], parts[1].Split('\n')[0]);
        // A failed setter did not change the stored value.
        Assert.Equal("true", parts[5]);
      }
      finally
      {
        contextB.Dispose();
      }
      return true;
    });
  }

  [Theory]
  [InlineData("({})", "NativeState entry for SharedObjectNativeState is missing.")]
  [InlineData("m.makeEntry('x')", "is not a 'Expo.ModulesCore.Tests.Modules.OptionalTrackedRef' instance.")]
  [InlineData("0", "Value is not an object.")]
  [InlineData("5", "Value is not an object.")]
  public void NullableSharedRefFailuresMatchTheExactParameter(string input, string expected)
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = CreateNullableThingsContext(fixture, runtime);
      using var setup = fixture.Evaluate(
          "globalThis.r0 = m.makeRef(); m.storedRef = r0; 'ready'",
          "nullable-shared-ref-failures-setup.js"
      );

      using var result = fixture.Evaluate(
          JsErrHelper +
          $"const v = () => ({input}); " +
          "[err(() => m.echoRef(v())), err(() => { m.storedRef = v(); }), " +
          "String(m.storedRef === r0)].join('|')",
          "nullable-shared-ref-failures.js"
      );

      var parts = result.AsString().Split('|');
      Assert.Equal(3, parts.Length);
      Assert.Contains(expected, parts[0]);
      Assert.Contains(expected, parts[1]);
      // A failed setter did not change the stored value.
      Assert.Equal("true", parts[2]);
      return true;
    });
  }

  [Fact]
  public void NullableSharedRefSubclassDoesNotDisposeItsValue()
  {
    using var fixture = HermesRuntimeFixture.Create();
    DotnetRuntimeContext? context = null;
    OptionalTrackedRef? released = null;
    OptionalTrackedRef? kept = null;

    fixture.Runtime.Execute(runtime =>
    {
      context = CreateNullableThingsContext(fixture, runtime);

      using var first = fixture.Evaluate(
          "const r = m.makeRef(); " +
          "const results = [m.echoRef(r) === r, m.echoRef(null) === null, " +
          "(m.storedRef = r, m.storedRef === r), (m.storedRef = null, m.storedRef === null), " +
          "r.isDisposed]; r.release(); results.join(':')",
          "nullable-shared-ref-subclass-first.js"
      );
      Assert.Equal("true:true:true:true:false", first.AsString());
      released = OptionalTrackedRef.LastCreated;
      Assert.False(released!.Ref.Disposed);

      using var second = fixture.Evaluate(
          "globalThis.k = m.makeRef(); m.storedRef = k; m.storedRef = null; String(m.echoRef(k) === k)",
          "nullable-shared-ref-subclass-second.js"
      );
      Assert.Equal("true", second.AsString());
      kept = OptionalTrackedRef.LastCreated;
      Assert.NotSame(released, kept);

      context.Dispose();
      return true;
    });

    Assert.False(released!.Ref.Disposed);
    Assert.False(kept!.Ref.Disposed);
  }

  [Fact]
  public void OptionalNullableSharedObjectDefaultsToNull()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = CreateNullableThingsContext(fixture, runtime);

      // The only legal default of a shared-object reference is null, so omission, undefined, and
      // explicit null cannot be told apart at runtime.
      using var result = fixture.Evaluate(
          "globalThis.a = m.makeEntry('a'); " +
          "[m.describe(), m.describe(undefined), m.describe(null), m.describe(a), " +
          "new m.OptionalEntry('c').parent === null, new m.OptionalEntry('d', undefined).parent === null, " +
          "new m.OptionalEntry('f', null).parent === null, new m.OptionalEntry('g', a).parent === a].join(':')",
          "nullable-shared-object-optional-default.js"
      );
      Assert.Equal("null:null:null:a:true:true:true:true", result.AsString());
      return true;
    });
  }

  [Fact]
  public void RequiredNullableSharedObjectStillRequiresItsArgument()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = CreateNullableThingsContext(fixture, runtime);
      using var setup = fixture.Evaluate(
          "globalThis.e = new m.OptionalEntry('e'); m.echo(e); 'ready'",
          "nullable-shared-object-required-setup.js"
      );
      var sentinel = NullableSharedThingsModule.LastEntry;
      Assert.NotNull(sentinel);

      using var result = fixture.Evaluate(
          JsErrHelper +
          "[err(() => m.echo()), err(() => m.echoRef()), err(() => globalThis.e.echoParent())].join('|')",
          "nullable-shared-object-required.js"
      );

      var parts = result.AsString().Split('|');
      Assert.Equal(3, parts.Length);
      foreach (var part in parts)
      {
        Assert.Contains("got 0", part);
      }
      // The failing calls never reached authored code, so the sentinel is still the last entry.
      Assert.Same(sentinel, NullableSharedThingsModule.LastEntry);
      return true;
    });
  }

  [Fact]
  public void NonNullSharedObjectParameterStaysStrict()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = CreateNullableThingsContext(fixture, runtime);

      using var result = fixture.Evaluate(
          JsErrHelper + "[err(() => m.echoExact(null)), err(() => m.echoExact(undefined))].join('|')",
          "nullable-shared-object-non-null-strict.js"
      );

      var parts = result.AsString().Split('|');
      Assert.Equal(2, parts.Length);
      foreach (var part in parts)
      {
        Assert.Contains("Value is not an object.", part);
      }
      return true;
    });
  }

  private static DotnetRuntimeContext CreateBaseThingsContext(
      HermesRuntimeFixture fixture,
      JavaScriptRuntime runtime)
  {
    BaseThingsModule.LastPath = null;
    var context = new DotnetRuntimeContext(runtime);
    using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
    ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
    using var setup = fixture.Evaluate(
        "globalThis.m = globalThis._expoDotnet.modules.BaseThings; 'ready'",
        "base-shared-object-module-setup.js"
    );
    return context;
  }

  [Fact]
  public void SharedObjectBaseParameterReceivesEverySubclass()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = CreateBaseThingsContext(fixture, runtime);
      var steps = new List<SharedObjectPairingStep>();

      using var setup = fixture.Evaluate(
          // The Hermes test host does not compile `class` syntax, so the subclass is built with
          // Reflect.construct and a prototype link, which gives the same prototype chain.
          "globalThis.f = new m.FileEntry('f'); " +
          "globalThis.SubFile = function SubFile(name) { return Reflect.construct(m.FileEntry, [name], SubFile); }; " +
          "Object.setPrototypeOf(SubFile.prototype, m.FileEntry.prototype); " +
          "Object.setPrototypeOf(SubFile, m.FileEntry); " +
          "globalThis.s = new SubFile('s'); globalThis.d = new m.DirEntry('d', f); 'ready'",
          "base-shared-object-subclass-setup.js"
      );
      // The DirEntry constructor received `f` as its PathBase parent.
      var file = Assert.IsType<FileEntry>(BaseThingsModule.LastPath);
      Assert.Equal("f", file.Name);

      using var describeDir = fixture.Evaluate("m.describe(d)", "base-shared-object-describe-dir.js");
      var dir = Assert.IsType<DirEntry>(BaseThingsModule.LastPath);
      Assert.Same(file, dir.Parent);

      using var describeSub = fixture.Evaluate("m.describe(s)", "base-shared-object-describe-sub.js");
      var sub = Assert.IsType<FileEntry>(BaseThingsModule.LastPath);
      Assert.Equal("s", sub.Name);

      // A JavaScript subclass instance as a constructor argument.
      BaseThingsModule.LastPath = null;
      using var madeFromSub = fixture.Evaluate("new m.DirEntry('d2', s); 'made'", "base-shared-object-ctor-sub.js");
      Assert.Same(sub, BaseThingsModule.LastPath);

      // Repeated calls receive the same instance.
      BaseThingsModule.LastPath = null;
      using var describeFileOnce = fixture.Evaluate("m.describe(f)", "base-shared-object-describe-file-1.js");
      Assert.Same(file, BaseThingsModule.LastPath);
      BaseThingsModule.LastPath = null;
      using var describeFileTwice = fixture.Evaluate("m.describe(f)", "base-shared-object-describe-file-2.js");
      Assert.Same(file, BaseThingsModule.LastPath);

      // Every instance is created and paired above, so only decodes run while the hook is set.
      context.SharedObjects.PairingStepForTesting = steps.Add;
      var before = context.SharedObjects.Count;

      using var result = fixture.Evaluate(
          "[m.describe(f), m.describe(d), m.describe(s), d.compare(f), d.compare(d), d.compare(s), " +
          "Object.getPrototypeOf(s) === SubFile.prototype, s instanceof m.FileEntry].join('|')",
          "base-shared-object-every-subclass.js"
      );
      Assert.Equal("FileEntry:f|DirEntry:d|FileEntry:s|parent|DirEntry:d|FileEntry:s|true|true", result.AsString());
      // The last call was d.compare(s).
      Assert.Same(sub, BaseThingsModule.LastPath);
      Assert.Empty(steps);
      Assert.Equal(before, context.SharedObjects.Count);

      // Control: a native entry creation records the pairing steps, so the empty list above is meaningful.
      using var control = fixture.Evaluate("new m.FileEntry('x'); 'made'", "base-shared-object-control.js");
      Assert.Equal(1, steps.Count(step => step == SharedObjectPairingStep.InstanceObjectCreated));
      return true;
    });
  }

  [Fact]
  public async Task SharedObjectBaseParameterKeepsIdentityAcrossAwait()
  {
    using var fixture = HermesRuntimeFixture.Create();
    DotnetRuntimeContext? context = null;
    FileEntry? file = null;
    DirEntry? dir = null;

    fixture.Runtime.Execute(runtime =>
    {
      context = CreateBaseThingsContext(fixture, runtime);
      using var setup = fixture.Evaluate(
          "globalThis.f = new m.FileEntry('f'); globalThis.d = new m.DirEntry('d', f); 'ready'",
          "base-shared-object-async-setup.js"
      );
      using var describeDir = fixture.Evaluate("m.describe(d)", "base-shared-object-async-describe-dir.js");
      dir = (DirEntry)BaseThingsModule.LastPath!;
      file = (FileEntry)dir.Parent;

      BaseThingsModule.LastPath = null;
      using var pending = fixture.Evaluate(
          "m.describeLater(f).then(v => { globalThis.__f = v; }, " +
          "error => { globalThis.__f = 'error:' + error.message; }); 'pending'",
          "base-shared-object-async-file.js"
      );
      return true;
    });

    await WaitForConditionAsync(fixture, "typeof globalThis.__f !== 'undefined'");

    fixture.Runtime.Execute(runtime =>
    {
      using var later = fixture.Evaluate("globalThis.__f", "base-shared-object-async-file-check.js");
      Assert.Equal("FileEntry:f", later.AsString());
      Assert.Same(file, BaseThingsModule.LastPath);

      BaseThingsModule.LastPath = null;
      using var pending = fixture.Evaluate(
          "m.describeLater(d).then(v => { globalThis.__d = v; }, " +
          "error => { globalThis.__d = 'error:' + error.message; }); 'pending'",
          "base-shared-object-async-dir.js"
      );
      return true;
    });

    await WaitForConditionAsync(fixture, "typeof globalThis.__d !== 'undefined'");

    fixture.Runtime.Execute(runtime =>
    {
      using var later = fixture.Evaluate("globalThis.__d", "base-shared-object-async-dir-check.js");
      Assert.Equal("DirEntry:d", later.AsString());
      Assert.Same(dir, BaseThingsModule.LastPath);

      // An unrelated class is rejected before DescribeLater runs, so LastPath keeps the sentinel.
      using var sentinel = fixture.Evaluate("m.describe(f)", "base-shared-object-async-sentinel.js");
      Assert.Same(file, BaseThingsModule.LastPath);
      using var pending = fixture.Evaluate(
          "m.describeLater(globalThis._expoDotnet.modules.SharedThings.makeSnapshot()).then(" +
          "v => { globalThis.__bad = 'resolved:' + v; }, error => { globalThis.__bad = error.message; }); " +
          "'pending'",
          "base-shared-object-async-unrelated.js"
      );
      return true;
    });

    await WaitForConditionAsync(fixture, "typeof globalThis.__bad !== 'undefined'");

    fixture.Runtime.Execute(runtime =>
    {
      using var bad = fixture.Evaluate("globalThis.__bad", "base-shared-object-async-unrelated-check.js");
      Assert.Contains(
          "The shared object receiver is not a 'Expo.ModulesCore.Tests.Modules.PathBase' instance.",
          bad.AsString()
      );
      Assert.Same(file, BaseThingsModule.LastPath);
      context!.Dispose();
      return true;
    });
  }

  [Theory]
  [InlineData("({})", "NativeState entry for SharedObjectNativeState is missing.", "NativeState entry for SharedObjectNativeState is missing.")]
  [InlineData("null", "Value is not an object.", "Value is not an object.")]
  [InlineData("undefined", "Value is not an object.", "Value is not an object.")]
  [InlineData("0", "Value is not an object.", "Value is not an object.")]
  [InlineData("'x'", "Value is not an object.", "Value is not an object.")]
  [InlineData("globalThis.released", "not an active shared object", "not an active shared object")]
  [InlineData("globalThis.foreign", "not an active shared object", "not an active shared object")]
  [InlineData(
      "globalThis.snapshot",
      "is not a 'Expo.ModulesCore.Tests.Modules.PathBase' instance.",
      "is not a 'Expo.ModulesCore.Tests.Modules.FileEntry' instance.")]
  public void SharedObjectBaseParameterFailuresMatchTheExactParameter(
      string input,
      string expected,
      string exactExpected)
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = CreateBaseThingsContext(fixture, runtime);
      using var setup = fixture.Evaluate(
          "globalThis.kept = new m.FileEntry('kept'); globalThis.d = new m.DirEntry('d', kept); " +
          "globalThis.released = new m.FileEntry('released'); released.release(); " +
          "globalThis.snapshot = globalThis._expoDotnet.modules.SharedThings.makeSnapshot(); 'ready'",
          "base-shared-object-failures-setup.js"
      );
      var sentinel = BaseThingsModule.LastPath;

      // An entry installed by another context in the same runtime is not active in this one.
      var contextB = new DotnetRuntimeContext(runtime);
      try
      {
        using var holder = runtime.CreateObject();
        GeneratedSharedObjectClass.Install(contextB, holder, typeof(FileEntry), "FileEntry", 0, null, null);
        using var foreign = SharedObjectCodec<FileEntry>.Encode(new FileEntry("foreign"), runtime, contextB);
        using var global = runtime.Global();
        global.SetProperty("foreign", foreign);

        var before = context.SharedObjects.Count;
        using var result = fixture.Evaluate(
            JsErrHelper +
            $"const v = () => ({input}); " +
            "[err(() => m.describe(v())), err(() => m.describeFile(v())), " +
            "err(() => d.compare(v())), err(() => new m.DirEntry('x', v()))].join('|')",
            "base-shared-object-failures.js"
        );

        var parts = result.AsString().Split('|');
        Assert.Equal(4, parts.Length);
        Assert.Contains(expected, parts[0]);
        Assert.Contains(exactExpected, parts[1]);
        Assert.Contains(expected, parts[2]);
        Assert.Contains(expected, parts[3]);
        if (expected == exactExpected)
        {
          // The base parameter fails exactly as the concrete one does. Compare the first line
          // only, because the two functions have different frames in the stack trace.
          Assert.Equal(parts[0].Split('\n')[0], parts[1].Split('\n')[0]);
        }
        // No authored code ran, and the failed constructor left no DirEntry behind.
        Assert.Same(sentinel, BaseThingsModule.LastPath);
        Assert.Equal(before, context.SharedObjects.Count);
      }
      finally
      {
        contextB.Dispose();
      }
      return true;
    });
  }

  [Fact]
  public void SharedObjectBaseParameterStillRequiresItsArgument()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = CreateBaseThingsContext(fixture, runtime);
      using var setup = fixture.Evaluate(
          "globalThis.f = new m.FileEntry('f'); globalThis.d = new m.DirEntry('d', f); 'ready'",
          "base-shared-object-required-setup.js"
      );
      var sentinel = BaseThingsModule.LastPath;

      using var result = fixture.Evaluate(
          JsErrHelper +
          "[err(() => m.describe()), err(() => d.compare()), err(() => new m.DirEntry('x'))].join('|')",
          "base-shared-object-required.js"
      );

      var parts = result.AsString().Split('|');
      Assert.Equal(3, parts.Length);
      Assert.Contains("got 0", parts[0]);
      Assert.Contains("got 0", parts[1]);
      Assert.Contains("new DirEntry expects 2 arguments, got 1.", parts[2]);
      Assert.Same(sentinel, BaseThingsModule.LastPath);
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
          "globalThis.__retainedSubscription = c.addListener('change', () => {}); 'retained'",
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
