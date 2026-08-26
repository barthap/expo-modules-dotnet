using Expo.JSI;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Testing;
using Xunit;

namespace ExampleModule.Tests;

public sealed class ExampleModuleShowcaseTests
{
  [Fact]
  public async Task ExampleModuleShowcasesAsyncRecordsEventsAndCallbacks()
  {
    using var host = ExpoModuleTestHost.Create(
        ExpoModulesProvider_ExampleModule.Register
    );
    var value = await host.EvaluatePromiseAsync(
        """
        Promise.resolve().then(function () {
          var module = globalThis._expoDotnet.modules.ExampleModule;
          var eventPayload = null;
          var subscription = module.addListener(
            'onStatus',
            function (value) { eventPayload = value; }
          );
          return Promise.resolve().then(function () {
            var add = module.add(20, 22);
            var record = module.describeUser({ name: 'Ada', age: 37 });
            var callbackResult = module.transformWithCallback(
              'JS',
              function (value) { return `callback(${value})`; }
            );
            return module.getMessageAsync().then(function (message) {
              return module.emitStatusAsync('ready').then(function () {
                return {
                  add: add,
                  asyncMessage: message,
                  recordSummary: `${record.name}:${record.age}:${record.summary}`,
                  callbackResult: callbackResult,
                  eventPayload: eventPayload
                };
              });
            });
          }).then(
            function (outcome) {
              subscription.remove();
              return outcome;
            },
            function (error) {
              subscription.remove();
              throw error;
            }
          );
        })
        """,
        TestContext.Current.CancellationToken
    );
    var outcome = host.Runtime.Execute(_ =>
    {
      using (value)
      {
        using var obj = value.AsObject();
        return ReadOutcome(obj);
      }
    });

    Assert.Equal(42, outcome.Add);
    Assert.Equal("Hello from async C#", outcome.AsyncMessage);
    Assert.Equal("Ada:37:Ada is 37", outcome.RecordSummary);
    Assert.Equal("callback(C# sent JS)", outcome.CallbackResult);
    Assert.Equal("C# event: ready", outcome.EventPayload);
  }

  private static ShowcaseOutcome ReadOutcome(JavaScriptObject obj)
  {
    using var add = obj.GetProperty("add");
    using var asyncMessage = obj.GetProperty("asyncMessage");
    using var recordSummary = obj.GetProperty("recordSummary");
    using var callbackResult = obj.GetProperty("callbackResult");
    using var eventPayload = obj.GetProperty("eventPayload");
    return new ShowcaseOutcome(
        checked((int)add.AsDouble()),
        asyncMessage.AsString(),
        recordSummary.AsString(),
        callbackResult.AsString(),
        eventPayload.AsString()
    );
  }

  [Fact]
  public void ExampleCounterSharedObjectRoundTrips()
  {
    using var host = ExpoModuleTestHost.Create(
        ExpoModulesProvider_ExampleModule.Register
    );
    var result = host.Runtime.Execute(_ =>
    {
      using var value = host.Evaluate(
          "const module = globalThis._expoDotnet.modules.ExampleModule; " +
          "const owned = new module.ExampleCounter(10); " +
          "const fromNative = module.makeCounter(5); " +
          "const echoed = module.echoCounter(fromNative); " +
          "const identity = fromNative === echoed && Object.getPrototypeOf(owned) === module.ExampleCounter.prototype; " +
          "const values = [owned.increment(2), owned.count, fromNative.increment(1), identity]; " +
          "owned.release(); fromNative.release(); " +
          "const afterRelease = (() => { try { owned.increment(1); return 'no error'; } " +
          "catch (error) { return error.message.includes('not an active shared object'); } })(); " +
          "values.concat([afterRelease]).join(':')",
          "example-counter-shared-object.js"
      );
      return value.AsString();
    });
    Assert.Equal("12:12:6:true:true", result);
  }

  private sealed record ShowcaseOutcome(
      int Add,
      string AsyncMessage,
      string RecordSummary,
      string CallbackResult,
      string EventPayload);
}
