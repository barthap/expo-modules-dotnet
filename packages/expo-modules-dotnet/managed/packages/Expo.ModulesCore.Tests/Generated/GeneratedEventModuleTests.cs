using System.Threading.Tasks;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Generated;

public sealed class GeneratedEventModuleTests
{
  [Fact]
  public async Task EmittedPayloadReachesJavaScriptListener()
  {
    var outcome = await EvaluateEventOutcomeAsync(
        "events.addListener('onChange', value => { seen = value; });",
        "events.EmitChangeAsync('payload')",
        "seen"
    );

    Assert.Equal("payload", outcome);
  }

  [Fact]
  public async Task EmittedPayloadReachesJavaScriptListenerWhenSyncExecutionUnsupported()
  {
    var outcome = await EvaluateEventOutcomeAsync(
        "events.addListener('onChange', value => { seen = value; });",
        "events.EmitChangeAsync('payload')",
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
        "events.EmitReadyAsync()",
        "seen"
    );

    Assert.Equal("ready", outcome);
  }

  [Fact]
  public async Task UndeclaredEventRejectsPromise()
  {
    var outcome = await EvaluateEventOutcomeAsync(
        "",
        "events.EmitUndeclaredAsync().then(() => 'fulfilled', error => error.message)",
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
        seen = events.ReadStarted();
        sub.remove();
        seen = seen + ':' + events.ReadStopped();
        """,
        "Promise.resolve()",
        "seen"
    );

    Assert.Equal("onChange:onChange", outcome);
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
}
