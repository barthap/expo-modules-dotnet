using Expo.ModulesCore;

namespace ExpoAssetDotnet;

[ExpoModule("ExpoAsset")]
public sealed partial class ExpoAssetModule : Module
{
  private readonly CancellationTokenSource cancellation = new();
  private readonly AssetDownloadService downloadService;

  public ExpoAssetModule(DotnetRuntimeContext context)
      : base(context)
  {
    downloadService = new AssetDownloadService(
        () => context.CacheDirectory,
        new HttpClientHandler()
    );
  }

  internal CancellationToken CancellationToken => cancellation.Token;

  [JS]
  public Task<string> DownloadAsync(string url, string? md5Hash, string type)
  {
    var request = AssetRequestValidation.Validate(url, md5Hash, type);
    return downloadService.DownloadAsync(request, cancellation.Token);
  }

  [OnDestroy]
  public void OnDestroy()
  {
    cancellation.Cancel();
    downloadService.Dispose();
    cancellation.Dispose();
  }
}
