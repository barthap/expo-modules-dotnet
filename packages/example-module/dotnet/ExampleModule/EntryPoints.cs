using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Expo.JSI;
using Expo.ModulesCore;
using Expo.ModulesCore.Generated;

namespace ExampleModule;

public static class EntryPoints
{
  [UnmanagedCallersOnly(
      EntryPoint = "example_module_create_session",
      CallConvs = new[] { typeof(CallConvCdecl) }
  )]
  public static nint CreateSession(nint api, nint runtimeHandle)
  {
    try
    {
      return CreateSessionCore(api, runtimeHandle);
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine(ex);
      return 0;
    }
  }

  [UnmanagedCallersOnly(
      EntryPoint = "example_module_teardown_session",
      CallConvs = new[] { typeof(CallConvCdecl) }
  )]
  public static void TeardownSession(nint sessionContext)
  {
    TeardownSessionCore(sessionContext);
  }

  [UnmanagedCallersOnly(
      EntryPoint = "example_module_register_modules",
      CallConvs = new[] { typeof(CallConvCdecl) }
  )]
  public static int RegisterModules(nint api, nint runtimeHandle)
  {
    try
    {
      if (CreateSessionCore(api, runtimeHandle) == 0)
      {
        return 1;
      }

      Console.WriteLine("ExampleModule registered ExampleModule.add.");
      return 0;
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine(ex);
      return 1;
    }
  }

  private static nint CreateSessionCore(nint api, nint runtimeHandle)
  {
    var runtime = JavaScriptRuntime.FromNative(api, runtimeHandle);
    var session = new RuntimeSession(runtime);
    try
    {
      ExpoModulesProvider_ExampleModule.Register(session);
      return GCHandle.ToIntPtr(GCHandle.Alloc(session));
    }
    catch
    {
      session.Dispose();
      throw;
    }
  }

  private static void TeardownSessionCore(nint sessionContext)
  {
    if (sessionContext == 0)
    {
      return;
    }

    var handle = GCHandle.FromIntPtr(sessionContext);
    if (handle.Target is RuntimeSession session)
    {
      session.Dispose();
    }

    handle.Free();
  }
}
