using Expo.JSI.Interop;
using Expo.JSI.Internal;

namespace Expo.JSI;

/// <summary>Scoped, non-owning reference to a JavaScript ArrayBuffer.</summary>
/// <remarks>
/// The ref is valid only during the active runtime access frame. Retain it before storing or
/// returning it. Byte access is exposed by the owned wrapper so the native handle and length are
/// acquired as one operation.
/// </remarks>
public readonly unsafe ref struct JavaScriptArrayBufferRef
{
  private readonly JavaScriptHandleScope? scope;
  private readonly JavaScriptValueInner inner;

  private JavaScriptArrayBufferRef(JavaScriptHandleScope scope, JavaScriptValueInner inner)
  {
    this.scope = scope;
    this.inner = inner;
  }

  internal static JavaScriptArrayBufferRef FromScopedHandle(
      JavaScriptHandleScope scope,
      JsiContext context,
      ExpoJsiValueHandle handle
  ) => new(scope, new JavaScriptValueInner(context, scope.TrackValue(handle)));

  internal static JavaScriptArrayBufferRef FromBorrowedRoot(
      JavaScriptHandleScope scope,
      JavaScriptValueInner inner
  ) => new(scope, inner);

  /// <summary>Retains this ref as an owned runtime-affine ArrayBuffer wrapper.</summary>
  public JavaScriptArrayBuffer Retain()
  {
    if (!Inner.IsArrayBuffer)
    {
      throw new InvalidOperationException("Value is not a JavaScript ArrayBuffer.");
    }
    var result = Inner.RetainArrayBuffer();
    if (!result.IsOk)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to retain JavaScript ArrayBuffer.");
    }
    return new JavaScriptArrayBuffer(Inner.Context, result.ArrayBuffer, result.ByteLength);
  }

  /// <summary>Returns retained MutableBuffer storage when the ArrayBuffer has native backing.</summary>
  public JavaScriptMutableBuffer? TryGetMutableBuffer()
  {
    if (!Inner.IsArrayBuffer)
    {
      throw new InvalidOperationException("Value is not a JavaScript ArrayBuffer.");
    }
    var result = Inner.TryGetMutableBuffer();
    if (!result.IsOk)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to inspect JavaScript ArrayBuffer backing.");
    }
    return result.HasValue
        ? new JavaScriptMutableBuffer(Inner.Context, result.MutableBuffer, result.ByteLength)
        : null;
  }

  private JavaScriptValueInner Inner
  {
    get
    {
      _ = Scope;
      if (inner.Handle == 0)
      {
        throw new ObjectDisposedException(nameof(JavaScriptHandleScope));
      }
      return inner;
    }
  }

  private JavaScriptHandleScope Scope =>
      scope ?? throw new ObjectDisposedException(nameof(JavaScriptHandleScope));
}
