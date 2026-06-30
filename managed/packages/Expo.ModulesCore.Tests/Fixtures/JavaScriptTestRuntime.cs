using Expo.JSI;

namespace Expo.ModulesCore.Tests.Fixtures;

public sealed class JavaScriptTestRuntime
{
  private readonly JavaScriptRuntime runtime;
  private readonly nint testHostRuntime;

  internal JavaScriptTestRuntime(JavaScriptRuntime runtime, nint testHostRuntime)
  {
    this.runtime = runtime;
    this.testHostRuntime = testHostRuntime;
  }

  public JavaScriptValue Evaluate(string source, string sourceUrl = "expo-jsi-test.js")
  {
    return NativeTestHost.Evaluate(runtime, testHostRuntime, source, sourceUrl);
  }
}
