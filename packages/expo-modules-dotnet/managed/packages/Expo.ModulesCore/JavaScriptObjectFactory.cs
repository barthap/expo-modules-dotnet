using Expo.JSI;

namespace Expo.ModulesCore;

public sealed class JavaScriptObjectFactory : IDisposable
{
  private readonly JavaScriptRuntime runtime;
  private readonly ExpoClassInstaller classInstaller;
  private bool disposed;

  internal JavaScriptObjectFactory(JavaScriptRuntime runtime)
  {
    this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    classInstaller = new ExpoClassInstaller(this.runtime);
    classInstaller.EnsureBaseClasses();
  }

  public JavaScriptFunction GetExpoClass(string className)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(className);
    ThrowIfDisposed();

    using var global = runtime.Global();
    using var expoDotnetValue = global.GetProperty("_expoDotnet");
    using var expoDotnet = expoDotnetValue.AsObject();
    using var classValue = expoDotnet.GetProperty(className);
    return classValue.AsFunction();
  }

  public JavaScriptObject CreateExpoClassInstance(string className)
  {
    ThrowIfDisposed();
    using var constructor = GetExpoClass(className);
    using var instanceValue = constructor.CallAsConstructor();
    return instanceValue.AsObject();
  }

  public void Dispose()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    classInstaller.Dispose();
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(disposed, this);
  }
}
