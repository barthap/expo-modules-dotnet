using System.Runtime.InteropServices;
using System.Text;

namespace Expo.JSI.Interop;

internal enum ExpoJsiValueKind : int
{
  Undefined = 0,
  Null = 1,
  Bool = 2,
  Number = 3,
  String = 4,
  Object = 5,
  Function = 6,
  ArrayBuffer = 7,
}

internal enum ExpoJsiTaskPriority : int
{
  Immediate = 1,
  UserBlocking = 2,
  Normal = 3,
  Low = 4,
  Idle = 5,
}

internal enum ExpoJsiValueExpectation : int
{
  Object = 1,
  Array = 2,
  Function = 3,
}

internal enum ExpoJsiPromiseSettlement : int
{
  Resolve = 0,
  Reject = 1,
}

[StructLayout(LayoutKind.Sequential)]
internal readonly unsafe struct ExpoJsiError
{
  public readonly int Code;
  public readonly byte* Message;
  public readonly int MessageLength;
  public readonly nint ReleaseContext;
  public readonly delegate* unmanaged[Cdecl]<nint, void> Release;

  public ExpoJsiError(
      int code,
      byte* message,
      int messageLength,
      nint releaseContext,
      delegate* unmanaged[Cdecl]<nint, void> release
  )
  {
    Code = code;
    Message = message;
    MessageLength = messageLength;
    ReleaseContext = releaseContext;
    Release = release;
  }

  public string GetMessageAndRelease()
  {
    try
    {
      return CopyMessage();
    }
    finally
    {
      if (Release is not null)
      {
        Release(ReleaseContext);
      }
    }
  }

  private string CopyMessage()
  {
    if (Message is null || MessageLength <= 0)
    {
      return string.Empty;
    }
    return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(Message, MessageLength));
  }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct ExpoJsiValueResult
{
  public readonly int Ok;
  public readonly ExpoJsiValueHandle Value;
  public readonly ExpoJsiError Error;

  public bool IsOk => Ok != 0 && Value != 0;

  public ExpoJsiValueResult(int ok, ExpoJsiValueHandle value, ExpoJsiError error)
  {
    Ok = ok;
    Value = value;
    Error = error;
  }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct ExpoJsiPromiseResult
{
  public readonly int Ok;
  public readonly ExpoJsiPromiseHandle Promise;
  public readonly ExpoJsiError Error;

  public bool IsOk => Ok != 0 && Promise != 0;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly unsafe struct ExpoJsiStringResult
{
  public readonly int Ok;
  public readonly byte* Data;
  public readonly int Length;
  public readonly nint ReleaseContext;
  public readonly delegate* unmanaged[Cdecl]<nint, void> Release;
  public readonly ExpoJsiError Error;

  public bool IsOk => Ok != 0;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly unsafe struct ExpoJsiPropertyName
{
  public readonly byte* Data;
  public readonly int Length;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly unsafe struct ExpoJsiPropertyNamesResult
{
  public readonly int Ok;
  public readonly ExpoJsiPropertyName* Names;
  public readonly int Count;
  public readonly nint ReleaseContext;
  public readonly delegate* unmanaged[Cdecl]<nint, void> Release;
  public readonly ExpoJsiError Error;

  public bool IsOk => Ok != 0;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct ExpoJsiNativeStateToken
{
  public readonly ulong TypeId;
  public readonly ulong RegistryId;
  public readonly uint Generation;

  public ExpoJsiNativeStateToken(ulong typeId, ulong registryId, uint generation)
  {
    TypeId = typeId;
    RegistryId = registryId;
    Generation = generation;
  }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct ExpoJsiNativeStateResult
{
  public readonly int Ok;
  public readonly int Found;
  public readonly ExpoJsiNativeStateToken Token;
  public readonly ExpoJsiError Error;

  public bool IsOk => Ok != 0;
  public bool HasValue => IsOk && Found != 0;
}
