using System.Diagnostics.CodeAnalysis;
using Expo.JSI.Internal;

namespace Expo.JSI;

/// <summary>
/// Owns a JavaScript object value handle.
/// </summary>
/// <remarks>
/// Methods on this type return owned wrappers. Dispose every returned value/object wrapper when it
/// is no longer needed. Use scoped refs when temporary traversal should avoid disposable
/// intermediates.
/// </remarks>
public sealed class JavaScriptObject : IJavaScriptValueRepresentable, IDisposable
{
  private readonly JsiContext context;
  private ExpoJsiValueHandle handle;

  internal JavaScriptObject(JsiContext context, ExpoJsiValueHandle handle)
  {
    this.context = context;
    this.handle = handle;
  }

  private JavaScriptObjectInner Inner
  {
    get
    {
      ThrowIfDisposed();
      return new JavaScriptObjectInner(context, handle);
    }
  }

  /// <summary>
  /// Sets a property to an existing JavaScript value.
  /// </summary>
  /// <remarks>
  /// This method borrows <paramref name="value" /> for the duration of the call. Ownership of
  /// <paramref name="value" /> stays with the caller.
  /// </remarks>
  public void SetProperty(string name, JavaScriptValue value)
  {
    ArgumentNullException.ThrowIfNull(value);
    Inner.SetProperty(name, value.Handle);
  }

  /// <summary>
  /// Gets a property as an owned JavaScript value.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptValue" /> must be disposed by the caller.
  /// </remarks>
  public JavaScriptValue GetProperty(string name) =>
    JavaScriptValue.FromOwnedHandle(context, Inner.GetProperty(name));

  /// <summary>
  /// Gets this object's own property names as managed strings.
  /// </summary>
  public IReadOnlyList<string> GetOwnPropertyNames() => Inner.GetOwnPropertyNames();

  public void SetNativeState<TState>(TState state)
      where TState : class, IJavaScriptNativeState<TState>
  {
    var registration = context.NativeStates.Register(state);
    try
    {
      Inner.SetNativeState(registration);
    }
    catch
    {
      context.NativeStates.Release(registration.Token);
      NativeStateRegistry.ReleaseContext(registration.ReleaseContext);
      throw;
    }
  }

  public TState GetNativeState<TState>()
      where TState : class, IJavaScriptNativeState<TState>
  {
    var result = Inner.GetNativeState(TState.TypeId.Value);
    if (!result.HasValue)
    {
      throw new InvalidOperationException(
          $"NativeState entry for {typeof(TState).Name} is missing."
      );
    }
    return context.NativeStates.Resolve<TState>(result.Token);
  }

  public bool TryGetNativeState<TState>([NotNullWhen(true)] out TState? state)
      where TState : class, IJavaScriptNativeState<TState>
  {
    var result = Inner.GetNativeState(TState.TypeId.Value);
    if (!result.HasValue)
    {
      state = null;
      return false;
    }
    return context.NativeStates.TryResolve(result.Token, out state);
  }

  public void ClearNativeState<TState>()
      where TState : class, IJavaScriptNativeState<TState>
  {
    Inner.ClearNativeState(TState.TypeId.Value);
  }

  /// <summary>
  /// Clones this object as an owned JavaScript value handle.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptValue" /> must be disposed independently. Disposing it does
  /// not dispose this object wrapper.
  /// </remarks>
  public JavaScriptValue AsValue() =>
    JavaScriptValue.FromOwnedHandle(context, Inner.AsValue());

  /// <summary>
  /// Releases the owned native object value handle.
  /// </summary>
  public void Dispose()
  {
    var value = Interlocked.Exchange(ref handle, IntPtr.Zero);
    if (value != 0)
    {
      unsafe
      {
        context.Api->ReleaseValueHandle(context.RuntimeHandle, value);
      }
    }
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(handle == 0, this);
  }
}
