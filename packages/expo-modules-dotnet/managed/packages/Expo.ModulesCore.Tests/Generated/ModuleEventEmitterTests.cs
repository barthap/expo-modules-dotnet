using Expo.JSI;
using Expo.ModulesCore.Codecs;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Generated;

public sealed class ModuleEventEmitterTests
{
  [Fact]
  public async Task JavaScriptValuePayloadKeepsCallerOwnerAndReleasesInvocationCopy()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var emitter = CreateEmitter(fixture);
    using var payload = fixture.Runtime.Execute(runtime => runtime.CreateString("direct value"));

    fixture.ResetCounters();
    fixture.DisableSyncExecutionForTesting();
    fixture.PauseRuntimeExecutor();
    var emitted = emitter.Emitter.EmitAsync(
        emitter.Module,
        "onValue",
        payload,
        TestContext.Current.CancellationToken
    );

    fixture.ResumeRuntimeExecutor();
    await emitted;
    fixture.WaitUntilIdle();
    fixture.SetSyncExecutionSupportedForTesting(true);

    var observed = fixture.Runtime.Execute(_ =>
    {
      using var listenerValue = fixture.Evaluate(
          "globalThis.__moduleEventValue",
          "event-value-result.js"
      );
      return (payload.AsString(), listenerValue.AsString());
    });
    var releasesAfterDispatch = fixture.Counters.ReleasedValues;
    payload.Dispose();

    Assert.Equal("direct value", observed.Item1);
    Assert.Equal("direct value", observed.Item2);
    Assert.Equal(releasesAfterDispatch + 1, fixture.Counters.ReleasedValues);
  }

  [Fact]
  public async Task CrossRuntimeJavaScriptValueReturnsFaultedTask()
  {
    using var sourceFixture = HermesRuntimeFixture.Create();
    using var targetFixture = HermesRuntimeFixture.Create();
    using var emitter = CreateEmitter(targetFixture);
    using var payload = sourceFixture.Runtime.Execute(runtime => runtime.CreateString("cross-runtime"));
    sourceFixture.ResetCounters();

    var emitted = emitter.Emitter.EmitAsync(
        emitter.Module,
        "onValue",
        payload,
        TestContext.Current.CancellationToken
    );

    Assert.NotNull(emitted);
    await Assert.ThrowsAsync<InvalidOperationException>(async () => await emitted);
    Assert.Equal(0u, sourceFixture.Counters.ReleasedValues);
  }

  [Fact]
  public async Task ArrayBufferPayloadRetainsLeaseBeforeScheduling()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var emitter = CreateEmitter(fixture);
    var payload = fixture.Runtime.Execute(runtime =>
    {
      using var value = fixture.Evaluate(
          "Uint8Array.from([7, 8, 9]).buffer",
          "event-array-buffer-payload.js"
      );
      return ArrayBufferCodec.Decode(value.Ref, runtime);
    });

    fixture.ResetCounters();
    fixture.DisableSyncExecutionForTesting();
    fixture.PauseRuntimeExecutor();
    var emitted = emitter.Emitter.EmitAsync(
        emitter.Module,
        "onBuffer",
        payload,
        TestContext.Current.CancellationToken
    );
    payload.Dispose();

    fixture.ResumeRuntimeExecutor();
    await emitted;
    fixture.WaitUntilIdle();
    fixture.SetSyncExecutionSupportedForTesting(true);

    var observed = fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Evaluate("globalThis.__moduleEventBuffer", "event-array-buffer-result.js");
      return value.AsString();
    });

    Assert.Equal("7,8,9", observed);
    Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersReleased);
    Assert.Equal(0u, fixture.Counters.LongLivedArrayBuffersAbandoned);
  }

  [Theory]
  [InlineData("value")]
  [InlineData("buffer")]
  public async Task DisposedDirectPayloadReturnsFaultedTask(string kind)
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var emitter = CreateEmitter(fixture);

    Task emitted;
    if (kind == "value")
    {
      var payload = fixture.Runtime.Execute(runtime => runtime.CreateString("disposed"));
      payload.Dispose();
      emitted = emitter.Emitter.EmitAsync(
          emitter.Module,
          "onValue",
          payload,
          TestContext.Current.CancellationToken
      );
    }
    else
    {
      var payload = ArrayBuffer.CopyFrom(new byte[] { 1 });
      payload.Dispose();
      emitted = emitter.Emitter.EmitAsync(
          emitter.Module,
          "onBuffer",
          payload,
          TestContext.Current.CancellationToken
      );
    }

    Assert.NotNull(emitted);
    await Assert.ThrowsAsync<ObjectDisposedException>(async () => await emitted);
  }

  [Fact]
  public async Task DisposedEmitterFaultsBeforeRuntimeScheduling()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var emitter = CreateEmitter(fixture);
    var capturedEmitter = emitter.Emitter;

    fixture.Runtime.Execute(_ =>
    {
      emitter.Context.Dispose();
      return true;
    });
    fixture.ResetCounters();
    var countersBefore = fixture.Counters;

    var emitted = capturedEmitter.EmitAsync(
        emitter.Module,
        "onValue",
        TestContext.Current.CancellationToken
    );

    Assert.NotNull(emitted);
    await Assert.ThrowsAsync<ObjectDisposedException>(async () => await emitted);
    Assert.Equal(countersBefore.SyncExecuteCalls, fixture.Counters.SyncExecuteCalls);
    Assert.Equal(countersBefore.ReleasedTaskContexts, fixture.Counters.ReleasedTaskContexts);

    fixture.PauseRuntimeExecutor();
    fixture.DropNextRuntimeTask(JavaScriptTaskPriority.Normal);
    var sentinelRan = false;
    var sentinel = fixture.Runtime.ScheduleAsync(
        _ => sentinelRan = true,
        JavaScriptTaskPriority.Normal,
        TestContext.Current.CancellationToken
    );
    fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Normal);
    fixture.ResumeRuntimeExecutor();
    fixture.WaitUntilIdle();

    await Assert.ThrowsAnyAsync<Exception>(async () => await sentinel);
    Assert.False(sentinelRan);
  }

  private static EventEmitterFixture CreateEmitter(HermesRuntimeFixture fixture)
  {
    var module = new object();
    DotnetRuntimeContext? context = null;

    fixture.Runtime.Execute(runtime =>
    {
      context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      using var target = context.ModuleRegistry.DefineNativeModule(modules, "DirectPayloadEvents");
      context.Events.Attach(module, target, "DirectPayloadEvents", new[] { "onValue", "onBuffer" });
      fixture.Evaluate(
          "const events = globalThis._expoDotnet.modules.DirectPayloadEvents;" +
          "events.addListener('onValue', value => { globalThis.__moduleEventValue = String(value); });" +
          "events.addListener('onBuffer', value => { " +
          "globalThis.__moduleEventBuffer = Array.from(new Uint8Array(value)).join(','); }); true",
          "module-event-emitter-listeners.js"
      ).Dispose();
      return true;
    });

    return new EventEmitterFixture(fixture, context!, module);
  }

  private sealed class EventEmitterFixture(
      HermesRuntimeFixture fixture,
      DotnetRuntimeContext context,
      object module) : IDisposable
  {
    public DotnetRuntimeContext Context { get; } = context;

    public ModuleEventEmitter Emitter => Context.Events;

    public object Module { get; } = module;

    public void Dispose()
    {
      fixture.SetSyncExecutionSupportedForTesting(true);
      fixture.Runtime.Execute(_ =>
      {
        Context.Dispose();
        return true;
      });
    }
  }
}
