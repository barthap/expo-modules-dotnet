using System.Runtime.InteropServices;
using Expo.JSI.Interop;
using Xunit;

namespace Expo.JSI.Tests.Interop;

public sealed class ExpoJsiApiTests
{
  [Fact]
  public unsafe void FromNativeReportsBothNativeAndManagedAbiVersions()
  {
    var api = new FakeExpoJsiApi
    {
      Size = (uint)sizeof(ExpoJsiApi),
      Version = ExpoJsiApi.ExpectedVersion + 1,
    };
    var apiPtr = (nint)(&api);

    var exception = Assert.Throws<InvalidOperationException>(() =>
      JavaScriptRuntime.FromNative(apiPtr, 1)
    );

    Assert.Equal(
      $"Expo JSI ABI version mismatch: native={api.Version} managed={ExpoJsiApi.ExpectedVersion}.",
      exception.Message
    );
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct FakeExpoJsiApi
  {
    public uint Size;
    public uint Version;
  }
}
