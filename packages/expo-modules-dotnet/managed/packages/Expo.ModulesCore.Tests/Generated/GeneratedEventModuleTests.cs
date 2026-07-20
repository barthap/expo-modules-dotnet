using System.Threading.Tasks;
using Expo.JSI;
using Expo.ModulesCore.Codecs;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Generated;

public sealed class GeneratedEventModuleTests
{
  [Fact]
  public void TypedEventDelegatesAreInitializedBeforeOnCreate()
  {
    using var typed = TypedEventsFixture.Create();

    Assert.Equal(
        "Event member 'GeneratedTypedEventsModule.OnReady' is unavailable before module registration.",
        typed.Module.ConstructorEventError
    );
    Assert.Same(typed.Module.OnReady, typed.Module.ReadySeenOnCreate);
  }

  [Fact]
  public async Task EmittedPayloadReachesJavaScriptListener()
  {
    var outcome = await EvaluateEventOutcomeAsync(
        "events.addListener('onChange', value => { seen = value; });",
        "events.emitChangeAsync('payload')",
        "seen"
    );

    Assert.Equal("payload", outcome);
  }

  [Fact]
  public async Task EmittedPayloadReachesJavaScriptListenerWhenSyncExecutionUnsupported()
  {
    var outcome = await EvaluateEventOutcomeAsync(
        "events.addListener('onChange', value => { seen = value; });",
        "events.emitChangeAsync('payload')",
        "seen",
        disableSyncExecutionBeforeEvaluate: true
    );

    Assert.Equal("payload", outcome);
  }

  [Fact]
  public async Task PayloadlessEventCallsJavaScriptListener()
  {
    var outcome = await EvaluateEventOutcomeAsync(
        "events.addListener('onReady', () => { seen = 'ready'; });",
        "events.emitReadyAsync()",
        "seen"
    );

    Assert.Equal("ready", outcome);
  }

  [Fact]
  public async Task UndeclaredEventRejectsPromise()
  {
    var outcome = await EvaluateEventOutcomeAsync(
        "",
        "events.emitUndeclaredAsync().then(() => 'fulfilled', error => error.message)",
        "seen"
    );

    Assert.Contains("missing", outcome);
  }

  [Fact]
  public async Task ObservingHooksReceiveListenerTransitions()
  {
    var outcome = await EvaluateEventOutcomeAsync(
        """
        const sub = events.addListener('onChange', () => {});
        seen = events.readStarted();
        sub.remove();
        seen = seen + ':' + events.readStopped();
        """,
        "Promise.resolve()",
        "seen"
    );

    Assert.Equal("onChange:onChange", outcome);
  }

  [Fact]
  public async Task TypedPayloadlessStringAndRecordEventsReachJavaScript()
  {
    using var typed = TypedEventsFixture.Create();
    typed.Evaluate(
        """
        const events = globalThis._expoDotnet.modules.GeneratedTypedEvents;
        events.addListener('onReady', () => { globalThis.__typedReady = 'ready'; });
        events.addListener('onChange', value => { globalThis.__typedChange = value; });
        events.addListener('onProgress', value => { globalThis.__typedProgress = value.percent; });
        true
        """,
        "typed-events-listeners.js"
    );

    await typed.Module.OnReady();
    await typed.Module.OnChange("changed");
    await typed.Module.OnProgress(new TypedProgress(73));

    Assert.Equal(
        "ready:changed:73",
        typed.ReadString("`${globalThis.__typedReady}:${globalThis.__typedChange}:${globalThis.__typedProgress}`")
    );
  }

  [Fact]
  public void TypedEventsTriggerFirstAndLastListenerHooks()
  {
    using var typed = TypedEventsFixture.Create();
    typed.Evaluate(
        """
        const events = globalThis._expoDotnet.modules.GeneratedTypedEvents;
        globalThis.__typedSubscription = events.addListener('onChange', () => {});
        true
        """,
        "typed-events-observing.js"
    );
    Assert.Equal("onChange", typed.Module.Started);

    typed.Evaluate("globalThis.__typedSubscription.remove(); true", "typed-events-stop-observing.js");
    Assert.Equal("onChange", typed.Module.Stopped);
  }

  [Fact]
  public async Task TypedAndLegacyEventsCoexistOnOneModule()
  {
    using var typed = TypedEventsFixture.Create();
    typed.Evaluate(
        """
        const events = globalThis._expoDotnet.modules.GeneratedTypedEvents;
        events.addListener('onLegacy', value => { globalThis.__typedLegacy = value; });
        events.addListener('onChange', value => { globalThis.__typedTyped = value; });
        true
        """,
        "typed-events-legacy.js"
    );

    await typed.Module.EmitLegacyAsync("legacy");
    await typed.Module.OnChange("typed");

    Assert.Equal(
        "legacy:typed",
        typed.ReadString("`${globalThis.__typedLegacy}:${globalThis.__typedTyped}`")
    );
  }

  [Fact]
  public async Task ThrowingListenerDoesNotPreventLaterTypedListener()
  {
    using var typed = TypedEventsFixture.Create();
    typed.Evaluate(
        """
        const events = globalThis._expoDotnet.modules.GeneratedTypedEvents;
        events.addListener('onReady', () => { throw new Error('listener failed'); });
        events.addListener('onReady', () => { globalThis.__typedListenerIsolation = 'reached'; });
        true
        """,
        "typed-events-listener-isolation.js"
    );

    await typed.Module.OnReady();

    Assert.Equal("reached", typed.ReadString("globalThis.__typedListenerIsolation"));
  }

  [Fact]
  public void RegisteringProviderTwicePreservesEveryTypedDelegateIdentity()
  {
    using var typed = TypedEventsFixture.Create();
    var module = typed.Module;
    var ready = module.OnReady;
    var change = module.OnChange;
    var progress = module.OnProgress;
    var value = module.OnValue;
    var buffer = module.OnBuffer;

    typed.RegisterProviderAgain();

    Assert.Same(ready, module.OnReady);
    Assert.Same(change, module.OnChange);
    Assert.Same(progress, module.OnProgress);
    Assert.Same(value, module.OnValue);
    Assert.Same(buffer, module.OnBuffer);
  }

  [Fact]
  public void GeneratedInitializerRejectsDifferentRuntimeContext()
  {
    using var typed = TypedEventsFixture.Create();
    var secondContext = typed.Fixture.Runtime.Execute(runtime => new DotnetRuntimeContext(runtime));
    try
    {
      var exception = Assert.Throws<InvalidOperationException>(() =>
          typed.Module.__ExpoModulesCoreInitializeEvents(
              secondContext,
              static () => Task.CompletedTask,
              static _ => Task.CompletedTask,
              static _ => Task.CompletedTask,
              static _ => Task.CompletedTask,
              static _ => Task.CompletedTask
          )
      );

      Assert.Contains("cannot be rebound", exception.Message);
    }
    finally
    {
      typed.Fixture.Runtime.Execute(_ =>
      {
        secondContext.Dispose();
        return true;
      });
    }
  }

  [Fact]
  public async Task CachedTypedDelegateFaultsAfterDisposalWithoutRuntimeScheduling()
  {
    using var typed = TypedEventsFixture.Create();
    var ready = typed.Module.OnReady;
    typed.DisposeContext();
    typed.Fixture.ResetCounters();
    var countersBefore = typed.Fixture.Counters;

    var emitted = ready();

    await Assert.ThrowsAsync<ObjectDisposedException>(async () => await emitted);
    Assert.Equal(countersBefore.SyncExecuteCalls, typed.Fixture.Counters.SyncExecuteCalls);
    Assert.Equal(countersBefore.ReleasedTaskContexts, typed.Fixture.Counters.ReleasedTaskContexts);
  }

  [Fact]
  public async Task TypedJavaScriptValueKeepsCallerAliveUntilScheduledDispatchCompletes()
  {
    using var typed = TypedEventsFixture.Create();
    typed.Evaluate(
        """
        const events = globalThis._expoDotnet.modules.GeneratedTypedEvents;
        events.addListener('onValue', value => { globalThis.__typedValue = value; });
        true
        """,
        "typed-events-value-listener.js"
    );
    var payload = typed.Fixture.Runtime.Execute(runtime =>
    {
      var value = runtime.CreateString("typed value");
      using var global = runtime.Global();
      global.SetProperty("__typedCallerValue", value);
      return value;
    });

    try
    {
      typed.Fixture.ResetCounters();
      typed.Fixture.DisableSyncExecutionForTesting();
      typed.Fixture.PauseRuntimeExecutor();
      var emitted = typed.Module.OnValue(payload);
      typed.Fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate);
      typed.Fixture.ResumeRuntimeExecutor();
      await emitted;
      typed.Fixture.WaitUntilIdle();
      typed.Fixture.SetSyncExecutionSupportedForTesting(true);

      Assert.Equal("typed value", typed.Fixture.Runtime.Execute(_ => payload.AsString()));
      Assert.Equal(
          "true:typed value",
          typed.ReadString(
              "`${globalThis.__typedValue === globalThis.__typedCallerValue}:${String(globalThis.__typedValue)}`"
          )
      );
      var releasesAfterDispatch = typed.Fixture.Counters.ReleasedValues;
      payload.Dispose();
      Assert.Equal(releasesAfterDispatch + 1, typed.Fixture.Counters.ReleasedValues);
    }
    finally
    {
      typed.Fixture.SetSyncExecutionSupportedForTesting(true);
      payload.Dispose();
    }
  }

  [Fact]
  public async Task TypedJavaScriptValueFromAnotherRuntimeFaultsBeforeCrossRuntimeAccess()
  {
    using var sourceFixture = HermesRuntimeFixture.Create();
    using var typed = TypedEventsFixture.Create();
    using var payload = sourceFixture.Runtime.Execute(runtime => runtime.CreateString("wrong runtime"));
    sourceFixture.ResetCounters();

    var emitted = typed.Module.OnValue(payload);

    await Assert.ThrowsAsync<InvalidOperationException>(async () => await emitted);
    Assert.Equal(0u, sourceFixture.Counters.ReleasedValues);
  }

  [Fact]
  public async Task DroppedTypedJavaScriptValueDispatchLeavesCallerOriginalAliveWithoutInvocationRetain()
  {
    using var typed = TypedEventsFixture.Create();
    var payload = typed.Fixture.Runtime.Execute(runtime => runtime.CreateString("dropped value"));
    try
    {
      typed.Fixture.ResetCounters();
      typed.Fixture.DisableSyncExecutionForTesting();
      typed.Fixture.PauseRuntimeExecutor();
      typed.Fixture.DropNextRuntimeTask(JavaScriptTaskPriority.Immediate);
      var emitted = typed.Module.OnValue(payload);
      typed.Fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate);
      typed.Fixture.ResumeRuntimeExecutor();
      typed.Fixture.WaitUntilIdle();
      typed.Fixture.SetSyncExecutionSupportedForTesting(true);

      await Assert.ThrowsAnyAsync<Exception>(async () => await emitted);
      Assert.Equal("dropped value", typed.Fixture.Runtime.Execute(_ => payload.AsString()));
      Assert.Equal(0u, typed.Fixture.Counters.ReleasedValues);

      payload.Dispose();
      Assert.Equal(1u, typed.Fixture.Counters.ReleasedValues);
    }
    finally
    {
      typed.Fixture.SetSyncExecutionSupportedForTesting(true);
      payload.Dispose();
    }
  }

  [Fact]
  public async Task ContextTeardownTypedJavaScriptValueDispatchReleasesInvocationCopyAfterTargetFailure()
  {
    uint releasesWithoutInvocationCopy;
    using (var baseline = TypedEventsFixture.Create())
    {
      baseline.Fixture.ResetCounters();
      baseline.Fixture.DisableSyncExecutionForTesting();
      baseline.Fixture.PauseRuntimeExecutor();
      var emitted = baseline.Module.OnReady();
      baseline.Fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate);
      baseline.Fixture.SetSyncExecutionSupportedForTesting(true);
      baseline.DisposeContextWithoutRuntimeAccess();
      baseline.Fixture.ResumeRuntimeExecutor();
      baseline.Fixture.WaitUntilIdle();

      await Assert.ThrowsAsync<ObjectDisposedException>(async () => await emitted);
      releasesWithoutInvocationCopy = baseline.Fixture.Counters.ReleasedValues;
    }

    using var typed = TypedEventsFixture.Create();
    var payload = typed.Fixture.Runtime.Execute(runtime => runtime.CreateString("teardown value"));
    try
    {
      typed.Fixture.ResetCounters();
      typed.Fixture.DisableSyncExecutionForTesting();
      typed.Fixture.PauseRuntimeExecutor();
      var emitted = typed.Module.OnValue(payload);
      typed.Fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate);
      typed.Fixture.SetSyncExecutionSupportedForTesting(true);
      typed.DisposeContextWithoutRuntimeAccess();
      typed.Fixture.ResumeRuntimeExecutor();
      typed.Fixture.WaitUntilIdle();

      await Assert.ThrowsAsync<ObjectDisposedException>(async () => await emitted);
      Assert.Equal(releasesWithoutInvocationCopy + 1, typed.Fixture.Counters.ReleasedValues);
      Assert.Equal("teardown value", typed.Fixture.Runtime.Execute(_ => payload.AsString()));

      payload.Dispose();
      Assert.Equal(releasesWithoutInvocationCopy + 2, typed.Fixture.Counters.ReleasedValues);
    }
    finally
    {
      typed.Fixture.SetSyncExecutionSupportedForTesting(true);
      payload.Dispose();
    }
  }

  [Fact]
  public async Task TypedJavaScriptValueTargetFailureReleasesInvocationCopyExactlyOnce()
  {
    uint releasesWithoutInvocationCopy;
    using (var baseline = TypedEventsFixture.Create())
    {
      baseline.Evaluate(
          """
          const events = globalThis._expoDotnet.modules.GeneratedTypedEvents;
          events.emit = 1;
          true
          """,
          "typed-events-no-payload-invalid-target.js"
      );
      baseline.Fixture.ResetCounters();
      baseline.Fixture.DisableSyncExecutionForTesting();
      baseline.Fixture.PauseRuntimeExecutor();
      var emitted = baseline.Module.OnReady();
      baseline.Fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate);
      baseline.Fixture.ResumeRuntimeExecutor();
      baseline.Fixture.WaitUntilIdle();
      baseline.Fixture.SetSyncExecutionSupportedForTesting(true);

      await Assert.ThrowsAnyAsync<Exception>(async () => await emitted);
      releasesWithoutInvocationCopy = baseline.Fixture.Counters.ReleasedValues;
    }

    using var typed = TypedEventsFixture.Create();
    typed.Evaluate(
        """
        const events = globalThis._expoDotnet.modules.GeneratedTypedEvents;
        events.emit = 1;
        true
        """,
        "typed-events-value-invalid-target.js"
    );
    var payload = typed.Fixture.Runtime.Execute(runtime => runtime.CreateString("target failure value"));
    try
    {
      typed.Fixture.ResetCounters();
      typed.Fixture.DisableSyncExecutionForTesting();
      typed.Fixture.PauseRuntimeExecutor();
      var emitted = typed.Module.OnValue(payload);
      typed.Fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate);
      typed.Fixture.ResumeRuntimeExecutor();
      typed.Fixture.WaitUntilIdle();
      typed.Fixture.SetSyncExecutionSupportedForTesting(true);

      await Assert.ThrowsAnyAsync<Exception>(async () => await emitted);
      Assert.Equal("target failure value", typed.Fixture.Runtime.Execute(_ => payload.AsString()));
      Assert.Equal(releasesWithoutInvocationCopy + 1, typed.Fixture.Counters.ReleasedValues);

      payload.Dispose();
      Assert.Equal(releasesWithoutInvocationCopy + 2, typed.Fixture.Counters.ReleasedValues);
    }
    finally
    {
      typed.Fixture.SetSyncExecutionSupportedForTesting(true);
      payload.Dispose();
    }
  }

  [Fact]
  public async Task TypedArrayBufferReleasesItsScheduledInvocationLeaseExactlyOnce()
  {
    using var typed = TypedEventsFixture.Create();
    typed.Evaluate(
        """
        const events = globalThis._expoDotnet.modules.GeneratedTypedEvents;
        events.addListener('onBuffer', value => {
          globalThis.__typedBuffer = Array.from(new Uint8Array(value)).join(',');
        });
        true
        """,
        "typed-events-buffer-listener.js"
    );
    var payload = CreateJavaScriptBackedArrayBuffer(typed.Fixture, "7,8,9");

    typed.Fixture.ResetCounters();
    typed.Fixture.DisableSyncExecutionForTesting();
    typed.Fixture.PauseRuntimeExecutor();
    var emitted = typed.Module.OnBuffer(payload);
    payload.Dispose();
    typed.Fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate);
    typed.Fixture.ResumeRuntimeExecutor();
    await emitted;
    typed.Fixture.WaitUntilIdle();
    typed.Fixture.SetSyncExecutionSupportedForTesting(true);

    Assert.Equal("7,8,9", typed.ReadString("globalThis.__typedBuffer"));
    Assert.Equal(1u, typed.Fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal(0u, typed.Fixture.Counters.LongLivedArrayBuffersAbandoned);
  }

  [Fact]
  public async Task DroppedTypedArrayBufferDispatchReleasesItsInvocationLeaseExactlyOnce()
  {
    using var typed = TypedEventsFixture.Create();
    var payload = CreateJavaScriptBackedArrayBuffer(typed.Fixture, "1,2");

    typed.Fixture.ResetCounters();
    typed.Fixture.DisableSyncExecutionForTesting();
    typed.Fixture.PauseRuntimeExecutor();
    typed.Fixture.DropNextRuntimeTask(JavaScriptTaskPriority.Immediate);
    var emitted = typed.Module.OnBuffer(payload);
    payload.Dispose();
    typed.Fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate);
    typed.Fixture.ResumeRuntimeExecutor();
    typed.Fixture.WaitUntilIdle();
    typed.Fixture.SetSyncExecutionSupportedForTesting(true);

    await Assert.ThrowsAnyAsync<Exception>(async () => await emitted);
    typed.Fixture.Runtime.Execute(runtime =>
    {
      using var value = runtime.CreateUndefined();
      return true;
    });
    Assert.Equal(1u, typed.Fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal(0u, typed.Fixture.Counters.LongLivedArrayBuffersAbandoned);
  }

  [Fact]
  public async Task ContextTeardownTerminatesTypedArrayBufferDispatchAndReleasesItsLease()
  {
    using var typed = TypedEventsFixture.Create();
    var payload = CreateJavaScriptBackedArrayBuffer(typed.Fixture, "4,5");

    typed.Fixture.ResetCounters();
    typed.Fixture.DisableSyncExecutionForTesting();
    typed.Fixture.PauseRuntimeExecutor();
    var emitted = typed.Module.OnBuffer(payload);
    payload.Dispose();
    typed.Fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate);
    typed.Fixture.SetSyncExecutionSupportedForTesting(true);
    typed.DisposeContextWithoutRuntimeAccess();
    typed.Fixture.ResumeRuntimeExecutor();
    typed.Fixture.WaitUntilIdle();

    await Assert.ThrowsAnyAsync<Exception>(async () => await emitted);
    Assert.Equal(1u, typed.Fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal(0u, typed.Fixture.Counters.LongLivedArrayBuffersAbandoned);
  }

  private static async Task<string> EvaluateEventOutcomeAsync(
      string listenerSetup,
      string expression,
      string resultExpression,
      bool disableSyncExecutionBeforeEvaluate = false)
  {
    using var fixture = HermesRuntimeFixture.Create();
    DotnetRuntimeContext? context = null;

    fixture.Runtime.Execute(runtime =>
    {
      context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      return true;
    });

    try
    {
      if (disableSyncExecutionBeforeEvaluate)
      {
        fixture.DisableSyncExecutionForTesting();
      }

      var setupResult = fixture.Evaluate(
          $$"""
          const events = globalThis._expoDotnet.modules.GeneratedEvents;
          let seen = '';
          {{listenerSetup}}
          Promise.resolve({{expression}}).then(
            value => { globalThis.__eventOutcome = {{resultExpression}} || value || ''; },
            error => { globalThis.__eventOutcome = error && error.message; }
          );
          true
          """,
          "generated-events-setup.js"
      );

      await Task.Yield();
      fixture.WaitUntilIdle();
      fixture.SetSyncExecutionSupportedForTesting(true);

      return fixture.Runtime.Execute(_ =>
      {
        setupResult.Dispose();
        using var result = fixture.Evaluate("globalThis.__eventOutcome", "generated-events-result.js");
        return result.AsString();
      });
    }
    finally
    {
      fixture.SetSyncExecutionSupportedForTesting(true);
      fixture.Runtime.Execute(_ =>
      {
        context?.Dispose();
        return true;
      });
    }
  }

  private static ArrayBuffer CreateJavaScriptBackedArrayBuffer(
      HermesRuntimeFixture fixture,
      string bytes)
  {
    return fixture.Runtime.Execute(runtime =>
    {
      using var value = fixture.Evaluate(
          $"Uint8Array.from([{bytes}]).buffer",
          "typed-events-array-buffer-payload.js"
      );
      return ArrayBufferCodec.Decode(value.Ref, runtime);
    });
  }

  private sealed class TypedEventsFixture : IDisposable
  {
    private bool contextDisposed;

    private TypedEventsFixture(
        HermesRuntimeFixture fixture,
        DotnetRuntimeContext context,
        GeneratedTypedEventsModule module)
    {
      Fixture = fixture;
      Context = context;
      Module = module;
    }

    public HermesRuntimeFixture Fixture { get; }

    public DotnetRuntimeContext Context { get; }

    public GeneratedTypedEventsModule Module { get; }

    public static TypedEventsFixture Create()
    {
      var fixture = HermesRuntimeFixture.Create();
      DotnetRuntimeContext? context = null;
      GeneratedTypedEventsModule? module = null;
      try
      {
        fixture.Runtime.Execute(runtime =>
        {
          context = new DotnetRuntimeContext(runtime);
          using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
          ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
          module = context.ModuleRegistry.GetOrCreateModule<GeneratedTypedEventsModule>(
              "GeneratedTypedEvents",
              static () => throw new InvalidOperationException("The generated module must already exist.")
          );
          return true;
        });
        return new TypedEventsFixture(fixture, context!, module!);
      }
      catch
      {
        fixture.Dispose();
        throw;
      }
    }

    public void Evaluate(string source, string sourceUrl)
    {
      Fixture.Runtime.Execute(_ =>
      {
        using var result = Fixture.Evaluate(source, sourceUrl);
        return true;
      });
    }

    public string ReadString(string expression)
    {
      return Fixture.Runtime.Execute(_ =>
      {
        using var result = Fixture.Evaluate(expression, "typed-events-result.js");
        return result.AsString();
      });
    }

    public void RegisterProviderAgain()
    {
      Fixture.Runtime.Execute(_ =>
      {
        using var modules = Context.ModuleRegistry.GetOrCreateDotnetModulesObject();
        ExpoModulesProvider_Expo_ModulesCore_Tests.Register(Context, modules);
        return true;
      });
    }

    public void DisposeContext()
    {
      if (contextDisposed)
      {
        return;
      }

      Fixture.Runtime.Execute(_ =>
      {
        Context.Dispose();
        return true;
      });
      contextDisposed = true;
    }

    public void DisposeContextWithoutRuntimeAccess()
    {
      if (contextDisposed)
      {
        return;
      }

      Context.Dispose();
      contextDisposed = true;
    }

    public void Dispose()
    {
      Fixture.SetSyncExecutionSupportedForTesting(true);
      Fixture.ResumeRuntimeExecutor();
      DisposeContext();
      Fixture.Dispose();
    }
  }
}
