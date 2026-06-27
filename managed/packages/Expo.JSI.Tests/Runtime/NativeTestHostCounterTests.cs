using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

public sealed class NativeTestHostCounterTests
{
  [Fact]
  public void ReleaseCountersStayAttachedToTheirRuntime()
  {
    using var first = HermesRuntimeFixture.Create();
    using var second = HermesRuntimeFixture.Create();
    first.ResetCounters();
    second.ResetCounters();

    using (first.Runtime.CreateNumber(1))
    {
    }

    Assert.True(first.Counters.ReleasedValues >= 1);
    Assert.Equal(0u, second.Counters.ReleasedValues);
  }
}
