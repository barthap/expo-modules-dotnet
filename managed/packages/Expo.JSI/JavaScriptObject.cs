using System.Text;

namespace Expo.JSI;

public sealed class JavaScriptObject : IDisposable
{
    private readonly JavaScriptRuntime runtime;
    private ExpoJsiObjectHandle handle;

    internal JavaScriptObject(JavaScriptRuntime runtime, ExpoJsiObjectHandle handle)
    {
        this.runtime = runtime;
        this.handle = handle;
    }

    public void SetProperty(string name, JavaScriptValue value)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);

        var nameBytes = Encoding.UTF8.GetBytes(name);
        runtime.SetObjectProperty(handle, nameBytes, value.Handle);
    }

    public JavaScriptValue AsValue()
    {
        ThrowIfDisposed();
        return runtime.ObjectAsValue(handle);
    }

    public void Dispose()
    {
        if (handle != 0)
        {
            runtime.ReleaseObject(handle);
            handle = 0;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(handle == 0, this);
    }
}
