using Expo.ModulesCore;
using Expo.ModulesCore.Codecs;

namespace HermesConsoleApp;

[ExpoModule("Showcase")]
[Events("onStatus")]
internal sealed partial class ShowcaseModule : Module
{
  public ShowcaseModule(DotnetRuntimeContext context)
      : base(context)
  {
  }

  [JS("getMessageAsync")]
  public async Task<string> GetMessageAsync()
  {
    await Task.Yield();
    return "Hello from async C#";
  }

  [JS("describeUser")]
  public ConsoleUserSummary DescribeUser(ConsoleUser user)
  {
    return new ConsoleUserSummary(user.Name, user.Age, $"{user.Name} is {user.Age}");
  }

  [JS("transformWithCallback")]
  public string TransformWithCallback(
      string value,
      JavaScriptCallback<ValueTuple<string>, string> callback)
  {
    return callback.Invoke(ValueTuple.Create($"C# sent {value}"));
  }

  [JS("emitStatusAsync")]
  public Task EmitStatusAsync(string label)
  {
    return SendEventAsync<StringCodec, string>("onStatus", $"C# event: {label}");
  }
}

internal readonly record struct ConsoleUser(string Name, int Age);

internal readonly record struct ConsoleUserSummary(string Name, int Age, string Summary);
