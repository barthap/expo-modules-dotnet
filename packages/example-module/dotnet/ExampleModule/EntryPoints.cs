using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Expo.JSI;
using Expo.ModulesCore;
using Expo.ModulesCore.Generated;

namespace ExampleModule;

public static class EntryPoints
{
  // Temporary app-composition entry points until .NET module autolinking
  // generates the composed provider list. ExampleModule contributes its
  // generated provider; it does not own the runtime context lifetime.
  [UnmanagedCallersOnly(
      EntryPoint = "expo_dotnet_create_runtime_context",
      CallConvs = new[] { typeof(CallConvCdecl) }
  )]
  public static nint CreateRuntimeContext(nint api, nint runtimeHandle)
  {
    try
    {
      return CreateRuntimeContextCore(api, runtimeHandle);
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine(ex);
      return 0;
    }
  }

  [UnmanagedCallersOnly(
      EntryPoint = "expo_dotnet_teardown_runtime_context",
      CallConvs = new[] { typeof(CallConvCdecl) }
  )]
  public static void TeardownRuntimeContext(nint runtimeContext)
  {
    TeardownRuntimeContextCore(runtimeContext);
  }

  private static nint CreateRuntimeContextCore(nint api, nint runtimeHandle)
  {
    var runtime = JavaScriptRuntime.FromNative(api, runtimeHandle);
    var context = new DotnetRuntimeContext(runtime);
    try
    {
      ExpoModulesProvider_ExampleModule.Register(context);
      return GCHandle.ToIntPtr(GCHandle.Alloc(context));
    }
    catch
    {
      context.Dispose();
      throw;
    }
  }

  private static void TeardownRuntimeContextCore(nint runtimeContext)
  {
    if (runtimeContext == 0)
    {
      return;
    }

    var handle = GCHandle.FromIntPtr(runtimeContext);
    if (handle.Target is DotnetRuntimeContext context)
    {
      context.Dispose();
    }

    handle.Free();
  }
}
