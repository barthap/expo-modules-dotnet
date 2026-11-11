using Microsoft.CodeAnalysis;
using Xunit;

namespace Expo.ModulesCore.Generator.Tests;

public sealed partial class ExpoModulesGeneratorTests
{
  // A union declaration is checked once, at the declaration, whether or not a member uses it, so an
  // authoring bug shows up before the first call site is written.
  [Theory]
  [InlineData("[Union] public sealed class Bad { }", "Bad", "it must be an abstract record class")]
  [InlineData("[Union] public abstract record Bad<T> { public sealed record A(string Value) : Bad<T>; }", "Bad<T>", "it must not be generic or nested in a generic type")]
  [InlineData("public static class Outer<T> { [Union] public abstract record Bad { public sealed record A(string Value) : Bad; } }", "Outer<T>.Bad", "it must not be generic or nested in a generic type")]
  [InlineData("[Union] public abstract record Bad(int X) { public sealed record A(string Value) : Bad(0); }", "Bad", "it must not have a parameter list")]
  [InlineData("public static class Holder { [Union] private abstract record Bad { private sealed record A(string Value) : Bad; } }", "Holder.Bad", "it and every containing type must be public or internal")]
  [InlineData("[Union] public abstract record Bad;", "Bad", "it declares no cases; a case is a nested sealed record that derives from it")]
  [InlineData("[Union] public abstract partial record Bad { public sealed record A(string Value) : Bad; } public abstract partial record Bad { public sealed record B(int Value) : Bad; }", "Bad", "its cases must all be declared in one partial declaration")]
  [InlineData("[Union] public abstract record Bad { public sealed record Many(IReadOnlyList<Bad> Value) : Bad; public sealed record One(string Value) : Bad; }", "Bad", "it contains itself through case 'Many'")]
  [InlineData("public sealed record Box(Bad Inner); [Union] public abstract record Bad { public sealed record Boxed(Box Value) : Bad; public sealed record Text(string Value) : Bad; }", "Bad", "it contains itself through case 'Boxed'")]
  public void GeneratorReportsInvalidUnionDeclarationOnce(string declarations, string unionName, string reason)
  {
    var result = GeneratorTestHost.Run(ModuleSource(declarations, "[JS] public int Ok() => 0;"));

    AssertNoCompilerErrors(result);
    Assert.Equal(new[] { "EXPOJSI029" }, GeneratorDiagnosticIds(result));
    Assert.Equal(
        $"Union 'Expo.TestModules.{unionName}' is invalid: {reason}",
        result.GeneratorDiagnostics!.Single().GetMessage());
  }

  // A member that uses an invalid union reports its own unsupported-type diagnostic, and the union
  // still reports once, not once per use.
  [Theory]
  [InlineData("[Union] public sealed class Bad { }")]
  [InlineData("[Union] public abstract record Bad;")]
  [InlineData("[Union] public abstract record Bad { public sealed record Many(IReadOnlyList<Bad> Value) : Bad; public sealed record One(string Value) : Bad; }")]
  public void GeneratorRejectsMembersThatUseAnInvalidUnion(string declarations)
  {
    var result = GeneratorTestHost.Run(ModuleSource(
        declarations,
        "[JS] public int M(Bad value) => 0; [JS] public Bad N() => null!;"));

    AssertNoCompilerErrors(result);
    var ids = GeneratorDiagnosticIds(result);
    Assert.Single(ids, id => id == "EXPOJSI029");
    Assert.Contains("EXPOJSI001", ids);
    Assert.Contains("EXPOJSI002", ids);
    var text = GeneratedText(result);
    Assert.DoesNotContain("\"m\"", text, StringComparison.Ordinal);
    Assert.DoesNotContain("\"n\"", text, StringComparison.Ordinal);
  }

  private const string ChoiceHead = "[Union] public abstract record Choice { public sealed record Text(string Value) : Choice; ";

  // Each invalid case reports at the case, with a message that tells the author what to write
  // instead, because the generator cannot pick a JavaScript shape for it.
  [Theory]
  [InlineData("public sealed record Empty : Choice; }", "Choice.Empty", "it has no payload; give it one positional parameter, and use a C# enum payload for a fixed set of values")]
  [InlineData("public sealed record Empty() : Choice; }", "Choice.Empty", "it has no payload; give it one positional parameter, and use a C# enum payload for a fixed set of values")]
  [InlineData("public sealed record Pair(string A, int B) : Choice; }", "Choice.Pair", "it has 2 positional parameters; group the values in a record and use that record as the payload")]
  [InlineData("public record Open(int Value) : Choice; }", "Choice.Open", "it must be a sealed record")]
  [InlineData("private sealed record Hidden(int Value) : Choice; }", "Choice.Hidden", "it must be public or internal")]
  [InlineData("public sealed record Opt(string Value = \"\") : Choice; }", "Choice.Opt", "its payload has a default value")]
  [InlineData("public sealed record Maybe(string? Value) : Choice; }", "Choice.Maybe", "its payload is nullable; make the union nullable where it is used instead")]
  [InlineData("public sealed record MaybeCount(int? Value) : Choice; }", "Choice.MaybeCount", "its payload is nullable; make the union nullable where it is used instead")]
  [InlineData("public sealed record Run(JavaScriptCallback<int> Value) : Choice; }", "Choice.Run", "its payload type 'Expo.ModulesCore.JavaScriptCallback<int>' has no union codec")]
  [InlineData("public sealed record Uri(string Value) : Choice; public sealed record Address(Uri Value) : Choice; }", "Choice.Address", "its payload type binds to the case 'Uri' of this union; rename that case or qualify the payload type")]
  [InlineData("public sealed record Other(Picker.Pick Value) : Choice; } [Union] public abstract record Picker { public sealed record Pick(int Value) : Picker; }", "Choice.Other", "its payload type 'Expo.TestModules.Picker.Pick' has no union codec")]
  [InlineData("} public sealed record Rogue(int Value) : Choice;", "Rogue", "it is not nested directly in the union")]
  public void GeneratorReportsInvalidUnionCases(string rest, string typeName, string reason)
  {
    var result = GeneratorTestHost.Run(ModuleSource(ChoiceHead + rest, "[JS] public int Ok() => 0;"));

    AssertNoCompilerErrors(result);
    Assert.Equal(new[] { "EXPOJSI030" }, GeneratorDiagnosticIds(result));
    Assert.Equal(
        $"Type 'Expo.TestModules.{typeName}' is not a valid case of union 'Expo.TestModules.Choice': {reason}",
        result.GeneratorDiagnostics!.Single().GetMessage());
  }

  // The spec's own example, `Uri(Uri Value)` inside the union body: C# already rejects it with
  // CS8910 (the primary constructor has the copy constructor's signature), and the generator adds
  // the reason the author needs. A probe on this branch confirmed CS8910.
  [Fact]
  public void GeneratorReportsCaseNamedLikeItsPayloadType()
  {
    var result = GeneratorTestHost.Run(ModuleSource(ChoiceHead + "public sealed record Uri(Uri Value) : Choice; }", "[JS] public int Ok() => 0;"));

    Assert.Contains(result.Diagnostics, item => item.Id == "CS8910");
    var diagnostic = Assert.Single(result.GeneratorDiagnostics!, item => item.Id == "EXPOJSI030");
    Assert.Equal(
        "Type 'Expo.TestModules.Choice.Uri' is not a valid case of union 'Expo.TestModules.Choice': its payload type binds to the case 'Uri' of this union; rename that case or qualify the payload type",
        diagnostic.GetMessage());
  }

  // A shared object needs a runtime context, which a union payload codec cannot receive.
  [Fact]
  public void GeneratorReportsSharedObjectUnionPayload()
  {
    var result = GeneratorTestHost.Run(SharedObjectMemberSource(
        "[JS] public int Ok() => 0;",
        "[Union] public abstract record EntryChoice { public sealed record Text(string Value) : EntryChoice; public sealed record Thing(Entry Value) : EntryChoice; }"));

    AssertNoCompilerErrors(result);
    Assert.Equal(new[] { "EXPOJSI030" }, GeneratorDiagnosticIds(result));
    Assert.EndsWith("its payload type 'Expo.TestModules.Entry' has no union codec", result.GeneratorDiagnostics!.Single().GetMessage());
  }

  // A self-referential record as a payload is reported before any codec resolves. Generated record
  // codecs overflow the generator's stack on such a record, so reaching GetCodec would crash the build.
  [Fact]
  public void GeneratorReportsUnionPayloadThatContainsItself()
  {
    var result = GeneratorTestHost.Run(ModuleSource(
        "public sealed record Node(string Name, IReadOnlyList<Node> Children); " + ChoiceHead + "public sealed record Tree(Node Value) : Choice; }",
        "[JS] public int M(Choice value) => 0;"));

    AssertNoCompilerErrors(result);
    var ids = GeneratorDiagnosticIds(result);
    Assert.Contains("EXPOJSI001", ids);
    var diagnostic = Assert.Single(result.GeneratorDiagnostics!, item => item.Id == "EXPOJSI030");
    Assert.EndsWith(
        "its payload type 'Expo.TestModules.Node' contains itself, which generated codecs do not support",
        diagnostic.GetMessage());
  }

  // A union that reaches itself through a callback type argument is a recursive union. The walk
  // used to stop at the callback, so codec resolution re-entered the union without end and
  // overflowed the generator's stack at the declaration.
  [Theory]
  [InlineData("[Union] public abstract record U { public sealed record Run(JavaScriptCallback<U> Value) : U; public sealed record Text(string Value) : U; }", "U", "Run")]
  [InlineData("public sealed record Box(JavaScriptCallback<U> F); [Union] public abstract record U { public sealed record Boxed(Box Value) : U; public sealed record Text(string Value) : U; }", "U", "Boxed")]
  [InlineData("[Union] public abstract record U { public sealed record Run(JavaScriptCallback<ValueTuple<U>, int> Value) : U; public sealed record Text(string Value) : U; }", "U", "Run")]
  public void GeneratorReportsUnionThatReachesItselfThroughACallback(string declarations, string unionName, string caseName)
  {
    var result = GeneratorTestHost.Run(ModuleSource(declarations, "[JS] public int Ok() => 0;"));

    AssertNoCompilerErrors(result);
    Assert.Equal(new[] { "EXPOJSI029" }, GeneratorDiagnosticIds(result));
    Assert.Equal(
        $"Union 'Expo.TestModules.{unionName}' is invalid: it contains itself through case '{caseName}'",
        result.GeneratorDiagnostics!.Single().GetMessage());
  }

  // A member that uses such a union must not recurse either, and the union still reports once.
  [Fact]
  public void GeneratorRejectsMembersThatUseAUnionThatReachesItselfThroughACallback()
  {
    var result = GeneratorTestHost.Run(ModuleSource(
        "[Union] public abstract record U { public sealed record Run(JavaScriptCallback<U> Value) : U; public sealed record Text(string Value) : U; }",
        "[JS] public int M(U value) => 0; [JS] public int N(JavaScriptCallback<U> callback) => 0;"));

    AssertNoCompilerErrors(result);
    var ids = GeneratorDiagnosticIds(result);
    Assert.Single(ids, id => id == "EXPOJSI029");
    Assert.Contains("EXPOJSI001", ids);
  }

  // A self-referential record behind a callback is the older record cycle: the union path reports
  // it at the case and never reaches the record codec.
  [Fact]
  public void GeneratorReportsUnionPayloadWhoseCallbackReachesASelfReferentialRecord()
  {
    var result = GeneratorTestHost.Run(ModuleSource(
        "public sealed record Node(string Name, IReadOnlyList<Node> Children); [Union] public abstract record U { public sealed record Run(JavaScriptCallback<Node> Value) : U; public sealed record Text(string Value) : U; }",
        "[JS] public int Ok() => 0;"));

    AssertNoCompilerErrors(result);
    Assert.Equal(new[] { "EXPOJSI030" }, GeneratorDiagnosticIds(result));
    Assert.EndsWith(
        "its payload type 'Expo.ModulesCore.JavaScriptCallback<Expo.TestModules.Node>' contains itself, which generated codecs do not support",
        result.GeneratorDiagnostics!.Single().GetMessage());
  }

  private const string UnionRecords =
      "public sealed record Point(double X, double Y); " +
      "public sealed record Circle(double X, double Y, double Radius); " +
      "public sealed record Line(string X, double Y); " +
      "public sealed record Wide(double X, double? Y); " +
      "public sealed record Soft(double X, double Y = 0); " +
      "public sealed record Partial(double X, double Y = 0, double Z = 0); " +
      "public sealed record Narrow(double X, double? Y, double Z); " +
      "public sealed record Loose(JavaScriptValue A); ";

  private static string ChoiceOf(string cases) =>
      UnionRecords + "[Union] public abstract record Choice { " + cases + " }";

  [Theory]
  [InlineData("public sealed record A(string Value) : Choice; public sealed record B(string Value) : Choice;", "A", "B", "string")]
  [InlineData("public sealed record A(Uint8Array Value) : Choice; public sealed record B(Uint8Array Value) : Choice;", "A", "B", "Expo.ModulesCore.Uint8Array")]
  public void GeneratorReportsDuplicateUnionPayload(string cases, string first, string second, string payload)
  {
    var result = GeneratorTestHost.Run(ModuleSource(ChoiceOf(cases), "[JS] public int Ok() => 0;"));

    AssertNoCompilerErrors(result);
    Assert.Equal(new[] { "EXPOJSI031" }, GeneratorDiagnosticIds(result));
    Assert.Equal(
        $"Union 'Expo.TestModules.Choice' cases '{first}' and '{second}' have the same payload type '{payload}'",
        result.GeneratorDiagnostics!.Single().GetMessage());
  }

  // Only provable overlap is an error: each row is a later case that the earlier case always takes
  // first under the matching rules, so the later case can never be chosen.
  [Theory]
  [InlineData("public sealed record Raw(JavaScriptValue Value) : Choice; public sealed record Text(string Value) : Choice;", "Text", "Raw")]
  [InlineData("public sealed record Amount(double Value) : Choice; public sealed record Count(int Value) : Choice;", "Count", "Amount")]
  [InlineData("public sealed record Big(long Value) : Choice; public sealed record Count(int Value) : Choice; public sealed record Amount(double Value) : Choice;", "Count", "Big")]
  [InlineData("public sealed record Text(string Value) : Choice; public sealed record Address(Uri Value) : Choice;", "Address", "Text")]
  [InlineData("public sealed record Buffer(ArrayBuffer Value) : Choice; public sealed record Bytes(byte[] Value) : Choice;", "Bytes", "Buffer")]
  [InlineData("public sealed record Bag(Dictionary<string, JavaScriptValue> Value) : Choice; public sealed record Dot(Point Value) : Choice;", "Dot", "Bag")]
  [InlineData("public sealed record Any(Loose Value) : Choice; public sealed record Dot(Point Value) : Choice;", "Dot", "Any")]
  [InlineData("public sealed record Anything(IReadOnlyList<JavaScriptValue> Value) : Choice; public sealed record Numbers(IReadOnlyList<int> Value) : Choice;", "Numbers", "Anything")]
  [InlineData("public sealed record Dot(Point Value) : Choice; public sealed record Round(Circle Value) : Choice;", "Round", "Dot")]
  [InlineData("public sealed record Spread(Wide Value) : Choice; public sealed record Dot(Point Value) : Choice;", "Dot", "Spread")]
  [InlineData("public sealed record Lenient(Soft Value) : Choice; public sealed record Dot(Point Value) : Choice;", "Dot", "Lenient")]
  public void GeneratorReportsUnreachableUnionCases(string cases, string later, string earlier)
  {
    var result = GeneratorTestHost.Run(ModuleSource(ChoiceOf(cases), "[JS] public int Ok() => 0;"));

    AssertNoCompilerErrors(result);
    Assert.Equal(new[] { "EXPOJSI032" }, GeneratorDiagnosticIds(result));
    Assert.Equal(
        $"Union 'Expo.TestModules.Choice' case '{later}' is unreachable: case '{earlier}' declared before it always takes its values first",
        result.GeneratorDiagnostics!.Single().GetMessage());
  }

  // Each later case here is reachable for some JavaScript value, so reporting it would reject a
  // working union. The Partial and Narrow rows are reachable only because of record field kinds.
  [Theory]
  [InlineData("public sealed record Count(int Value) : Choice; public sealed record Amount(double Value) : Choice;")]
  [InlineData("public sealed record Address(Uri Value) : Choice; public sealed record Text(string Value) : Choice;")]
  [InlineData("public sealed record Round(Circle Value) : Choice; public sealed record Dot(Point Value) : Choice;")]
  [InlineData("public sealed record Map(Dictionary<string, int> Value) : Choice; public sealed record Numbers(IReadOnlyList<int> Value) : Choice;")]
  [InlineData("public sealed record Dot(Point Value) : Choice; public sealed record Spread(Line Value) : Choice;")]
  [InlineData("public sealed record Big(long Value) : Choice; public sealed record Count(int Value) : Choice;")]
  [InlineData("public sealed record Dot(Point Value) : Choice; public sealed record Relaxed(Partial Value) : Choice;")]
  [InlineData("public sealed record Dot(Point Value) : Choice; public sealed record Sparse(Narrow Value) : Choice;")]
  public void GeneratorAcceptsReachableUnionCaseOrders(string cases)
  {
    var result = GeneratorTestHost.Run(ModuleSource(ChoiceOf(cases), "[JS] public int Ok() => 0;"));

    AssertNoCompilerErrors(result);
    Assert.Empty(GeneratorDiagnosticIds(result));
  }

  private const string PayloadUnion =
      "[Union] public abstract record Payload { " +
      "public sealed record Count(int Value) : Payload; " +
      "public sealed record Amount(double Value) : Payload; " +
      "public sealed record Buffer(ArrayBuffer Value) : Payload; }";

  // The union codec struct of one union, from its declaration to its closing brace.
  private static string UnionCodecText(string text, string unionName)
  {
    var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
    var start = normalized.IndexOf($"private readonly struct {unionName}UnionCodec_", StringComparison.Ordinal);
    Assert.True(start >= 0);
    return normalized[start..normalized.IndexOf("\n  }\n", start, StringComparison.Ordinal)];
  }

  [Fact]
  public void GeneratorEmitsUnionCodecForThreeCases()
  {
    var result = GeneratorTestHost.Run(ModuleSource(
        PayloadUnion,
        "[JS] public string Describe(Payload value) => \"\"; [JS] public Payload Make() => new Payload.Count(1);"));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
    var text = GeneratedText(result);
    var codec = UnionCodecText(text, "Payload");
    Assert.Contains("var __expoNumber = __expoKind == global::Expo.JSI.JavaScriptValueKind.Number ? value.AsDouble() : 0d;", codec, StringComparison.Ordinal);
    // int is not the last number case, so it takes only whole numbers that int holds.
    Assert.Contains("if (__expoKind == global::Expo.JSI.JavaScriptValueKind.Number && global::System.Double.IsInteger(__expoNumber) && __expoNumber >= -2147483648d && __expoNumber <= 2147483647d)", codec, StringComparison.Ordinal);
    Assert.Contains("__expoPayload0 = NumberCodec<int>.Decode(value, runtime);", codec, StringComparison.Ordinal);
    Assert.Contains("return new global::Expo.TestModules.Payload.Count(__expoPayload0);", codec, StringComparison.Ordinal);
    Assert.Contains("throw new global::Expo.ModulesCore.UnionCaseMismatchException(\"Payload\", new[] { \"int\", \"double\", \"ArrayBuffer\" }, __expoKind, __expoFailure);", codec, StringComparison.Ordinal);
    // Each attempt lets a release failure propagate before it moves to the next case.
    Assert.Equal(3, CountOccurrences(codec, "catch (global::System.AggregateException)"));
    Assert.True(codec.IndexOf("catch (global::System.AggregateException)", StringComparison.Ordinal) <
        codec.IndexOf("catch (global::System.Exception __expoAttemptFailure)", StringComparison.Ordinal));
    // A buffer payload whose case construction throws is released before the rethrow.
    Assert.Contains("CodecRelease.Release<global::Expo.ModulesCore.ArrayBuffer, ArrayBufferCodec>(__expoPayload2, ref __expoReleaseFailures);", codec, StringComparison.Ordinal);
    // Encode ends with the not-a-case throw; Release releases only the case it holds.
    Assert.True(codec.IndexOf("global::Expo.TestModules.Payload.Buffer __expoCase2 => ArrayBufferCodec.Encode(__expoCase2.Value, runtime),", StringComparison.Ordinal) <
        codec.IndexOf("_ => throw global::Expo.ModulesCore.Codecs.UnionCodecErrors.NotACase(value, \"Expo.TestModules.Payload\"),", StringComparison.Ordinal));
    Assert.Contains("CodecRelease.Release<global::Expo.ModulesCore.ArrayBuffer, ArrayBufferCodec>(__expoCase2.Value, ref __expoReleaseFailures);", codec, StringComparison.Ordinal);
    Assert.DoesNotContain("__expoCase0.Value, ref", codec, StringComparison.Ordinal);
    // The glue releases a decoded argument and a returned value through the union codec.
    Assert.Contains("CodecRelease.Release<global::Expo.TestModules.Payload, PayloadUnionCodec_", text, StringComparison.Ordinal);
  }

  [Fact]
  public void GeneratorKeepsLeafFreeUnionGlueWithoutRelease()
  {
    var result = GeneratorTestHost.Run(ModuleSource(
        "[Union] public abstract record Choice { public sealed record Number(double Value) : Choice; public sealed record Flag(bool Value) : Choice; }",
        "[JS] public int Pick(Choice value) => 0;"));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
    var text = GeneratedText(result);
    Assert.DoesNotContain("CodecRelease", text, StringComparison.Ordinal);
    Assert.DoesNotContain("public static void Release(global::Expo.TestModules.Choice", text, StringComparison.Ordinal);
    Assert.Contains("var __expoArg0 = ChoiceUnionCodec_", text, StringComparison.Ordinal);
  }

  // Spec scenario "A release failure in an attempt propagates": a leaf-bearing record attempt
  // releases what it decoded, and if that release fails, the AggregateException is not swallowed
  // as a failed attempt. Generated glue has no hook to make a real release fail, so the generated
  // text is the test.
  [Fact]
  public void GeneratorRethrowsReleaseFailuresFromUnionAttempts()
  {
    var result = GeneratorTestHost.Run(ModuleSource(
        "public sealed record Pair(ArrayBuffer Data, int Count); " +
        "[Union] public abstract record Fallback { public sealed record Packed(Pair Value) : Fallback; public sealed record Raw(JavaScriptValue Value) : Fallback; }",
        "[JS] public string M(Fallback value) => \"\";"));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
    var codec = UnionCodecText(GeneratedText(result), "Fallback");
    var attempt = codec.IndexOf("__expoPayload0 = PairCodec_", StringComparison.Ordinal);
    var rethrow = codec.IndexOf("catch (global::System.AggregateException)\n        {\n          throw;\n        }", attempt, StringComparison.Ordinal);
    var fallThrough = codec.IndexOf("catch (global::System.Exception __expoAttemptFailure)", attempt, StringComparison.Ordinal);
    Assert.True(attempt >= 0 && rethrow > attempt && fallThrough > rethrow);
    // The JavaScriptValue case takes every kind, so it is attempted without a kind check.
    Assert.DoesNotContain("JavaScriptValueKind.Undefined", codec, StringComparison.Ordinal);
  }

  [Fact]
  public void GeneratorUnionCodecsAvoidReflection()
  {
    var result = GeneratorTestHost.Run(ModuleSource(PayloadUnion, "[JS] public Payload Echo(Payload value) => value;"));

    var codec = UnionCodecText(GeneratedText(result), "Payload");
    foreach (var forbidden in new[] { "GetType(", "typeof(", "MethodInfo", "Activator", "DynamicInvoke", "object?[]", "(object)" })
    {
      Assert.DoesNotContain(forbidden, codec, StringComparison.Ordinal);
    }
  }

  // An integer case before a later number case takes only whole numbers its type holds; the last
  // number case decodes like its plain codec, so a lone int still truncates a fraction.
  [Theory]
  [InlineData("public sealed record Count(int Value) : Choice; public sealed record Amount(double Value) : Choice;", "__expoNumber >= -2147483648d && __expoNumber <= 2147483647d")]
  [InlineData("public sealed record Small(sbyte Value) : Choice; public sealed record Amount(double Value) : Choice;", "__expoNumber >= -128d && __expoNumber <= 127d")]
  [InlineData("public sealed record Big(long Value) : Choice; public sealed record Amount(double Value) : Choice;", "__expoNumber >= -9223372036854775808d && __expoNumber < 9223372036854775808d")]
  [InlineData("public sealed record Wide(ulong Value) : Choice; public sealed record Amount(double Value) : Choice;", "__expoNumber >= 0d && __expoNumber < 18446744073709551616d")]
  [InlineData("public sealed record Level(ByteLevel Value) : Choice; public sealed record Amount(double Value) : Choice;", "__expoNumber >= 0d && __expoNumber <= 255d")]
  [InlineData("public sealed record Text(string Value) : Choice; public sealed record Count(int Value) : Choice;", null)]
  public void GeneratorGatesIntegerCasesOnlyBeforeALaterNumberCase(string cases, string? gate)
  {
    var result = GeneratorTestHost.Run(ModuleSource(
        "[JSEnum(EnumRepresentation.Number)] public enum ByteLevel : byte { Low = 1 } " +
        "[Union] public abstract record Choice { " + cases + " }",
        "[JS] public int M(Choice value) => 0;"));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
    var codec = UnionCodecText(GeneratedText(result), "Choice");
    if (gate is null)
    {
      Assert.DoesNotContain("IsInteger", codec, StringComparison.Ordinal);
      Assert.DoesNotContain("__expoNumber", codec, StringComparison.Ordinal);
    }
    else
    {
      Assert.Contains("global::System.Double.IsInteger(__expoNumber) && " + gate, codec, StringComparison.Ordinal);
    }
  }

  [Fact]
  public void GeneratorComposesNestedUnions()
  {
    var result = GeneratorTestHost.Run(ModuleSource(
        "[Union] public abstract record Inner { public sealed record Text(string Value) : Inner; public sealed record Count(int Value) : Inner; } " +
        "[Union] public abstract record Outer { public sealed record Flag(bool Value) : Outer; public sealed record Nested(Inner Value) : Outer; }",
        "[JS] public int M(Outer value) => 0;"));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
    var text = GeneratedText(result);
    var outer = UnionCodecText(text, "Outer");
    // A nested union is attempted for every kind of its own cases, in JavaScriptValueKind order.
    Assert.Contains("if (__expoKind == global::Expo.JSI.JavaScriptValueKind.Number || __expoKind == global::Expo.JSI.JavaScriptValueKind.String)", outer, StringComparison.Ordinal);
    Assert.Contains("new[] { \"bool\", \"Inner\" }", outer, StringComparison.Ordinal);
    Assert.True(text.IndexOf("private readonly struct InnerUnionCodec_", StringComparison.Ordinal) <
        text.IndexOf("private readonly struct OuterUnionCodec_", StringComparison.Ordinal));
  }

  [Fact]
  public void GeneratorRejectsUnionCaseTypesUsedDirectly()
  {
    var result = GeneratorTestHost.Run(ModuleSource(
        "[Union] public abstract record NonceInput { public sealed record Base64(string Value) : NonceInput; public sealed record Bytes(byte[] Value) : NonceInput; public sealed record Length(int Value) : NonceInput; }",
        "[JS] public int M(NonceInput.Bytes bytes) => 0;"));

    AssertNoCompilerErrors(result);
    Assert.Equal(new[] { "EXPOJSI001" }, GeneratorDiagnosticIds(result));
    Assert.Contains("'global::Expo.TestModules.NonceInput.Bytes'", result.GeneratorDiagnostics!.Single().GetMessage(), StringComparison.Ordinal);
    Assert.DoesNotContain("BytesCodec_", GeneratedText(result), StringComparison.Ordinal);
  }

  [Fact]
  public void GeneratorEmitsEachUnionCodecOncePerProvider()
  {
    var result = GeneratorTestHost.Run(ModuleSource(
        PayloadUnion + " public sealed record Holder(Payload Content);",
        "[JS] public Payload A(Payload value) => value; [JS] public int B(IReadOnlyList<Payload> values) => 0; [JS] public int C(Holder holder) => 0;"));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
    Assert.Equal(1, CountOccurrences(GeneratedText(result), "private readonly struct PayloadUnionCodec_"));
  }

  private const string ContentUnion =
      "[Union] public abstract record Content { public sealed record Text(string Value) : Content; public sealed record Buffer(ArrayBuffer Value) : Content; } " +
      "public sealed record Holder(Content Content, int Count); " +
      "public sealed record Shelf(IReadOnlyList<Content> Parts);";

  private const string ContentType = "global::Expo.TestModules.Content";

  // A union composes like any ordinary codec, so every position works with no rule of its own, and
  // a leaf-bearing union follows the leaf release rules of that position.
  [Theory]
  [InlineData(false, "[JS] public string M(Content content) => \"\";", "m", "__expoArg0 = ContentUnionCodec_")]
  [InlineData(false, "[JS] public Task<string> M(Content content) => Task.FromResult(\"\");", "m", ".Decode(arguments.GetValue(0), jsRuntime);")]
  [InlineData(false, "[JS] public string M(Content? content) => \"\";", "m", "NullableReferenceCodec<" + ContentType + ", ContentUnionCodec_")]
  [InlineData(false, "[JS] public string M(Content? content = null) => \"\";", "m", "Undefined ? null : NullableReferenceCodec<" + ContentType + ", ContentUnionCodec_")]
  [InlineData(false, "[JS] public int M(IReadOnlyList<Content> parts) => 0;", "m", "JavaScriptArrayCodec<" + ContentType + ", ContentUnionCodec_")]
  [InlineData(false, "[JS] public int M(IReadOnlyDictionary<string, Content> parts) => 0;", "m", "JavaScriptDictionaryCodec<" + ContentType + ", ContentUnionCodec_")]
  [InlineData(false, "[JS] public Content M() => null!;", "m", "CodecRelease.Release<" + ContentType + ", ContentUnionCodec_")]
  [InlineData(false, "[JS] public Task<Content> M() => Task.FromResult<Content>(null!);", "m", "StrongBox<" + ContentType + ">(__expoResult)")]
  [InlineData(false, "[JS] public Content? M() => null;", "m", "NullableReferenceCodec<" + ContentType + ", ContentUnionCodec_")]
  [InlineData(false, "[JS] public Content Value { get; set; } = null!;", "value", "__expoValue = ContentUnionCodec_")]
  [InlineData(false, "[JS] public Content? Maybe { get; set; }", "maybe", "NullableReferenceCodec<" + ContentType + ", ContentUnionCodec_")]
  [InlineData(false, "[JS] public Content Current => null!;", "current", "CodecRelease.Release<" + ContentType + ", ContentUnionCodec_")]
  [InlineData(false, "[JS] public IReadOnlyList<Content> M() => Array.Empty<Content>();", "m", "CodecRelease.Release<global::System.Collections.Generic.IReadOnlyList<" + ContentType + ">, JavaScriptArrayCodec<" + ContentType + ", ContentUnionCodec_")]
  [InlineData(false, "[JS] public Dictionary<string, Content> M() => new();", "m", "JavaScriptDictionaryCodec<" + ContentType + ", ContentUnionCodec_")]
  [InlineData(false, "[JS] public int M(Holder holder) => 0;", "m", ".Decode(contentValue, runtime)")]
  [InlineData(false, "[JS] public Holder M() => null!;", "m", "CodecRelease.Release<global::Expo.TestModules.Holder, HolderCodec_")]
  [InlineData(false, "[JS] public int M(Shelf shelf) => 0;", "m", "(value.Parts, ref __expoReleaseFailures);")]
  [InlineData(false, "[JS] public int M(JavaScriptCallback<ValueTuple<Content>, int> callback) => 0;", "m", "ValueTupleCodec<" + ContentType + ", ContentUnionCodec_")]
  [InlineData(false, "[JS] public int M(JavaScriptCallback<Content> callback) => 0;", "m", "JavaScriptCallbackCodec<" + ContentType + ", ContentUnionCodec_")]
  [InlineData(false, "[Event] public partial Func<Content, Task> OnContent { get; }", "onContent", "EmitAsync<ContentUnionCodec_")]
  [InlineData(true, "[JS] public Entry(Content seed) { }", "Entry", "__expoArg0 = ContentUnionCodec_")]
  [InlineData(true, "[JS] public string Write(Content content) => \"\";", "write", "__expoArg0 = ContentUnionCodec_")]
  [InlineData(true, "[JS] public Task<string> WriteLater(Content content) => Task.FromResult(\"\");", "writeLater", "__expoArg0 = ContentUnionCodec_")]
  [InlineData(true, "[JS] public Content Give() => null!;", "give", "CodecRelease.Release<" + ContentType + ", ContentUnionCodec_")]
  [InlineData(true, "[JS] public Content Value { get; set; } = null!;", "value", "__expoValue = ContentUnionCodec_")]
  [InlineData(true, "[Event] public partial Func<Content, Task> OnContent { get; }", "onContent", "EmitAsync<ContentUnionCodec_")]
  public void GeneratorAcceptsNamedUnionsInEveryPosition(bool onSharedObject, string member, string jsName, string expectedFragment)
  {
    var result = GeneratorTestHost.Run(
        onSharedObject ? SharedObjectMemberSource(member, ContentUnion) : ModuleSource(ContentUnion, member));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerErrors(result);
    Assert.DoesNotContain(result.Diagnostics, item => item.Id is "CS8604" or "CS8625" or "CS8631");
    var text = GeneratedText(result);
    Assert.Contains($"\"{jsName}\"", text, StringComparison.Ordinal);
    Assert.Contains(expectedFragment, text, StringComparison.Ordinal);
  }

  // An event payload is encoded, so a decode-only callback inside a union case is named as the
  // reason, not hidden behind "no encode-capable codec".
  [Fact]
  public void GeneratorNamesTheCallbackInsideAUnionEventPayload()
  {
    var result = GeneratorTestHost.Run(ModuleSource(
        "[Union] public abstract record Runner { public sealed record Run(JavaScriptCallback<int> Value) : Runner; public sealed record Text(string Value) : Runner; }",
        "[Event] public partial Func<Runner, Task> OnRun { get; }"));

    AssertNoCompilerErrors(result);
    var ids = GeneratorDiagnosticIds(result);
    Assert.Contains("EXPOJSI030", ids);
    var payload = Assert.Single(result.GeneratorDiagnostics!, item => item.Id == "EXPOJSI019");
    Assert.Contains("it contains JavaScriptCallback, which is decode-only", payload.GetMessage(), StringComparison.Ordinal);
  }

  // A property cannot hold a callback at any depth, also inside a union case.
  [Fact]
  public void GeneratorRejectsAUnionPropertyThatHoldsACallback()
  {
    var result = GeneratorTestHost.Run(ModuleSource(
        "[Union] public abstract record Runner { public sealed record Run(JavaScriptCallback<int> Value) : Runner; public sealed record Text(string Value) : Runner; }",
        "[JS] public Runner Value { get; set; } = null!;"));

    AssertNoCompilerErrors(result);
    var ids = GeneratorDiagnosticIds(result);
    Assert.Contains("EXPOJSI030", ids);
    Assert.Contains("EXPOJSI015", ids);
    Assert.DoesNotContain("\"value\"", GeneratedText(result), StringComparison.Ordinal);
  }

  private const string ExternalUnionSource =
      "namespace External; [Expo.ModulesCore.Union] public abstract record Shared { public sealed record Text(string Value) : Shared; public sealed record Count(int Value) : Shared; }";

  // Case order is not available from metadata, so a union from a referenced assembly is rejected
  // where it is used, and the position reports its own unsupported-type diagnostic.
  [Theory]
  [InlineData("", "[JS] public int M(External.Shared value) => 0;", "value", "EXPOJSI001")]
  [InlineData("", "[JS] public External.Shared N() => null!;", "N", "EXPOJSI002")]
  [InlineData("", "[JS] public External.Shared Value { get; set; } = null!;", "Value", "EXPOJSI015")]
  [InlineData("public sealed record Holder(External.Shared Content);", "[JS] public int M(Holder holder) => 0;", "Content", "EXPOJSI007")]
  [InlineData("", "[Event] public partial Func<External.Shared, Task> OnShared { get; }", "OnShared", "EXPOJSI019")]
  public void GeneratorReportsUnionFromReferencedAssemblyAtTheUseSite(
      string declarations,
      string member,
      string useSite,
      string positionDiagnostic)
  {
    var source = ModuleSource(declarations, member);
    var result = GeneratorTestHost.Run(
        source,
        additionalReferences: new[] { GeneratorTestHost.CompileReference(ExternalUnionSource, "External.Unions") });

    AssertNoCompilerErrors(result);
    var ids = GeneratorDiagnosticIds(result);
    Assert.Contains(positionDiagnostic, ids);
    var diagnostic = Assert.Single(result.GeneratorDiagnostics!, item => item.Id == "EXPOJSI029");
    Assert.Equal(
        "Union 'External.Shared' is invalid: it is declared in referenced assembly 'External.Unions'; declare the union in the project that uses it",
        diagnostic.GetMessage());
    var span = diagnostic.Location.SourceSpan;
    Assert.Equal(useSite, source.Substring(span.Start, span.Length));
    Assert.DoesNotContain("SharedUnionCodec_", GeneratedText(result), StringComparison.Ordinal);
  }

  // Payload codecs resolve by type, not by the declaring assembly, so records, enums, and lists of
  // them from a referenced assembly work as union payloads.
  [Fact]
  public void GeneratorResolvesUnionCasePayloadsFromReferencedAssembly()
  {
    var reference = GeneratorTestHost.CompileReference(
        "namespace External; public sealed record ExternalRecord(string Name, int Count); " +
        "[Expo.ModulesCore.JSEnum(Expo.ModulesCore.EnumRepresentation.Number)] public enum ExternalLevel { Low = 1, High = 2 }",
        "External.Types");
    var result = GeneratorTestHost.Run(
        ModuleSource(
            "[Union] public abstract record Sth { " +
            "public sealed record Foo(External.ExternalRecord Bar) : Sth; " +
            "public sealed record Level(External.ExternalLevel Value) : Sth; " +
            "public sealed record Many(IReadOnlyList<External.ExternalRecord> Value) : Sth; " +
            "public sealed record Text(string Value) : Sth; }",
            "[JS] public string Describe(Sth value) => \"\"; [JS] public Sth Make() => null!;"),
        additionalReferences: new[] { reference });

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
    var text = GeneratedText(result);
    Assert.Contains("private readonly struct ExternalRecordCodec_", text, StringComparison.Ordinal);
    var codec = UnionCodecText(text, "Sth");
    Assert.Contains("NumberEnumCodec<global::External.ExternalLevel>.Decode(value, runtime)", codec, StringComparison.Ordinal);
    Assert.Contains("JavaScriptArrayCodec<global::External.ExternalRecord, ExternalRecordCodec_", codec, StringComparison.Ordinal);
    Assert.Contains("new[] { \"ExternalRecord\", \"ExternalLevel\", \"IReadOnlyList<ExternalRecord>\", \"string\" }", codec, StringComparison.Ordinal);
  }

  // A shared-object member that uses an invalid union reports the union once and its own position.
  [Theory]
  [InlineData("[JS] public string Write(EntryChoice content) => \"\";", "parameter 'content' uses unsupported type 'global::Expo.TestModules.EntryChoice'")]
  [InlineData("[JS] public Entry(EntryChoice other) { }", "its [JS] constructor parameter 'other' uses unsupported type 'global::Expo.TestModules.EntryChoice'")]
  public void GeneratorRejectsInvalidUnionsOnSharedObjectMembers(string member, string reason)
  {
    var result = GeneratorTestHost.Run(SharedObjectMemberSource(
        member,
        "[Union] public abstract record EntryChoice { public sealed record Text(string Value) : EntryChoice; public sealed record Thing(Entry Value) : EntryChoice; }"));

    AssertNoCompilerErrors(result);
    var ids = GeneratorDiagnosticIds(result);
    Assert.Single(ids, id => id == "EXPOJSI030");
    var usage = Assert.Single(result.GeneratorDiagnostics!, item => item.Id == "EXPOJSI023");
    Assert.Contains(reason, usage.GetMessage(), StringComparison.Ordinal);
  }

  // A Uint8Array payload is a leaf with kind Object, so it pairs with a string or an ArrayBuffer
  // payload, takes an object that is a Uint8Array, and releases through its own codec.
  [Theory]
  [InlineData("public sealed record Text(string Value) : Choice; public sealed record View(Uint8Array Value) : Choice;", "Text", "string", "Uint8ArrayCodec.Decode(value, runtime)")]
  [InlineData("public sealed record Buffer(ArrayBuffer Value) : Choice; public sealed record View(Uint8Array Value) : Choice;", "Buffer", "ArrayBuffer", "Uint8ArrayCodec.Decode(value, runtime)")]
  public void GeneratorAcceptsUint8ArrayUnionPayloads(string cases, string firstCase, string firstPayload, string expectedFragment)
  {
    var result = GeneratorTestHost.Run(ModuleSource("[Union] public abstract record Choice { " + cases + " }", "[JS] public int M(Choice value) => 0;"));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
    var codec = UnionCodecText(GeneratedText(result), "Choice");
    Assert.Contains(expectedFragment, codec, StringComparison.Ordinal);
    Assert.Contains($"new[] {{ \"{firstPayload}\", \"Uint8Array\" }}", codec, StringComparison.Ordinal);
    Assert.Contains($"global::Expo.TestModules.Choice.{firstCase}", codec, StringComparison.Ordinal);
    Assert.Contains("CodecRelease.Release<global::Expo.ModulesCore.Uint8Array, Uint8ArrayCodec>(", codec, StringComparison.Ordinal);
  }
}
