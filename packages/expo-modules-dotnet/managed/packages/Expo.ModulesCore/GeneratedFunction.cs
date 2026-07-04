using Expo.JSI;

namespace Expo.ModulesCore;

public static class GeneratedFunction
{
  [ThreadStatic]
  private static DotnetRuntimeContext? currentRuntimeContext;

  public static DotnetRuntimeContext CurrentRuntimeContext =>
      currentRuntimeContext ?? throw new InvalidOperationException(
          "No generated function runtime context is active."
      );

  internal static DotnetRuntimeContext? SetCurrentRuntimeContext(DotnetRuntimeContext? runtimeContext)
  {
    var previous = currentRuntimeContext;
    currentRuntimeContext = runtimeContext;
    return previous;
  }

  public static void DefineSync(
      DotnetRuntimeContext runtimeContext,
      JavaScriptObject module,
      string name,
      uint parameterCount,
      JavaScriptHostFunction callback,
      object context)
  {
    ArgumentNullException.ThrowIfNull(runtimeContext);
    ArgumentNullException.ThrowIfNull(module);
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(callback);
    ArgumentNullException.ThrowIfNull(context);

    var registration = runtimeContext.RegisterHostFunction(callback, context);
    using var function = runtimeContext.Runtime.CreateHostFunction(
        name,
        parameterCount,
        InvokeGeneratedHostFunction,
        registration
    );
    using var functionValue = function.AsValue();
    module.SetProperty(name, functionValue);
  }

  public static void DefineAsync(
      DotnetRuntimeContext runtimeContext,
      JavaScriptObject module,
      string name,
      uint parameterCount,
      JavaScriptHostFunction callback,
      object context)
  {
    ArgumentNullException.ThrowIfNull(runtimeContext);
    ArgumentNullException.ThrowIfNull(module);
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(callback);
    ArgumentNullException.ThrowIfNull(context);

    var registration = runtimeContext.RegisterHostFunction(callback, context);
    using var function = runtimeContext.Runtime.CreateHostFunction(
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

  public static JavaScriptValue CreateRejectedPromise(JavaScriptRuntime runtime, Exception exception)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentNullException.ThrowIfNull(exception);

    using var promiseValue = runtime.CreatePromise(
        _ => global::System.Threading.Tasks.Task.FromResult(
            JavaScriptPromiseResult.Reject(jsRuntime =>
            {
              using var error = jsRuntime.CreateErrorObject(exception.Message);
              return error.AsValue();
            })
        )
    );
    return promiseValue.AsValue();
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

  public static void RequireArgumentCount(
      string functionName,
      JavaScriptArguments arguments,
      uint min,
      uint max)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(functionName);
    if (min > max)
    {
      throw new ArgumentOutOfRangeException(nameof(min), "Minimum count cannot exceed maximum count.");
    }

    if (arguments.Count < min || arguments.Count > max)
    {
      throw new ArgumentException(
          $"{functionName} expects between {min} and {max} arguments, got {arguments.Count}."
      );
    }
  }
}
