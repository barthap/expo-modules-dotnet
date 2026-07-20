using Expo.JSI.Internal;

namespace Expo.JSI;

/// <summary>Owns an opaque, runtime-affine weak reference to a JavaScript object.</summary>
/// <remarks>
/// <see cref="Lock" /> requires an active access frame for the originating runtime and returns a
/// fresh owned <see cref="JavaScriptObject" /> or <see langword="null" /> when its referent has
/// been collected. Dispose every object returned from <see cref="Lock" />. <see cref="Dispose" />
/// is idempotent, needs neither an access frame nor JSI access, releases only this opaque handle,
/// and causes subsequent <see cref="Lock" /> calls to throw <see cref="ObjectDisposedException" />.
/// </remarks>
public sealed class JavaScriptWeakObject : IDisposable
{
  private readonly JsiContext context;
  private readonly object gate = new();
  private ExpoJsiWeakObjectHandle handle;

  internal JavaScriptWeakObject(JsiContext context, ExpoJsiWeakObjectHandle handle)
  {
    this.context = context;
    this.handle = handle;
  }

  internal ExpoJsiWeakObjectHandle Handle
  {
    get
    {
      lock (gate)
      {
        ObjectDisposedException.ThrowIf(handle == 0, this);
        return handle;
      }
    }
  }

  /// <summary>Locks this weak reference in its originating runtime access frame.</summary>
  /// <returns>A fresh independently owned object wrapper, or <see langword="null" />.</returns>
  public unsafe JavaScriptObject? Lock()
  {
    lock (gate)
    {
      ObjectDisposedException.ThrowIf(handle == 0, this);
      JavaScriptHandleScope.CurrentFor(context);
      var result = context.Api->LockWeakObject(context.RuntimeHandle, handle);
      if (!result.IsOk)
      {
        JsiContext.ThrowNativeError(result.Error, "Failed to lock JavaScript weak object.");
      }
      return result.HasValue ? new JavaScriptObject(context, result.Value) : null;
    }
  }

  /// <summary>Releases this opaque handle without entering JSI.</summary>
  public unsafe void Dispose()
  {
    ExpoJsiWeakObjectHandle detached;
    lock (gate)
    {
      detached = handle;
      handle = IntPtr.Zero;
    }
    if (detached != 0)
    {
      context.Api->ReleaseWeakObject(detached);
    }
  }
}
