using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Expo.JSI;
using Expo.ModulesCore;
using Expo.ModulesCore.Generated;

namespace HermesConsoleApp;

public static class EntryPoints
{
  [UnmanagedCallersOnly(
      EntryPoint = "hermes_console_app_create_session",
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
      EntryPoint = "hermes_console_app_teardown_session",
      CallConvs = new[] { typeof(CallConvCdecl) }
  )]
  public static void TeardownSession(nint sessionContext)
  {
    TeardownSessionCore(sessionContext);
  }

  [UnmanagedCallersOnly(
      EntryPoint = "hermes_console_app_run",
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

      using var ascii = runtime.CreateString("hello");
      if (ascii.Kind != JavaScriptValueKind.String)
      {
        Console.Error.WriteLine($"Expected String, got {ascii.Kind}.");
        return 4;
      }
      if (ascii.AsString() != "hello")
      {
        Console.Error.WriteLine($"Expected hello, got {ascii.AsString()}.");
        return 5;
      }

      const string nonAscii = "Zoë";
      using var unicode = runtime.CreateString(nonAscii);
      if (unicode.AsString() != nonAscii)
      {
        Console.Error.WriteLine($"Expected {nonAscii}, got {unicode.AsString()}.");
        return 6;
      }

      const string embeddedNul = "a\0b";
      using var nul = runtime.CreateString(embeddedNul);
      if (nul.AsString() != embeddedNul)
      {
        Console.Error.WriteLine("Embedded NUL string did not round trip.");
        return 7;
      }

      Console.WriteLine("managed JSI proof: primitive strings round-tripped");
      return 0;
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine(ex);
      return 1;
    }
  }

  [UnmanagedCallersOnly(
      EntryPoint = "hermes_console_app_register_modules",
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
      using var modules = session.GetOrCreateDotnetModulesObject();
      GeneratedModuleProvider.Register(session, modules);
      ExpoModulesProvider_HermesConsoleApp.Register(session, modules);
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
