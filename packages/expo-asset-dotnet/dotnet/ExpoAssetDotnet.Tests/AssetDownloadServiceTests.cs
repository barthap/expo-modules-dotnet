using System.Net;
using Xunit;

namespace ExpoAssetDotnet.Tests;

public sealed class AssetDownloadServiceTests
{
  [Fact]
  public async Task DownloadAsync_FileUrlReturnsUnchangedWithoutCacheOrHttp()
  {
    var handler = new TestHttpMessageHandler((_, _) => throw new InvalidOperationException());
    using var service = new AssetDownloadService(
        () => throw new InvalidOperationException("cache provider must not run"),
        handler
    );
    const string url = "FILE:///tmp/My%20Asset.png";
    var request = AssetRequestValidation.Validate(url, null, "png");

    var result = await service.DownloadAsync(request, TestContext.Current.CancellationToken);

    Assert.Equal(url, result);
    Assert.Equal(0, handler.RequestCount);
  }

  [Fact]
  public async Task DownloadAsync_ExistingFileWithoutHashIsAnUnconditionalHit()
  {
    using var temp = new TemporaryDirectory();
    var finalPath = PrepareFile(
        temp.Path,
        "ExponentAsset-8c11d92f07c76baf29e49afd2301deb2.png",
        "cached"
    );
    var handler = new TestHttpMessageHandler((_, _) => throw new InvalidOperationException());
    using var service = new AssetDownloadService(() => temp.Path, handler);
    var request = AssetRequestValidation.Validate("https://example.com/a.png", null, "png");

    var result = await service.DownloadAsync(request, TestContext.Current.CancellationToken);

    Assert.Equal(new Uri(finalPath).AbsoluteUri, result);
    Assert.Equal("cached", File.ReadAllText(finalPath));
    Assert.Equal(0, handler.RequestCount);
  }

  [Fact]
  public async Task DownloadAsync_PreCancelledRequestRejectsBeforeReturningACacheHit()
  {
    using var temp = new TemporaryDirectory();
    PrepareFile(
        temp.Path,
        "ExponentAsset-8c11d92f07c76baf29e49afd2301deb2.png",
        "cached"
    );
    var handler = new TestHttpMessageHandler((_, _) => throw new InvalidOperationException());
    using var service = new AssetDownloadService(() => temp.Path, handler);
    using var cancellation = new CancellationTokenSource();
    await cancellation.CancelAsync();
    var request = AssetRequestValidation.Validate("https://example.com/a.png", null, "png");

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
        () => service.DownloadAsync(request, cancellation.Token)
    );

    Assert.Equal(0, handler.RequestCount);
  }

  [Fact]
  public async Task DownloadAsync_ExistingFileWithMatchingHashIsAHit()
  {
    using var temp = new TemporaryDirectory();
    var finalPath = PrepareFile(
        temp.Path,
        "ExponentAsset-5d41402abc4b2a76b9719d911017c592.png",
        "hello"
    );
    var handler = new TestHttpMessageHandler((_, _) => throw new InvalidOperationException());
    using var service = new AssetDownloadService(() => temp.Path, handler);
    var request = AssetRequestValidation.Validate(
        "https://example.com/a.png",
        "5D41402ABC4B2A76B9719D911017C592",
        "png"
    );

    var result = await service.DownloadAsync(request, TestContext.Current.CancellationToken);

    Assert.Equal(new Uri(finalPath).AbsoluteUri, result);
    Assert.Equal(0, handler.RequestCount);
  }

  [Fact]
  public async Task DownloadAsync_HashMismatchRedownloadsAndReplacesTheFile()
  {
    using var temp = new TemporaryDirectory();
    var finalPath = PrepareFile(
        temp.Path,
        "ExponentAsset-00000000000000000000000000000000.png",
        "stale"
    );
    var handler = new TestHttpMessageHandler(
        (_, _) => Task.FromResult(TestHttpMessageHandler.Bytes("fresh"))
    );
    using var service = new AssetDownloadService(() => temp.Path, handler);
    var request = AssetRequestValidation.Validate(
        "https://example.com/a.png",
        "00000000000000000000000000000000",
        "png"
    );

    var result = await service.DownloadAsync(request, TestContext.Current.CancellationToken);

    Assert.Equal(new Uri(finalPath).AbsoluteUri, result);
    Assert.Equal("fresh", File.ReadAllText(finalPath));
    Assert.Equal(1, handler.RequestCount);
  }

  [Fact]
  public async Task DownloadAsync_UnreadableMatchingFileIsAMiss()
  {
    using var temp = new TemporaryDirectory();
    var finalPath = PrepareFile(
        temp.Path,
        "ExponentAsset-5d41402abc4b2a76b9719d911017c592.png",
        "hello"
    );
    var exclusiveStream = File.Open(finalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    var handler = new TestHttpMessageHandler((_, _) =>
    {
      exclusiveStream.Dispose();
      return Task.FromResult(TestHttpMessageHandler.Bytes("fresh"));
    });
    using var service = new AssetDownloadService(() => temp.Path, handler);
    var request = AssetRequestValidation.Validate(
        "https://example.com/a.png",
        "5d41402abc4b2a76b9719d911017c592",
        "png"
    );

    var result = await service.DownloadAsync(request, TestContext.Current.CancellationToken);

    Assert.Equal(new Uri(finalPath).AbsoluteUri, result);
    Assert.Equal("fresh", File.ReadAllText(finalPath));
    Assert.Equal(1, handler.RequestCount);
  }

  [Fact]
  public async Task DownloadAsync_FreshBytesDoNotHaveToMatchSuppliedHash()
  {
    using var temp = new TemporaryDirectory();
    var handler = new TestHttpMessageHandler(
        (_, _) => Task.FromResult(TestHttpMessageHandler.Bytes("fresh"))
    );
    using var service = new AssetDownloadService(() => temp.Path, handler);
    var request = AssetRequestValidation.Validate(
        "https://example.com/a.png",
        "00000000000000000000000000000000",
        "png"
    );

    var result = await service.DownloadAsync(request, TestContext.Current.CancellationToken);

    Assert.Equal("fresh", File.ReadAllText(new Uri(result).LocalPath));
    Assert.Empty(DownloadFiles(temp.Path));
  }

  [Theory]
  [InlineData(HttpStatusCode.NotFound, 404)]
  [InlineData(HttpStatusCode.InternalServerError, 500)]
  public async Task DownloadAsync_NonSuccessStatusRejectsWithoutPartialFile(
      HttpStatusCode statusCode,
      int numericStatus)
  {
    using var temp = new TemporaryDirectory();
    var handler = new TestHttpMessageHandler(
        (_, _) => Task.FromResult(TestHttpMessageHandler.Bytes("failure", statusCode))
    );
    using var service = new AssetDownloadService(() => temp.Path, handler);
    var request = AssetRequestValidation.Validate("https://example.com/a.png", null, "png");

    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => service.DownloadAsync(request, TestContext.Current.CancellationToken)
    );

    Assert.StartsWith(
        "Unable to download asset from url: 'https://example.com/a.png'",
        exception.Message
    );
    Assert.Contains(numericStatus.ToString(), exception.Message);
    Assert.Empty(AllCacheFiles(temp.Path));
  }

  [Fact]
  public async Task DownloadAsync_HandlerFailureUsesDownloadErrorWithoutPathLeak()
  {
    using var temp = new TemporaryDirectory();
    var handler = new TestHttpMessageHandler(
        (_, _) => throw new HttpRequestException($"failed near {temp.Path}")
    );
    using var service = new AssetDownloadService(() => temp.Path, handler);
    var request = AssetRequestValidation.Validate("https://example.com/a.png", null, "png");

    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => service.DownloadAsync(request, TestContext.Current.CancellationToken)
    );

    Assert.Equal(
        "Unable to download asset from url: 'https://example.com/a.png'",
        exception.Message
    );
    Assert.IsType<HttpRequestException>(exception.InnerException);
    Assert.DoesNotContain(temp.Path, exception.Message);
    Assert.Empty(AllCacheFiles(temp.Path));
  }

  [Fact]
  public async Task DownloadAsync_HandlerCancellationWithoutCallerCancellationUsesDownloadError()
  {
    using var temp = new TemporaryDirectory();
    var handler = new TestHttpMessageHandler(
        (_, _) => throw new OperationCanceledException("simulated timeout")
    );
    using var service = new AssetDownloadService(() => temp.Path, handler);
    var request = AssetRequestValidation.Validate("https://example.com/a.png", null, "png");

    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => service.DownloadAsync(request, TestContext.Current.CancellationToken)
    );

    Assert.Equal(
        "Unable to download asset from url: 'https://example.com/a.png'",
        exception.Message
    );
    Assert.IsType<OperationCanceledException>(exception.InnerException);
    Assert.DoesNotContain(temp.Path, exception.Message);
    Assert.Empty(AllCacheFiles(temp.Path));
  }

  [Fact]
  public async Task DownloadAsync_ResponseStreamFailureDeletesTheTempFile()
  {
    using var temp = new TemporaryDirectory();
    var handler = new TestHttpMessageHandler((_, _) => Task.FromResult(
        new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StreamContent(new FailingReadStream()),
        }
    ));
    using var service = new AssetDownloadService(() => temp.Path, handler);
    var request = AssetRequestValidation.Validate("https://example.com/a.png", null, "png");

    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => service.DownloadAsync(request, TestContext.Current.CancellationToken)
    );

    Assert.Equal("Unable to save asset to directory: 'ExponentAsset'", exception.Message);
    Assert.Empty(DownloadFiles(temp.Path));
  }

  [Fact]
  public async Task DownloadAsync_ResponseCancellationWithoutCallerCancellationUsesSaveError()
  {
    using var temp = new TemporaryDirectory();
    var handler = new TestHttpMessageHandler((_, _) => Task.FromResult(
        new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StreamContent(new TimeoutReadStream()),
        }
    ));
    using var service = new AssetDownloadService(() => temp.Path, handler);
    var request = AssetRequestValidation.Validate("https://example.com/a.png", null, "png");

    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => service.DownloadAsync(request, TestContext.Current.CancellationToken)
    );

    Assert.Equal("Unable to save asset to directory: 'ExponentAsset'", exception.Message);
    Assert.IsType<OperationCanceledException>(exception.InnerException);
    Assert.DoesNotContain(temp.Path, exception.Message);
    Assert.Empty(DownloadFiles(temp.Path));
  }

  [Fact]
  public async Task DownloadAsync_MoveFailureDeletesTheTempFile()
  {
    using var temp = new TemporaryDirectory();
    var request = AssetRequestValidation.Validate(
        "https://example.com/a.png",
        "00000000000000000000000000000000",
        "png"
    );
    var finalPath = Path.Combine(
        temp.Path,
        "ExponentAsset",
        "ExponentAsset-00000000000000000000000000000000.png"
    );
    Directory.CreateDirectory(finalPath);
    var handler = new TestHttpMessageHandler(
        (_, _) => Task.FromResult(TestHttpMessageHandler.Bytes("fresh"))
    );
    using var service = new AssetDownloadService(() => temp.Path, handler);

    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => service.DownloadAsync(request, TestContext.Current.CancellationToken)
    );

    Assert.Equal("Unable to save asset to directory: 'ExponentAsset'", exception.Message);
    Assert.Empty(DownloadFiles(temp.Path));
  }

  [Fact]
  public async Task DownloadAsync_CancellationDeletesTheTempFile()
  {
    using var temp = new TemporaryDirectory();
    using var contentStream = new BlockingReadStream();
    var handler = new TestHttpMessageHandler((_, _) => Task.FromResult(
        new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StreamContent(contentStream),
        }
    ));
    using var service = new AssetDownloadService(() => temp.Path, handler);
    using var cancellation = new CancellationTokenSource();
    var request = AssetRequestValidation.Validate("https://example.com/a.png", null, "png");

    var download = service.DownloadAsync(request, cancellation.Token);
    await contentStream.ReadStarted;
    await cancellation.CancelAsync();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
    Assert.Empty(DownloadFiles(temp.Path));
  }

  [Fact]
  public async Task DownloadAsync_ConcurrentDifferentAssetsDoNotCrossContaminate()
  {
    using var temp = new TemporaryDirectory();
    var handler = new TestHttpMessageHandler((request, _) => Task.FromResult(
        TestHttpMessageHandler.Bytes(request.RequestUri!.AbsolutePath)
    ));
    using var service = new AssetDownloadService(() => temp.Path, handler);
    var first = AssetRequestValidation.Validate("https://example.com/first", null, "txt");
    var second = AssetRequestValidation.Validate("https://example.com/second", null, "txt");

    var results = await Task.WhenAll(
        service.DownloadAsync(first, TestContext.Current.CancellationToken),
        service.DownloadAsync(second, TestContext.Current.CancellationToken)
    );

    Assert.Equal("/first", File.ReadAllText(new Uri(results[0]).LocalPath));
    Assert.Equal("/second", File.ReadAllText(new Uri(results[1]).LocalPath));
  }

  [Fact]
  public async Task DownloadAsync_ConcurrentSameAssetUsesIndependentTempFiles()
  {
    using var temp = new TemporaryDirectory();
    var bothRequestsEntered = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    var enteredCount = 0;
    var handler = new TestHttpMessageHandler(async (_, cancellationToken) =>
    {
      if (Interlocked.Increment(ref enteredCount) == 2)
      {
        bothRequestsEntered.TrySetResult();
      }
      await bothRequestsEntered.Task.WaitAsync(cancellationToken);
      return TestHttpMessageHandler.Bytes("same");
    });
    using var service = new AssetDownloadService(() => temp.Path, handler);
    var request = AssetRequestValidation.Validate("https://example.com/same", null, "txt");

    var results = await Task.WhenAll(
        service.DownloadAsync(request, TestContext.Current.CancellationToken),
        service.DownloadAsync(request, TestContext.Current.CancellationToken)
    );

    Assert.Equal(results[0], results[1]);
    Assert.Equal("same", File.ReadAllText(new Uri(results[0]).LocalPath));
    Assert.Single(Directory.GetFiles(Path.Combine(temp.Path, "ExponentAsset")));
    Assert.Empty(DownloadFiles(temp.Path));
  }

  [Fact]
  public async Task DownloadAsync_CacheProviderFailureHasNoFallback()
  {
    var handler = new TestHttpMessageHandler((_, _) => throw new InvalidOperationException());
    using var service = new AssetDownloadService(
        () => throw new InvalidOperationException("host cache unavailable"),
        handler
    );
    var request = AssetRequestValidation.Validate("https://example.com/a.png", null, "png");

    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => service.DownloadAsync(request, TestContext.Current.CancellationToken)
    );

    Assert.Equal("host cache unavailable", exception.Message);
    Assert.Equal(0, handler.RequestCount);
  }

  private static string PrepareFile(string root, string fileName, string content)
  {
    var cacheDirectory = Path.Combine(root, "ExponentAsset");
    Directory.CreateDirectory(cacheDirectory);
    var finalPath = Path.Combine(cacheDirectory, fileName);
    File.WriteAllText(finalPath, content);
    return finalPath;
  }

  private static string[] DownloadFiles(string root)
  {
    var cacheDirectory = Path.Combine(root, "ExponentAsset");
    return Directory.Exists(cacheDirectory)
        ? Directory.GetFiles(cacheDirectory, "*.download")
        : [];
  }

  private static string[] AllCacheFiles(string root)
  {
    var cacheDirectory = Path.Combine(root, "ExponentAsset");
    return Directory.Exists(cacheDirectory)
        ? Directory.GetFiles(cacheDirectory)
        : [];
  }

  private sealed class FailingReadStream : MemoryStream
  {
    public override int Read(byte[] buffer, int offset, int count) =>
        throw new IOException("response read failed");

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        Task.FromException<int>(new IOException("response read failed"));

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<int>(new IOException("response read failed"));
  }

  private sealed class TimeoutReadStream : MemoryStream
  {
    public override int Read(byte[] buffer, int offset, int count) =>
        throw new OperationCanceledException("simulated response timeout");

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        Task.FromException<int>(
            new OperationCanceledException("simulated response timeout")
        );

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<int>(
            new OperationCanceledException("simulated response timeout")
        );
  }

  private sealed class BlockingReadStream : MemoryStream
  {
    private readonly TaskCompletionSource readStarted = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    internal Task ReadStarted => readStarted.Task;

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
      readStarted.TrySetResult();
      await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
      return 0;
    }
  }
}
