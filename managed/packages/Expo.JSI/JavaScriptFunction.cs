namespace Expo.JSI;

public sealed class JavaScriptFunction : IDisposable
{
    private readonly JsiContext context;
    private ExpoJsiFunctionHandle handle;

    internal JavaScriptFunction(JsiContext context, ExpoJsiFunctionHandle handle)
    {
        this.context = context;
        this.handle = handle;
    }

    public JavaScriptValue AsValue()
    {
        ThrowIfDisposed();
        unsafe
        {
            var result = context.Api->ConvertFunctionToValue(context.RuntimeHandle, handle);
            if (result.Ok == 0 || result.Value == 0)
            {
                JsiContext.ThrowNativeError(
                    result.Error,
                    "Failed to convert JavaScript function to value."
                );
            }
            return JavaScriptValue.FromOwnedHandle(context, result.Value);
        }
    }

    public void Dispose()
    {
        if (handle != 0)
        {
            unsafe
            {
                context.Api->ReleaseFunctionHandle(context.RuntimeHandle, handle);
            }
            handle = 0;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(handle == 0, this);
    }
}
