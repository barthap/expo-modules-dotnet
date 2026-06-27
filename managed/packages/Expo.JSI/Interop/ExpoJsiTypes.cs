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

[StructLayout(LayoutKind.Sequential)]
internal readonly unsafe struct ExpoJsiError
{
  public readonly int Code;
  public readonly byte* Message;
  public readonly int MessageLength;

  public string GetMessage()
  {
    if (Message is null || MessageLength <= 0) {
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

  public ExpoJsiValueResult(int ok, ExpoJsiValueHandle value, ExpoJsiError error)
  {
    Ok = ok;
    Value = value;
    Error = error;
  }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct ExpoJsiObjectResult
{
  public readonly int Ok;
  public readonly ExpoJsiObjectHandle Object;
  public readonly ExpoJsiError Error;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct ExpoJsiFunctionResult
{
  public readonly int Ok;
  public readonly ExpoJsiFunctionHandle Function;
  public readonly ExpoJsiError Error;
}
