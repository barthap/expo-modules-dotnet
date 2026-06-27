namespace Expo.JSI;

public readonly struct JavaScriptBorrowedValue
{
    private readonly JavaScriptRuntime runtime;
    private readonly ExpoJsiValueHandle handle;

    internal JavaScriptBorrowedValue(JavaScriptRuntime runtime, ExpoJsiValueHandle handle)
    {
        this.runtime = runtime;
        this.handle = handle;
    }

    public JavaScriptValueKind Kind
    {
        get
        {
            ThrowIfNull();
            return runtime.GetValueKind(handle);
        }
    }

    public bool AsBool()
    {
        ThrowIfNull();
        return runtime.GetBool(handle);
    }

    public double AsDouble()
    {
        ThrowIfNull();
        return runtime.GetDouble(handle);
    }

    private void ThrowIfNull()
    {
        if (handle == 0)
        {
            throw new ObjectDisposedException(nameof(JavaScriptBorrowedValue));
        }
    }
}
