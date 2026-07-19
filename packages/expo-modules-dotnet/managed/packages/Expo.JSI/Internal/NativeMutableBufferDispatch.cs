using System.Threading;
using Expo.JSI.Interop;

namespace Expo.JSI.Internal;

/// <summary>
/// Owns a managed copy of the runtime-independent MutableBuffer ABI dispatch.
/// The ABI function targets are supplied by the bridge library and must outlive
/// every MutableBuffer that uses them.
/// </summary>
internal sealed unsafe class NativeMutableBufferDispatch
{
  private static NativeMutableBufferDispatch? defaultDispatch;
  private readonly ExpoJsiApi api;

  private NativeMutableBufferDispatch(ExpoJsiApi* api)
  {
    this.api = *api;
  }

  internal static NativeMutableBufferDispatch Create(ExpoJsiApi* api) => new(api);

  internal static void SetDefault(ExpoJsiApi* api)
  {
    Volatile.Write(ref defaultDispatch, new NativeMutableBufferDispatch(api));
  }

  internal static NativeMutableBufferDispatch Default =>
      Volatile.Read(ref defaultDispatch)
      ?? throw new InvalidOperationException("No JavaScript runtime has initialized the MutableBuffer ABI.");

  internal ExpoJsiMutableBufferResult Allocate(int byteLength) => api.AllocateMutableBuffer(byteLength);

  internal ExpoJsiMutableBufferResult Copy(ReadOnlySpan<byte> bytes) => api.CopyMutableBuffer(bytes);

  internal ExpoJsiMutableBufferResult Clone(ExpoJsiMutableBufferHandle handle) =>
      api.CloneMutableBuffer(handle);

  internal ExpoJsiByteSpanResult GetBytes(ExpoJsiMutableBufferHandle handle) =>
      api.GetMutableBufferBytes(handle);

  internal void Release(ExpoJsiMutableBufferHandle handle) => api.ReleaseMutableBuffer(handle);
}
