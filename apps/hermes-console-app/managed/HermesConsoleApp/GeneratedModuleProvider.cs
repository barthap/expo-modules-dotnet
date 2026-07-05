using Expo.JSI;
using Expo.ModulesCore;

namespace HermesConsoleApp;

internal static class GeneratedModuleProvider
{
  public static void Register(DotnetRuntimeContext context, JavaScriptObject modules)
  {
    var runtime = context.Runtime;
    using var global = runtime.Global();
    using var math = runtime.CreateObject();
    using var text = runtime.CreateObject();

    var mathModule = context.ModuleRegistry.GetOrCreateModule("HermesConsoleApp.Math", static () => new MathModule());
    var textModule = context.ModuleRegistry.GetOrCreateModule("HermesConsoleApp.Text", static () => new TextModule());

    using var globalValue = global.AsValue();
    global.SetProperty("global", globalValue);

    GeneratedFunction.DefineSync(context, math, "add", 2, MathAddHostFunction, mathModule);
    GeneratedFunction.DefineSync(context, text, "greet", 1, TextGreetHostFunction, textModule);

    using var mathValue = math.AsValue();
    modules.SetProperty("Math", mathValue);

    using var textValue = text.AsValue();
    modules.SetProperty("Text", textValue);

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
