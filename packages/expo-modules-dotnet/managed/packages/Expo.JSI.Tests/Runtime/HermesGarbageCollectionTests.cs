using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

public sealed class HermesGarbageCollectionTests
{
  [Fact]
  public void CollectGarbageForTestingRunsOnTheHermesRuntimeExecutor()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.CollectGarbageForTesting();

    fixture.Runtime.Execute(_ =>
    {
      using var result = fixture.Evaluate("40 + 2", "collect-garbage-afterward.js");
      Assert.Equal(42, result.AsDouble());
      return true;
    });
  }

  [Fact]
  public async Task CollectGarbageForTestingWaitsForTheRuntimeExecutor()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.PauseRuntimeExecutor();
    var collection = Task.Run(
        fixture.CollectGarbageForTesting,
        TestContext.Current.CancellationToken
    );

    try
    {
      fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate);
      Assert.False(collection.IsCompleted);
    }
    finally
    {
      fixture.ResumeRuntimeExecutor();
      await collection;
    }

    fixture.Runtime.Execute(_ =>
    {
      using var result = fixture.Evaluate("40 + 2", "collect-garbage-after-sync.js");
      Assert.Equal(42, result.AsDouble());
      return true;
    });
  }
}
