using Expo.ModulesCore;
using Expo.ModulesCore.Codecs;

namespace ExampleModule;

[ExpoModule("ExampleModule")]
[Events("onStatus")]
public sealed partial class ExampleMathModule : Module
{
  public ExampleMathModule(DotnetRuntimeContext context)
      : base(context)
  {
  }

  [OnCreate]
  public void OnCreate()
  {
    Console.WriteLine("ExampleModule created");
  }

  [OnDestroy]
  public void OnDestroy()
  {
    Console.WriteLine("ExampleModule destroyed");
  }

  [JS("add")]
  public double Add(double a, double b)
  {
    return a + b;
  }

  [JS("getMessageAsync")]
  public async Task<string> GetMessageAsync()
  {
    await Task.Yield();
    return "Hello from async C#";
  }

  [JS("describeUser")]
  public ExampleUserSummary DescribeUser(ExampleUser user)
  {
    return new ExampleUserSummary(user.Name, user.Age, $"{user.Name} is {user.Age}");
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

public readonly record struct ExampleUser(string Name, int Age);

public readonly record struct ExampleUserSummary(string Name, int Age, string Summary);
