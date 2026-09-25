using System.Net;
using System.Security.Cryptography;

namespace ExpoAssetDotnet;

internal sealed class AssetDownloadService : IDisposable
{
  private const string CacheSubdirectory = "ExponentAsset";

  private readonly Func<string> cacheRootProvider;
  private readonly HttpClient httpClient;

  internal AssetDownloadService(
      Func<string> cacheRootProviderArgument,
      HttpMessageHandler handler)
  {
    cacheRootProvider = cacheRootProviderArgument ??
        throw new ArgumentNullException(nameof(cacheRootProviderArgument));
    httpClient = new HttpClient(handler ?? throw new ArgumentNullException(nameof(handler)));
  }

  internal async Task<string> DownloadAsync(
      AssetRequest request,
      CancellationToken cancellationToken)
  {
    if (request.Uri.Scheme.Equals(Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
    {
      return request.OriginalUrl;
    }

    cancellationToken.ThrowIfCancellationRequested();

    var hostCacheRoot = cacheRootProvider();
    AssetCachePaths paths;
    try
    {
      paths = AssetCachePaths.Create(hostCacheRoot, request);
    }
    catch (Exception exception)
    {
      throw SaveException(exception);
    }

    if (await IsCacheHitAsync(paths, request.Md5Hash, cancellationToken))
    {
      return new Uri(paths.FinalPath).AbsoluteUri;
    }

    using var response = await SendAsync(request, cancellationToken);
    if (!response.IsSuccessStatusCode)
    {
      throw DownloadStatusException(request.OriginalUrl, response.StatusCode);
    }

    var suffix = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
    var tempPath = $"{paths.FinalPath}.{suffix}.download";
    try
    {
      await using (var output = new FileStream(
          tempPath,
          new FileStreamOptions
          {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous,
          }))
      {
        await response.Content.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
      }

      File.Move(tempPath, paths.FinalPath, overwrite: true);
      return new Uri(paths.FinalPath).AbsoluteUri;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception exception)
    {
      throw SaveException(exception);
    }
    finally
    {
      DeleteTempFile(tempPath);
    }
  }

  public void Dispose()
  {
    httpClient.Dispose();
  }

  private async Task<HttpResponseMessage> SendAsync(
      AssetRequest request,
      CancellationToken cancellationToken)
  {
    using var message = new HttpRequestMessage(HttpMethod.Get, request.Uri);
    try
    {
      return await httpClient.SendAsync(
          message,
          HttpCompletionOption.ResponseHeadersRead,
          cancellationToken
      );
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception exception)
    {
      throw new InvalidOperationException(
          $"Unable to download asset from url: '{request.OriginalUrl}'",
          exception
      );
    }
  }

  private static async Task<bool> IsCacheHitAsync(
      AssetCachePaths paths,
      string? md5Hash,
      CancellationToken cancellationToken)
  {
    if (!File.Exists(paths.FinalPath))
    {
      return false;
    }

    if (md5Hash is null)
    {
      return true;
    }

    try
    {
      await using var stream = new FileStream(
          paths.FinalPath,
          FileMode.Open,
          FileAccess.Read,
          FileShare.Read,
          bufferSize: 4096,
          useAsync: true
      );
      using var md5 = MD5.Create();
      var contentHash = await md5.ComputeHashAsync(stream, cancellationToken);
      return Convert.ToHexStringLower(contentHash).Equals(
          md5Hash,
          StringComparison.OrdinalIgnoreCase
      );
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (IOException)
    {
      return false;
    }
    catch (UnauthorizedAccessException)
    {
      return false;
    }
  }

  private static InvalidOperationException DownloadStatusException(
      string url,
      HttpStatusCode statusCode)
  {
    return new InvalidOperationException(
        $"Unable to download asset from url: '{url}': HTTP {(int)statusCode} ({statusCode})."
    );
  }

  private static InvalidOperationException SaveException(Exception innerException)
  {
    return new InvalidOperationException(
        $"Unable to save asset to directory: '{CacheSubdirectory}'",
        innerException
    );
  }

  private static void DeleteTempFile(string tempPath)
  {
    try
    {
      File.Delete(tempPath);
    }
    catch
    {
      // Cleanup must not replace the failure that caused it.
    }
  }
}
