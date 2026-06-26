using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace HostFxrSmoke;

public static unsafe class EntryPoints
{
  private const string Payload = "expo-csharp-jsi-smoke";

  [UnmanagedCallersOnly(EntryPoint = "hostfxr_smoke_get_message", CallConvs = new[] { typeof(CallConvCdecl) })]
  public static nint GetMessage()
  {
    var bytes = Encoding.UTF8.GetBytes(Payload + "\0");
    var buffer = (byte*)NativeMemory.Alloc((nuint)bytes.Length);
    bytes.CopyTo(new Span<byte>(buffer, bytes.Length));
    return (nint)buffer;
  }

  [UnmanagedCallersOnly(EntryPoint = "hostfxr_smoke_release_message", CallConvs = new[] { typeof(CallConvCdecl) })]
  public static void ReleaseMessage(nint message)
  {
    NativeMemory.Free((void*)message);
  }
}
