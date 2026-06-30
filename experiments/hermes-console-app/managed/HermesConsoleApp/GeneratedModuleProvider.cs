using Expo.JSI;

namespace HermesConsoleApp;

internal static class GeneratedModuleProvider
{
  public static void Register(JavaScriptRuntime runtime)
  {
    using var global = runtime.Global();
    using var expo = runtime.CreateObject();
    using var modules = runtime.CreateObject();
    using var math = runtime.CreateObject();
    using var text = runtime.CreateObject();

    var mathModule = new MathModule();
    var textModule = new TextModule();
    using var add = runtime.CreateHostFunction("add", 2, MathAddHostFunction, mathModule);
    using var greet = runtime.CreateHostFunction("greet", 1, TextGreetHostFunction, textModule);

    using var globalValue = global.AsValue();
    global.SetProperty("global", globalValue);

    using var addValue = add.AsValue();
    math.SetProperty("add", addValue);

    using var greetValue = greet.AsValue();
    text.SetProperty("greet", greetValue);

    using var mathValue = math.AsValue();
    modules.SetProperty("Math", mathValue);

    using var textValue = text.AsValue();
    modules.SetProperty("Text", textValue);

    using var modulesValue = modules.AsValue();
    expo.SetProperty("modules", modulesValue);

    using var expoValue = expo.AsValue();
    global.SetProperty("expo", expoValue);

    Console.WriteLine("registered generated-looking Math module");
  }

  private static JavaScriptValue MathAddHostFunction(
      JavaScriptRuntime runtime,
      JavaScriptValueRef thisValue,
      JavaScriptArguments arguments,
      object context
  )
  {
    if (arguments.Count != 2)
    {
      throw new ArgumentException($"Math.add expects 2 arguments, got {arguments.Count}.");
    }

    var module = (MathModule)context;
    var value = arguments.GetValue(0).AsDouble();
    var shouldAddOne = arguments.GetValue(1).AsBool();
    return runtime.CreateNumber(module.Add(value, shouldAddOne));
  }

  private static JavaScriptValue TextGreetHostFunction(
      JavaScriptRuntime runtime,
      JavaScriptValueRef thisValue,
      JavaScriptArguments arguments,
      object context
  )
  {
    if (arguments.Count != 1)
    {
      throw new ArgumentException($"Text.greet expects 1 argument, got {arguments.Count}.");
    }

    var module = (TextModule)context;
    var name = arguments.GetValue(0).AsString();
    return runtime.CreateString(module.Greet(name));
  }
}
