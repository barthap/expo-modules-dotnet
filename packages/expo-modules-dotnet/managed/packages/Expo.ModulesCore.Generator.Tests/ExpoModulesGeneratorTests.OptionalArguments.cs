using Microsoft.CodeAnalysis;
using Xunit;

namespace Expo.ModulesCore.Generator.Tests;

public sealed partial class ExpoModulesGeneratorTests
{
  [Theory]
  [InlineData("string a, string? b", 1)]
  [InlineData("string? a, string b", 2)]
  [InlineData("string? a, int? b", 0)]
  [InlineData("string a, string? b, int? c", 1)]
  [InlineData("string a, string b = \"x\", int? c = null", 1)]
  [InlineData("string? a, string b = \"x\"", 0)]
  public void GeneratorCountsTheRequiredParametersOfAFunction(string parameters, int required)
  {
    var result = GeneratorTestHost.Run(
        $$"""
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule("Optional")]
        public sealed partial class OptionalModule
        {
          [JS] public string Run({{parameters}}) => "";
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    var source = Assert.Single(result.GeneratedSources).Text;
    var parameterCount = parameters.Split(',').Length;
    Assert.Contains(
        $"GeneratedFunction.RequireArgumentCount(\"Optional.run\", arguments, {required}, {parameterCount});",
        source
    );
    Assert.Matches($@"\n\s+""run"",\s+{required},\n", source);
  }

  [Theory]
  [InlineData("string a, string? b", 1)]
  [InlineData("string? a, string b", 2)]
  [InlineData("string a, string? b, int? c", 1)]
  public void GeneratorCountsTheRequiredParametersOfASharedObjectConstructor(string parameters, int required)
  {
    var result = GeneratorTestHost.Run(
        $$"""
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule("Optional", Classes = new[] { typeof(OptionalThing) })]
        public sealed partial class OptionalModule
        {
        }

        [ExpoSharedObject]
        public sealed partial class OptionalThing : SharedObject
        {
          [JS] public OptionalThing({{parameters}}) { }
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    var source = Assert.Single(result.GeneratedSources).Text;
    var parameterCount = parameters.Split(',').Length;
    Assert.Contains(
        $"GeneratedSharedObjectClass.RequireArgumentCount(\"OptionalThing\", arguments, {required}, {parameterCount});",
        source
    );
  }

  [Fact]
  public void GeneratorDecodesAnOmittedTrailingNullableParameterAsNullWithoutReadingTheArgument()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule("Optional")]
        public sealed partial class OptionalModule
        {
          [JS] public string Run(string a, string? b) => "";
        }
        """
    );

    var source = Assert.Single(result.GeneratedSources).Text;
    Assert.Matches(@"arguments\.Count <= 1 \? default : [^\n]*arguments\.GetValue\(1\)", source);
    Assert.DoesNotMatch(@"arguments\.Count <= 0", source);
  }
}
