using Expo.JSI.Interop;
using Expo.JSI.Internal;

namespace Expo.JSI;

/// <summary>
/// Owns a JavaScript value handle.
/// </summary>
/// <remarks>
/// Dispose this wrapper when the value handle is no longer needed. Conversions such as
  /// <see cref="AsObject" />, <see cref="AsArray" />, <see cref="AsFunction" />, and
  /// <see cref="AsValue" /> create new owned wrappers that must be disposed independently. Use
  /// <see cref="Ref" /> for scoped temporary inspection without disposable intermediates.
/// </remarks>
public sealed class JavaScriptValue : IJavaScriptValueRepresentable, IDisposable
{
  private readonly JsiContext context;
  private ExpoJsiValueHandle handle;

  private JavaScriptValue(JsiContext context, ExpoJsiValueHandle handle)
  {
    this.context = context;
    this.handle = handle;
  }

  internal static JavaScriptValue FromOwnedHandle(JsiContext context, ExpoJsiValueHandle handle) =>
    new(context, handle);

  internal ExpoJsiValueHandle Handle
  {
    get
    {
      ThrowIfDisposed();
      return handle;
    }
  }

  private JavaScriptValueInner Inner
  {
    get
    {
      ThrowIfDisposed();
      return new JavaScriptValueInner(context, handle);
    }
  }

  /// <summary>
  /// Gets the JavaScript value kind.
  /// </summary>
  public JavaScriptValueKind Kind => Inner.Kind;

  /// <summary>
  /// Gets whether this value is a JavaScript Promise.
  /// </summary>
  public bool IsPromise => Inner.IsPromise;

  /// <summary>
  /// Gets whether this value is a JavaScript Error object.
  /// </summary>
  public bool IsError => Inner.IsError;

  /// <summary>
  /// Gets whether this value is a JavaScript boolean.
  /// </summary>
  public bool IsBool => Kind == JavaScriptValueKind.Bool;

  /// <summary>
  /// Gets whether this value is JavaScript <c>null</c> or <c>undefined</c>.
  /// </summary>
  public bool IsNullish => Inner.IsNullish;

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

  internal string CoerceToString() => Inner.CoerceToString();

  /// <summary>
  /// Gets whether this value is a JavaScript object.
  /// </summary>
  public bool IsObject => Kind == JavaScriptValueKind.Object;

  /// <summary>
  /// Converts this value to an owned JavaScript object wrapper.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptObject" /> must be disposed independently. Use
  /// <see cref="Ref" /> followed by <see cref="JavaScriptValueRef.AsObject" /> for scoped temporary
  /// object traversal.
  /// </remarks>
  public JavaScriptObject AsObject() => new(context, Inner.AsObject());

  /// <summary>
  /// Converts this value to an owned JavaScript array wrapper.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptArray" /> must be disposed independently. Use
  /// <see cref="Ref" /> followed by <see cref="JavaScriptValueRef.AsArray" /> for scoped temporary
  /// array traversal.
  /// </remarks>
  public JavaScriptArray AsArray() => new(context, Inner.AsArray());

  /// <summary>
  /// Converts this value to an owned JavaScript function wrapper.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptFunction" /> must be disposed independently.
  /// </remarks>
  public JavaScriptFunction AsFunction() => new(context, Inner.AsFunction());

  /// <summary>
  /// Wraps this value as an owned JavaScript promise value.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptPromiseValue" /> must be disposed independently.
  /// </remarks>
  public JavaScriptPromiseValue AsPromiseValue()
  {
    ThrowIfDisposed();
    if (!IsPromise)
    {
      throw new InvalidOperationException("Value is not a JavaScript Promise.");
    }
    return new JavaScriptPromiseValue(AsValue());
  }

  /// <summary>
  /// Wraps this value as an owned JavaScript error object.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptErrorObject" /> must be disposed independently.
  /// </remarks>
  public JavaScriptErrorObject AsErrorObject()
  {
    ThrowIfDisposed();
    if (!IsError)
    {
      throw new InvalidOperationException("Value is not a JavaScript Error object.");
    }
    return new JavaScriptErrorObject(AsValue());
  }

  /// <summary>
  /// Clones this value into a new owned JavaScript value wrapper.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptValue" /> must be disposed independently.
  /// </remarks>
  public JavaScriptValue AsValue() => FromOwnedHandle(context, Inner.Retain());

  /// <summary>
  /// Clones this value into a new owned JavaScript value wrapper.
  /// </summary>
  /// <remarks>
  /// Equivalent to <see cref="AsValue" />. Use this when converting from scoped ref vocabulary back
  /// to an escaping owned value.
  /// </remarks>
  public JavaScriptValue Retain() => AsValue();

  /// <summary>
  /// Gets a scoped, non-disposable reference to this value.
  /// </summary>
  /// <remarks>
  /// The returned ref is valid only during the active JavaScript runtime access frame. It does not
  /// own this value handle. Call <see cref="JavaScriptValueRef.Retain" /> to create an owned value
  /// that can escape the current frame.
  /// </remarks>
  public JavaScriptValueRef Ref =>
    JavaScriptValueRef.FromBorrowedRoot(
        JavaScriptHandleScope.CurrentFor(context),
        Inner
    );

  /// <summary>
  /// Releases the owned native value handle.
  /// </summary>
  public void Dispose()
  {
    if (handle != 0)
    {
      unsafe
      {
        context.Api->ReleaseValueHandle(context.RuntimeHandle, handle);
      }
    }
    handle = 0;
  }

  /// <summary>
  /// Transfers the native value handle out of this wrapper.
  /// </summary>
  /// <remarks>
  /// After calling this method, this wrapper is disposed and must not be used again. The caller is
  /// responsible for handing the returned handle to code that will release it exactly once.
  /// </remarks>
  public ExpoJsiValueHandle Detach()
  {
    ThrowIfDisposed();
    var detached = handle;
    handle = 0;
    return detached;
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(handle == 0, this);
  }
}
