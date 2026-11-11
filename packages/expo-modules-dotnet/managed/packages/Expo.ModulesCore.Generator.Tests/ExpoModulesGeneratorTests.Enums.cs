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

  // A usage-level [JSEnum] wins over the enum type's and reaches the enum inside Nullable<TEnum>. A
  // record field has no usage-level attribute, so it keeps the type's Number.
  [Fact]
  public void GeneratorLetsUsageLevelEnumRepresentationWin()
  {
    var result = GeneratorTestHost.Run(ModuleSource(
        "[JSEnum(EnumRepresentation.Number)] public enum TypeMode { Slow, Fast } " +
        "public enum PlainMode { Slow, Fast } " +
        "public sealed record Holder(TypeMode Mode);",
        "[JS] public string A([JSEnum(EnumRepresentation.String)] TypeMode mode) => \"\"; " +
        "[JS] public int B(Holder holder) => 0; " +
        "[JS] [return: JSEnum(EnumRepresentation.Number)] public PlainMode? C([JSEnum(EnumRepresentation.Number)] PlainMode? mode) => mode;"));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
    var text = GeneratedText(result);
    Assert.Contains("StringEnumCodec<global::Expo.TestModules.TypeMode>.Decode(arguments.GetValue(0), runtime)", text, StringComparison.Ordinal);
    Assert.Contains("NumberEnumCodec<global::Expo.TestModules.TypeMode>.Decode(modeValue, runtime)", text, StringComparison.Ordinal);
    Assert.Contains("NullableCodec<global::Expo.TestModules.PlainMode, NumberEnumCodec<global::Expo.TestModules.PlainMode>>.Decode(arguments.GetValue(0), runtime)", text, StringComparison.Ordinal);
    Assert.Contains("NullableCodec<global::Expo.TestModules.PlainMode, NumberEnumCodec<global::Expo.TestModules.PlainMode>>.Encode(", text, StringComparison.Ordinal);
  }
}
