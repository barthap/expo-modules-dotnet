using Expo.JSI.Interop;
using Expo.JSI.Internal;

namespace Expo.JSI;

/// <summary>
/// Owns a JavaScript function value handle.
/// </summary>
/// <remarks>
/// Dispose this wrapper when the function handle is no longer needed. Use <see cref="AsValue" />
/// to create an owned value wrapper for storing or passing the function as a JavaScript value.
/// </remarks>
public sealed class JavaScriptFunction : IJavaScriptValueRepresentable, IDisposable
{
  private readonly JsiContext context;
  private ExpoJsiValueHandle handle;

  internal JavaScriptFunction(JsiContext context, ExpoJsiValueHandle handle)
  {
    this.context = context;
    this.handle = handle;
  }

  /// <summary>
  /// Clones this function as an owned JavaScript value handle.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptValue" /> must be disposed independently. Disposing it does
  /// not dispose this function wrapper.
  /// </remarks>
  public JavaScriptValue AsValue()
  {
    ThrowIfDisposed();
    unsafe
    {
      var result = context.Api->RetainValueAs(
          context.RuntimeHandle,
          handle,
          ExpoJsiValueExpectation.Function
      );
      if (!result.IsOk)
      {
        JsiContext.ThrowNativeError(
            result.Error,
            "Failed to retain JavaScript function value."
        );
      }
      return JavaScriptValue.FromOwnedHandle(context, result.Value);
    }
  }

  /// <summary>
  /// Calls this JavaScript function with JavaScript <c>undefined</c> as <c>this</c>.
  /// </summary>
  /// <remarks>
  /// Arguments are cloned into temporary owned values for the duration of the call. Ownership of the
  /// source wrappers stays with the caller.
  /// </remarks>
  public JavaScriptValue Call(params IJavaScriptValueRepresentable[] arguments)
  {
    ArgumentNullException.ThrowIfNull(arguments);
    return Call(arguments.AsSpan());
  }

  /// <summary>
  /// Calls this JavaScript function with JavaScript <c>undefined</c> as <c>this</c>.
  /// </summary>
  /// <remarks>
  /// Arguments are cloned into temporary owned values for the duration of the call. Ownership of the
  /// source wrappers stays with the caller.
  /// </remarks>
  public JavaScriptValue Call(ReadOnlySpan<IJavaScriptValueRepresentable> arguments)
  {
    ThrowIfDisposed();
    using var callArguments = JavaScriptCallArguments.FromRepresentables(arguments);
    unsafe
    {
      var result = context.Api->CallFunction(
          context.RuntimeHandle,
          handle,
          callArguments.Handles
      );
      if (!result.IsOk)
      {
        JsiContext.ThrowNativeError(result.Error, "Failed to call JavaScript function.");
      }

      return JavaScriptValue.FromOwnedHandle(context, result.Value);
    }
  }

  /// <summary>
  /// Calls this JavaScript function with an explicit object as <c>this</c>.
  /// </summary>
  public JavaScriptValue CallWithThis(
      JavaScriptObject thisValue,
      params IJavaScriptValueRepresentable[] arguments)
  {
    ArgumentNullException.ThrowIfNull(arguments);
    return CallWithThis(thisValue, arguments.AsSpan());
  }

  /// <summary>
  /// Calls this JavaScript function with an explicit object as <c>this</c>.
  /// </summary>
  public JavaScriptValue CallWithThis(
      JavaScriptObject thisValue,
      ReadOnlySpan<IJavaScriptValueRepresentable> arguments)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(thisValue);
    using var receiver = thisValue.AsValue();
    using var callArguments = JavaScriptCallArguments.FromRepresentables(arguments);
    unsafe
    {
      var result = context.Api->CallFunctionWithThis(
          context.RuntimeHandle,
          handle,
          receiver.Handle,
          callArguments.Handles
      );
      if (!result.IsOk)
      {
        JsiContext.ThrowNativeError(result.Error, "Failed to call JavaScript function with this.");
      }

      return JavaScriptValue.FromOwnedHandle(context, result.Value);
    }
  }

  /// <summary>
  /// Calls this JavaScript function as a constructor.
  /// </summary>
  public JavaScriptValue CallAsConstructor(params IJavaScriptValueRepresentable[] arguments)
  {
    ArgumentNullException.ThrowIfNull(arguments);
    return CallAsConstructor(arguments.AsSpan());
  }

  /// <summary>
  /// Calls this JavaScript function as a constructor.
  /// </summary>
  public JavaScriptValue CallAsConstructor(ReadOnlySpan<IJavaScriptValueRepresentable> arguments)
  {
    ThrowIfDisposed();
    using var callArguments = JavaScriptCallArguments.FromRepresentables(arguments);
    unsafe
    {
      var result = context.Api->CallFunctionAsConstructor(
          context.RuntimeHandle,
          handle,
          callArguments.Handles
      );
      if (!result.IsOk)
      {
        JsiContext.ThrowNativeError(
            result.Error,
            "Failed to call JavaScript function as constructor."
        );
      }

      return JavaScriptValue.FromOwnedHandle(context, result.Value);
    }
  }

  /// <summary>
  /// Releases the owned native function value handle.
  /// </summary>
  public void Dispose()
  {
    if (handle != 0)
    {
      unsafe
      {
        context.Api->ReleaseValueHandle(context.RuntimeHandle, handle);
      }
      handle = 0;
    }
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(handle == 0, this);
  }

  private sealed class JavaScriptCallArguments : IDisposable
  {
    private readonly JavaScriptValue[] values;

    private JavaScriptCallArguments(JavaScriptValue[] values)
    {
      this.values = values;
      Handles = new ExpoJsiValueHandle[values.Length];
      for (var index = 0; index < values.Length; index++)
      {
        Handles[index] = values[index].Handle;
      }
    }

    public ExpoJsiValueHandle[] Handles { get; }

    public static JavaScriptCallArguments FromRepresentables(
        ReadOnlySpan<IJavaScriptValueRepresentable> arguments)
    {
      var values = new JavaScriptValue[arguments.Length];
      try
      {
        for (var index = 0; index < arguments.Length; index++)
        {
          var argument = arguments[index] ??
              throw new ArgumentNullException(nameof(arguments), "Function call argument is null.");
          values[index] = argument.AsValue();
        }
        return new JavaScriptCallArguments(values);
      }
      catch
      {
        foreach (var value in values)
        {
          value?.Dispose();
        }
        throw;
      }
    }

    public void Dispose()
    {
      foreach (var value in values)
      {
        value.Dispose();
      }
    }
  }
}
