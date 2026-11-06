namespace Expo.ModulesCore;

/// <summary>A view of bytes over an owned <see cref="ArrayBuffer" />, accepted by generated Expo module bindings.</summary>
/// <remarks>
/// Each instance owns one <see cref="ArrayBuffer" /> lease plus a byte offset and a length.
/// <see cref="Dispose" /> atomically releases that lease and is idempotent, but does not
/// synchronize disposal with active use. Call <see cref="Retain" /> to give a concurrent consumer
/// independent ownership. The backing wrappers have no finalizers; callers must dispose this
/// wrapper promptly.
/// </remarks>
public sealed class Uint8Array : IDisposable
{
  private ArrayBuffer? buffer;
  private readonly int byteOffset;
  private readonly int length;

  /// <summary>Creates a view over <paramref name="buffer" />; the view owns the buffer only when this constructor succeeds.</summary>
  /// <exception cref="ArgumentNullException">Thrown when <paramref name="buffer" /> is <see langword="null" />.</exception>
  /// <exception cref="ArgumentOutOfRangeException">
  /// Thrown when an argument is negative or the view extends past the end of the buffer.
  /// </exception>
  internal Uint8Array(ArrayBuffer buffer, int byteOffset, int length)
  {
    ArgumentNullException.ThrowIfNull(buffer);
    ArgumentOutOfRangeException.ThrowIfNegative(byteOffset);
    ArgumentOutOfRangeException.ThrowIfNegative(length);
    if ((long)byteOffset + length > buffer.ByteLength)
    {
      throw new ArgumentOutOfRangeException(
          nameof(length),
          "The view extends past the end of its buffer."
      );
    }

    this.buffer = buffer;
    this.byteOffset = byteOffset;
    this.length = length;
  }

  /// <summary>Gets the buffer that holds this view's bytes.</summary>
  /// <remarks>The view owns the returned buffer. Do not dispose it; dispose the view instead.</remarks>
  /// <exception cref="ObjectDisposedException">Thrown after this view has been disposed.</exception>
  public ArrayBuffer Buffer
  {
    get
    {
      ThrowIfDisposed();
      return buffer!;
    }
  }

  /// <summary>Gets the offset in bytes from the start of <see cref="Buffer" /> to the first byte of this view.</summary>
  /// <exception cref="ObjectDisposedException">Thrown after this view has been disposed.</exception>
  public int ByteOffset
  {
    get
    {
      ThrowIfDisposed();
      return byteOffset;
    }
  }

  /// <summary>Gets the number of bytes in this view.</summary>
  /// <exception cref="ObjectDisposedException">Thrown after this view has been disposed.</exception>
  public int Length
  {
    get
    {
      ThrowIfDisposed();
      return length;
    }
  }

  /// <summary>Allocates a zero-filled native-backed view with the requested length.</summary>
  /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="length" /> is negative.</exception>
  public static Uint8Array Allocate(int length) => new(ArrayBuffer.Allocate(length), 0, length);

  /// <summary>Copies the supplied bytes into a new native-backed view.</summary>
  /// <remarks>The new view has storage independent from the supplied span.</remarks>
  public static Uint8Array CopyFrom(ReadOnlySpan<byte> bytes) =>
      new(ArrayBuffer.CopyFrom(bytes), 0, bytes.Length);

  /// <summary>Creates an independently owned view over the same backing storage.</summary>
  /// <remarks>
  /// The returned view owns a retained lease and must be disposed independently. Changes to the
  /// shared storage remain visible through both views.
  /// </remarks>
  /// <exception cref="ObjectDisposedException">Thrown after this view has been disposed.</exception>
  public Uint8Array Retain()
  {
    ThrowIfDisposed();
    var retained = buffer!.Retain();
    try
    {
      return new Uint8Array(retained, byteOffset, length);
    }
    catch
    {
      retained.Dispose();
      throw;
    }
  }

  /// <summary>Copies this view's bytes into an independent managed array.</summary>
  /// <exception cref="ObjectDisposedException">Thrown after this view has been disposed.</exception>
  public byte[] ToArray() => WithReadOnlyBytes(bytes => bytes.ToArray());

  /// <summary>Asynchronously copies this view's bytes into an independent managed array.</summary>
  /// <remarks>
  /// JavaScript-backed views schedule work on their owning JavaScript runtime. The cancellation
  /// token can cancel the returned task before its callback starts.
  /// </remarks>
  /// <exception cref="ObjectDisposedException">Thrown after this view has been disposed.</exception>
  public async Task<byte[]> ToArrayAsync(CancellationToken cancellationToken = default) =>
      await WithReadOnlyBytesAsync(bytes => bytes.ToArray(), cancellationToken).ConfigureAwait(false);

  /// <summary>Invokes an action with this view's writable bytes borrowed for the synchronous callback.</summary>
  /// <remarks>The supplied span is valid only until the callback returns.</remarks>
  /// <exception cref="ArgumentNullException">Thrown when <paramref name="action" /> is <see langword="null" />.</exception>
  /// <exception cref="ObjectDisposedException">Thrown after this view has been disposed.</exception>
  public void WithBytes(ArrayBufferBytesAction action)
  {
    ArgumentNullException.ThrowIfNull(action);
    WithBytes(bytes =>
    {
      action(bytes);
      return 0;
    });
  }

  /// <summary>Invokes a function with this view's writable bytes borrowed for the synchronous callback.</summary>
  /// <remarks>The supplied span is valid only until the callback returns.</remarks>
  /// <exception cref="ArgumentNullException">Thrown when <paramref name="action" /> is <see langword="null" />.</exception>
  /// <exception cref="ObjectDisposedException">Thrown after this view has been disposed.</exception>
  public TResult WithBytes<TResult>(ArrayBufferBytesFunc<TResult> action)
  {
    ArgumentNullException.ThrowIfNull(action);
    ThrowIfDisposed();
    return buffer!.WithBytes(bytes => action(bytes.Slice(byteOffset, length)));
  }

  /// <summary>Invokes an action with this view's readable bytes borrowed for the synchronous callback.</summary>
  /// <remarks>The supplied span is valid only until the callback returns.</remarks>
  /// <exception cref="ArgumentNullException">Thrown when <paramref name="action" /> is <see langword="null" />.</exception>
  /// <exception cref="ObjectDisposedException">Thrown after this view has been disposed.</exception>
  public void WithReadOnlyBytes(ArrayBufferReadOnlyBytesAction action)
  {
    ArgumentNullException.ThrowIfNull(action);
    WithReadOnlyBytes(bytes =>
    {
      action(bytes);
      return 0;
    });
  }

  /// <summary>Invokes a function with this view's readable bytes borrowed for the synchronous callback.</summary>
  /// <remarks>The supplied span is valid only until the callback returns.</remarks>
  /// <exception cref="ArgumentNullException">Thrown when <paramref name="action" /> is <see langword="null" />.</exception>
  /// <exception cref="ObjectDisposedException">Thrown after this view has been disposed.</exception>
  public TResult WithReadOnlyBytes<TResult>(ArrayBufferReadOnlyBytesFunc<TResult> action)
  {
    ArgumentNullException.ThrowIfNull(action);
    ThrowIfDisposed();
    return buffer!.WithReadOnlyBytes(bytes => action(bytes.Slice(byteOffset, length)));
  }

  /// <summary>Asynchronously invokes an action with this view's writable bytes.</summary>
  /// <remarks>
  /// JavaScript-backed views schedule the callback on their owning JavaScript runtime. The
  /// cancellation token can cancel the returned task before the callback starts.
  /// </remarks>
  /// <exception cref="ArgumentNullException">Thrown when <paramref name="action" /> is <see langword="null" />.</exception>
  /// <exception cref="ObjectDisposedException">Thrown after this view has been disposed.</exception>
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

  /// <summary>Asynchronously invokes a function with this view's writable bytes.</summary>
  /// <remarks>
  /// JavaScript-backed views schedule the callback on their owning JavaScript runtime. The
  /// cancellation token can cancel the returned task before the callback starts.
  /// </remarks>
  /// <exception cref="ArgumentNullException">Thrown when <paramref name="action" /> is <see langword="null" />.</exception>
  /// <exception cref="ObjectDisposedException">Thrown after this view has been disposed.</exception>
  public Task<TResult> WithBytesAsync<TResult>(
      ArrayBufferBytesFunc<TResult> action,
      CancellationToken cancellationToken = default
  )
  {
    ArgumentNullException.ThrowIfNull(action);
    ThrowIfDisposed();
    var offset = byteOffset;
    var count = length;
    return buffer!.WithBytesAsync(bytes => action(bytes.Slice(offset, count)), cancellationToken);
  }

  /// <summary>Asynchronously invokes an action with this view's readable bytes.</summary>
  /// <remarks>
  /// JavaScript-backed views schedule the callback on their owning JavaScript runtime. The
  /// cancellation token can cancel the returned task before the callback starts.
  /// </remarks>
  /// <exception cref="ArgumentNullException">Thrown when <paramref name="action" /> is <see langword="null" />.</exception>
  /// <exception cref="ObjectDisposedException">Thrown after this view has been disposed.</exception>
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

  /// <summary>Asynchronously invokes a function with this view's readable bytes.</summary>
  /// <remarks>
  /// JavaScript-backed views schedule the callback on their owning JavaScript runtime. The
  /// cancellation token can cancel the returned task before the callback starts.
  /// </remarks>
  /// <exception cref="ArgumentNullException">Thrown when <paramref name="action" /> is <see langword="null" />.</exception>
  /// <exception cref="ObjectDisposedException">Thrown after this view has been disposed.</exception>
  public Task<TResult> WithReadOnlyBytesAsync<TResult>(
      ArrayBufferReadOnlyBytesFunc<TResult> action,
      CancellationToken cancellationToken = default
  )
  {
    ArgumentNullException.ThrowIfNull(action);
    ThrowIfDisposed();
    var offset = byteOffset;
    var count = length;
    return buffer!.WithReadOnlyBytesAsync(
        bytes => action(bytes.Slice(offset, count)), cancellationToken);
  }

  /// <summary>Releases this view's buffer lease.</summary>
  /// <remarks>Calling this method more than once is safe and has no further effect.</remarks>
  public void Dispose() => Interlocked.Exchange(ref buffer, null)?.Dispose();

  private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(buffer is null, this);
}
