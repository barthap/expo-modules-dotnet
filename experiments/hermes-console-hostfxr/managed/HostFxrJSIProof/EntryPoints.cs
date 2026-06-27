using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Expo.JSI;

namespace HostFxrJSIProof;

public static class EntryPoints
{
    [UnmanagedCallersOnly(
        EntryPoint = "hostfxr_jsi_proof_run",
        CallConvs = new[] { typeof(CallConvCdecl) }
    )]
    public static int Run(nint api, nint runtimeHandle)
    {
        try
        {
            var runtime = JavaScriptRuntime.FromNative(api, runtimeHandle);
            using var value = runtime.CreateNumber(42.5);

            if (value.Kind != JavaScriptValueKind.Number)
            {
                Console.Error.WriteLine($"Expected Number, got {value.Kind}.");
                return 2;
            }
            if (value.AsDouble() != 42.5)
            {
                Console.Error.WriteLine($"Expected 42.5, got {value.AsDouble()}.");
                return 3;
            }

            Console.WriteLine("managed JSI proof: number kind=Number value=42.5");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    [UnmanagedCallersOnly(
        EntryPoint = "hostfxr_jsi_proof_add_one",
        CallConvs = new[] { typeof(CallConvCdecl) }
    )]
    public static nint AddOne(
        nint api,
        nint runtimeHandle,
        nint valueHandle,
        nint shouldAddOneHandle
    )
    {
        try
        {
            var runtime = JavaScriptRuntime.FromNative(api, runtimeHandle);
            using var value = runtime.BorrowValue(valueHandle);
            using var shouldAddOne = runtime.BorrowValue(shouldAddOneHandle);

            var shouldAdd = shouldAddOne.AsBool();

            if (value.Kind != JavaScriptValueKind.Number)
            {
                Console.Error.WriteLine($"Expected Number argument, got {value.Kind}.");
                return 0;
            }

            var number = shouldAdd ? value.AsDouble() + 1.0 : value.AsDouble();
            var result = runtime.CreateNumber(number);
            Console.WriteLine("managed callback: AddOne(JavaScriptRuntime, JavaScriptValue)");
            return result.Detach();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 0;
        }
    }
}
