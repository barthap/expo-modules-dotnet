using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Expo.JSI;
using Expo.ModulesCore;
using Expo.ModulesCore.Generated;

namespace ExpoMobileV2Module;

public static class EntryPoints
{
  [UnmanagedCallersOnly(
      EntryPoint = "expo_mobile_v2_register_modules",
      CallConvs = new[] { typeof(CallConvCdecl) }
  )]
  public static int RegisterModules(nint api, nint runtimeHandle)
  {
    try
    {
      var runtime = JavaScriptRuntime.FromNative(api, runtimeHandle);
      using var modules = ModuleRegistry.GetOrCreateExpoModulesObject(runtime);
      ExpoModulesProvider_ExpoMobileV2Module.Register(runtime, modules);
      Console.WriteLine("ExpoMobileV2Module registered ExpoCSharpV2.add.");
      return 0;
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine(ex);
      return 1;
    }
  }
}
