namespace Expo.JSI;

public sealed class JavaScriptFunction : IDisposable
{
    private readonly JavaScriptRuntime runtime;
    private ExpoJsiFunctionHandle handle;

    internal JavaScriptFunction(JavaScriptRuntime runtime, ExpoJsiFunctionHandle handle)
    {
        this.runtime = runtime;
        this.handle = handle;
    }

    public JavaScriptValue AsValue()
    {
        ThrowIfDisposed();
        return runtime.FunctionAsValue(handle);
    }

    public void Dispose()
    {
        if (handle != 0)
        {
            runtime.ReleaseFunction(handle);
            handle = 0;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(handle == 0, this);
    }
}
