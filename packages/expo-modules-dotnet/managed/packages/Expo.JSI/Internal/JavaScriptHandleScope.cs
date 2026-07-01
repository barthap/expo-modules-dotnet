using Expo.JSI.Interop;

namespace Expo.JSI.Internal;

/// <summary>
/// Tracks temporary native handles created while scoped JavaScript refs are active.
/// </summary>
/// <remarks>
/// <para>
/// A handle scope is entered for each managed runtime access frame, such as
/// <see cref="JavaScriptRuntime.Execute{T}" />, scheduled runtime work, or a host-function
/// callback. Scoped refs use the current scope as their lifetime marker.
/// </para>
/// <para>
/// The scope does not own the root handle behind an existing owned wrapper or host-function
/// argument. It only tracks temporary handles produced by traversal operations, such as reading an
/// object property or converting a scoped value ref to a scoped object/array ref. Those temporary
/// handles are released when the scope is disposed.
/// </para>
/// </remarks>
internal sealed unsafe class JavaScriptHandleScope : IDisposable
{
  [ThreadStatic]
  private static JavaScriptHandleScope? current;

  private readonly JsiContext context;
  private readonly JavaScriptHandleScope? previous;
  private List<ExpoJsiValueHandle>? values;
  private bool disposed;

  private JavaScriptHandleScope(JsiContext context, JavaScriptHandleScope? previous)
  {
    this.context = context;
    this.previous = previous;
  }

  /// <summary>
  /// Enters a new handle scope for the supplied runtime context.
  /// </summary>
  /// <remarks>
  /// Scopes are thread-local and must be disposed in strict stack order. The returned scope should
  /// be used with <c>using</c> around the runtime access frame that creates scoped refs.
  /// </remarks>
  public static JavaScriptHandleScope Enter(JsiContext context)
  {
    var scope = new JavaScriptHandleScope(context, current);
    current = scope;
    return scope;
  }

  /// <summary>
  /// Gets the current handle scope for a runtime context.
  /// </summary>
  /// <remarks>
  /// This is used when creating a ref from an owned wrapper or callback argument. It verifies that
  /// managed code is currently inside a runtime access frame for the same native runtime.
  /// </remarks>
  /// <exception cref="InvalidOperationException">
  /// Thrown when there is no active scope for <paramref name="context" />.
  /// </exception>
  public static JavaScriptHandleScope CurrentFor(JsiContext context)
  {
    var scope = current;
    if (scope is null || scope.disposed || scope.context.Api != context.Api
        || scope.context.RuntimeHandle != context.RuntimeHandle)
    {
      throw new InvalidOperationException(
          "Scoped JavaScript refs require active runtime access."
      );
    }
    return scope;
  }

  /// <summary>
  /// Registers a temporary value handle to release when the scope exits.
  /// </summary>
  /// <remarks>
  /// Passing a zero handle is allowed and is treated as a no-op. The returned handle is the same
  /// handle that was passed in, so callers can inline tracking at construction sites.
  /// </remarks>
  public ExpoJsiValueHandle TrackValue(ExpoJsiValueHandle handle)
  {
    ThrowIfDisposed();
    if (handle != 0)
    {
      values ??= [];
      values.Add(handle);
    }
    return handle;
  }

  /// <summary>
  /// Leaves the current handle scope and releases all tracked temporary handles.
  /// </summary>
  /// <remarks>
  /// Handles are released in reverse tracking order. Disposing out of stack order is an error
  /// because it would make scoped ref lifetimes ambiguous.
  /// </remarks>
  public void Dispose()
  {
    if (!ReferenceEquals(current, this))
    {
      throw new InvalidOperationException("JavaScript handle scopes must be disposed in stack order.");
    }

    current = previous;
    if (disposed)
    {
      return;
    }

    disposed = true;
    if (values is not null)
    {
      for (var index = values.Count - 1; index >= 0; index--)
      {
        context.Api->ReleaseValueHandle(context.RuntimeHandle, values[index]);
      }
    }
  }

  private void ThrowIfDisposed()
  {
    if (disposed)
    {
      throw new ObjectDisposedException(nameof(JavaScriptHandleScope));
    }
  }
}
