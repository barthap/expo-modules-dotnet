using Microsoft.CodeAnalysis;
using Xunit;

namespace Expo.ModulesCore.Generator.Tests;

public sealed partial class ExpoModulesGeneratorTests
{
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

  private const string EM = "System.Runtime.Serialization.EnumMember";

  private static string SpanText(string source, Diagnostic diagnostic) =>
      source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length);

  // A value that cannot cross is a build error at the member, and the using position reports its
  // own unsupported-type diagnostic, so no binding is emitted.
  [Theory]
  [InlineData("[" + EM + "(Value = \"\")] A, B", "A", "[EnumMember] Value is empty")]
  [InlineData("[" + EM + "(Value = null)] A, B", "A", "[EnumMember] Value is null; remove Value to cross as the member name")]
  [InlineData("[" + EM + "(Value = \"\\uD800\")] A, B", "A", "[EnumMember] Value contains a lone surrogate, which cannot cross to JavaScript")]
  [InlineData("A, [" + EM + "(Value = \"x\\uDC00\")] B", "B", "[EnumMember] Value contains a lone surrogate, which cannot cross to JavaScript")]
  public void GeneratorReportsInvalidEnumJavaScriptValues(string members, string member, string reason)
  {
    var source = ModuleSource($"public enum E {{ {members} }}", "[JS] public int M(E value) => 0;");
    var result = GeneratorTestHost.Run(source);

    AssertNoCompilerErrors(result);
    Assert.Equal(new[] { "EXPOJSI033", "EXPOJSI001" }, GeneratorDiagnosticIds(result));
    var diagnostic = result.GeneratorDiagnostics![0];
    Assert.Equal($"Enum 'Expo.TestModules.E' member '{member}' has an invalid JavaScript value: {reason}", diagnostic.GetMessage());
    Assert.Equal(member, SpanText(source, diagnostic));
    Assert.DoesNotContain("\"m\"", GeneratedText(result), StringComparison.Ordinal);
  }

  // A value on a number enum would be silently ignored, so it is an error at the member and the
  // enum has no codec anywhere, also where a usage-level String overrides the type. A usage-level
  // Number on an enum with a value is an error at that parameter only: D still binds.
  [Fact]
  public void GeneratorReportsEnumMemberValuesOnNumberEnums()
  {
    var source = ModuleSource(
        "[JSEnum(EnumRepresentation.Number)] public enum Level { [" + EM + "(Value = \"low\")] Low, High } " +
        "public enum DigestAlgorithm { MD5, [" + EM + "(Value = \"SHA-1\")] SHA1 }",
        "[JS] public int A(Level level) => 0; " +
        "[JS] public int B([JSEnum(EnumRepresentation.Number)] DigestAlgorithm algorithm) => 0; " +
        "[JS] public int C([JSEnum(EnumRepresentation.String)] Level level) => 0; " +
        "[JS] public int D(DigestAlgorithm algorithm) => 0;");
    var result = GeneratorTestHost.Run(source);

    AssertNoCompilerErrors(result);
    // C finds the same error at Low as A, so the provider reports it once.
    Assert.Equal(new[] { "EXPOJSI033", "EXPOJSI001", "EXPOJSI033", "EXPOJSI001", "EXPOJSI001" }, GeneratorDiagnosticIds(result));
    var values = result.GeneratorDiagnostics!.Where(item => item.Id == "EXPOJSI033").ToArray();
    Assert.Equal(
        "Enum 'Expo.TestModules.Level' member 'Low' has an invalid JavaScript value: it sets [EnumMember] Value, but [JSEnum(EnumRepresentation.Number)] on the enum crosses it as a number",
        values[0].GetMessage());
    Assert.Equal("Low", SpanText(source, values[0]));
    Assert.Equal(
        "Enum 'Expo.TestModules.DigestAlgorithm' member 'SHA1' has an invalid JavaScript value: it sets [EnumMember] Value, but [JSEnum(EnumRepresentation.Number)] at this use crosses it as a number",
        values[1].GetMessage());
    Assert.Equal("algorithm", SpanText(source, values[1]));
    var text = GeneratedText(result);
    Assert.Contains("\"d\"", text, StringComparison.Ordinal);
    Assert.DoesNotContain("\"a\"", text, StringComparison.Ordinal);
    Assert.DoesNotContain("\"b\"", text, StringComparison.Ordinal);
    Assert.DoesNotContain("\"c\"", text, StringComparison.Ordinal);
  }

  // [EnumMember] without Value names no string, so it changes nothing on a number enum.
  [Fact]
  public void GeneratorAcceptsEnumMemberWithoutValueOnNumberEnums()
  {
    var result = GeneratorTestHost.Run(ModuleSource(
        "[JSEnum(EnumRepresentation.Number)] public enum Level { [" + EM + "] Low = 1, High = 2 }",
        "[JS] public Level M(Level level) => level;"));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
    Assert.Contains("NumberEnumCodec<global::Expo.TestModules.Level>.Decode(arguments.GetValue(0), runtime)", GeneratedText(result), StringComparison.Ordinal);
  }

  // Encode cannot pick one string for two members with one value. A pair with the same value and
  // the same string reports only EXPOJSI035, because the shared value is the cause.
  [Theory]
  [InlineData("Slow, Fast, Default = Fast", "Fast", "Default", "1")]
  [InlineData("A = 1, B = 1", "A", "B", "1")]
  [InlineData("[" + EM + "(Value = \"x\")] A = 2, [" + EM + "(Value = \"x\")] B = 2", "A", "B", "2")]
  public void GeneratorReportsAliasedStringEnumMembers(string members, string earlier, string later, string value)
  {
    var source = ModuleSource($"public enum E {{ {members} }}", "[JS] public int M(E value) => 0;");
    var result = GeneratorTestHost.Run(source);

    AssertNoCompilerErrors(result);
    Assert.Equal(new[] { "EXPOJSI035", "EXPOJSI001" }, GeneratorDiagnosticIds(result));
    var diagnostic = result.GeneratorDiagnostics![0];
    Assert.Equal(
        $"Enum 'Expo.TestModules.E' members '{earlier}' and '{later}' have the same value {value}, so a string enum cannot tell them apart",
        diagnostic.GetMessage());
    Assert.Equal(later, SpanText(source, diagnostic));
  }

  // A number enum may alias members; the string checks run only where the enum crosses as a string.
  [Fact]
  public void GeneratorAcceptsAliasedMembersWhereTheEnumCrossesAsANumber()
  {
    var result = GeneratorTestHost.Run(ModuleSource(
        "[JSEnum(EnumRepresentation.Number)] public enum Mode { Slow, Fast, Default = Fast } public enum Plain { A = 1, B = 1 }",
        "[JS] public int M(Mode mode) => 0; [JS] public int N([JSEnum(EnumRepresentation.Number)] Plain plain) => 0;"));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
  }

  [Fact]
  public void GeneratorReportsAliasesWhereAUsageLevelStringUsesANumberEnum()
  {
    var source = ModuleSource(
        "[JSEnum(EnumRepresentation.Number)] public enum Mode { Slow, Fast, Default = Fast }",
        "[JS] public int M(Mode mode) => 0; [JS] public int N([JSEnum(EnumRepresentation.String)] Mode mode) => 0;");
    var result = GeneratorTestHost.Run(source);

    AssertNoCompilerErrors(result);
    Assert.Equal(new[] { "EXPOJSI035", "EXPOJSI001" }, GeneratorDiagnosticIds(result));
    var text = GeneratedText(result);
    Assert.Contains("\"m\"", text, StringComparison.Ordinal);
    Assert.DoesNotContain("\"n\"", text, StringComparison.Ordinal);
  }

  // Decode needs one member per string. A string written with [EnumMember] and a member name collide
  // like two written strings do; [EnumMember] without Value means the member name.
  [Theory]
  [InlineData("[" + EM + "(Value = \"B\")] A, B", "A", "B", "B")]
  [InlineData("A, [" + EM + "(Value = \"A\")] B", "A", "B", "A")]
  [InlineData("[" + EM + "(Value = \"x\")] A, [" + EM + "(Value = \"x\")] B", "A", "B", "x")]
  [InlineData("[" + EM + "] A, [" + EM + "(Value = \"A\")] B", "A", "B", "A")]
  public void GeneratorReportsDuplicateEnumJavaScriptValues(string members, string earlier, string later, string value)
  {
    var source = ModuleSource($"public enum E {{ {members} }}", "[JS] public int M(E value) => 0;");
    var result = GeneratorTestHost.Run(source);

    AssertNoCompilerErrors(result);
    Assert.Equal(new[] { "EXPOJSI034", "EXPOJSI001" }, GeneratorDiagnosticIds(result));
    var diagnostic = result.GeneratorDiagnostics![0];
    Assert.Equal(
        $"Enum 'Expo.TestModules.E' members '{earlier}' and '{later}' both cross to JavaScript as \"{value}\"",
        diagnostic.GetMessage());
    Assert.Equal(later, SpanText(source, diagnostic));
  }

  // Matching is ordinal, so strings that differ in case are distinct; a surrogate pair encodes; an
  // enum with no members compiles to a codec that always fails.
  [Theory]
  [InlineData("A, [" + EM + "(Value = \"a\")] B")]
  [InlineData("[" + EM + "] A, B")]
  [InlineData("[" + EM + "(Value = \"\\uD83D\\uDE00\")] Smile, B")]
  [InlineData("")]
  public void GeneratorAcceptsValidStringEnumDeclarations(string members)
  {
    var result = GeneratorTestHost.Run(ModuleSource($"public enum E {{ {members} }}", "[JS] public E M(E value) => value;"));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
  }

  // Every position that uses the enum finds the same error; the provider reports it once.
  [Fact]
  public void GeneratorReportsAnInvalidEnumOncePerDeclaration()
  {
    var result = GeneratorTestHost.Run(ModuleSource(
        "public enum E { [" + EM + "(Value = \"B\")] A, B } " +
        "public sealed record Holder(E Value); " +
        "[ExpoSharedObject] public sealed partial class EnumEntry : SharedObject { [JS] public int Use(E value) => 0; } " +
        "[ExpoModule(\"Second\", Classes = new[] { typeof(EnumEntry) })] public sealed partial class SecondModule { [JS] public int M(E value) => 0; }",
        "[JS] public int A(E value) => 0; [JS] public int B(Holder holder) => 0; [JS] public int C(IReadOnlyList<E> values) => 0; [JS] public E D() => default;"));

    AssertNoCompilerErrors(result);
    var ids = GeneratorDiagnosticIds(result);
    Assert.Single(ids, id => id == "EXPOJSI034");
    Assert.Contains("EXPOJSI001", ids);
    Assert.Contains("EXPOJSI002", ids);
    Assert.Contains("EXPOJSI007", ids);
    Assert.Contains("EXPOJSI023", ids);
  }

  // An enum that never crosses is not analyzed.
  [Fact]
  public void GeneratorIgnoresInvalidEnumsThatNeverCross()
  {
    var result = GeneratorTestHost.Run(ModuleSource(
        "public enum E { [" + EM + "(Value = \"\")] A, B = 0 }",
        "[JS] public int Ok() => 0;"));

    Assert.Empty(GeneratorDiagnosticIds(result));
  }
}
