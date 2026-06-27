using Expo.JSI;

namespace HostFxrJSIProof;

internal static class GeneratedModuleProvider
{
  public static void Register(JavaScriptRuntime runtime)
  {
    using var global = runtime.Global();
    using var expo = runtime.CreateObject();
    using var modules = runtime.CreateObject();
    using var math = runtime.CreateObject();

    var module = new MathModule();
    using var add = runtime.CreateHostFunction("add", 2, MathAddHostFunction, module);

    using var globalValue = global.AsValue();
    global.SetProperty("global", globalValue);

    using var addValue = add.AsValue();
    math.SetProperty("add", addValue);

    using var mathValue = math.AsValue();
    modules.SetProperty("Math", mathValue);

    using var modulesValue = modules.AsValue();
    expo.SetProperty("modules", modulesValue);

    using var expoValue = expo.AsValue();
    global.SetProperty("expo", expoValue);

    Console.WriteLine("registered generated-looking Math module");
  }

  private static JavaScriptValue MathAddHostFunction(
      JavaScriptRuntime runtime,
      JavaScriptBorrowedValue thisValue,
      JavaScriptArguments arguments,
      object context
  )
  {
    if (arguments.Count != 2)
    {
      throw new ArgumentException($"Math.add expects 2 arguments, got {arguments.Count}.");
    }

    var module = (MathModule)context;
    var value = arguments.GetBorrowedValue(0).AsDouble();
    var shouldAddOne = arguments.GetBorrowedValue(1).AsBool();
    return runtime.CreateNumber(module.Add(value, shouldAddOne));
  }
}
