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
      EntryPoint = "hostfxr_jsi_register_modules",
      CallConvs = new[] { typeof(CallConvCdecl) }
  )]
  public static int RegisterModules(nint api, nint runtimeHandle)
  {
    try
    {
      var runtime = JavaScriptRuntime.FromNative(api, runtimeHandle);
      GeneratedModuleProvider.Register(runtime);
      return 0;
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine(ex);
      return 1;
    }
  }
}
