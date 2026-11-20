using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Expo.ModulesCore.Generator.Tests;

public sealed partial class ExpoModulesGeneratorTests
{
  private static string ConstantModuleSource(string members, string declarations = "", string classes = "") =>
      $$"""
      #nullable enable
      using System;
      using System.Collections.Generic;
      using System.Threading.Tasks;
      using Expo.JSI;
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      {{declarations}}

      [ExpoModule("Constants"{{(classes.Length == 0 ? "" : ", " + classes)}})]
      public sealed partial class ConstantsModule
      {
        {{members}}
      }
      """;

  private const string ConstantEntryDeclarations =
      """
      [ExpoSharedObject("Entry")]
      public sealed partial class Entry : SharedObject
      {
        [JS] public Entry() { }
      }

      public sealed record EntryPair(Entry First, Entry? Second);
      public sealed record Plain(string Name, int Size);
      """;

  [Fact]
  public void ConstantWithoutJSReportsEXPOJSI036()
  {
    var result = GeneratorTestHost.Run(ConstantModuleSource("[Constant] public int Count => 1;"));

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI036");
    Assert.Contains("Count", diagnostic.GetMessage());
    Assert.DoesNotContain("\"count\"", GeneratedText(result), StringComparison.Ordinal);
  }

  [Fact]
  public void ConstantOnEventReportsEXPOJSI036()
  {
    var result = GeneratorTestHost.Run(ConstantModuleSource(
        "[Event] [Constant] public partial Func<int, Task> OnTick { get; }"));

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI036");
    Assert.Contains("OnTick", diagnostic.GetMessage());
  }

  [Theory]
  [InlineData("[JS] [Constant] public int Count { get; set; }")]
  [InlineData("[JS] [Constant] public int Count { get; internal set; }")]
  public void ConstantWithSetterReportsEXPOJSI037(string property)
  {
    var result = GeneratorTestHost.Run(ConstantModuleSource(property));

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI037");
    Assert.Contains("Count", diagnostic.GetMessage());
  }

  [Fact]
  public void ConstantWithSetterOnSharedObjectReportsEXPOJSI037()
  {
    var result = GeneratorTestHost.Run(ConstantModuleSource(
        string.Empty,
        """
        [ExpoSharedObject("Thing")]
        public sealed partial class Thing : SharedObject
        {
          [JS] public Thing() { }
          [JS] [Constant] public int Count { get; set; }
        }
        """,
        "Classes = new[] { typeof(Thing) }"));

    Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI037");
    Assert.DoesNotContain(result.Diagnostics, item => item.Id == "EXPOJSI023");
  }

  [Fact]
  public void ConstantWithPrivateSetterIsAccepted()
  {
    var result = GeneratorTestHost.Run(ConstantModuleSource(
        "[JS] [Constant] public int Held { get; private set; } = 5;"));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
    Assert.Contains("GeneratedProperty.CacheConstant", GeneratedText(result), StringComparison.Ordinal);
  }

  [Theory]
  [InlineData("ArrayBuffer")]
  [InlineData("Uint8Array")]
  [InlineData("JavaScriptValue")]
  [InlineData("IReadOnlyList<ArrayBuffer>")]
  [InlineData("LeafRecord")]
  [InlineData("LeafUnion")]
  public void ConstantOfLeafTypeReportsEXPOJSI038(string type)
  {
    var result = GeneratorTestHost.Run(ConstantModuleSource(
        $"[JS] [Constant] public {type} Value => default!;",
        """
        public sealed record LeafRecord(ArrayBuffer Buffer);
        [Union]
        public abstract record LeafUnion
        {
          public sealed record LeafCase(ArrayBuffer Value) : LeafUnion;
          public sealed record TextCase(string Value) : LeafUnion;
        }
        """));

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI038");
    Assert.Contains("Value", diagnostic.GetMessage());
    Assert.Contains("leaf", diagnostic.GetMessage(), StringComparison.Ordinal);
  }

  [Theory]
  [InlineData("byte[]")]
  [InlineData("Memory<byte>")]
  [InlineData("string")]
  [InlineData("Plain")]
  [InlineData("IReadOnlyList<int>")]
  public void ConstantOfLeafFreeTypeIsAccepted(string type)
  {
    var result = GeneratorTestHost.Run(ConstantModuleSource(
        $"[JS] [Constant] public {type} Value => default!;",
        ConstantEntryDeclarations,
        "Classes = new[] { typeof(Entry) }"));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
  }

  [Theory]
  [InlineData("Entry")]
  [InlineData("Entry?")]
  [InlineData("IReadOnlyList<Entry>")]
  [InlineData("Dictionary<string, Entry>")]
  [InlineData("EntryPair")]
  public void ConstantOfSharedObjectTypeReportsEXPOJSI038(string type)
  {
    var result = GeneratorTestHost.Run(ConstantModuleSource(
        $"[JS] [Constant] public {type} Value => default!;",
        ConstantEntryDeclarations,
        "Classes = new[] { typeof(Entry) }"));

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI038");
    Assert.Contains("shared object", diagnostic.GetMessage(), StringComparison.Ordinal);
  }

  [Fact]
  public void ValidConstantEmitsCacheCallWithoutReflection()
  {
    var result = GeneratorTestHost.Run(ConstantModuleSource(
        """
        [JS] [Constant] public int Count => 1;
        [JS] public int Plain => 2;
        """));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
    var text = GeneratedText(result);
    var constantGetter = GetMethodText(text, "Constants_Count_Getter");
    Assert.Contains("GeneratedProperty.CacheConstant(runtime, thisValue, \"count\", true, __expoEncoded)", constantGetter, StringComparison.Ordinal);
    Assert.True(
        constantGetter.IndexOf(".Encode(", StringComparison.Ordinal) <
        constantGetter.IndexOf("CacheConstant", StringComparison.Ordinal));
    foreach (var forbidden in new[] { "System.Reflection", "MethodInfo", "DynamicInvoke", "GetType()", "CodecRelease", "__expoResult" })
    {
      Assert.DoesNotContain(forbidden, constantGetter, StringComparison.Ordinal);
    }

    // A getter without [Constant] keeps its text and never calls the cache.
    var plainGetter = GetMethodText(text, "Constants_Plain_Getter");
    Assert.DoesNotContain("CacheConstant", plainGetter, StringComparison.Ordinal);
    Assert.Contains("return NumberCodec", plainGetter, StringComparison.Ordinal);
  }

  [Fact]
  public void SharedObjectConstantEmitsNonEnumerableCache()
  {
    var result = GeneratorTestHost.Run(ConstantModuleSource(
        string.Empty,
        """
        [ExpoSharedObject("Thing")]
        public sealed partial class Thing : SharedObject
        {
          [JS] public Thing() { }
          [JS] [Constant] public int Tag => 1;
        }
        """,
        "Classes = new[] { typeof(Thing) }"));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
    Assert.Contains("\"tag\", false, __expoEncoded)", GeneratedText(result), StringComparison.Ordinal);
  }

  // The text of one generated static method, from its name to the next blank-line-separated member.
  private static string GetMethodText(string generated, string methodName)
  {
    var start = generated.IndexOf(methodName + "(", StringComparison.Ordinal);
    Assert.True(start >= 0, $"{methodName} was not generated");
    var end = generated.IndexOf("\n  }\n", start, StringComparison.Ordinal);
    return generated.Substring(start, end - start);
  }
}
