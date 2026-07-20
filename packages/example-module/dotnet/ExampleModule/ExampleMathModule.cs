using Expo.ModulesCore;

namespace ExampleModule;

[ExpoModule("ExampleModule")]
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

  [JS]
  public double Add(double a, double b)
  {
    return a + b;
  }

  [JS]
  public bool Ready => true;

  [JS]
  public async Task<string> GetMessageAsync()
  {
    await Task.Yield();
    return "Hello from async C#";
  }

  [JS]
  public ExampleUserSummary DescribeUser(ExampleUser user)
  {
    return new ExampleUserSummary(user.Name, user.Age, $"{user.Name} is {user.Age}");
  }

  [JS]
  public string TransformWithCallback(
      string value,
      JavaScriptCallback<ValueTuple<string>, string> callback)
  {
    return callback.Invoke(ValueTuple.Create($"C# sent {value}"));
  }

  [JS]
  public Task EmitStatusAsync(string label) => OnStatus($"C# event: {label}");

  [Event]
  public partial Func<string, Task> OnStatus { get; }
}

public readonly record struct ExampleUser(string Name, int Age);

public readonly record struct ExampleUserSummary(string Name, int Age, string Summary);
