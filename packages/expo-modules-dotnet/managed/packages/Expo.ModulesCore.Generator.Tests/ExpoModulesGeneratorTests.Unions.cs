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
}
