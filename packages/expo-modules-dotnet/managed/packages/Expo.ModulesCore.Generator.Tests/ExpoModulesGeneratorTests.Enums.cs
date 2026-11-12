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
    Assert.Contains($"{EnumCodecName("TypeMode", "global::Expo.TestModules.TypeMode")}.Decode(arguments.GetValue(0), runtime)", text, StringComparison.Ordinal);
    Assert.Contains("NumberEnumCodec<global::Expo.TestModules.TypeMode>.Decode(modeValue, runtime)", text, StringComparison.Ordinal);
    Assert.Contains("NullableCodec<global::Expo.TestModules.PlainMode, NumberEnumCodec<global::Expo.TestModules.PlainMode>>.Decode(arguments.GetValue(0), runtime)", text, StringComparison.Ordinal);
    Assert.Contains("NullableCodec<global::Expo.TestModules.PlainMode, NumberEnumCodec<global::Expo.TestModules.PlainMode>>.Encode(", text, StringComparison.Ordinal);
  }

  // A return attribute reaches the result of a Task, also inside Nullable<TEnum>, and not a
  // collection of enums.
  [Fact]
  public void GeneratorAppliesAReturnLevelEnumRepresentationToAnAsyncResult()
  {
    var result = GeneratorTestHost.Run(ModuleSource(
        "public enum PlainMode { Slow, Fast }",
        "[JS] [return: JSEnum(EnumRepresentation.Number)] public Task<PlainMode> A() => Task.FromResult(PlainMode.Slow); " +
        "[JS] [return: JSEnum(EnumRepresentation.Number)] public Task<PlainMode?> B() => Task.FromResult<PlainMode?>(null); " +
        "[JS] [return: JSEnum(EnumRepresentation.Number)] public Task<IReadOnlyList<PlainMode>> C() => Task.FromResult<IReadOnlyList<PlainMode>>(new List<PlainMode>());"));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
    var text = GeneratedText(result);
    Assert.Contains("NumberEnumCodec<global::Expo.TestModules.PlainMode>.Encode(", text, StringComparison.Ordinal);
    Assert.Contains("NullableCodec<global::Expo.TestModules.PlainMode, NumberEnumCodec<global::Expo.TestModules.PlainMode>>.Encode(", text, StringComparison.Ordinal);
    Assert.Contains($"JavaScriptArrayCodec<global::Expo.TestModules.PlainMode, {EnumCodecName("PlainMode", "global::Expo.TestModules.PlainMode")}>", text, StringComparison.Ordinal);
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

  private static string EnumCodecName(string simpleName, string fullyQualifiedTypeName) =>
      $"{simpleName}EnumCodec_{string.Concat(fullyQualifiedTypeName.Select(character => ((ushort)character).ToString("X4", System.Globalization.CultureInfo.InvariantCulture)))}";

  // The enum codec struct of one enum, from its declaration to (not including) its closing brace.
  private static string EnumCodecText(string text, string enumName)
  {
    var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
    var start = normalized.IndexOf($"private readonly struct {enumName}EnumCodec_", StringComparison.Ordinal);
    Assert.True(start >= 0);
    return normalized[start..normalized.IndexOf("\n  }\n", start, StringComparison.Ordinal)];
  }

  private const string DigestAlgorithmSource =
      "public enum DigestAlgorithm { MD5, [" + EM + "(Value = \"SHA-1\")] SHA1, [" + EM + "(Value = \"SHA-256\")] SHA256 }";

  private static readonly string DigestAlgorithmCodec =
      EnumCodecName("DigestAlgorithm", "global::Expo.TestModules.DigestAlgorithm");

  // The emitted codec is two constant switches, so it needs no reflection and runs under NativeAOT.
  // Keyword member names and values with quotes, backslashes, braces, or newlines must compile and
  // keep the runtime string verbatim.
  [Fact]
  public void GeneratorEmitsAStringEnumCodecWithDeclaredStrings()
  {
    var result = GeneratorTestHost.Run(ModuleSource(
        """
        public enum DigestAlgorithm
        {
          MD5,
          [System.Runtime.Serialization.EnumMember(Value = "SHA-1")] SHA1,
          [System.Runtime.Serialization.EnumMember] SHA256,
          @class,
          [System.Runtime.Serialization.EnumMember(Value = "q\"b\\{x}\n")] Odd,
        }
        """,
        "[JS] public DigestAlgorithm Echo(DigestAlgorithm algorithm) => algorithm;"));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
    var text = GeneratedText(result);
    var codecName = DigestAlgorithmCodec;
    Assert.Equal(
        $$"""
        private readonly struct {{codecName}} : global::Expo.ModulesCore.Codecs.IJavaScriptCodec<global::Expo.TestModules.DigestAlgorithm>
          {
            public static global::Expo.TestModules.DigestAlgorithm Decode(global::Expo.JSI.JavaScriptValueRef value, global::Expo.JSI.JavaScriptRuntime runtime) => FromJavaScriptString(value.AsString());

            public static global::Expo.TestModules.DigestAlgorithm Decode(global::Expo.JSI.JavaScriptValue value, global::Expo.JSI.JavaScriptRuntime runtime) => FromJavaScriptString(value.AsString());

            public static global::Expo.JSI.JavaScriptValue Encode(global::Expo.TestModules.DigestAlgorithm value, global::Expo.JSI.JavaScriptRuntime runtime) => runtime.CreateString(ToJavaScriptString(value));

            private static global::Expo.TestModules.DigestAlgorithm FromJavaScriptString(string value) => value switch
            {
              "MD5" => global::Expo.TestModules.DigestAlgorithm.MD5,
              "SHA-1" => global::Expo.TestModules.DigestAlgorithm.SHA1,
              "SHA256" => global::Expo.TestModules.DigestAlgorithm.SHA256,
              "class" => global::Expo.TestModules.DigestAlgorithm.@class,
              "q\"b\\{x}\n" => global::Expo.TestModules.DigestAlgorithm.Odd,
              _ => throw new global::System.ArgumentException("'" + value + "' is not a valid Expo.TestModules.DigestAlgorithm value. It must be one of: 'MD5', 'SHA-1', 'SHA256', 'class', 'q\"b\\{x}\n'"),
            };

            private static string ToJavaScriptString(global::Expo.TestModules.DigestAlgorithm value) => value switch
            {
              global::Expo.TestModules.DigestAlgorithm.MD5 => "MD5",
              global::Expo.TestModules.DigestAlgorithm.SHA1 => "SHA-1",
              global::Expo.TestModules.DigestAlgorithm.SHA256 => "SHA256",
              global::Expo.TestModules.DigestAlgorithm.@class => "class",
              global::Expo.TestModules.DigestAlgorithm.Odd => "q\"b\\{x}\n",
              _ => throw new global::System.InvalidOperationException(((int)value).ToString(global::System.Globalization.CultureInfo.InvariantCulture) + " is not a member of Expo.TestModules.DigestAlgorithm, so it has no JavaScript value."),
            };
        """,
        EnumCodecText(text, "DigestAlgorithm"));
    Assert.Contains($"{codecName}.Decode(arguments.GetValue(0), runtime)", text, StringComparison.Ordinal);
    Assert.Contains($"{codecName}.Encode(module.Echo(__expoArg0), runtime)", text, StringComparison.Ordinal);
  }

  // One codec per enum per provider, whatever the number of modules, shared objects, and positions;
  // no runtime enum API anywhere in it; and no leaf release, because the codec holds no leaves.
  [Fact]
  public void GeneratorEmitsOneEnumCodecPerProvider()
  {
    var result = GeneratorTestHost.Run(ModuleSource(
        DigestAlgorithmSource + " public sealed record Options(DigestAlgorithm Algorithm); " +
        "[ExpoSharedObject] public sealed partial class Hasher : SharedObject { [JS] public DigestAlgorithm Swap(DigestAlgorithm next) => next; } " +
        "[ExpoModule(\"Second\", Classes = new[] { typeof(Hasher) })] public sealed partial class SecondModule { [JS] public DigestAlgorithm? M(IReadOnlyList<DigestAlgorithm> values) => null; }",
        "[JS] public DigestAlgorithm A(DigestAlgorithm algorithm) => algorithm; [JS] public int B(Options options) => 0; " +
        "[JS] public Task<DigestAlgorithm> C() => Task.FromResult(DigestAlgorithm.MD5); [JS] public DigestAlgorithm Preferred { get; set; }"));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
    var text = GeneratedText(result);
    Assert.Equal(1, CountOccurrences(text, $"private readonly struct {DigestAlgorithmCodec} :"));
    Assert.DoesNotContain("StringEnumCodec", text, StringComparison.Ordinal);
    Assert.DoesNotContain("CodecRelease.Release<global::Expo.TestModules.DigestAlgorithm", text, StringComparison.Ordinal);
    var codec = EnumCodecText(text, "DigestAlgorithm");
    foreach (var forbidden in new[] { "Enum.TryParse", "Enum.IsDefined", "Enum.GetName", "GetType(", "typeof(", "value.ToString(", "public static void Release" })
    {
      Assert.DoesNotContain(forbidden, codec, StringComparison.Ordinal);
    }
  }

  // Union and event analysis resolve payloads in a scratch collector and adopt it on success, so an
  // enum reached only there still gets its codec emitted.
  [Theory]
  [InlineData("[Union] public abstract record Choice { public sealed record Pick(DigestAlgorithm Value) : Choice; public sealed record Count(int Value) : Choice; }", "[JS] public int M(Choice value) => 0;")]
  [InlineData("", "[Event] public partial Func<DigestAlgorithm, Task> OnPick { get; }")]
  public void GeneratorEmitsEnumCodecsReachedOnlyThroughAUnionOrAnEvent(string declarations, string member)
  {
    var result = GeneratorTestHost.Run(ModuleSource(DigestAlgorithmSource + " " + declarations, member));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
    Assert.Equal(1, CountOccurrences(GeneratedText(result), $"private readonly struct {DigestAlgorithmCodec} :"));
  }

  // The encode error casts to the underlying type, so it never calls Enum.ToString and compiles for
  // every underlying type. A nested enum is named as C# writes it, with a dot.
  [Fact]
  public void GeneratorRendersEnumEncodeErrorsFromTheUnderlyingType()
  {
    var result = GeneratorTestHost.Run(ModuleSource(
        "public enum Wide : ulong { Max = ulong.MaxValue } public enum Signed : sbyte { Minus = -1 } public static class Outer { public enum Mode { Slow } }",
        "[JS] public Wide A(Wide value) => value; [JS] public Signed B(Signed value) => value; [JS] public Outer.Mode C(Outer.Mode value) => value;"));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
    var text = GeneratedText(result);
    Assert.Contains(
        "_ => throw new global::System.InvalidOperationException(((ulong)value).ToString(global::System.Globalization.CultureInfo.InvariantCulture) + \" is not a member of Expo.TestModules.Wide, so it has no JavaScript value.\"),",
        EnumCodecText(text, "Wide"),
        StringComparison.Ordinal);
    Assert.Contains("((sbyte)value).ToString(global::System.Globalization.CultureInfo.InvariantCulture) + \" is not a member of Expo.TestModules.Signed,", EnumCodecText(text, "Signed"), StringComparison.Ordinal);
    Assert.Contains("is not a valid Expo.TestModules.Outer.Mode value. It must be one of: 'Slow'", EnumCodecText(text, "Mode"), StringComparison.Ordinal);
  }

  // The messages name the enum by its C# name without `global::`: type arguments of a generic
  // container are qualified the same way, and a keyword identifier shows without its `@`.
  [Theory]
  [InlineData("public static class Outer<T> { public enum Mode { Slow } }", "Outer<System.Guid>.Mode", "Expo.TestModules.Outer<System.Guid>.Mode")]
  [InlineData("public enum @event { Slow }", "@event", "Expo.TestModules.event")]
  public void GeneratorNamesTheEnumWithoutGlobalOrEscapesInRuntimeErrors(string declaration, string use, string expectedName)
  {
    var result = GeneratorTestHost.Run(ModuleSource(declaration, $"[JS] public {use} A({use} value) => value;"));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
    var codec = EnumCodecText(GeneratedText(result), use.Split('.').Last().TrimStart('@'));
    Assert.Contains($"is not a valid {expectedName} value. It must be one of: 'Slow'", codec, StringComparison.Ordinal);
    Assert.Contains($" is not a member of {expectedName}, so it has no JavaScript value.", codec, StringComparison.Ordinal);
  }

  // The default of a parameter and of a record field is a cast of the member's number; it never goes
  // through a string, so a declared value cannot change which member is the default.
  [Fact]
  public void GeneratorRendersDefaultsOfRawValueEnumsAsCasts()
  {
    var result = GeneratorTestHost.Run(ModuleSource(
        DigestAlgorithmSource + " public sealed record Settings(DigestAlgorithm Algorithm = DigestAlgorithm.SHA1);",
        "[JS] public int M(Settings settings, DigestAlgorithm algorithm = DigestAlgorithm.SHA1) => 0;"));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
    // Both record Decode overloads and the host function render the default.
    Assert.Equal(
        3,
        CountOccurrences(GeneratedText(result), $"Undefined ? (global::Expo.TestModules.DigestAlgorithm)(1) : {DigestAlgorithmCodec}.Decode("));
  }

  // Dictionary keys are strings only, here and upstream, so an enum key stays unsupported.
  [Fact]
  public void GeneratorRejectsEnumDictionaryKeys()
  {
    var result = GeneratorTestHost.Run(ModuleSource(DigestAlgorithmSource, "[JS] public int M(Dictionary<DigestAlgorithm, int> values) => 0;"));

    AssertNoCompilerErrors(result);
    Assert.Equal(new[] { "EXPOJSI001" }, GeneratorDiagnosticIds(result));
  }

  private const string BadEnumSource = "public enum Bad { [" + EM + "(Value = \"B\")] A, B }";

  private const string BadChoiceSource =
      "[Union] public abstract record Choice { public sealed record Pick(Bad Value) : Choice; public sealed record Text(string Value) : Choice; }";

  // The union case names the enum as the reason, and the enum's own error is reported once where the
  // union is used, by the provider.
  [Fact]
  public void GeneratorReportsAnInvalidEnumUsedOnlyAsAUnionPayload()
  {
    var result = GeneratorTestHost.Run(ModuleSource(BadEnumSource + " " + BadChoiceSource, "[JS] public int M(Choice value) => 0;"));

    AssertNoCompilerErrors(result);
    var ids = GeneratorDiagnosticIds(result);
    Assert.Single(ids, id => id == "EXPOJSI034");
    Assert.Contains("EXPOJSI001", ids);
    var unionCase = Assert.Single(result.GeneratorDiagnostics!, item => item.Id == "EXPOJSI030");
    Assert.Equal(
        "Type 'Expo.TestModules.Choice.Pick' is not a valid case of union 'Expo.TestModules.Choice': its payload enum 'Expo.TestModules.Bad' has an invalid declaration (EXPOJSI034)",
        unionCase.GetMessage());
  }

  // The [Union] declaration pipeline reports outside the provider, so it never reports the enum's
  // own diagnostics; an unused union still names the enum in its case reason.
  [Fact]
  public void GeneratorNamesTheInvalidEnumOfAnUnusedUnion()
  {
    var result = GeneratorTestHost.Run(ModuleSource(BadEnumSource + " " + BadChoiceSource, "[JS] public int Ok() => 0;"));

    Assert.Equal(new[] { "EXPOJSI030" }, GeneratorDiagnosticIds(result));
    Assert.EndsWith(
        "its payload enum 'Expo.TestModules.Bad' has an invalid declaration (EXPOJSI034)",
        result.GeneratorDiagnostics!.Single().GetMessage());
  }

  [Fact]
  public void GeneratorReportsAnInvalidEnumOnceAcrossAUnionAndAParameter()
  {
    var result = GeneratorTestHost.Run(ModuleSource(
        BadEnumSource + " " + BadChoiceSource,
        "[JS] public int M(Choice value) => 0; [JS] public int N(Bad value) => 0;"));

    Assert.Single(GeneratorDiagnosticIds(result), id => id == "EXPOJSI034");
  }

  [Fact]
  public void GeneratorReportsAnInvalidEnumUsedOnlyAsAnEventPayload()
  {
    var result = GeneratorTestHost.Run(ModuleSource(BadEnumSource, "[Event] public partial Func<Bad, Task> OnBad { get; }"));

    AssertNoCompilerErrors(result);
    var ids = GeneratorDiagnosticIds(result);
    Assert.Single(ids, id => id == "EXPOJSI034");
    Assert.Contains("EXPOJSI019", ids);
  }

  // A string enum is kind String: each enum case is attempted on a string, and the generated enum
  // codec is the payload codec.
  [Fact]
  public void GeneratorGatesStringEnumUnionCasesOnStrings()
  {
    var result = GeneratorTestHost.Run(ModuleSource(
        DigestAlgorithmSource + " public enum DigestEncoding { [" + EM + "(Value = \"hex\")] Hex } " +
        "[Union] public abstract record Choice { public sealed record Pick(DigestAlgorithm Value) : Choice; public sealed record Output(DigestEncoding Value) : Choice; public sealed record Text(string Value) : Choice; }",
        "[JS] public int M(Choice value) => 0; [JS] public Choice N() => null!;"));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
    var codec = UnionCodecText(GeneratedText(result), "Choice");
    Assert.Equal(3, CountOccurrences(codec, "if (__expoKind == global::Expo.JSI.JavaScriptValueKind.String)"));
    Assert.Contains($"__expoPayload0 = {DigestAlgorithmCodec}.Decode(value, runtime);", codec, StringComparison.Ordinal);
    Assert.Contains("new[] { \"DigestAlgorithm\", \"DigestEncoding\", \"string\" }", codec, StringComparison.Ordinal);
  }

  // A string case takes every string, so a later enum case is unreachable. The other
  // order is reachable: a string the enum does not declare fails the enum attempt and moves on.
  [Fact]
  public void GeneratorReportsAnEnumCaseAfterAStringCaseAsUnreachable()
  {
    var unreachable = GeneratorTestHost.Run(ModuleSource(
        DigestAlgorithmSource + " [Union] public abstract record Choice { public sealed record Text(string Value) : Choice; public sealed record Pick(DigestAlgorithm Value) : Choice; }",
        "[JS] public int M(Choice value) => 0;"));
    var reachable = GeneratorTestHost.Run(ModuleSource(
        DigestAlgorithmSource + " [Union] public abstract record Choice { public sealed record Pick(DigestAlgorithm Value) : Choice; public sealed record Text(string Value) : Choice; }",
        "[JS] public int M(Choice value) => 0;"));

    Assert.Equal(
        "Union 'Expo.TestModules.Choice' case 'Pick' is unreachable: case 'Text' declared before it always takes its values first",
        Assert.Single(unreachable.GeneratorDiagnostics!, item => item.Id == "EXPOJSI032").GetMessage());
    Assert.Empty(GeneratorDiagnosticIds(reachable));
  }

  // Member attributes come from metadata, so a referenced enum crosses as its declared strings.
  [Fact]
  public void GeneratorReadsEnumMemberValuesFromReferencedAssemblies()
  {
    var reference = GeneratorTestHost.CompileReference(
        "namespace External; public enum ExternalAlgorithm { MD5, [" + EM + "(Value = \"SHA-1\")] SHA1 }",
        "External.Enums");
    var result = GeneratorTestHost.Run(
        ModuleSource("", "[JS] public External.ExternalAlgorithm Echo(External.ExternalAlgorithm algorithm) => algorithm;"),
        additionalReferences: new[] { reference });

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
    var codec = EnumCodecText(GeneratedText(result), "ExternalAlgorithm");
    Assert.Contains("\"SHA-1\" => global::External.ExternalAlgorithm.SHA1,", codec, StringComparison.Ordinal);
    Assert.Contains("global::External.ExternalAlgorithm.SHA1 => \"SHA-1\",", codec, StringComparison.Ordinal);
  }

  // A member of a referenced enum has no location in this compilation, so the error is reported
  // where the enum is used and names the enum's assembly; the position reports its own diagnostic.
  [Theory]
  [InlineData("", "[JS] public int M(External.Bad value) => 0;", "value", "EXPOJSI001")]
  [InlineData("", "[JS] public External.Bad N() => default;", "N", "EXPOJSI002")]
  [InlineData("", "[JS] public External.Bad Value { get; set; }", "Value", "EXPOJSI015")]
  [InlineData("public sealed record Holder(External.Bad Content);", "[JS] public int M(Holder holder) => 0;", "Content", "EXPOJSI007")]
  [InlineData("", "[Event] public partial Func<External.Bad, Task> OnBad { get; }", "OnBad", "EXPOJSI019")]
  public void GeneratorReportsAnInvalidEnumFromAReferencedAssemblyAtTheUseSite(
      string declarations,
      string member,
      string useSite,
      string positionDiagnostic)
  {
    var reference = GeneratorTestHost.CompileReference(
        "namespace External; public enum Bad { [" + EM + "(Value = \"B\")] A, B }",
        "External.Enums");
    var source = ModuleSource(declarations, member);
    var result = GeneratorTestHost.Run(source, additionalReferences: new[] { reference });

    AssertNoCompilerErrors(result);
    Assert.Contains(positionDiagnostic, GeneratorDiagnosticIds(result));
    var diagnostic = Assert.Single(result.GeneratorDiagnostics!, item => item.Id == "EXPOJSI034");
    Assert.Equal(
        "Enum 'External.Bad' members 'A' and 'B' both cross to JavaScript as \"B\" (declared in referenced assembly 'External.Enums')",
        diagnostic.GetMessage());
    Assert.Equal(useSite, SpanText(source, diagnostic));
    Assert.DoesNotContain("BadEnumCodec_", GeneratedText(result), StringComparison.Ordinal);
  }

  // A record from a referenced project (source symbols of another compilation) holds a field that
  // cannot cross. Its declaration is not in this compilation, so the diagnostics land where the
  // record is used. Roslyn rejects a diagnostic located in another compilation's file.
  private const string UnionHolderCases =
      "[Union] public abstract record Choice { public sealed record Pick(External.Holder Value) : Choice; public sealed record Text(string Value) : Choice; }";

  [Theory]
  [InlineData("enum Bad { [" + EM + "(Value = \"B\")] A, B }", "Bad", "", "[JS] public int M(External.Holder holder) => 0;", "holder", "EXPOJSI034")]
  [InlineData("enum Bad { [" + EM + "(Value = \"B\")] A, B }", "Bad", "", "[Event] public partial Func<External.Holder, Task> OnHolder { get; }", "OnHolder", "EXPOJSI034")]
  [InlineData("enum Bad { [" + EM + "(Value = \"B\")] A, B }", "Bad", UnionHolderCases, "[JS] public int M(Choice value) => 0;", "value", "EXPOJSI034")]
  [InlineData("class Unsupported { }", "object", "", "[JS] public int M(External.Holder holder) => 0;", "holder", "EXPOJSI007")]
  [InlineData("class Unsupported { }", "object", "", "[Event] public partial Func<External.Holder, Task> OnHolder { get; }", "OnHolder", "EXPOJSI019")]
  [InlineData("class Unsupported { }", "object", UnionHolderCases, "[JS] public int M(Choice value) => 0;", "Pick", "EXPOJSI030")]
  public void GeneratorReportsADiagnosticOfAReferencedProjectRecordAtTheUseSite(
      string referencedDeclaration,
      string contentType,
      string declarations,
      string member,
      string useSite,
      string expectedDiagnostic)
  {
    var reference = GeneratorTestHost.CompileSourceReference(
        "using System.Runtime.Serialization; namespace External; public " + referencedDeclaration + " public sealed record Holder(" +
        (contentType == "object" ? "object" : "Bad") + " Content);",
        "External.Project");
    var source = ModuleSource(declarations, member);
    var result = GeneratorTestHost.Run(source, additionalReferences: new[] { reference });

    Assert.DoesNotContain(result.GeneratorDiagnostics!, item => item.Id.StartsWith("CS", StringComparison.Ordinal));
    var diagnostic = Assert.Single(result.GeneratorDiagnostics!, item => item.Id == expectedDiagnostic);
    Assert.NotNull(diagnostic.Location.SourceTree);
    Assert.Equal(source, diagnostic.Location.SourceTree!.ToString());
    Assert.Equal(useSite, SpanText(source, diagnostic));
  }

  // From a PE reference the enum's members have no location either, so the diagnostic lands at the
  // position that uses the record.
  [Fact]
  public void GeneratorReportsAnInvalidEnumInAPeRecordAtTheUseSite()
  {
    var reference = GeneratorTestHost.CompileReference(
        "using System.Runtime.Serialization; namespace External; public enum Bad { [" + EM + "(Value = \"B\")] A, B } public sealed record Holder(Bad Content);",
        "External.Pe");
    var source = ModuleSource("", "[JS] public int M(External.Holder holder) => 0;");
    var result = GeneratorTestHost.Run(source, additionalReferences: new[] { reference });

    var diagnostic = Assert.Single(result.GeneratorDiagnostics!, item => item.Id == "EXPOJSI034");
    Assert.Equal("holder", SpanText(source, diagnostic));
  }

  // The same holds for a type used directly at a boundary position: a parameter, a result, an
  // async result, a property, an event payload or a union case payload.
  [Theory]
  [InlineData("", "[JS] public int M(External.Bad value) => 0;")]
  [InlineData("", "[JS] public External.Bad N() => default;")]
  [InlineData("", "[JS] public Task<External.Bad> N() => default!;")]
  [InlineData("", "[JS] public External.Bad Value { get; set; }")]
  [InlineData("", "[Event] public partial Func<External.Bad, Task> OnBad { get; }")]
  [InlineData("[Union] public abstract record Choice { public sealed record Pick(External.Bad Value) : Choice; public sealed record Text(string Value) : Choice; }", "[JS] public int M(Choice value) => 0;")]
  [InlineData("[Union] public abstract record Choice { public sealed record Pick(External.Bad Value) : Choice; public sealed record Text(string Value) : Choice; }", "[Event] public partial Func<Choice, Task> OnBad { get; }")]
  [InlineData("", "[JS] public int M(External.Shared value) => 0;")]
  [InlineData("", "[JS] public External.Shared N() => null!;")]
  [InlineData("", "[JS] public External.Shared Value { get; set; } = null!;")]
  [InlineData("", "[Event] public partial Func<External.Shared, Task> OnBad { get; }")]
  [InlineData("[Union] public abstract record Choice { public sealed record Pick(External.Shared Value) : Choice; public sealed record Text(string Value) : Choice; }", "[JS] public int M(Choice value) => 0;")]
  [InlineData("[Union] public abstract record Choice { public sealed record Pick(External.Bad Value) : Choice; public sealed record Text(string Value) : Choice; }", "[JS] public Choice M() => null!;")]
  public void GeneratorLocatesEveryDiagnosticOfAReferencedProjectTypeInThisCompilation(string declarations, string member)
  {
    var reference = GeneratorTestHost.CompileSourceReference(
        "using System.Runtime.Serialization; namespace External; public enum Bad { [System.Runtime.Serialization.EnumMember(Value = \"B\")] A, B } [Expo.ModulesCore.Union] public abstract record Shared { public sealed record Text(string Value) : Shared; public sealed record Count(int Value) : Shared; }",
        "External.Project");
    var source = ModuleSource(declarations, member);
    var result = GeneratorTestHost.Run(source, additionalReferences: new[] { reference });

    Assert.DoesNotContain(result.GeneratorDiagnostics!, item => item.Id.StartsWith("CS", StringComparison.Ordinal));
    Assert.NotEmpty(result.GeneratorDiagnostics!);
    Assert.All(
        result.GeneratorDiagnostics!.Where(item => item.Location.IsInSource),
        item => Assert.Equal(source, item.Location.SourceTree!.ToString()));
  }
}
