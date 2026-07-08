using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Expo.JSI.Internal;

namespace Expo.JSI.Interop;

internal sealed unsafe class HostObjectContext
{
  public HostObjectContext(JsiContext jsiContext, JavaScriptHostObjectDescriptor descriptor)
  {
    JsiContext = jsiContext;
    Descriptor = descriptor;
  }

  public JsiContext JsiContext { get; }
  public JavaScriptHostObjectDescriptor Descriptor { get; }

  public ExpoJsiError CaptureException(Exception exception)
  {
    var message = exception.ToString();
    if (string.IsNullOrEmpty(message))
    {
      message = exception.GetType().FullName ?? exception.GetType().Name;
    }

    var length = Encoding.UTF8.GetByteCount(message);
    if (length == 0)
    {
      return new ExpoJsiError(100, null, 0, 0, null);
    }

    var errorMessage = (byte*)NativeMemory.Alloc((nuint)length);
    Encoding.UTF8.GetBytes(message, new Span<byte>(errorMessage, length));
    return new ExpoJsiError(100, errorMessage, length, (nint)errorMessage, &ReleaseErrorMessage);
  }

  public nint ToIntPtr()
  {
    return GCHandle.ToIntPtr(GCHandle.Alloc(this));
  }

  public static HostObjectContext FromIntPtr(nint pointer)
  {
    return (HostObjectContext)GCHandle.FromIntPtr(pointer).Target!;
  }

  public static void Release(nint pointer)
  {
    if (pointer == 0)
    {
      return;
    }

    var handle = GCHandle.FromIntPtr(pointer);
    handle.Free();
  }

  public static string DecodePropertyName(byte* name, int length)
  {
    if (name is null || length < 0)
    {
      throw new ArgumentException("HostObject property name is invalid.");
    }
    return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(name, length));
  }

  public static ExpoJsiPropertyNamesResult CreatePropertyNamesResult(IReadOnlyList<string> names)
  {
    if (names.Count == 0)
    {
      return new ExpoJsiPropertyNamesResult(1, null, 0, 0, null, default);
    }

    var buffer = PropertyNamesBuffer.Allocate(names);
    return new ExpoJsiPropertyNamesResult(
        1,
        buffer.Names,
        names.Count,
        buffer.ToIntPtr(),
        &ReleasePropertyNamesBuffer,
        default
    );
  }

  [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
  private static void ReleasePropertyNamesBuffer(nint context)
  {
    PropertyNamesBuffer.Release(context);
  }

  [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
  private static void ReleaseErrorMessage(nint context)
  {
    if (context != 0)
    {
      NativeMemory.Free((void*)context);
    }
  }

  private sealed class PropertyNamesBuffer
  {
    private readonly List<nint> strings = [];
    private nint names;

    private PropertyNamesBuffer()
    {
    }

    public ExpoJsiPropertyName* Names => (ExpoJsiPropertyName*)names;

    public static PropertyNamesBuffer Allocate(IReadOnlyList<string> names)
    {
      var buffer = new PropertyNamesBuffer
      {
        names = (nint)NativeMemory.Alloc(
            (nuint)checked(names.Count * sizeof(ExpoJsiPropertyName))
        ),
      };
      var span = new Span<ExpoJsiPropertyName>((void*)buffer.names, names.Count);

      try
      {
        for (var index = 0; index < names.Count; index++)
        {
          var bytes = Encoding.UTF8.GetBytes(names[index]);
          var pointer = (byte*)NativeMemory.Alloc((nuint)bytes.Length);
          bytes.CopyTo(new Span<byte>(pointer, bytes.Length));
          buffer.strings.Add((nint)pointer);
          span[index] = new ExpoJsiPropertyName(pointer, bytes.Length);
        }
      }
      catch
      {
        buffer.Dispose();
        throw;
      }

      return buffer;
    }

    public nint ToIntPtr()
    {
      return GCHandle.ToIntPtr(GCHandle.Alloc(this));
    }

    public static void Release(nint pointer)
    {
      if (pointer == 0)
      {
        return;
      }

      var handle = GCHandle.FromIntPtr(pointer);
      if (handle.Target is PropertyNamesBuffer buffer)
      {
        buffer.Dispose();
      }
      handle.Free();
    }

    private void Dispose()
    {
      foreach (var pointer in strings)
      {
        NativeMemory.Free((void*)pointer);
      }
      strings.Clear();
      if (names != 0)
      {
        NativeMemory.Free((void*)names);
        names = 0;
      }
    }
  }
}
