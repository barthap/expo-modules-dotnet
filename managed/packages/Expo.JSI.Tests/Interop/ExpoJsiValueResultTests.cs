using Expo.JSI.Interop;
using System.Runtime.InteropServices;
using Xunit;

namespace Expo.JSI.Tests.Interop;

public sealed unsafe class ExpoJsiValueResultTests
{
  [Fact]
  public void IsOkRequiresNativeSuccessAndValueHandle()
  {
    var value = new ExpoJsiValueResult(1, 123, default);
    var failed = new ExpoJsiValueResult(0, 123, default);
    var empty = new ExpoJsiValueResult(1, 0, default);

    Assert.True(value.IsOk);
    Assert.False(failed.IsOk);
    Assert.False(empty.IsOk);
  }

  [Fact]
  public void PromiseResultIsOkRequiresNativeSuccessAndPromiseHandle()
  {
    var promise = PromiseResult(1, 123);
    var failed = PromiseResult(0, 123);
    var empty = PromiseResult(1, 0);

    Assert.True(promise.IsOk);
    Assert.False(failed.IsOk);
    Assert.False(empty.IsOk);
  }

  [Fact]
  public void StringResultIsOkOnlyRequiresNativeSuccess()
  {
    var emptyString = StringResult(1);
    var failed = StringResult(0);

    Assert.True(emptyString.IsOk);
    Assert.False(failed.IsOk);
  }

  private static ExpoJsiPromiseResult PromiseResult(int ok, nint promise)
  {
    var raw = new RawPromiseResult
    {
      Ok = ok,
      Promise = promise,
      Error = default,
    };
    return MemoryMarshal.Cast<RawPromiseResult, ExpoJsiPromiseResult>(
        MemoryMarshal.CreateSpan(ref raw, 1)
    )[0];
  }

  private static ExpoJsiStringResult StringResult(int ok)
  {
    var raw = new RawStringResult
    {
      Ok = ok,
      Data = null,
      Length = 0,
      ReleaseContext = 0,
      Release = null,
      Error = default,
    };
    return MemoryMarshal.Cast<RawStringResult, ExpoJsiStringResult>(
        MemoryMarshal.CreateSpan(ref raw, 1)
    )[0];
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct RawPromiseResult
  {
    public int Ok;
    public nint Promise;
    public ExpoJsiError Error;
  }

  [StructLayout(LayoutKind.Sequential)]
  private unsafe struct RawStringResult
  {
    public int Ok;
    public byte* Data;
    public int Length;
    public nint ReleaseContext;
    public delegate* unmanaged[Cdecl]<nint, void> Release;
    public ExpoJsiError Error;
  }
}
