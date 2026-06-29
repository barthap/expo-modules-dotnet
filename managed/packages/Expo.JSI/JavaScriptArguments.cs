using Expo.JSI.Interop;
using Expo.JSI.Internal;

namespace Expo.JSI;

/// <summary>
/// Provides access to JavaScript arguments during a managed host-function callback.
/// </summary>
/// <remarks>
/// Argument values are scoped to the callback invocation. Use <see cref="GetValue" /> to inspect an
/// argument without creating an owned wrapper, and call <see cref="JavaScriptValueRef.Retain" /> if
/// a value must escape the callback.
/// </remarks>
public readonly struct JavaScriptArguments
{
  private readonly JsiContext context;
  private readonly ExpoJsiArgumentsHandle handle;

  internal JavaScriptArguments(JsiContext context, ExpoJsiArgumentsHandle handle)
  {
    this.context = context;
    this.handle = handle;
  }

  /// <summary>
  /// Gets the number of JavaScript arguments.
  /// </summary>
  public uint Count
  {
    get
    {
      ThrowIfNull();
      unsafe
      {
        ExpoJsiError error;
        var count = context.Api->GetArgumentCount(context.RuntimeHandle, handle, &error);
        context.ThrowIfError(error, "Failed to read JavaScript argument count.");
        return count;
      }
    }
  }

  /// <summary>
  /// Gets an argument as a scoped JavaScript value ref.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptValueRef" /> is valid only during the active host-function
  /// callback. It does not own the argument handle.
  /// </remarks>
  public JavaScriptValueRef GetValue(uint index)
  {
    ThrowIfNull();
    unsafe
    {
      var result = context.Api->GetArgument(context.RuntimeHandle, handle, index);
      if (result.Ok == 0 || result.Value == 0)
      {
        JsiContext.ThrowNativeError(result.Error, "Failed to read JavaScript argument.");
      }
      var scope = JavaScriptHandleScope.CurrentFor(context);
      return JavaScriptValueRef.FromBorrowedRoot(
          scope,
          new JavaScriptValueInner(context, result.Value)
      );
    }
  }

  private void ThrowIfNull()
  {
    if (handle == 0)
    {
      throw new ObjectDisposedException(nameof(JavaScriptArguments));
    }
  }
}
