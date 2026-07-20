using Expo.JSI;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Generated;

public sealed class ExampleModuleShowcaseTests
{
  [Fact]
  public async Task ExampleModuleShowcasesAsyncRecordsEventsAndCallbacks()
  {
    using var fixture = HermesRuntimeFixture.Create();
    DotnetRuntimeContext? context = null;

    fixture.Runtime.Execute(runtime =>
    {
      context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_ExampleModule.Register(context, modules);
      return true;
    });

    try
    {
      fixture.Evaluate(
          """
          globalThis.__exampleShowcase = {
            add: null,
            asyncMessage: null,
            recordSummary: null,
            callbackResult: null,
            eventPayload: null,
            eventDone: false
          };

          const module = globalThis._expoDotnet.modules.ExampleModule;
          const subscription = module.addListener(
            'onStatus',
            value => { globalThis.__exampleShowcase.eventPayload = value; }
          );

          globalThis.__exampleShowcase.add = module.add(20, 22);
          const record = module.describeUser({ name: 'Ada', age: 37 });
          globalThis.__exampleShowcase.recordSummary =
            `${record.name}:${record.age}:${record.summary}`;
          globalThis.__exampleShowcase.callbackResult =
            module.transformWithCallback('JS', value => `callback(${value})`);

          module.getMessageAsync().then(
            value => { globalThis.__exampleShowcase.asyncMessage = value; },
            error => { globalThis.__exampleShowcase.asyncMessage = error && error.message; }
          );
          module.emitStatusAsync('ready').then(
            () => {
              globalThis.__exampleShowcase.eventDone = true;
              subscription.remove();
            },
            error => { globalThis.__exampleShowcase.eventPayload = error && error.message; }
          );

          true;
          """,
          "example-module-showcase.js"
      ).Dispose();

      await WaitForShowcaseAsync(fixture);

      var outcome = fixture.Runtime.Execute(_ =>
      {
        using var value = fixture.Evaluate(
            "globalThis.__exampleShowcase",
            "example-module-showcase-outcome.js"
        );
        using var obj = value.AsObject();
        return ReadOutcome(obj);
      });

      Assert.Equal(42, outcome.Add);
      Assert.Equal("Hello from async C#", outcome.AsyncMessage);
      Assert.Equal("Ada:37:Ada is 37", outcome.RecordSummary);
      Assert.Equal("callback(C# sent JS)", outcome.CallbackResult);
      Assert.Equal("C# event: ready", outcome.EventPayload);
      Assert.True(outcome.EventDone);
    }
    finally
    {
      fixture.Runtime.Execute(_ =>
      {
        context?.Dispose();
        return true;
      });
    }
  }

  private static async Task WaitForShowcaseAsync(HermesRuntimeFixture fixture)
  {
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
    while (DateTime.UtcNow < deadline)
    {
      fixture.DrainTasks();
      var ready = fixture.Runtime.Execute(_ =>
      {
        using var value = fixture.Evaluate(
            """
            globalThis.__exampleShowcase.asyncMessage !== null &&
              globalThis.__exampleShowcase.eventDone === true
            """,
            "example-module-showcase-ready.js"
        );
        return value.AsBool();
      });
      if (ready)
      {
        return;
      }

      await Task.Delay(10, TestContext.Current.CancellationToken);
    }

    Assert.Fail("Timed out waiting for ExampleModule showcase promises.");
  }

  private static ShowcaseOutcome ReadOutcome(JavaScriptObject obj)
  {
    using var add = obj.GetProperty("add");
    using var asyncMessage = obj.GetProperty("asyncMessage");
    using var recordSummary = obj.GetProperty("recordSummary");
    using var callbackResult = obj.GetProperty("callbackResult");
    using var eventPayload = obj.GetProperty("eventPayload");
    using var eventDone = obj.GetProperty("eventDone");
    return new ShowcaseOutcome(
        checked((int)add.AsDouble()),
        asyncMessage.AsString(),
        recordSummary.AsString(),
        callbackResult.AsString(),
        eventPayload.AsString(),
        eventDone.AsBool()
    );
  }

  private sealed record ShowcaseOutcome(
      int Add,
      string AsyncMessage,
      string RecordSummary,
      string CallbackResult,
      string EventPayload,
      bool EventDone);
}
