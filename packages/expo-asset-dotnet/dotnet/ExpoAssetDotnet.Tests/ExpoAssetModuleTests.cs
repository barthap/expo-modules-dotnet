using Expo.ModulesCore;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Testing;
using Xunit;

namespace ExpoAssetDotnet.Tests;

public sealed class ExpoAssetModuleTests
{
  [Fact]
  public void GeneratedProviderRegistersOnlyDownloadAsync()
  {
    using var host = ExpoModuleTestHost.Create(
        ExpoModulesProvider_ExpoAssetDotnet.Register
    );

    var names = host.Runtime.Execute(_ =>
    {
      using var value = host.Evaluate(
          "Object.getOwnPropertyNames(globalThis._expoDotnet.modules.ExpoAsset).sort().join(',')",
          "expo-asset-members.js"
      );
      return value.AsString();
    });

    Assert.Equal("downloadAsync", names);
  }

  [Fact]
  public async Task DownloadAsync_FileUrlRoundTripsBeforeCacheAccess()
  {
    using var host = ExpoModuleTestHost.Create(
        ExpoModulesProvider_ExpoAssetDotnet.Register
    );
    const string url = "FILE:///tmp/My%20Asset.png";

    var value = await host.EvaluatePromiseAsync(
        $"globalThis._expoDotnet.modules.ExpoAsset.downloadAsync('{url}', null, 'png')",
        TestContext.Current.CancellationToken
    );
    var result = host.Runtime.Execute(_ =>
    {
      using (value)
      {
        return value.AsString();
      }
    });

    Assert.Equal(url, result);
  }

  [Theory]
  [InlineData(
      "globalThis._expoDotnet.modules.ExpoAsset.downloadAsync('https://example.com/a', null, 'p.g')",
      "Asset type must match ^[A-Za-z0-9]{1,16}$."
  )]
  [InlineData(
      "globalThis._expoDotnet.modules.ExpoAsset.downloadAsync('https://example.com/a', 'bad', 'png')",
      "Asset md5Hash must be null or 32 hexadecimal characters."
  )]
  [InlineData(
      "globalThis._expoDotnet.modules.ExpoAsset.downloadAsync('ftp://example.com/a', null, 'png')",
      "Unsupported asset URL scheme: 'ftp'."
  )]
  public async Task DownloadAsync_InvalidRequestRejectsPromise(
      string expression,
      string expectedMessage)
  {
    using var host = ExpoModuleTestHost.Create(
        ExpoModulesProvider_ExpoAssetDotnet.Register
    );

    var exception = await Assert.ThrowsAsync<JavaScriptPromiseRejectedException>(
        () => host.EvaluatePromiseAsync(
            expression,
            TestContext.Current.CancellationToken
        )
    );

    Assert.Equal("Error", exception.JavaScriptName);
    Assert.Equal(expectedMessage, exception.Message);
    Assert.False(string.IsNullOrWhiteSpace(exception.JavaScriptStack));
  }

  [Fact]
  public void OnDestroy_CancelsTheModuleToken()
  {
    using var runtime = HermesTestRuntime.Create();
    var cancellationObserved = runtime.Runtime.Execute(_ =>
    {
      using var context = new DotnetRuntimeContext(runtime.Runtime);
      var module = new ExpoAssetModule(context);
      var moduleToken = module.CancellationToken;

      Assert.False(moduleToken.IsCancellationRequested);
      module.OnDestroy();
      return moduleToken.IsCancellationRequested;
    });

    Assert.True(cancellationObserved);
  }
}
