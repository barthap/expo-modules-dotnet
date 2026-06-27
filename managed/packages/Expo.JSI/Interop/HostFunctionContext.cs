using System.Runtime.InteropServices;
using Expo.JSI;

namespace Expo.JSI.Interop;

internal sealed unsafe class HostFunctionContext
{
    public HostFunctionContext(
        ExpoJsiApi* api,
        JavaScriptHostFunction callback,
        object context
    )
    {
        Api = api;
        Callback = callback;
        Context = context;
    }

    public ExpoJsiApi* Api { get; }
    public JavaScriptHostFunction Callback { get; }
    public object Context { get; }

    public nint ToIntPtr()
    {
        return GCHandle.ToIntPtr(GCHandle.Alloc(this));
    }

    public static HostFunctionContext FromIntPtr(nint pointer)
    {
        return (HostFunctionContext)GCHandle.FromIntPtr(pointer).Target!;
    }

    public static void Release(nint pointer)
    {
        if (pointer == 0)
        {
            return;
        }
        GCHandle.FromIntPtr(pointer).Free();
    }
}
