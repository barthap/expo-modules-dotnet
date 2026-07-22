using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

/// <summary>
/// Encodes and decodes one exact generated shared-object class through the context-owned
/// identity registry.
/// </summary>
/// <remarks>
/// <para>
/// Decoding resolves the receiver or argument through the registry's NativeState identity and
/// returns the original managed instance; it rejects plain objects, foreign or released shared
/// objects, and instances of any other shared class before authored code runs. Encoding pairs a
/// borrowed managed instance with its JavaScript counterpart via the class registration owned by
/// <see cref="GeneratedSharedObjectClass" />, so a repeated encode returns the strictly equal
/// JavaScript object.
/// </para>
/// <para>
/// Unlike <see cref="IJavaScriptCodec{T}" /> codecs, shared-object conversion requires the
/// <see cref="DotnetRuntimeContext" /> that owns the registry, so generated code passes the
/// current (or captured) runtime context explicitly at every call site.
/// </para>
/// </remarks>
public static class SharedObjectCodec<T>
    where T : SharedObject
{
  /// <summary>
  /// Decodes a scoped JavaScript value to the original managed instance.
  /// </summary>
  public static T Decode(
      JavaScriptValueRef value,
      JavaScriptRuntime runtime,
      DotnetRuntimeContext runtimeContext
  )
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentNullException.ThrowIfNull(runtimeContext);

    using var owned = value.Retain();
    return DecodeOwned(owned, runtimeContext);
  }

  /// <summary>
  /// Decodes an owned JavaScript value to the original managed instance.
  /// </summary>
  /// <remarks>
  /// This overload borrows <paramref name="value" />; ownership stays with the caller.
  /// </remarks>
  public static T Decode(
      JavaScriptValue value,
      JavaScriptRuntime runtime,
      DotnetRuntimeContext runtimeContext
  )
  {
    ArgumentNullException.ThrowIfNull(value);
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentNullException.ThrowIfNull(runtimeContext);

    return DecodeOwned(value, runtimeContext);
  }

  /// <summary>
  /// Encodes a borrowed managed instance as its paired JavaScript object.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptValue" /> is owned by the caller. The managed instance
  /// stays caller-owned; a first encode pairs it with this context's registry and a repeat encode
  /// returns the strictly equal JavaScript object.
  /// </remarks>
  public static JavaScriptValue Encode(
      T value,
      JavaScriptRuntime runtime,
      DotnetRuntimeContext runtimeContext
  )
  {
    ArgumentNullException.ThrowIfNull(value);
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentNullException.ThrowIfNull(runtimeContext);
    if (value.GetType() != typeof(T))
    {
      throw new InvalidOperationException(
          $"The shared object runtime type '{value.GetType()}' does not exactly match '{typeof(T)}'."
      );
    }

    var registration = GeneratedSharedObjectClass.GetRegistration(runtimeContext, typeof(T));
    using var objectValue = runtimeContext.SharedObjects.GetOrCreateJavaScriptObject(
        value,
        registration
    );
    return objectValue.AsValue();
  }

  private static T DecodeOwned(JavaScriptValue value, DotnetRuntimeContext runtimeContext)
  {
    using var objectValue = value.AsObject();
    var managed = runtimeContext.SharedObjects.ResolveManaged(objectValue);
    if (managed is not T instance || instance.GetType() != typeof(T))
    {
      throw new InvalidOperationException(
          $"The shared object receiver is not a '{typeof(T)}' instance."
      );
    }
    return instance;
  }
}
