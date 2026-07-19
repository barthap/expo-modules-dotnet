using Expo.JSI.Interop;
using Expo.JSI.Internal;

namespace Expo.JSI;

/// <summary>Owns runtime-neutral MutableBuffer storage.</summary>
/// <remarks>
/// Byte callbacks borrow storage synchronously. The buffer can be encoded into any live runtime.
/// Its ABI dispatch is retained independently of the originating runtime's API-table storage.
/// Each instance owns one lease. <see cref="Dispose" /> atomically releases that lease and is
/// idempotent, but does not synchronize disposal with active use; callers that need independent
/// concurrent ownership must call <see cref="Retain" />. This type deliberately has neither a
/// finalizer nor SafeHandle backing: forgetting to dispose it leaks its native MutableBuffer handle
/// because runtime teardown does not own runtime-neutral MutableBuffer storage.
/// </remarks>
public sealed class JavaScriptMutableBuffer : IDisposable
{
  private readonly NativeMutableBufferDispatch dispatch;
  private readonly int byteLength;
  private ExpoJsiMutableBufferHandle handle;

  internal unsafe JavaScriptMutableBuffer(
      JsiContext context,
      ExpoJsiMutableBufferHandle handle,
      int byteLength
  )
      : this(NativeMutableBufferDispatch.Create(context.Api), handle, byteLength)
  {
  }

  private JavaScriptMutableBuffer(
      NativeMutableBufferDispatch dispatch,
      ExpoJsiMutableBufferHandle handle,
      int byteLength
  )
  {
    this.dispatch = dispatch;
    this.handle = handle;
    this.byteLength = byteLength;
  }

  public static JavaScriptMutableBuffer Allocate(int byteLength)
  {
    ArgumentOutOfRangeException.ThrowIfNegative(byteLength);
    var dispatch = NativeMutableBufferDispatch.Default;
    var result = dispatch.Allocate(byteLength);
    if (!result.IsOk || !result.HasValue)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to allocate MutableBuffer.");
    }
    return new JavaScriptMutableBuffer(dispatch, result.MutableBuffer, result.ByteLength);
  }

  public static JavaScriptMutableBuffer CopyFrom(ReadOnlySpan<byte> bytes)
  {
    var dispatch = NativeMutableBufferDispatch.Default;
    var result = dispatch.Copy(bytes);
    if (!result.IsOk || !result.HasValue)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to copy MutableBuffer.");
    }
    return new JavaScriptMutableBuffer(dispatch, result.MutableBuffer, result.ByteLength);
  }

  public int ByteLength
  {
    get
    {
      ThrowIfDisposed();
      return byteLength;
    }
  }

  public JavaScriptMutableBuffer Retain()
  {
    ThrowIfDisposed();
    var result = dispatch.Clone(handle);
    if (!result.IsOk || !result.HasValue)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to retain MutableBuffer.");
    }
    return new JavaScriptMutableBuffer(dispatch, result.MutableBuffer, result.ByteLength);
  }

  public void WithBytes(JavaScriptBytesAction action)
  {
    ArgumentNullException.ThrowIfNull(action);
    WithBytes(bytes =>
    {
      action(bytes);
      return 0;
    });
  }

  public unsafe TResult WithBytes<TResult>(JavaScriptBytesFunc<TResult> action)
  {
    ArgumentNullException.ThrowIfNull(action);
    ThrowIfDisposed();
    var result = dispatch.GetBytes(handle);
    if (!result.IsOk)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to access MutableBuffer bytes.");
    }
    ValidateSpanShape(result);
    if (result.Data is null && result.Length != 0)
    {
      throw new InvalidOperationException("Native JSI returned a null byte pointer for non-empty storage.");
    }
    return action(new Span<byte>(result.Data, result.Length));
  }

  public void WithReadOnlyBytes(JavaScriptReadOnlyBytesAction action)
  {
    ArgumentNullException.ThrowIfNull(action);
    WithReadOnlyBytes(bytes =>
    {
      action(bytes);
      return 0;
    });
  }

  public unsafe TResult WithReadOnlyBytes<TResult>(JavaScriptReadOnlyBytesFunc<TResult> action)
  {
    ArgumentNullException.ThrowIfNull(action);
    ThrowIfDisposed();
    var result = dispatch.GetBytes(handle);
    if (!result.IsOk)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to access MutableBuffer bytes.");
    }
    ValidateSpanShape(result);
    if (result.Data is null && result.Length != 0)
    {
      throw new InvalidOperationException("Native JSI returned a null byte pointer for non-empty storage.");
    }
    return action(new ReadOnlySpan<byte>(result.Data, result.Length));
  }

  /// <summary>Creates an owned JavaScript ArrayBuffer value over this storage.</summary>
  /// <remarks>The supplied runtime must have active runtime access.</remarks>
  public unsafe JavaScriptValue AsValue(JavaScriptRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ThrowIfDisposed();
    JavaScriptHandleScope.CurrentFor(runtime.Context);
    var result = runtime.Context.Api->ConvertMutableBufferToValue(runtime.Context.RuntimeHandle, handle);
    if (!result.IsOk)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to convert MutableBuffer to ArrayBuffer.");
    }
    return JavaScriptValue.FromOwnedHandle(runtime.Context, result.Value);
  }

  public void Dispose()
  {
    var value = Interlocked.Exchange(ref handle, IntPtr.Zero);
    if (value != 0)
    {
      dispatch.Release(value);
    }
  }

  private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(handle == 0, this);

  private void ValidateSpanShape(ExpoJsiByteSpanResult result)
  {
    if (result.IsOk && (result.Length < 0 || result.Length != byteLength))
    {
      throw new InvalidOperationException("Native JSI returned an invalid MutableBuffer byte length.");
    }
  }
}
