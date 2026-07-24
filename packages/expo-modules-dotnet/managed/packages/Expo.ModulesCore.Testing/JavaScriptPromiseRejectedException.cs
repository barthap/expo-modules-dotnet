namespace Expo.ModulesCore.Testing;

public sealed class JavaScriptPromiseRejectedException : Exception
{
  internal JavaScriptPromiseRejectedException(
      string message,
      string? javaScriptName,
      string? javaScriptStack
  ) : base(message)
  {
    JavaScriptName = javaScriptName;
    JavaScriptStack = javaScriptStack;
  }

  public string? JavaScriptName { get; }

  public string? JavaScriptStack { get; }
}
