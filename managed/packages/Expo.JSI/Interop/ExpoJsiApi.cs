using System.Runtime.InteropServices;

namespace Expo.JSI.Interop;

[StructLayout(LayoutKind.Sequential)]
internal readonly unsafe struct ExpoJsiApi
{
  public readonly uint Size;
  public readonly uint Version;
  public readonly delegate* unmanaged[Cdecl]<nint, double, ExpoJsiValueResult> CreateNumber;
  public readonly delegate* unmanaged[Cdecl]<nint, nint, ExpoJsiError*, ExpoJsiValueKind> GetValueKind;
  public readonly delegate* unmanaged[Cdecl]<nint, nint, ExpoJsiError*, double> GetDouble;
  public readonly delegate* unmanaged[Cdecl]<nint, nint, void> ReleaseValue;

  public static uint ExpectedSize => (uint)sizeof(ExpoJsiApi);
  public const uint ExpectedVersion = 1;
}
