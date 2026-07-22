using Expo.JSI;

namespace Expo.ModulesCore;

internal enum SharedObjectEventSubscriptionSetupStep
{
  BeforeCreateHostFunction,
  AfterRemovePropertyDefined,
}

internal static class SharedObjectEventPrototype
{
  internal static Action? SubscriptionDisposedForTesting { get; set; }
  internal static Action<SharedObjectEventSubscriptionSetupStep>? SubscriptionSetupStepForTesting { get; set; }
  internal static void Install(
      DotnetRuntimeContext context,
      SharedObjectClassRegistration registration)
  {
    var prototype = registration.Prototype;
    GeneratedFunction.DefineSync(context, prototype, "addListener", 2, AddListener, registration);
    GeneratedFunction.DefineSync(context, prototype, "removeListener", 2, RemoveListener, registration);
    GeneratedFunction.DefineSync(context, prototype, "removeAllListeners", 1, RemoveAllListeners, registration);
    GeneratedFunction.DefineSync(context, prototype, "emit", 1, Emit, registration);
    GeneratedFunction.DefineSync(context, prototype, "listenerCount", 1, ListenerCount, registration);
    GeneratedFunction.DefineSync(context, prototype, "removeSubscription", 1, RemoveSubscription, registration);
  }

  internal static void Dispatch(
      JavaScriptRuntime runtime,
      JavaScriptObject target,
      SharedObjectClassRegistration registration,
      string eventName,
      JavaScriptValue? payload = null)
  {
    using var storage = GetStorage(target, registration.EventStorageKey);
    if (storage is null)
    {
      return;
    }
    var listenerCount = storage.Length;
    for (uint index = 0; index < listenerCount; index++)
    {
      using var entryValue = storage.GetValue(index);
      using var entry = entryValue.AsObject();
      using var storedName = entry.GetProperty("eventName");
      if (!string.Equals(storedName.AsString(), eventName, StringComparison.Ordinal))
      {
        continue;
      }
      using var listenerValue = entry.GetProperty("listener");
      using var listener = listenerValue.AsFunction();
      try
      {
        using var result = payload is null
            ? listener.CallWithThis(target)
            : listener.CallWithThis(target, payload);
      }
      catch
      {
        // Match module events: one listener must not prevent later listeners.
      }
    }
  }

  private static JavaScriptValue AddListener(
      JavaScriptRuntime runtime,
      JavaScriptValueRef thisValue,
      JavaScriptArguments arguments,
      object context)
  {
    if (arguments.Count < 2)
    {
      throw new ArgumentException("addListener expects an event name and listener.");
    }
    var registration = (SharedObjectClassRegistration)context;
    var eventName = arguments.GetValue(0).AsString();
    using var target = ResolveTarget(thisValue, registration);
    using var listener = arguments.GetValue(1).AsFunction();
    using var storage = GetStorage(target, registration.EventStorageKey);
    var storageLength = storage?.Length ?? 0;
    using var replacement = runtime.CreateArray(storageLength + 1);
    for (uint index = 0; index < storageLength; index++)
    {
      using var storedEntry = storage!.GetValue(index);
      replacement.SetValue(index, storedEntry);
    }
    var listenerId = registration.NextEventListenerId();
    using var entry = runtime.CreateObject();
    using var idValue = runtime.CreateNumber(listenerId);
    entry.SetProperty("id", idValue);
    using var nameValue = runtime.CreateString(eventName);
    entry.SetProperty("eventName", nameValue);
    using var listenerValue = listener.AsValue();
    entry.SetProperty("listener", listenerValue);
    using var entryValue = entry.AsValue();
    replacement.SetValue(storageLength, entryValue);

    using var subscription = runtime.CreateObject();
    var state = new SubscriptionState(
        target.CreateWeak(),
        registration.EventStorageKey,
        eventName,
        listenerId
    );
    JavaScriptValue? result = null;
    try
    {
      SubscriptionSetupStepForTesting?.Invoke(
          SharedObjectEventSubscriptionSetupStep.BeforeCreateHostFunction
      );
      using var remove = runtime.CreateHostFunction(
          "remove",
          0,
          RemoveSubscriptionByState,
          state,
          static value => ((SubscriptionState)value).Dispose()
      );
      using var removeValue = remove.AsValue();
      subscription.SetProperty("remove", removeValue);
      SubscriptionSetupStepForTesting?.Invoke(
          SharedObjectEventSubscriptionSetupStep.AfterRemovePropertyDefined
      );
      result = subscription.AsValue();

      if (storage is null)
      {
        DefineStorageProperty(runtime, target, registration.EventStorageKey, replacement);
      }
      else
      {
        using var replacementValue = replacement.AsValue();
        target.SetProperty(registration.EventStorageKey, replacementValue);
      }

      var returnValue = result;
      result = null;
      return returnValue;
    }
    catch
    {
      result?.Dispose();
      state.Dispose();
      throw;
    }
  }

  private static JavaScriptValue RemoveListener(
      JavaScriptRuntime runtime,
      JavaScriptValueRef thisValue,
      JavaScriptArguments arguments,
      object context)
  {
    if (arguments.Count < 2)
    {
      throw new ArgumentException("removeListener expects an event name and listener.");
    }
    var registration = (SharedObjectClassRegistration)context;
    var eventName = arguments.GetValue(0).AsString();
    using var target = ResolveTarget(thisValue, registration);
    using var listenerFunction = arguments.GetValue(1).AsFunction();
    using var listener = listenerFunction.AsValue();
    Compact(runtime, target, registration.EventStorageKey, entry =>
    {
      using var name = entry.GetProperty("eventName");
      using var storedListener = entry.GetProperty("listener");
      return string.Equals(name.AsString(), eventName, StringComparison.Ordinal) &&
          runtime.StrictEquals(storedListener, listener);
    });
    return runtime.CreateUndefined();
  }

  private static JavaScriptValue RemoveAllListeners(
      JavaScriptRuntime runtime,
      JavaScriptValueRef thisValue,
      JavaScriptArguments arguments,
      object context)
  {
    if (arguments.Count < 1)
    {
      throw new ArgumentException("removeAllListeners expects an event name.");
    }
    var registration = (SharedObjectClassRegistration)context;
    var eventName = arguments.GetValue(0).AsString();
    using var target = ResolveTarget(thisValue, registration);
    Compact(runtime, target, registration.EventStorageKey, entry =>
    {
      using var name = entry.GetProperty("eventName");
      return string.Equals(name.AsString(), eventName, StringComparison.Ordinal);
    });
    return runtime.CreateUndefined();
  }

  private static JavaScriptValue Emit(
      JavaScriptRuntime runtime,
      JavaScriptValueRef thisValue,
      JavaScriptArguments arguments,
      object context)
  {
    if (arguments.Count < 1)
    {
      throw new ArgumentException("emit expects an event name.");
    }
    var registration = (SharedObjectClassRegistration)context;
    var eventName = arguments.GetValue(0).AsString();
    using var target = ResolveTarget(thisValue, registration);
    using var payload = arguments.Count > 1 ? arguments.GetValue(1).Retain() : null;
    Dispatch(runtime, target, registration, eventName, payload);
    return runtime.CreateUndefined();
  }

  private static JavaScriptValue ListenerCount(
      JavaScriptRuntime runtime,
      JavaScriptValueRef thisValue,
      JavaScriptArguments arguments,
      object context)
  {
    if (arguments.Count < 1)
    {
      throw new ArgumentException("listenerCount expects an event name.");
    }
    var registration = (SharedObjectClassRegistration)context;
    var eventName = arguments.GetValue(0).AsString();
    using var target = ResolveTarget(thisValue, registration);
    using var storage = GetStorage(target, registration.EventStorageKey);
    var count = 0;
    if (storage is not null)
    {
      for (uint index = 0; index < storage.Length; index++)
      {
        using var entryValue = storage.GetValue(index);
        using var entry = entryValue.AsObject();
        using var name = entry.GetProperty("eventName");
        if (string.Equals(name.AsString(), eventName, StringComparison.Ordinal)) count++;
      }
    }
    return runtime.CreateNumber(count);
  }

  private static JavaScriptValue RemoveSubscription(
      JavaScriptRuntime runtime,
      JavaScriptValueRef thisValue,
      JavaScriptArguments arguments,
      object context)
  {
    if (arguments.Count < 1)
    {
      throw new ArgumentException("removeSubscription expects a subscription.");
    }
    using var subscription = arguments.GetValue(0).AsObject().Retain();
    using var removeValue = subscription.GetProperty("remove");
    using var remove = removeValue.AsFunction();
    using var result = remove.CallWithThis(subscription);
    return runtime.CreateUndefined();
  }

  private static JavaScriptValue RemoveSubscriptionByState(
      JavaScriptRuntime runtime,
      JavaScriptValueRef thisValue,
      JavaScriptArguments arguments,
      object context)
  {
    var state = (SubscriptionState)context;
    using var target = state.TakeTarget();
    if (target is not null)
    {
      Compact(runtime, target, state.EventStorageKey, entry =>
      {
        using var name = entry.GetProperty("eventName");
        using var id = entry.GetProperty("id");
        return string.Equals(name.AsString(), state.EventName, StringComparison.Ordinal) &&
            id.AsDouble() == state.ListenerId;
      });
    }
    return runtime.CreateUndefined();
  }

  private static JavaScriptObject ResolveTarget(
      JavaScriptValueRef thisValue,
      SharedObjectClassRegistration registration)
  {
    var target = thisValue.AsObject().Retain();
    try
    {
      var managed = registration.Registry.ResolveManaged(target);
      if (managed.GetType() != registration.SharedObjectType)
      {
        throw new InvalidOperationException(
            $"The event receiver is not an active '{registration.SharedObjectType.Name}'."
        );
      }
      return target;
    }
    catch
    {
      target.Dispose();
      throw;
    }
  }

  private static JavaScriptArray? GetStorage(
      JavaScriptObject target,
      string eventStorageKey)
  {
    using var value = target.GetProperty(eventStorageKey);
    if (value.Kind != JavaScriptValueKind.Undefined)
    {
      return value.AsArray();
    }
    return null;
  }

  private static void DefineStorageProperty(
      JavaScriptRuntime runtime,
      JavaScriptObject target,
      string key,
      JavaScriptArray storage)
  {
    using var descriptor = runtime.CreateObject();
    using var storageValue = storage.AsValue();
    descriptor.SetProperty("value", storageValue);
    using var writable = runtime.CreateBool(true);
    descriptor.SetProperty("writable", writable);
    using var global = runtime.Global();
    using var objectValue = global.GetProperty("Object");
    using var objectConstructor = objectValue.AsObject();
    using var defineValue = objectConstructor.GetProperty("defineProperty");
    using var define = defineValue.AsFunction();
    using var targetValue = target.AsValue();
    using var keyValue = runtime.CreateString(key);
    using var result = define.Call(targetValue, keyValue, descriptor);
  }

  private static void Compact(
      JavaScriptRuntime runtime,
      JavaScriptObject target,
      string eventStorageKey,
      Func<JavaScriptObject, bool> remove)
  {
    using var storage = GetStorage(target, eventStorageKey);
    if (storage is null) return;
    var keptEntries = new List<JavaScriptValue>();
    for (uint index = 0; index < storage.Length; index++)
    {
      using var entryValue = storage.GetValue(index);
      using var entry = entryValue.AsObject();
      if (!remove(entry))
      {
        keptEntries.Add(entryValue.Retain());
      }
    }
    try
    {
      using var replacement = runtime.CreateArray((uint)keptEntries.Count);
      for (var index = 0; index < keptEntries.Count; index++)
      {
        replacement.SetValue((uint)index, keptEntries[index]);
      }
      using var replacementValue = replacement.AsValue();
      target.SetProperty(eventStorageKey, replacementValue);
    }
    finally
    {
      foreach (var entry in keptEntries) entry.Dispose();
    }
  }

  private sealed class SubscriptionState(
      JavaScriptWeakObject target,
      string eventStorageKey,
      string eventName,
      long listenerId) : IDisposable
  {
    private JavaScriptWeakObject? target = target;

    internal string EventStorageKey { get; } = eventStorageKey;
    internal string EventName { get; } = eventName;
    internal long ListenerId { get; } = listenerId;

    internal JavaScriptObject? TakeTarget()
    {
      var weak = Interlocked.Exchange(ref target, null);
      if (weak is null) return null;
      try { return weak.Lock(); }
      finally { DisposeWeak(weak); }
    }

    public void Dispose()
    {
      var weak = Interlocked.Exchange(ref target, null);
      if (weak is not null) DisposeWeak(weak);
    }

    private static void DisposeWeak(JavaScriptWeakObject weak)
    {
      weak.Dispose();
      SubscriptionDisposedForTesting?.Invoke();
    }
  }
}
