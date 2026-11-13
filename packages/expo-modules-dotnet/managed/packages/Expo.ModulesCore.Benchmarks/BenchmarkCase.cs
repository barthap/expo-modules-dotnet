namespace Expo.ModulesCore.Benchmarks;

internal enum BenchmarkDirection
{
  None,
  Argument,
  Return,
  RoundTrip,
}

/// <param name="Name">Unique name of the case in the report.</param>
/// <param name="Direction">Which side of the call carries the measured value.</param>
/// <param name="Prelude">JavaScript statements that run once, before the loop.</param>
/// <param name="Call">JavaScript expression that makes the managed call. Empty for the floor.</param>
/// <param name="ControlCall">JavaScript expression on <c>__expoBenchNative</c> that does the same work in native code, or null.</param>
/// <param name="Verify">JavaScript expression that is true after one <paramref name="Call" />.</param>
/// <param name="IsAsync">Whether <paramref name="Call" /> returns a Promise.</param>
internal sealed record BenchmarkCase(
    string Name,
    BenchmarkDirection Direction,
    string Prelude,
    string Call,
    string? ControlCall,
    string Verify,
    bool IsAsync = false);
