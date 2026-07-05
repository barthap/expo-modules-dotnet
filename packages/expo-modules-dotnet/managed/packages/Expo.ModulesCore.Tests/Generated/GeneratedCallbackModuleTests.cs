using Expo.JSI;
using Expo.ModulesCore.Codecs;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Generated;

public sealed class GeneratedCallbackModuleTests
{
  [Fact]
  public void GeneratedModuleInvokesCallbackParameter()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "globalThis._expoDotnet.modules.GeneratedCallbacks.callNow('JS', name => `Hello ${name}`)",
          "generated-callback-call-now.js"
      );

      Assert.Equal("Hello JS", result.AsString());
      return true;
    });
  }

  [Fact]
  public void GeneratedModuleInvokesZeroArgumentCallbackParameter()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "globalThis._expoDotnet.modules.GeneratedCallbacks.callNoArgs(() => 'No args')",
          "generated-callback-call-no-args.js"
      );

      Assert.Equal("No args", result.AsString());
      return true;
    });
  }

  [Fact]
  public void GeneratedModuleInvokesExplicitValueTupleCallbackParameter()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "globalThis._expoDotnet.modules.GeneratedCallbacks.callExplicitTuple('A', 'B', (first, second) => first + second)",
          "generated-callback-explicit-tuple.js"
      );

      Assert.Equal("AB", result.AsString());
      return true;
    });
  }

  [Fact]
  public async Task GeneratedModuleInvokesRetainedCallbackLater()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var context = new DotnetRuntimeContext(fixture.Runtime);

    fixture.Runtime.Execute(runtime =>
    {
      using var modules = context.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var _ = fixture.Evaluate(
          """
          globalThis.__callbackResult = null;
          globalThis._expoDotnet.modules.GeneratedCallbacks.store(name => `Stored ${name}`);
          globalThis._expoDotnet.modules.GeneratedCallbacks.callStored('JS').then(
            value => { globalThis.__callbackResult = value; },
            error => { globalThis.__callbackResult = String(error && error.message || error); }
          );
          true;
          """,
          "generated-callback-stored.js"
      );
      return true;
    });

    var result = await WaitForGlobalStringAsync(fixture, "__callbackResult");
    Assert.Equal("Stored JS", result);
  }

  [Fact]
  public void RetainedCallbackInvokesImmediately()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var functionValue = fixture.Evaluate("(name) => `Hello ${name}`", "callback-now.js");
      using var function = functionValue.AsFunction();
      using var callback = JavaScriptCallback<ValueTuple<string>, string>.FromFunction(
          context,
          function,
          static (args, jsRuntime) => [StringCodec.Encode(args.Item1, jsRuntime)],
          static (value, jsRuntime) => StringCodec.Decode(value, jsRuntime)
      );

      Assert.Equal("Hello JS", callback.Invoke(ValueTuple.Create("JS")));
      return true;
    });
  }

  private static async Task<string?> WaitForGlobalStringAsync(
      HermesRuntimeFixture fixture,
      string globalName)
  {
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
    while (DateTime.UtcNow < deadline)
    {
      fixture.DrainTasks();
      var value = fixture.Runtime.Execute(_ =>
      {
        using var result = fixture.Evaluate($"globalThis.{globalName}", "callback-global-read.js");
        return result.IsNullish ? null : result.AsString();
      });
      if (value is not null)
      {
        return value;
      }

      await Task.Delay(10, TestContext.Current.CancellationToken);
    }

    return null;
  }

  [Fact]
  public async Task RetainedCallbackInvokesLaterOnRuntime()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var context = new DotnetRuntimeContext(fixture.Runtime);
    JavaScriptCallback<ValueTuple<string>, string> callback = fixture.Runtime.Execute(runtime =>
    {
      using var functionValue = fixture.Evaluate("(name) => `Later ${name}`", "callback-later.js");
      using var function = functionValue.AsFunction();
      return JavaScriptCallback<ValueTuple<string>, string>.FromFunction(
          context,
          function,
          static (args, jsRuntime) => [StringCodec.Encode(args.Item1, jsRuntime)],
          static (value, jsRuntime) => StringCodec.Decode(value, jsRuntime)
      );
    });

    var task = callback.InvokeAsync(ValueTuple.Create("JS"), TestContext.Current.CancellationToken);
    await WaitForTaskAsync(fixture, task);

    Assert.Equal("Later JS", await task);
  }

  [Fact]
  public void RetainedCallbackSupportsTwoArguments()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var functionValue = fixture.Evaluate("(first, second) => first + second", "callback-two.js");
      using var function = functionValue.AsFunction();
      using var callback = JavaScriptCallback<(string, string), string>.FromFunction(
          context,
          function,
          static (args, jsRuntime) =>
              [
                StringCodec.Encode(args.Item1, jsRuntime),
                StringCodec.Encode(args.Item2, jsRuntime),
              ],
          static (value, jsRuntime) => StringCodec.Decode(value, jsRuntime)
      );

      Assert.Equal("AB", callback.Invoke(("A", "B")));
      return true;
    });
  }

  [Fact]
  public void RuntimeContextDisposeReleasesRetainedCallback()
  {
    using var fixture = HermesRuntimeFixture.Create();
    JavaScriptCallback<ValueTuple<string>, string> callback;
    using (var context = new DotnetRuntimeContext(fixture.Runtime))
    {
      callback = fixture.Runtime.Execute(runtime =>
      {
        using var functionValue = fixture.Evaluate("(name) => name", "callback-dispose.js");
        using var function = functionValue.AsFunction();
        return JavaScriptCallback<ValueTuple<string>, string>.FromFunction(
            context,
            function,
            static (args, jsRuntime) => [StringCodec.Encode(args.Item1, jsRuntime)],
            static (value, jsRuntime) => StringCodec.Decode(value, jsRuntime)
        );
      });
    }

    Assert.Throws<InvalidOperationException>(() => callback.Invoke(ValueTuple.Create("JS")));
  }

  private static async Task WaitForTaskAsync<TResult>(
      HermesRuntimeFixture fixture,
      Task<TResult> task)
  {
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
    while (!task.IsCompleted && DateTime.UtcNow < deadline)
    {
      fixture.DrainTasks();
      await Task.Delay(10, TestContext.Current.CancellationToken);
    }
  }
}
