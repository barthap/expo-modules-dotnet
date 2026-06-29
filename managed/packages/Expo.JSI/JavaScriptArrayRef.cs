using Expo.JSI.Internal;

namespace Expo.JSI;

/// <summary>
/// Scoped, non-disposable reference to a JavaScript array.
/// </summary>
/// <remarks>
/// Array refs are valid only during the active JavaScript runtime access frame. Indexed reads
/// return scoped value refs. Use <see cref="Retain" /> or <see cref="RetainAsValue" /> to create
/// owned wrappers that can escape the frame.
/// </remarks>
public readonly ref struct JavaScriptArrayRef
{
  private readonly JavaScriptHandleScope? scope;
  private readonly JavaScriptArrayInner inner;

  private JavaScriptArrayRef(JavaScriptHandleScope scope, JavaScriptArrayInner inner)
  {
    this.scope = scope;
    this.inner = inner;
  }

  internal static JavaScriptArrayRef FromScopedHandle(
      JavaScriptHandleScope scope,
      JsiContext context,
      ExpoJsiValueHandle handle
  ) => new(scope, new JavaScriptArrayInner(context, scope.TrackValue(handle)));

  /// <summary>
  /// Gets the current JavaScript array length.
  /// </summary>
  public uint Length => Inner.Length;

  /// <summary>
  /// Gets an array element as a scoped JavaScript value ref.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptValueRef" /> is valid only during the same active runtime
  /// access frame.
  /// </remarks>
  public JavaScriptValueRef GetValue(uint index)
  {
    var handle = Inner.GetValue(index);
    return JavaScriptValueRef.FromScopedHandle(Scope, Inner.Context, handle);
  }

  /// <summary>
  /// Retains this array ref as an owned JavaScript array wrapper.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptArray" /> must be disposed by the caller and may outlive the
  /// current scoped ref frame.
  /// </remarks>
  public JavaScriptArray Retain()
  {
    using var value = RetainAsValue();
    return value.AsArray();
  }

  /// <summary>
  /// Retains this array ref as an owned JavaScript value wrapper.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptValue" /> must be disposed by the caller and may outlive the
  /// current scoped ref frame.
  /// </remarks>
  public JavaScriptValue RetainAsValue() =>
    JavaScriptValue.FromOwnedHandle(Inner.Context, Inner.AsValue());

  private JavaScriptArrayInner Inner
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
