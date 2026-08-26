using Xunit;

namespace ExampleModule.Tests;

public sealed class ExampleCounterTests
{
  [Fact]
  public void IncrementUpdatesCount()
  {
    var counter = new global::ExampleModule.ExampleCounter(10);

    var result = counter.Increment(2);

    Assert.Equal(12, result);
    Assert.Equal(12, counter.Count);
  }
}
