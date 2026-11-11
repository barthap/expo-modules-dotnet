using Xunit;

namespace Expo.ModulesCore.Generator.Tests;

public sealed partial class ExpoModulesGeneratorTests
{
  // Pins the StringEnumCodec behavior that EXPOJSI035 changes: two members with one value compile,
  // and encode picks one of the names.
  [Fact]
  public void GeneratorAcceptsAliasedStringEnumMembersToday()
  {
    var result = GeneratorTestHost.Run(ModuleSource(
        "public enum Mode { Slow, Fast, Default = Fast }",
        "[JS] public Mode M(Mode mode) => mode;"));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
  }
}
