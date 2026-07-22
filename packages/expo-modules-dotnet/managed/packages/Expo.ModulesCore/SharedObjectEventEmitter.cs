using Expo.JSI;
using Expo.ModulesCore.Codecs;

namespace Expo.ModulesCore;

/// <summary>Generated shared-object event dispatch glue.</summary>
public static class GeneratedSharedObjectEvents
{
  public static Task EmitAsync(
      DotnetRuntimeContext context,
      SharedObject instance,
      string eventName) =>
      ExecuteAsync(context, instance, eventName, null);

  public static Task EmitAsync(
      DotnetRuntimeContext context,
      SharedObject instance,
      string eventName,
      JavaScriptValue payload) =>
      ExecuteAsync(context, instance, eventName, (runtime, target, registration) =>
      {
        using var retained = payload.Ref.Retain();
        SharedObjectEventPrototype.Dispatch(runtime, target, registration, eventName, retained);
      });

  public static async Task EmitAsync(
      DotnetRuntimeContext context,
      SharedObject instance,
      string eventName,
      ArrayBuffer payload)
  {
    try
    {
      using var retained = payload.Retain();
      await ExecuteAsync(context, instance, eventName, (runtime, target, registration) =>
      {
        using var value = retained.Encode(runtime);
        SharedObjectEventPrototype.Dispatch(runtime, target, registration, eventName, value);
      }).ConfigureAwait(false);
    }
    catch (Exception exception)
    {
      await Task.FromException(exception).ConfigureAwait(false);
    }
  }

  public static Task EmitAsync<TCodec, T>(
      DotnetRuntimeContext context,
      SharedObject instance,
      string eventName,
      T payload)
      where TCodec : struct, IJavaScriptCodec<T> =>
      ExecuteAsync(context, instance, eventName, (runtime, target, registration) =>
      {
        using var value = TCodec.Encode(payload, runtime);
        SharedObjectEventPrototype.Dispatch(runtime, target, registration, eventName, value);
      });

  private static Task ExecuteAsync(
      DotnetRuntimeContext context,
      SharedObject instance,
      string eventName,
      Action<JavaScriptRuntime, JavaScriptObject, SharedObjectClassRegistration>? dispatch)
  {
    try
    {
      ArgumentNullException.ThrowIfNull(context);
      ArgumentNullException.ThrowIfNull(instance);
      ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
      var registration = GeneratedSharedObjectClass.GetRegistration(context, instance.GetType());
      registration.RequireEvent(eventName);
      var runtime = context.Runtime;
      bool Run(JavaScriptRuntime current)
      {
        using var target = registration.Registry.GetLiveJavaScriptObject(instance);
        if (dispatch is null)
        {
          SharedObjectEventPrototype.Dispatch(current, target, registration, eventName);
        }
        else
        {
          dispatch(current, target, registration);
        }
        return true;
      }

      if (runtime.HasExclusiveRuntimeAccess)
      {
        Run(runtime);
        return Task.CompletedTask;
      }
      if (runtime.CanExecuteSync)
      {
        runtime.Execute(Run);
        return Task.CompletedTask;
      }
      return runtime.ExecuteAsync(Run);
    }
    catch (Exception exception)
    {
      return Task.FromException(exception);
    }
  }
}
