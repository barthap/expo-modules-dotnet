using Expo.JSI.Internal;

namespace Expo.JSI;

/// <summary>
/// Scoped, non-disposable reference to a JavaScript object.
/// </summary>
/// <remarks>
/// Object refs are valid only during the active JavaScript runtime access frame. Property reads
/// return scoped value refs. Use <see cref="Retain" /> or <see cref="RetainAsValue" /> to create
/// owned wrappers that can escape the frame.
/// </remarks>
public readonly ref struct JavaScriptObjectRef
{
  private readonly JavaScriptHandleScope? scope;
  private readonly JavaScriptObjectInner inner;

  private JavaScriptObjectRef(JavaScriptHandleScope scope, JavaScriptObjectInner inner)
  {
    this.scope = scope;
    this.inner = inner;
  }

  internal static JavaScriptObjectRef FromScopedHandle(
      JavaScriptHandleScope scope,
      JsiContext context,
      ExpoJsiValueHandle handle
  ) => new(scope, new JavaScriptObjectInner(context, scope.TrackValue(handle)));

  /// <summary>
  /// Gets a property as a scoped JavaScript value ref.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptValueRef" /> is valid only during the same active runtime
  /// access frame.
  /// </remarks>
  public JavaScriptValueRef GetProperty(string name)
  {
    var handle = Inner.GetProperty(name);
    return JavaScriptValueRef.FromScopedHandle(Scope, Inner.Context, handle);
  }

  /// <summary>
  /// Gets this object's own property names as managed strings.
  /// </summary>
  public IReadOnlyList<string> GetOwnPropertyNames() => Inner.GetOwnPropertyNames();

  /// <summary>
  /// Retains this object ref as an owned JavaScript object wrapper.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptObject" /> must be disposed by the caller and may outlive the
  /// current scoped ref frame.
  /// </remarks>
  public JavaScriptObject Retain()
  {
    using var value = RetainAsValue();
    return value.AsObject();
  }

  /// <summary>
  /// Retains this object ref as an owned JavaScript value wrapper.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptValue" /> must be disposed by the caller and may outlive the
  /// current scoped ref frame.
  /// </remarks>
  public JavaScriptValue RetainAsValue() =>
    JavaScriptValue.FromOwnedHandle(Inner.Context, Inner.AsValue());

  private JavaScriptObjectInner Inner
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
