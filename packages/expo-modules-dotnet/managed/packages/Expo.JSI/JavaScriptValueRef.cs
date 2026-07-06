using Expo.JSI.Internal;

namespace Expo.JSI;

/// <summary>
/// Scoped, non-disposable reference to a JavaScript value.
/// </summary>
/// <remarks>
/// A value ref is valid only during the active JavaScript runtime access frame, such as an
/// <see cref="JavaScriptRuntime.Execute{T}" /> body or a host-function callback. It does not own
/// the underlying handle. Use <see cref="Retain" /> to create an owned <see cref="JavaScriptValue" />
/// that can escape the current frame.
/// </remarks>
public readonly ref struct JavaScriptValueRef
{
  private readonly JavaScriptHandleScope? scope;
  private readonly JavaScriptValueInner inner;

  private JavaScriptValueRef(JavaScriptHandleScope scope, JavaScriptValueInner inner)
  {
    this.scope = scope;
    this.inner = inner;
  }

  internal static JavaScriptValueRef FromBorrowedRoot(
      JavaScriptHandleScope scope,
      JavaScriptValueInner inner
  ) => new(scope, inner);

  internal static JavaScriptValueRef FromScopedHandle(
      JavaScriptHandleScope scope,
      JsiContext context,
      ExpoJsiValueHandle handle
  ) => new(scope, new JavaScriptValueInner(context, scope.TrackValue(handle)));

  /// <summary>
  /// Gets the JavaScript value kind.
  /// </summary>
  public JavaScriptValueKind Kind => Inner.Kind;

  /// <summary>
  /// Gets whether this value is JavaScript <c>null</c> or <c>undefined</c>.
  /// </summary>
  public bool IsNullish => Inner.IsNullish;

  /// <summary>
  /// Gets whether this value is a JavaScript boolean.
  /// </summary>
  public bool IsBool => Kind == JavaScriptValueKind.Bool;

  /// <summary>
  /// Reads this value as a boolean.
  /// </summary>
  public bool AsBool() => Inner.AsBool();

  /// <summary>
  /// Gets whether this value is a JavaScript number.
  /// </summary>
  public bool IsDouble => Kind == JavaScriptValueKind.Number;

  /// <summary>
  /// Reads this value as a double.
  /// </summary>
  public double AsDouble() => Inner.AsDouble();

  /// <summary>
  /// Gets whether this value is a JavaScript string.
  /// </summary>
  public bool IsString => Kind == JavaScriptValueKind.String;

  /// <summary>
  /// Reads this value as a string.
  /// </summary>
  public string AsString() => Inner.AsString();

  /// <summary>
  /// Coerces this value to a JavaScript string and returns the resulting text.
  /// </summary>
  public string CoerceToString() => Inner.CoerceToString();

  /// <summary>
  /// Gets whether this value is a JavaScript object.
  /// </summary>
  public bool IsObject => Kind == JavaScriptValueKind.Object;

  /// <summary>
  /// Gets whether this value is a JavaScript function.
  /// </summary>
  public bool IsFunction => Kind == JavaScriptValueKind.Function;

  /// <summary>
  /// Converts this value ref to a scoped object ref.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptObjectRef" /> is valid only during the same active runtime
  /// access frame.
  /// </remarks>
  public JavaScriptObjectRef AsObject()
  {
    var objectHandle = Inner.AsObject();
    return JavaScriptObjectRef.FromScopedHandle(Scope, Inner.Context, objectHandle);
  }

  /// <summary>
  /// Converts this value ref to a scoped array ref.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptArrayRef" /> is valid only during the same active runtime
  /// access frame.
  /// </remarks>
  public JavaScriptArrayRef AsArray()
  {
    var arrayHandle = Inner.AsArray();
    return JavaScriptArrayRef.FromScopedHandle(Scope, Inner.Context, arrayHandle);
  }

  /// <summary>
  /// Converts this value ref to an owned JavaScript function wrapper.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptFunction" /> must be disposed independently and may outlive
  /// the current scoped ref frame.
  /// </remarks>
  public JavaScriptFunction AsFunction() => new(Inner.Context, Inner.AsFunction());

  /// <summary>
  /// Clones this ref into an owned JavaScript value wrapper.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptValue" /> must be disposed by the caller and may outlive the
  /// current scoped ref frame.
  /// </remarks>
  public JavaScriptValue Retain() =>
    JavaScriptValue.FromOwnedHandle(Inner.Context, Inner.Retain());

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
