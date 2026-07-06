using Expo.JSI;

namespace Expo.ModulesCore;

internal static class EventEmitterPrototype
{
  public static void Install(
      JavaScriptRuntime runtime,
      JavaScriptObject prototype,
      EventEmitterRuntimeState state,
      Func<JavaScriptFunction, JavaScriptFunction> retainHostFunction)
  {
    using var addListener = retainHostFunction(
        runtime.CreateHostFunction("addListener", 2, AddListener, state)
    );
    using var removeListener = retainHostFunction(
        runtime.CreateHostFunction("removeListener", 2, RemoveListener, state)
    );
    using var removeAllListeners = retainHostFunction(
        runtime.CreateHostFunction("removeAllListeners", 1, RemoveAllListeners, state)
    );
    using var emit = retainHostFunction(
        runtime.CreateHostFunction("emit", 1, Emit, state)
    );
    using var listenerCount = retainHostFunction(
        runtime.CreateHostFunction("listenerCount", 1, ListenerCount, state)
    );
    using var removeSubscription = retainHostFunction(
        runtime.CreateHostFunction("removeSubscription", 1, RemoveSubscription, state)
    );

    SetFunctionProperty(prototype, "addListener", addListener);
    SetFunctionProperty(prototype, "removeListener", removeListener);
    SetFunctionProperty(prototype, "removeAllListeners", removeAllListeners);
    SetFunctionProperty(prototype, "emit", emit);
    SetFunctionProperty(prototype, "listenerCount", listenerCount);
    SetFunctionProperty(prototype, "removeSubscription", removeSubscription);
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

    var state = (EventEmitterRuntimeState)context;
    var eventName = arguments.GetValue(0).AsString();
    using var emitter = thisValue.AsObject().Retain();
    using var listener = arguments.GetValue(1).AsFunction();
    var emitterId = state.GetOrCreateEmitterId(runtime, emitter);
    var wasEmpty = state.ListenerCount(emitterId, eventName) == 0;
    var listenerId = state.AddListener(emitterId, eventName, listener);
    if (wasEmpty)
    {
      CallObservingFunction(runtime, emitter, "startObserving", eventName);
    }

    using var subscription = runtime.CreateObject();
    using var remove = runtime.CreateHostFunction(
        "remove",
        0,
        RemoveSubscriptionByState,
        new ListenerSubscription(state, emitterId, eventName, listenerId)
    );
    SetFunctionProperty(subscription, "remove", remove);
    return subscription.AsValue();
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

    var state = (EventEmitterRuntimeState)context;
    var eventName = arguments.GetValue(0).AsString();
    using var emitter = thisValue.AsObject().Retain();
    var emitterId = state.GetEmitterId(emitter);
    using var listener = arguments.GetValue(1).AsFunction();
    using var listenerValue = listener.AsValue();
    RemoveListenerByValue(runtime, state, emitter, emitterId, eventName, listenerValue);
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

    var state = (EventEmitterRuntimeState)context;
    var eventName = arguments.GetValue(0).AsString();
    using var emitter = thisValue.AsObject().Retain();
    var emitterId = state.GetEmitterId(emitter);
    if (emitterId is not null && state.RemoveAll(emitterId.Value, eventName) > 0)
    {
      CallObservingFunction(runtime, emitter, "stopObserving", eventName);
    }
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

    var state = (EventEmitterRuntimeState)context;
    var eventName = arguments.GetValue(0).AsString();
    using var emitter = thisValue.AsObject().Retain();
    var emitterId = state.GetEmitterId(emitter);
    if (emitterId is null)
    {
      return runtime.CreateUndefined();
    }

    var listeners = state.GetListeners(emitterId.Value, eventName);
    var payload = RetainPayload(arguments);
    try
    {
      foreach (var listener in listeners)
      {
        try
        {
          using var result = listener.CallWithThis(emitter, payload);
        }
        catch
        {
          // Match Expo upstream: one listener throwing must not prevent later listeners.
        }
      }
    }
    finally
    {
      foreach (var listener in listeners)
      {
        listener.Dispose();
      }
      foreach (var value in payload)
      {
        value.Dispose();
      }
    }
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

    var state = (EventEmitterRuntimeState)context;
    var eventName = arguments.GetValue(0).AsString();
    using var emitter = thisValue.AsObject().Retain();
    var emitterId = state.GetEmitterId(emitter);
    return runtime.CreateNumber(emitterId is null ? 0 : state.ListenerCount(emitterId.Value, eventName));
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
    var subscription = (ListenerSubscription)context;
    using var emitter = subscription.State.GetEmitter(subscription.EmitterId);
    RemoveListenerById(
        runtime,
        subscription.State,
        emitter,
        subscription.EmitterId,
        subscription.EventName,
        subscription.ListenerId
    );
    return runtime.CreateUndefined();
  }

  private static void RemoveListenerById(
      JavaScriptRuntime runtime,
      EventEmitterRuntimeState state,
      JavaScriptObject emitter,
      int? emitterId,
      string eventName,
      int? listenerId)
  {
    if (emitterId is null || listenerId is null)
    {
      return;
    }

    var before = state.ListenerCount(emitterId.Value, eventName);
    state.RemoveListener(emitterId.Value, eventName, listenerId.Value);
    if (before > 0 && state.ListenerCount(emitterId.Value, eventName) == 0)
    {
      CallObservingFunction(runtime, emitter, "stopObserving", eventName);
    }
  }

  private static void RemoveListenerByValue(
      JavaScriptRuntime runtime,
      EventEmitterRuntimeState state,
      JavaScriptObject emitter,
      int? emitterId,
      string eventName,
      JavaScriptValue listener)
  {
    if (emitterId is null)
    {
      return;
    }

    var before = state.ListenerCount(emitterId.Value, eventName);
    state.RemoveListeners(runtime, emitterId.Value, eventName, listener);
    if (before > 0 && state.ListenerCount(emitterId.Value, eventName) == 0)
    {
      CallObservingFunction(runtime, emitter, "stopObserving", eventName);
    }
  }

  private static void CallObservingFunction(
      JavaScriptRuntime runtime,
      JavaScriptObject emitter,
      string functionName,
      string eventName)
  {
    using var functionValue = emitter.GetProperty(functionName);
    if (!functionValue.IsFunction)
    {
      return;
    }

    using var function = functionValue.AsFunction();
    using var eventNameValue = runtime.CreateString(eventName);
    using var result = function.CallWithThis(emitter, eventNameValue);
  }

  private static JavaScriptValue[] RetainPayload(JavaScriptArguments arguments)
  {
    if (arguments.Count <= 1)
    {
      return [];
    }

    var payload = new JavaScriptValue[arguments.Count - 1];
    for (uint index = 1; index < arguments.Count; index++)
    {
      payload[index - 1] = arguments.GetValue(index).Retain();
    }
    return payload;
  }

  private static void SetFunctionProperty(
      JavaScriptObject owner,
      string name,
      JavaScriptFunction function)
  {
    using var value = function.AsValue();
    owner.SetProperty(name, value);
  }

  private sealed record ListenerSubscription(
      EventEmitterRuntimeState State,
      int EmitterId,
      string EventName,
      int ListenerId);
}
