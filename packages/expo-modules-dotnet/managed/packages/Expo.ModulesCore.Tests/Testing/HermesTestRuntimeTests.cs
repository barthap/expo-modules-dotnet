using Expo.ModulesCore.Testing;
using Expo.ModulesCore.Testing.Internal;
using Xunit;

namespace Expo.ModulesCore.Tests.Testing;

public sealed class HermesTestRuntimeTests
{
  [Fact]
  public void RuntimeEvaluatesJavaScriptAndDisposesIdempotently()
  {
    var testRuntime = HermesTestRuntime.Create();
    testRuntime.Runtime.Execute(_ =>
    {
      using var result = testRuntime.Evaluate("20 + 22", "hermes-test-runtime.js");
      Assert.Equal(42, result.AsDouble());
      return true;
    });
    testRuntime.WaitUntilIdle();
    testRuntime.DrainTasks();
    testRuntime.Dispose();
    testRuntime.Dispose();
  }

  [Fact]
  public void MissingLibraryConfigurationNamesCanonicalRunner()
  {
    var exception = Assert.Throws<InvalidOperationException>(
        () => NativeTestHost.ValidateLibraryPath(null)
    );

    Assert.Contains("EXPO_JSI_TESTHOST_LIBRARY", exception.Message);
    Assert.Contains("scripts/test-managed", exception.Message);
  }

  [Fact]
  public void MissingLibraryFileNamesCanonicalRunner()
  {
    var missingPath = Path.Combine(
        Path.GetTempPath(),
        $"missing-testhost-{Guid.NewGuid():N}"
    );
    var exception = Assert.Throws<FileNotFoundException>(
        () => NativeTestHost.ValidateLibraryPath(missingPath)
    );

    Assert.Contains("scripts/test-managed", exception.Message);
  }
}
