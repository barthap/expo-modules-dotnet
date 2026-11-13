using Expo.ModulesCore.Benchmarks;
using Xunit;

namespace Expo.ModulesCore.Tests.Benchmarks;

// The runner reports the median and flags noisy cases from these numbers, and sizes each sample
// from the calibration helpers. A wrong median or boundary would change which cases look noisy.
public sealed class BenchmarkStatisticsTests
{
  [Fact]
  public void Median_OddAndEvenCounts()
  {
    Assert.Equal(2, BenchmarkStatistics.Median([3, 1, 2]));
    Assert.Equal(2.5, BenchmarkStatistics.Median([4, 1, 3, 2]));
  }

  [Fact]
  public void Min_ReturnsSmallestValue()
  {
    Assert.Equal(1, BenchmarkStatistics.Min([3, 1, 2]));
  }

  [Fact]
  public void Spread_IsMaxOverMedian()
  {
    Assert.Equal(1.3, BenchmarkStatistics.Spread([10, 10, 10, 13]), 10);
  }

  [Fact]
  public void IsNoisy_BoundaryIsExclusive()
  {
    Assert.False(BenchmarkStatistics.IsNoisy([10, 10, 12]));
    Assert.True(BenchmarkStatistics.IsNoisy([10, 10, 12.1]));
  }

  [Fact]
  public void Step_IsMedianOverMin()
  {
    Assert.Equal(1.5, BenchmarkStatistics.Step([10, 15, 15]), 10);
  }

  // Seven slow samples and three fast ones: max over median is 1.0, so spread sees nothing, but the
  // typical sample is 56% above the fastest. That is the shape of a run that sits on a slow level.
  [Fact]
  public void IsStepped_SeesAMajorityOnASlowLevelThatSpreadMisses()
  {
    double[] values = [320, 320, 320, 320, 320, 320, 320, 205, 205, 205];

    Assert.True(BenchmarkStatistics.Spread(values) <= BenchmarkStatistics.NoisySpreadLimit);
    Assert.True(BenchmarkStatistics.IsStepped(values));
    Assert.True(BenchmarkStatistics.IsNoisy(values));
  }

  [Fact]
  public void IsStepped_BoundaryIsExclusive()
  {
    Assert.False(BenchmarkStatistics.IsStepped([10, 12, 12]));
    Assert.True(BenchmarkStatistics.IsStepped([10, 12.1, 12.1]));
  }

  [Fact]
  public void IsNoisy_IsTrueWhenOnlyTheSpreadIsAboveTheLimit()
  {
    double[] values = [10, 10, 10, 10, 13];

    Assert.False(BenchmarkStatistics.IsStepped(values));
    Assert.True(BenchmarkStatistics.IsNoisy(values));
  }

  [Fact]
  public void EmptyInputThrows()
  {
    double[] empty = [];
    Assert.Throws<ArgumentException>(() => BenchmarkStatistics.Median(empty));
    Assert.Throws<ArgumentException>(() => BenchmarkStatistics.Min(empty));
    Assert.Throws<ArgumentException>(() => BenchmarkStatistics.Spread(empty));
    Assert.Throws<ArgumentException>(() => BenchmarkStatistics.Step(empty));
    Assert.Throws<ArgumentException>(() => BenchmarkStatistics.IsStepped(empty));
    Assert.Throws<ArgumentException>(() => BenchmarkStatistics.IsNoisy(empty));
  }

  [Fact]
  public void NextIterations_ScalesTowardTarget()
  {
    Assert.Equal(2000, BenchmarkStatistics.NextIterations(1000, TimeSpan.FromMilliseconds(25)));
  }

  [Fact]
  public void NextIterations_DoublesAtLeastWhenFarBelowTarget()
  {
    Assert.Equal(2, BenchmarkStatistics.NextIterations(1, TimeSpan.Zero));
  }

  [Fact]
  public void CalibrationHelpers_UseAGivenTargetInsteadOfTheDefault()
  {
    var target = TimeSpan.FromMilliseconds(2);

    Assert.Equal(2000, BenchmarkStatistics.NextIterations(1000, TimeSpan.FromMilliseconds(1), target));
    Assert.True(BenchmarkStatistics.IsCalibrated(TimeSpan.FromMilliseconds(1), target));
    Assert.False(BenchmarkStatistics.IsCalibrated(TimeSpan.FromMilliseconds(1)));
  }

  [Fact]
  public void IsCalibrated_AtHalfTarget()
  {
    Assert.True(BenchmarkStatistics.IsCalibrated(TimeSpan.FromMilliseconds(25)));
    Assert.False(BenchmarkStatistics.IsCalibrated(TimeSpan.FromMilliseconds(24)));
  }
}
