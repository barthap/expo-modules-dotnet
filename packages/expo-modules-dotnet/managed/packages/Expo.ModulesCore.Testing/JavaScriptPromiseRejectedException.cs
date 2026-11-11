namespace Expo.ModulesCore.Testing;

public sealed class JavaScriptPromiseRejectedException : Exception
{
  internal JavaScriptPromiseRejectedException(
      string message,
      string? javaScriptName,
      string? javaScriptStack,
      string? javaScriptCode
  ) : base(message)
  {
    JavaScriptName = javaScriptName;
    JavaScriptStack = javaScriptStack;
    JavaScriptCode = javaScriptCode;
  }

  public string? JavaScriptName { get; }

  public string? JavaScriptStack { get; }

  /// <summary>
  /// Gets the rejection's <c>code</c> property as a string, or <see langword="null" /> when the
  /// rejection is not an <c>Error</c> or its <c>code</c> is missing, <c>null</c> or <c>undefined</c>.
  /// </summary>
  public string? JavaScriptCode { get; }
}
