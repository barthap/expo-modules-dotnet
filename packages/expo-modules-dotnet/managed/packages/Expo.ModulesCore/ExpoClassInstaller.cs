using Expo.JSI;

namespace Expo.ModulesCore;

internal sealed class ExpoClassInstaller : IDisposable
{
  private const string EventEmitterMarker = "__expo_dotnet_event_emitter__";
  private const string NativeModuleMarker = "__expo_dotnet_native_module__";

  private readonly JavaScriptRuntime runtime;
  private readonly EventEmitterRuntimeState eventEmitterState = new();
  private readonly List<IDisposable> retainedHostFunctions = [];
  private readonly string classMarkerValue = Guid.NewGuid().ToString("N");
  private bool disposed;

  public ExpoClassInstaller(JavaScriptRuntime runtime)
  {
    this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
  }

  public void EnsureBaseClasses()
  {
    ThrowIfDisposed();

    using var global = runtime.Global();
    using var expoDotnet = GetOrCreateObject(global, "_expoDotnet");
    InstallEventEmitterClass(expoDotnet);
    InstallNativeModuleClass(expoDotnet);
  }

  public void Dispose()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    eventEmitterState.Dispose();
    foreach (var function in retainedHostFunctions)
    {
      function.Dispose();
    }
    retainedHostFunctions.Clear();
  }

  private JavaScriptObject GetOrCreateObject(JavaScriptObject owner, string propertyName)
  {
    using var existing = owner.GetProperty(propertyName);
    if (existing.IsObject)
    {
      return existing.AsObject();
    }

    var created = runtime.CreateObject();
    using var createdValue = created.AsValue();
    owner.SetProperty(propertyName, createdValue);
    return created;
  }

  private void InstallEventEmitterClass(JavaScriptObject expoDotnet)
  {
    using var existing = expoDotnet.GetProperty("EventEmitter");
    if (IsMarkedClass(existing, EventEmitterMarker))
    {
      return;
    }

    using var eventEmitterClass = runtime.CreateClass("EventEmitter");
    using var eventEmitterClassValue = eventEmitterClass.AsValue();
    using var eventEmitterObject = eventEmitterClassValue.AsObject();
    using var prototypeValue = eventEmitterObject.GetProperty("prototype");
    using var prototype = prototypeValue.AsObject();
    using var marker = runtime.CreateString(classMarkerValue);

    eventEmitterObject.SetProperty(EventEmitterMarker, marker);
    EventEmitterPrototype.Install(runtime, prototype, eventEmitterState, RetainHostFunction);
    SetFunctionProperty(expoDotnet, "EventEmitter", eventEmitterClass);
  }

  private void InstallNativeModuleClass(JavaScriptObject expoDotnet)
  {
    using var existing = expoDotnet.GetProperty("NativeModule");
    if (IsMarkedClass(existing, NativeModuleMarker))
    {
      return;
    }

    using var eventEmitterValue = expoDotnet.GetProperty("EventEmitter");
    using var eventEmitter = eventEmitterValue.AsFunction();
    using var nativeModuleClass = runtime.CreateClass("NativeModule", eventEmitter);
    using var nativeModuleClassValue = nativeModuleClass.AsValue();
    using var nativeModuleObject = nativeModuleClassValue.AsObject();
    using var marker = runtime.CreateString(classMarkerValue);

    nativeModuleObject.SetProperty(NativeModuleMarker, marker);
    SetFunctionProperty(expoDotnet, "NativeModule", nativeModuleClass);
  }

  private JavaScriptFunction RetainHostFunction(JavaScriptFunction function)
  {
    using var value = function.AsValue();
    retainedHostFunctions.Add(value.AsFunction());
    return function;
  }

  private bool IsMarkedClass(JavaScriptValue value, string markerName)
  {
    if (!value.IsObject)
    {
      return false;
    }

    using var obj = value.AsObject();
    if (!obj.GetOwnPropertyNames().Contains(markerName, StringComparer.Ordinal))
    {
      return false;
    }

    using var marker = obj.GetProperty(markerName);
    return marker.IsString && marker.AsString() == classMarkerValue;
  }

  private static void SetFunctionProperty(
      JavaScriptObject owner,
      string name,
      JavaScriptFunction function)
  {
    using var value = function.AsValue();
    owner.SetProperty(name, value);
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(disposed, this);
  }
}
