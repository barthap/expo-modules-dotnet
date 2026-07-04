using Expo.JSI;
using Expo.ModulesCore.Codecs;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Generated;

public sealed class GeneratedCallbackModuleTests
{
  [Fact]
  public void RetainedCallbackInvokesImmediately()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var functionValue = fixture.Evaluate("(name) => `Hello ${name}`", "callback-now.js");
      using var function = functionValue.AsFunction();
      using var callback = JavaScriptCallback<string, string>.FromFunction(
          context,
          function,
          static (value, jsRuntime) => StringCodec.Encode(value, jsRuntime),
          static (value, jsRuntime) => StringCodec.Decode(value, jsRuntime)
      );

      Assert.Equal("Hello JS", callback.Invoke("JS"));
      return true;
    });
  }

  [Fact]
  public async Task RetainedCallbackInvokesLaterOnRuntime()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var context = new DotnetRuntimeContext(fixture.Runtime);
    JavaScriptCallback<string, string> callback = fixture.Runtime.Execute(runtime =>
    {
      using var functionValue = fixture.Evaluate("(name) => `Later ${name}`", "callback-later.js");
      using var function = functionValue.AsFunction();
      return JavaScriptCallback<string, string>.FromFunction(
          context,
          function,
          static (value, jsRuntime) => StringCodec.Encode(value, jsRuntime),
          static (value, jsRuntime) => StringCodec.Decode(value, jsRuntime)
      );
    });

    var task = callback.InvokeAsync("JS", TestContext.Current.CancellationToken);
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
      using var callback = JavaScriptCallback<string, string, string>.FromFunction(
          context,
          function,
          static (value, jsRuntime) => StringCodec.Encode(value, jsRuntime),
          static (value, jsRuntime) => StringCodec.Encode(value, jsRuntime),
          static (value, jsRuntime) => StringCodec.Decode(value, jsRuntime)
      );

      Assert.Equal("AB", callback.Invoke("A", "B"));
      return true;
    });
  }

  [Fact]
  public void RuntimeContextDisposeReleasesRetainedCallback()
  {
    using var fixture = HermesRuntimeFixture.Create();
    JavaScriptCallback<string, string> callback;
    using (var context = new DotnetRuntimeContext(fixture.Runtime))
    {
      callback = fixture.Runtime.Execute(runtime =>
      {
        using var functionValue = fixture.Evaluate("(name) => name", "callback-dispose.js");
        using var function = functionValue.AsFunction();
        return JavaScriptCallback<string, string>.FromFunction(
            context,
            function,
            static (value, jsRuntime) => StringCodec.Encode(value, jsRuntime),
            static (value, jsRuntime) => StringCodec.Decode(value, jsRuntime)
        );
      });
    }

    Assert.Throws<ObjectDisposedException>(() => callback.Invoke("JS"));
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
