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
}
