using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Expo.JSI;
using Expo.ModulesCore;
using Expo.ModulesCore.Generated;

namespace ExampleModule;

public static class EntryPoints
{
  [UnmanagedCallersOnly(
      EntryPoint = "example_module_register_modules",
      CallConvs = new[] { typeof(CallConvCdecl) }
  )]
  public static int RegisterModules(nint api, nint runtimeHandle)
  {
    try
    {
      var runtime = JavaScriptRuntime.FromNative(api, runtimeHandle);
      using var modules = ModuleRegistry.GetOrCreateExpoModulesObject(runtime);
      ExpoModulesProvider_ExampleModule.Register(runtime, modules);
      Console.WriteLine("ExampleModule registered ExampleModule.add.");
      return 0;
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine(ex);
      return 1;
    }
  }
}
