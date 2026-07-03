using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests;

public sealed class RuntimeInvalidationTests
{
  [Fact]
  public async Task ScheduledWorkFailsAfterTesthostInvalidatesRuntime()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.InvalidateRuntimeForTesting();

    var error = await Assert.ThrowsAsync<InvalidOperationException>(
        () => fixture.Runtime.ScheduleAsync(
            _ => { },
            cancellationToken: TestContext.Current.CancellationToken
        )
    );
    Assert.Contains("runtime", error.Message, StringComparison.OrdinalIgnoreCase);
  }
}
