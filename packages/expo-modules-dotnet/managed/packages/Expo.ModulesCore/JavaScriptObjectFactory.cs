using Expo.JSI;

namespace Expo.ModulesCore;

public sealed class JavaScriptObjectFactory
{
  private readonly JavaScriptRuntime runtime;

  internal JavaScriptObjectFactory(JavaScriptRuntime runtime)
  {
    this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    this.runtime.EnsureExpoBaseClasses();
  }

  public JavaScriptFunction GetExpoClass(string className)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(className);

    using var global = runtime.Global();
    using var expoDotnetValue = global.GetProperty("_expoDotnet");
    using var expoDotnet = expoDotnetValue.AsObject();
    using var classValue = expoDotnet.GetProperty(className);
    return classValue.AsFunction();
  }

  public JavaScriptObject CreateExpoClassInstance(string className)
  {
    using var constructor = GetExpoClass(className);
    using var instanceValue = constructor.CallAsConstructor();
    return instanceValue.AsObject();
  }
}
