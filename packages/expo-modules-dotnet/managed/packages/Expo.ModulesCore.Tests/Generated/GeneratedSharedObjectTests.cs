using System;
using System.Threading.Tasks;
using Expo.JSI;
using Expo.ModulesCore;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Generated;

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
