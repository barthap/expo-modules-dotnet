using Expo.JSI;

namespace Expo.ModulesCore;

public static class GeneratedFunction
{
  public static void DefineSync(
      RuntimeSession session,
      JavaScriptObject module,
      string name,
      uint parameterCount,
      JavaScriptHostFunction callback,
      object context)
  {
    ArgumentNullException.ThrowIfNull(session);
    ArgumentNullException.ThrowIfNull(module);
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(callback);
    ArgumentNullException.ThrowIfNull(context);

    var registration = session.RegisterHostFunction(callback, context);
    using var function = session.Runtime.CreateHostFunction(
        name,
        parameterCount,
        InvokeGeneratedHostFunction,
        registration
    );
    using var functionValue = function.AsValue();
    module.SetProperty(name, functionValue);
  }

  public static void DefineSync(
      JavaScriptRuntime runtime,
      JavaScriptObject module,
      string name,
      uint parameterCount,
      JavaScriptHostFunction callback,
      object context)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentNullException.ThrowIfNull(module);
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(callback);
    ArgumentNullException.ThrowIfNull(context);

    using var function = runtime.CreateHostFunction(name, parameterCount, callback, context);
    using var functionValue = function.AsValue();
    module.SetProperty(name, functionValue);
  }

  private static JavaScriptValue InvokeGeneratedHostFunction(
      JavaScriptRuntime runtime,
      JavaScriptValueRef thisValue,
      JavaScriptArguments arguments,
      object context)
  {
    var registration = (GeneratedHostFunctionRegistration)context;
    return registration.Invoke(runtime, thisValue, arguments);
  }

  public static void RequireArgumentCount(
      string functionName,
      JavaScriptArguments arguments,
      uint expected)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(functionName);
    if (arguments.Count != expected)
    {
      throw new ArgumentException(
          $"{functionName} expects {expected} arguments, got {arguments.Count}."
      );
    }
  }
}
