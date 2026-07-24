using Expo.JSI;

namespace Expo.ModulesCore;

/// <summary>Owned binary storage accepted by generated Expo module bindings.</summary>
/// <remarks>
/// Each instance owns one backing lease. <see cref="Dispose" /> atomically releases that lease and
/// is idempotent, but does not synchronize disposal with active use. Call <see cref="Retain" /> to
/// give a concurrent consumer independent ownership. The backing wrappers have no finalizers;
/// callers must dispose this wrapper promptly.
/// </remarks>
public sealed class ArrayBuffer : IDisposable
{
  private JavaScriptArrayBuffer? javaScriptBacking;
  private JavaScriptMutableBuffer? nativeBacking;

  internal ArrayBuffer(JavaScriptArrayBuffer backing) => javaScriptBacking = backing;
  internal ArrayBuffer(JavaScriptMutableBuffer backing) => nativeBacking = backing;

  /// <summary>Gets the byte length captured when this buffer acquired its backing handle.</summary>
  /// <remarks>
  /// JavaScript-backed buffers validate that their current length still matches this captured value
  /// before exposing bytes.
  /// </remarks>
  /// <exception cref="ObjectDisposedException">Thrown after this buffer has been disposed.</exception>
  public int ByteLength
  {
    get
    {
      ThrowIfDisposed();
      return javaScriptBacking?.ByteLength ?? nativeBacking!.ByteLength;
    }
  }

  /// <summary>Allocates a zero-filled native-backed buffer with the requested length.</summary>
  /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="byteLength" /> is negative.</exception>
  public static ArrayBuffer Allocate(int byteLength) =>
      new(JavaScriptMutableBuffer.Allocate(byteLength));

  /// <summary>Copies the supplied bytes into a new native-backed buffer.</summary>
  /// <remarks>The new buffer has storage independent from the supplied span.</remarks>
  public static ArrayBuffer CopyFrom(ReadOnlySpan<byte> bytes) =>
      new(JavaScriptMutableBuffer.CopyFrom(bytes));

  /// <summary>Creates an independently owned wrapper over the same backing storage.</summary>
  /// <remarks>
  /// The returned wrapper owns a retained lease and must be disposed independently. Changes to the
  /// shared storage remain visible through both wrappers.
  /// </remarks>
  /// <exception cref="ObjectDisposedException">Thrown after this buffer has been disposed.</exception>
  public ArrayBuffer Retain()
  {
    ThrowIfDisposed();
    return javaScriptBacking is { } javaScript
        ? new ArrayBuffer(javaScript.Retain())
        : new ArrayBuffer(nativeBacking!.Retain());
  }

  /// <summary>Copies this buffer's bytes into an independent managed array.</summary>
  /// <exception cref="ObjectDisposedException">Thrown after this buffer has been disposed.</exception>
  public byte[] ToArray() => WithReadOnlyBytes(bytes => bytes.ToArray());

  /// <summary>Asynchronously copies this buffer's bytes into an independent managed array.</summary>
  /// <remarks>
  /// JavaScript-backed buffers schedule work on their owning JavaScript runtime. The cancellation
  /// token can cancel the returned task before its callback starts.
  /// </remarks>
  /// <exception cref="ObjectDisposedException">Thrown after this buffer has been disposed.</exception>
  public async Task<byte[]> ToArrayAsync(CancellationToken cancellationToken = default) =>
      await WithReadOnlyBytesAsync(bytes => bytes.ToArray(), cancellationToken).ConfigureAwait(false);

  /// <summary>Copies this buffer's bytes into an independent native-backed buffer.</summary>
  /// <exception cref="ObjectDisposedException">Thrown after this buffer has been disposed.</exception>
  public ArrayBuffer Copy() =>
      new(JavaScriptMutableBuffer.CopyFrom(WithReadOnlyBytes(bytes => bytes.ToArray())));

  /// <summary>Asynchronously copies this buffer's bytes into an independent native-backed buffer.</summary>
  /// <remarks>
  /// JavaScript-backed buffers schedule the source read on their owning JavaScript runtime. The
  /// cancellation token can cancel the returned task before its callback starts.
  /// </remarks>
  /// <exception cref="ObjectDisposedException">Thrown after this buffer has been disposed.</exception>
  public async Task<ArrayBuffer> CopyAsync(CancellationToken cancellationToken = default)
  {
    var bytes = await ToArrayAsync(cancellationToken).ConfigureAwait(false);
    return CopyFrom(bytes);
  }

  /// <summary>Invokes an action with writable bytes borrowed for the synchronous callback.</summary>
  /// <remarks>The supplied span is valid only until the callback returns.</remarks>
  /// <exception cref="ArgumentNullException">Thrown when <paramref name="action" /> is <see langword="null" />.</exception>
  /// <exception cref="ObjectDisposedException">Thrown after this buffer has been disposed.</exception>
  public void WithBytes(ArrayBufferBytesAction action)
  {
    ArgumentNullException.ThrowIfNull(action);
    WithBytes(bytes =>
    {
      action(bytes);
      return 0;
    });
  }

  /// <summary>Invokes a function with writable bytes borrowed for the synchronous callback.</summary>
  /// <remarks>The supplied span is valid only until the callback returns.</remarks>
  /// <exception cref="ArgumentNullException">Thrown when <paramref name="action" /> is <see langword="null" />.</exception>
  /// <exception cref="ObjectDisposedException">Thrown after this buffer has been disposed.</exception>
  public TResult WithBytes<TResult>(ArrayBufferBytesFunc<TResult> action)
  {
    ArgumentNullException.ThrowIfNull(action);
    ThrowIfDisposed();
    return javaScriptBacking is { } javaScript
        ? javaScript.WithBytes(bytes => action(bytes))
        : nativeBacking!.WithBytes(bytes => action(bytes));
  }

  /// <summary>Invokes an action with readable bytes borrowed for the synchronous callback.</summary>
  /// <remarks>The supplied span is valid only until the callback returns.</remarks>
  /// <exception cref="ArgumentNullException">Thrown when <paramref name="action" /> is <see langword="null" />.</exception>
  /// <exception cref="ObjectDisposedException">Thrown after this buffer has been disposed.</exception>
  public void WithReadOnlyBytes(ArrayBufferReadOnlyBytesAction action)
  {
    ArgumentNullException.ThrowIfNull(action);
    WithReadOnlyBytes(bytes =>
    {
      action(bytes);
      return 0;
    });
  }

  /// <summary>Invokes a function with readable bytes borrowed for the synchronous callback.</summary>
  /// <remarks>The supplied span is valid only until the callback returns.</remarks>
  /// <exception cref="ArgumentNullException">Thrown when <paramref name="action" /> is <see langword="null" />.</exception>
  /// <exception cref="ObjectDisposedException">Thrown after this buffer has been disposed.</exception>
  public TResult WithReadOnlyBytes<TResult>(ArrayBufferReadOnlyBytesFunc<TResult> action)
  {
    ArgumentNullException.ThrowIfNull(action);
    ThrowIfDisposed();
    return javaScriptBacking is { } javaScript
        ? javaScript.WithReadOnlyBytes(bytes => action(bytes))
        : nativeBacking!.WithReadOnlyBytes(bytes => action(bytes));
  }

  /// <summary>Asynchronously invokes an action with writable buffer bytes.</summary>
  /// <remarks>
  /// JavaScript-backed buffers schedule the callback on their owning JavaScript runtime. The
  /// cancellation token can cancel the returned task before the callback starts.
  /// </remarks>
  /// <exception cref="ArgumentNullException">Thrown when <paramref name="action" /> is <see langword="null" />.</exception>
  /// <exception cref="ObjectDisposedException">Thrown after this buffer has been disposed.</exception>
  public Task WithBytesAsync(
      ArrayBufferBytesAction action,
      CancellationToken cancellationToken = default
  )
  {
    ArgumentNullException.ThrowIfNull(action);
    return WithBytesAsync<object?>(bytes =>
    {
      action(bytes);
      return null;
    }, cancellationToken);
  }

  /// <summary>Asynchronously invokes a function with writable buffer bytes.</summary>
  /// <remarks>
  /// JavaScript-backed buffers schedule the callback on their owning JavaScript runtime. The
  /// cancellation token can cancel the returned task before the callback starts.
  /// </remarks>
  /// <exception cref="ArgumentNullException">Thrown when <paramref name="action" /> is <see langword="null" />.</exception>
  /// <exception cref="ObjectDisposedException">Thrown after this buffer has been disposed.</exception>
  public Task<TResult> WithBytesAsync<TResult>(
      ArrayBufferBytesFunc<TResult> action,
      CancellationToken cancellationToken = default
  )
  {
    ArgumentNullException.ThrowIfNull(action);
    ThrowIfDisposed();
    if (nativeBacking is { } native)
    {
      return InvokeInlineAsync(() => native.WithBytes(bytes => action(bytes)), cancellationToken);
    }
    return javaScriptBacking!.WithBytesAsync(bytes => action(bytes), cancellationToken);
  }

  /// <summary>Asynchronously invokes an action with readable buffer bytes.</summary>
  /// <remarks>
  /// JavaScript-backed buffers schedule the callback on their owning JavaScript runtime. The
  /// cancellation token can cancel the returned task before the callback starts.
  /// </remarks>
  /// <exception cref="ArgumentNullException">Thrown when <paramref name="action" /> is <see langword="null" />.</exception>
  /// <exception cref="ObjectDisposedException">Thrown after this buffer has been disposed.</exception>
  public Task WithReadOnlyBytesAsync(
      ArrayBufferReadOnlyBytesAction action,
      CancellationToken cancellationToken = default
  )
  {
    ArgumentNullException.ThrowIfNull(action);
    return WithReadOnlyBytesAsync<object?>(bytes =>
    {
      action(bytes);
      return null;
    }, cancellationToken);
  }

  /// <summary>Asynchronously invokes a function with readable buffer bytes.</summary>
  /// <remarks>
  /// JavaScript-backed buffers schedule the callback on their owning JavaScript runtime. The
  /// cancellation token can cancel the returned task before the callback starts.
  /// </remarks>
  /// <exception cref="ArgumentNullException">Thrown when <paramref name="action" /> is <see langword="null" />.</exception>
  /// <exception cref="ObjectDisposedException">Thrown after this buffer has been disposed.</exception>
  public Task<TResult> WithReadOnlyBytesAsync<TResult>(
      ArrayBufferReadOnlyBytesFunc<TResult> action,
      CancellationToken cancellationToken = default
  )
  {
    ArgumentNullException.ThrowIfNull(action);
    ThrowIfDisposed();
    if (nativeBacking is { } native)
    {
      return InvokeInlineAsync(
          () => native.WithReadOnlyBytes(bytes => action(bytes)), cancellationToken);
    }
    return javaScriptBacking!.WithReadOnlyBytesAsync(
        bytes => action(bytes), cancellationToken);
  }

  internal JavaScriptValue Encode(JavaScriptRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ThrowIfDisposed();
    return javaScriptBacking is { } javaScript
        ? javaScript.AsValue(runtime)
        : nativeBacking!.AsValue(runtime);
  }

  /// <summary>Releases this wrapper's backing lease.</summary>
  /// <remarks>Calling this method more than once is safe and has no further effect.</remarks>
  public void Dispose()
  {
    var javaScript = Interlocked.Exchange(ref javaScriptBacking, null);
    var native = Interlocked.Exchange(ref nativeBacking, null);
    javaScript?.Dispose();
    native?.Dispose();
  }

  private static Task<TResult> InvokeInlineAsync<TResult>(
      Func<TResult> callback,
      CancellationToken cancellationToken
  )
  {
    if (cancellationToken.IsCancellationRequested)
    {
      return Task.FromCanceled<TResult>(cancellationToken);
    }
    try
    {
      return Task.FromResult(callback());
    }
    catch (Exception exception)
    {
      return Task.FromException<TResult>(exception);
    }
  }

  private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(
      javaScriptBacking is null && nativeBacking is null,
      this
  );
}
