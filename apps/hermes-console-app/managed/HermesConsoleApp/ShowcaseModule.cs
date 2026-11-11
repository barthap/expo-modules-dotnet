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

  [OnCreate]
  public void OnCreate()
  {
    Console.WriteLine("Showcase module created");
  }

  [OnDestroy]
  public void OnDestroy()
  {
    Console.WriteLine("Showcase module destroyed");
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

  [JS("failWithCode")]
  public void FailWithCode(string label)
  {
    throw new InvalidLabelException(label);
  }

  [JS("failWithCodeAsync")]
  public async Task FailWithCodeAsync(string label)
  {
    await Task.Yield();
    throw new InvalidLabelException(label);
  }
}

// JavaScript reads error.code === 'ERR_INVALID_LABEL', derived from the class name.
internal sealed class InvalidLabelException(string label)
    : CodedException($"Invalid label: {label}");

internal readonly record struct ConsoleUser(string Name, int Age);

internal readonly record struct ConsoleUserSummary(string Name, int Age, string Summary);
