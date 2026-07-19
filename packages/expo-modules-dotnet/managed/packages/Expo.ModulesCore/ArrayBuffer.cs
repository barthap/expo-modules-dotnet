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

  public int ByteLength
  {
    get
    {
      ThrowIfDisposed();
      return javaScriptBacking?.ByteLength ?? nativeBacking!.ByteLength;
    }
  }

  public static ArrayBuffer Allocate(int byteLength) =>
      new(JavaScriptMutableBuffer.Allocate(byteLength));

  public static ArrayBuffer CopyFrom(ReadOnlySpan<byte> bytes) =>
      new(JavaScriptMutableBuffer.CopyFrom(bytes));

  public ArrayBuffer Retain()
  {
    ThrowIfDisposed();
    return javaScriptBacking is { } javaScript
        ? new ArrayBuffer(javaScript.Retain())
        : new ArrayBuffer(nativeBacking!.Retain());
  }

  public void WithBytes(ArrayBufferBytesAction action)
  {
    ArgumentNullException.ThrowIfNull(action);
    WithBytes(bytes =>
    {
      action(bytes);
      return 0;
    });
  }

  public TResult WithBytes<TResult>(ArrayBufferBytesFunc<TResult> action)
  {
    ArgumentNullException.ThrowIfNull(action);
    ThrowIfDisposed();
    return javaScriptBacking is { } javaScript
        ? javaScript.WithBytes(bytes => action(bytes))
        : nativeBacking!.WithBytes(bytes => action(bytes));
  }

  public void WithReadOnlyBytes(ArrayBufferReadOnlyBytesAction action)
  {
    ArgumentNullException.ThrowIfNull(action);
    WithReadOnlyBytes(bytes =>
    {
      action(bytes);
      return 0;
    });
  }

  public TResult WithReadOnlyBytes<TResult>(ArrayBufferReadOnlyBytesFunc<TResult> action)
  {
    ArgumentNullException.ThrowIfNull(action);
    ThrowIfDisposed();
    return javaScriptBacking is { } javaScript
        ? javaScript.WithReadOnlyBytes(bytes => action(bytes))
        : nativeBacking!.WithReadOnlyBytes(bytes => action(bytes));
  }

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

  public ArrayBuffer Copy() =>
      new(JavaScriptMutableBuffer.CopyFrom(WithReadOnlyBytes(bytes => bytes.ToArray())));

  public async Task<ArrayBuffer> CopyAsync(CancellationToken cancellationToken = default)
  {
    var bytes = await ToArrayAsync(cancellationToken).ConfigureAwait(false);
    return CopyFrom(bytes);
  }

  public byte[] ToArray() => WithReadOnlyBytes(bytes => bytes.ToArray());

  public async Task<byte[]> ToArrayAsync(CancellationToken cancellationToken = default) =>
      await WithReadOnlyBytesAsync(bytes => bytes.ToArray(), cancellationToken).ConfigureAwait(false);

  internal JavaScriptValue Encode(JavaScriptRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ThrowIfDisposed();
    return javaScriptBacking is { } javaScript
        ? javaScript.AsValue(runtime)
        : nativeBacking!.AsValue(runtime);
  }

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
