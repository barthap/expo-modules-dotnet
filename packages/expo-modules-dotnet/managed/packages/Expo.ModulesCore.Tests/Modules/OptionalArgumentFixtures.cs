using System;
using System.Threading.Tasks;
using Expo.JSI;
using Expo.ModulesCore.Codecs;

namespace Expo.ModulesCore.Tests.Modules;

[ExpoModule("OptionalArguments", Classes = new[] { typeof(OptionalBox) })]
public sealed partial class OptionalArgumentsModule
{
  [JS]
  public string Describe(string first, string? second, int? third) =>
      $"{first}|{second ?? "null"}|{third?.ToString() ?? "null"}";

  [JS]
  public string RequiredOnly(string first) => first;

  [JS]
  public string NullableBeforeRequired(string? first, string second) =>
      $"{first ?? "null"}|{second}";

  [JS]
  public string NullableThenDefaulted(string? first, string second = "fallback") =>
      $"{first ?? "null"}|{second}";

  [JS]
  public async Task<string> DescribeAsync(string first, string? second, int? third)
  {
    await Task.Yield();
    return Describe(first, second, third);
  }

  [JS]
  public int SumThenLabel(ReadOnlySpan<byte> bytes, string? label)
  {
    var sum = 0;
    foreach (var value in bytes)
    {
      sum += value;
    }

    return label is null ? sum : sum + label.Length;
  }
}

[ExpoSharedObject]
public sealed partial class OptionalBox : SharedObject
{
  private readonly string description;

  [JS]
  public OptionalBox(string first, string? second, int? third)
  {
    description = $"{first}|{second ?? "null"}|{third?.ToString() ?? "null"}";
  }

  [JS]
  public string Describe(string first, string? second, int? third) =>
      $"{description}/{first}|{second ?? "null"}|{third?.ToString() ?? "null"}";

  [JS]
  public async Task<string> DescribeAsync(string first, string? second)
  {
    await Task.Yield();
    return $"{description}/{first}|{second ?? "null"}";
  }
}
