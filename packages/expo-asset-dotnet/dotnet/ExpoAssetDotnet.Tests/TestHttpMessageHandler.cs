namespace ExpoAssetDotnet.Tests;

internal sealed class TestHttpMessageHandler : HttpMessageHandler
{
  private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond;
  private int requestCount;

  internal TestHttpMessageHandler(
      Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respondArgument)
  {
    respond = respondArgument;
  }

  internal int RequestCount => Volatile.Read(ref requestCount);

  protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken cancellationToken)
  {
    Interlocked.Increment(ref requestCount);
    return respond(request, cancellationToken);
  }

  internal static HttpResponseMessage Bytes(
      string content,
      System.Net.HttpStatusCode statusCode = System.Net.HttpStatusCode.OK)
  {
    return new HttpResponseMessage(statusCode)
    {
      Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content)),
    };
  }
}
