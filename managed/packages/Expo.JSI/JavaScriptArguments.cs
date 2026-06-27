namespace Expo.JSI;

public readonly struct JavaScriptArguments
{
    private readonly JavaScriptRuntime runtime;
    private readonly ExpoJsiArgumentsHandle handle;

    internal JavaScriptArguments(JavaScriptRuntime runtime, ExpoJsiArgumentsHandle handle)
    {
        this.runtime = runtime;
        this.handle = handle;
    }

    public uint Count
    {
        get
        {
            ThrowIfNull();
            return runtime.GetArgumentsCount(handle);
        }
    }

    public JavaScriptBorrowedValue GetBorrowedValue(uint index)
    {
        ThrowIfNull();
        return runtime.GetBorrowedArgument(handle, index);
    }

    private void ThrowIfNull()
    {
        if (handle == 0)
        {
            throw new ObjectDisposedException(nameof(JavaScriptArguments));
        }
    }
}
