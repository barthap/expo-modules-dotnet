namespace Expo.ModulesCore.Tests.Fixtures;

/// <summary>
/// A leaf codec ignores the runtime context and does not null-check it, so tests of leaf codecs
/// pass this value. A test that checks which context a codec receives creates a real
/// <see cref="DotnetRuntimeContext"/>.
/// </summary>
internal static class CodecTestContext
{
  public static DotnetRuntimeContext Unused => null!;
}
