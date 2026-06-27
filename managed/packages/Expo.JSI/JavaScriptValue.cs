namespace Expo.JSI;

public sealed class JavaScriptValue : IDisposable
{
    private readonly JavaScriptRuntime runtime;
    private readonly bool ownsHandle;
    private ExpoJsiValueHandle handle;

    private JavaScriptValue(JavaScriptRuntime runtime, ExpoJsiValueHandle handle, bool ownsHandle)
    {
        this.runtime = runtime;
        this.handle = handle;
        this.ownsHandle = ownsHandle;
    }

    internal static JavaScriptValue FromOwnedHandle(
        JavaScriptRuntime runtime,
        ExpoJsiValueHandle handle
    ) => new(runtime, handle, ownsHandle: true);

    internal static JavaScriptValue FromBorrowedHandle(
        JavaScriptRuntime runtime,
        ExpoJsiValueHandle handle
    ) => new(runtime, handle, ownsHandle: false);

    public JavaScriptValueKind Kind
    {
        get
        {
            ThrowIfDisposed();
            return runtime.GetValueKind(handle);
        }
    }

    public double AsDouble()
    {
        ThrowIfDisposed();
        return runtime.GetDouble(handle);
    }

    public void Dispose()
    {
        if (handle != 0 && ownsHandle)
        {
            runtime.ReleaseValue(handle);
        }
        handle = 0;
    }

    public ExpoJsiValueHandle Detach()
    {
        ThrowIfDisposed();
        if (!ownsHandle)
        {
            throw new InvalidOperationException("Borrowed JavaScript values cannot be detached.");
        }

        var detached = handle;
        handle = 0;
        return detached;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(handle == 0, this);
    }
}
