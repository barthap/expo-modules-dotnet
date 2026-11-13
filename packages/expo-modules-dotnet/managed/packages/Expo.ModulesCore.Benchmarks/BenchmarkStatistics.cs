namespace Expo.ModulesCore.Benchmarks;

/// <summary>Sample statistics and calibration arithmetic for the benchmark runner.</summary>
internal static class BenchmarkStatistics
{
  /// <summary>A case is noisy when its slowest sample exceeds its median by more than this ratio.</summary>
  public const double NoisySpreadLimit = 1.20;

  public const int SampleCount = 10;

  public static readonly TimeSpan SampleTarget = TimeSpan.FromMilliseconds(50);

  /// <summary>Median of an even count is the mean of the two middle values.</summary>
  public static double Median(IReadOnlyList<double> values)
  {
    RequireValues(values);
    var sorted = values.OrderBy(value => value).ToArray();
    var middle = sorted.Length / 2;
    return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
  }

  public static double Min(IReadOnlyList<double> values)
  {
    RequireValues(values);
    return values.Min();
  }

  /// <summary>Largest value divided by the median.</summary>
  public static double Spread(IReadOnlyList<double> values)
  {
    return values.Count == 0 ? throw EmptyValues() : values.Max() / Median(values);
  }

  public static bool IsNoisy(IReadOnlyList<double> values)
  {
    return Spread(values) > NoisySpreadLimit;
  }

  /// <summary>
  /// Next iteration count for calibration: scales <paramref name="iterations"/> so the next sample lands
  /// on <see cref="SampleTarget"/>, at least doubling while <paramref name="elapsed"/> is under a tenth of
  /// the target, never below 1.
  /// </summary>
  public static int NextIterations(int iterations, TimeSpan elapsed)
  {
    var doubled = (double)iterations * 2;
    var scaled = elapsed <= TimeSpan.Zero
      ? doubled
      : iterations * (SampleTarget.TotalMilliseconds / elapsed.TotalMilliseconds);
    var next = elapsed < SampleTarget / 10 ? Math.Max(scaled, doubled) : scaled;
    return (int)Math.Clamp(Math.Round(next), 1, int.MaxValue);
  }

  public static bool IsCalibrated(TimeSpan elapsed)
  {
    return elapsed >= SampleTarget / 2;
  }

  private static void RequireValues(IReadOnlyList<double> values)
  {
    if (values.Count == 0)
    {
      throw EmptyValues();
    }
  }

  private static ArgumentException EmptyValues()
  {
    return new ArgumentException("At least one value is required.", "values");
  }
}
