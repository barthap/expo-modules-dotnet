namespace Expo.JSI;

public sealed class JavaScriptValue : IDisposable
{
    private readonly JavaScriptRuntime runtime;
    private ExpoJsiValueHandle handle;

    private JavaScriptValue(JavaScriptRuntime runtime, ExpoJsiValueHandle handle)
    {
        this.runtime = runtime;
        this.handle = handle;
    }

    internal static JavaScriptValue FromOwnedHandle(
        JavaScriptRuntime runtime,
        ExpoJsiValueHandle handle
    ) => new(runtime, handle);

    internal ExpoJsiValueHandle Handle
    {
        get
        {
            ThrowIfDisposed();
            return handle;
        }
    }

    public JavaScriptValueKind Kind
    {
        get
        {
            ThrowIfDisposed();
            return runtime.GetValueKind(handle);
        }
    }

    public bool AsBool()
    {
        ThrowIfDisposed();
        return runtime.GetBool(handle);
    }

    public double AsDouble()
    {
        ThrowIfDisposed();
        return runtime.GetDouble(handle);
    }

    public void Dispose()
    {
        if (handle != 0)
        {
            runtime.ReleaseValue(handle);
        }
        handle = 0;
    }

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
