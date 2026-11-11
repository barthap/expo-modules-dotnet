using System.Text;
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
        registration,
        null,
        CreateJavaScriptError
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
        registration,
        null,
        CreateJavaScriptError
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

    using var function = runtime.CreateHostFunction(name, parameterCount, callback, context, null, CreateJavaScriptError);
    using var functionValue = function.AsValue();
    module.SetProperty(name, functionValue);
  }

  public static JavaScriptValue CreateRejectedPromise(JavaScriptRuntime runtime, Exception exception)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentNullException.ThrowIfNull(exception);

    // The scheduler converts the exception, so a failing converter falls back to the original
    // exception's Message (spec: "Converter itself fails").
    using var promiseValue = runtime.CreatePromise(
        _ => global::System.Threading.Tasks.Task.FromException<JavaScriptPromiseResult>(exception),
        createError: CreateJavaScriptError
    );
    return promiseValue.AsValue();
  }

  private const string CauseSeparator = "\n→ Caused by: ";

  /// <summary>
  /// Converts an exception into the JavaScript <c>Error</c> that a module member throws or rejects
  /// with. The message is <see cref="Exception.Message" /> followed by each inner exception's
  /// message after <c>"\n→ Caused by: "</c>. A <see cref="CodedException" /> with a non-empty
  /// <see cref="CodedException.Code" /> adds a string <c>code</c> property.
  /// </summary>
  /// <remarks>
  /// Matches <see cref="JavaScriptErrorFactory" />. Returns a new owned value. Every module-created
  /// JavaScript function passes it to <c>JavaScriptRuntime.CreateHostFunction</c> or the async
  /// <c>JavaScriptRuntime.CreatePromise</c>. The chain is not filtered: every inner message
  /// reaches JavaScript.
  /// </remarks>
  public static JavaScriptValue CreateJavaScriptError(JavaScriptRuntime runtime, Exception exception)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentNullException.ThrowIfNull(exception);

    // Read Message and Code before any handle exists, so a throwing override creates nothing.
    var message = CreateJavaScriptErrorMessage(exception);
    var code = exception is CodedException coded ? coded.Code : null;

    using var error = runtime.CreateErrorObject(message);
    var value = error.AsValue();
    if (string.IsNullOrEmpty(code))
    {
      return value;
    }

    try
    {
      using var errorObject = value.AsObject();
      using var codeValue = runtime.CreateString(code);
      errorObject.SetProperty("code", codeValue);
      return value;
    }
    catch
    {
      value.Dispose();
      throw;
    }
  }

  private static string CreateJavaScriptErrorMessage(Exception exception)
  {
    if (exception.InnerException is null)
    {
      return exception.Message;
    }

    var builder = new StringBuilder(exception.Message);
    for (var cause = exception.InnerException; cause is not null; cause = cause.InnerException)
    {
      builder.Append(CauseSeparator).Append(cause.Message);
    }
    return builder.ToString();
  }

  internal static JavaScriptValue InvokeGeneratedHostFunction(
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
