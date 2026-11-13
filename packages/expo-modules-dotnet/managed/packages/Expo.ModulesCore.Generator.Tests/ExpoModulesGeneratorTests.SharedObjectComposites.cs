using Microsoft.CodeAnalysis;
using Xunit;

namespace Expo.ModulesCore.Generator.Tests;

public sealed partial class ExpoModulesGeneratorTests
{
  private const string CompositeEntryDeclarations =
      """
      [ExpoSharedObject("Entry")]
      public sealed partial class Entry : SharedObject
      {
        [JS] public Entry() { }
      }

      public sealed record EntryPair(Entry First, Entry? Second);
      """;

  private static string CompositeSharedObjectSource(string declarations, string members, string classes = "typeof(Entry)") =>
      $$"""
      #nullable enable
      using System;
      using System.Collections.Generic;
      using System.Threading.Tasks;
      using Expo.JSI;
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      {{CompositeEntryDeclarations}}

      {{declarations}}

      [ExpoModule("Composite", Classes = new[] { {{classes}} })]
      public sealed partial class CompositeModule
      {
        {{members}}
      }
      """;

  [Fact]
  public void ConcreteSharedObjectInListSelectsArrayCodec()
  {
    var result = GeneratorTestHost.Run(CompositeSharedObjectSource(
        string.Empty,
        "[JS] public int Count(IReadOnlyList<Entry?> entries) => entries.Count;"
    ));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
    Assert.Contains(
        "JavaScriptArrayCodec<global::Expo.TestModules.Entry?, NullableReferenceCodec<global::Expo.TestModules.Entry, SharedObjectCodec<global::Expo.TestModules.Entry>>>",
        GeneratedText(result),
        StringComparison.Ordinal
    );
  }

  [Fact]
  public void ConcreteSharedObjectInDictionaryAndRecordIsAccepted()
  {
    var result = GeneratorTestHost.Run(CompositeSharedObjectSource(
        string.Empty,
        """
        [JS] public int A(Dictionary<string, Entry> value) => value.Count;
        [JS] public int B(IReadOnlyDictionary<string, Entry> value) => value.Count;
        [JS] public EntryPair C(EntryPair value) => value;
        [JS] public int D(IReadOnlyList<IReadOnlyList<Entry>> value) => value.Count;
        [JS] public Entry? E(Entry? value) => value;
        [JS] public IReadOnlyList<Entry>? F { get; set; }
        """
    ));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
  }

  [Fact]
  public void CompositeOverSharedObjectsEmitsNoRelease()
  {
    var result = GeneratorTestHost.Run(CompositeSharedObjectSource(
        string.Empty,
        "[JS] public IReadOnlyDictionary<string, Entry> M(IReadOnlyList<Entry?> a) => new Dictionary<string, Entry>();"
    ));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
    var source = GeneratedText(result);
    Assert.DoesNotContain("Release(", source, StringComparison.Ordinal);
    Assert.DoesNotContain("finally", source, StringComparison.Ordinal);
  }

  [Fact]
  public void CompositeWithLeafAndSharedObjectReleasesTheLeafOnly()
  {
    var result = GeneratorTestHost.Run(CompositeSharedObjectSource(
        "public sealed record Held(ArrayBuffer Buffer, Entry Entry);",
        "[JS] public Held M(Held value) => value;"
    ));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
    var source = GeneratedText(result).Replace("\r\n", "\n", StringComparison.Ordinal);
    var release = RecordCodecRelease(source, "Held");
    Assert.Equal(1, CountOccurrences(release, "CodecRelease.Release<"));
    Assert.Contains("value.Buffer", release, StringComparison.Ordinal);
    Assert.DoesNotContain("value.Entry", release, StringComparison.Ordinal);
    Assert.Contains("SharedObjectCodec<global::Expo.TestModules.Entry>.Decode(", source, StringComparison.Ordinal);
    Assert.Contains("SharedObjectCodec<global::Expo.TestModules.Entry>.Encode(", source, StringComparison.Ordinal);
  }

  [Theory]
  [InlineData("IReadOnlyList<SharedObject>", "global::Expo.ModulesCore.SharedObject", "polymorphic SharedObject base")]
  [InlineData("IReadOnlyList<SharedRef<int>>", "global::Expo.ModulesCore.SharedRef<int>", "SharedRef<T> managed carrier base")]
  [InlineData("BaseHolder", "global::Expo.TestModules.PathBase", "abstract shared-object base")]
  [InlineData("IReadOnlyList<PathBase>", "global::Expo.TestModules.PathBase", "abstract shared-object base")]
  [InlineData("IReadOnlyList<Unsealed>", "global::Expo.TestModules.Unsealed", "sealed [ExpoSharedObject] class")]
  [InlineData("IReadOnlyList<Outer.Nested>", "global::Expo.TestModules.Outer.Nested", "top-level, non-generic, sealed")]
  [InlineData("IReadOnlyList<Generic<int>>", "global::Expo.TestModules.Generic<int>", "top-level, non-generic, sealed")]
  [InlineData("IReadOnlyList<Plain>", "global::Expo.TestModules.Plain", "not marked [ExpoSharedObject]")]
  public void SharedObjectBasesInsideComposedTypesReportExpoJsi023(string parameterType, string reportedType, string rule)
  {
    var result = GeneratorTestHost.Run(CompositeSharedObjectSource(
        """
        public abstract class PathBase : SharedObject { }
        public sealed record BaseHolder(PathBase Base);
        [ExpoSharedObject] public partial class Unsealed : SharedObject { }
        public static class Outer { [ExpoSharedObject] public sealed partial class Nested : SharedObject { } }
        [ExpoSharedObject] public sealed partial class Generic<T> : SharedObject { }
        public sealed class Plain : SharedObject { }
        """,
        $"[JS] public int Use({parameterType} value) => 0;",
        "typeof(Entry)"
    ));

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI023" && item.GetMessage().Contains("'Use'", StringComparison.Ordinal));
    Assert.Contains(reportedType, diagnostic.GetMessage(), StringComparison.Ordinal);
    Assert.Contains("inside a composed codec", diagnostic.GetMessage(), StringComparison.Ordinal);
    Assert.Contains(rule, diagnostic.GetMessage(), StringComparison.Ordinal);
    Assert.DoesNotContain("\"use\"", GeneratedText(result), StringComparison.Ordinal);
  }

  [Fact]
  public void RecordWithCallbackFieldStaysRejected()
  {
    var result = GeneratorTestHost.Run(CompositeSharedObjectSource(
        "public sealed record WithCallback(JavaScriptCallback<int> Callback, Entry Entry);",
        "[JS] public int Use(WithCallback value) => 0;"
    ));

    Assert.Equal(new[] { "EXPOJSI007", "EXPOJSI001" }, GeneratorDiagnosticIds(result));
    Assert.DoesNotContain("\"use\"", GeneratedText(result), StringComparison.Ordinal);
  }

  [Fact]
  public void CallbackInsideListStaysRejected()
  {
    var result = GeneratorTestHost.Run(CompositeSharedObjectSource(
        string.Empty,
        "[JS] public int Use(IReadOnlyList<JavaScriptCallback<int>> value) => 0;"
    ));

    Assert.Equal(new[] { "EXPOJSI001" }, GeneratorDiagnosticIds(result));
    Assert.DoesNotContain("\"use\"", GeneratedText(result), StringComparison.Ordinal);
  }

  [Fact]
  public void ForeignModuleSharedObjectInCompositeReportsExpoJsi024()
  {
    // No module lists Entry in Classes, so the ownership check reports it at its declaration.
    var result = GeneratorTestHost.Run(CompositeSharedObjectSource(
        "[ExpoSharedObject] public sealed partial class Mine : SharedObject { }",
        "[JS] public int Use(IReadOnlyList<Entry> value) => 0;",
        classes: "typeof(Mine)"
    ));

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI024");
    Assert.Contains("Entry", diagnostic.GetMessage(), StringComparison.Ordinal);
    Assert.Equal(new[] { "EXPOJSI024" }, GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
  }

  [Fact]
  public void AsyncSettlementDoesNotReadCurrentRuntimeContext()
  {
    var result = GeneratorTestHost.Run(CompositeSharedObjectSource(
        string.Empty,
        "[JS] public Task<IReadOnlyList<Entry>> ListAsync() => Task.FromResult<IReadOnlyList<Entry>>(new Entry[0]);"
    ));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
    var factory = Assert.Single(AsyncSettlementFactories(GeneratedText(result)));
    Assert.DoesNotContain("CurrentRuntimeContext", factory, StringComparison.Ordinal);
    Assert.Contains(
        "JavaScriptArrayCodec<global::Expo.TestModules.Entry, SharedObjectCodec<global::Expo.TestModules.Entry>>.Encode(__expoState.Value.Item1, runtime, __expoState.Value.Item2)",
        factory,
        StringComparison.Ordinal
    );
  }

  [Fact]
  public void CallbackWithSharedObjectArgsAndResultIsAccepted()
  {
    var result = GeneratorTestHost.Run(CompositeSharedObjectSource(
        string.Empty,
        "[JS] public int Use(JavaScriptCallback<ValueTuple<IReadOnlyList<Entry>>, Entry?> callback) => 0;"
    ));

    Assert.Empty(GeneratorDiagnosticIds(result));
    AssertNoCompilerDiagnostics(result);
    var source = GeneratedText(result);
    Assert.Contains("JavaScriptCallbackCodec<", source, StringComparison.Ordinal);
    Assert.Contains(
        "JavaScriptArrayCodec<global::Expo.TestModules.Entry, SharedObjectCodec<global::Expo.TestModules.Entry>>",
        source,
        StringComparison.Ordinal
    );
    Assert.Contains(
        "NullableReferenceCodec<global::Expo.TestModules.Entry, SharedObjectCodec<global::Expo.TestModules.Entry>>",
        source,
        StringComparison.Ordinal
    );
  }

  // Each Promise factory of the generated text, from its signature to the abandon callback.
  private static IReadOnlyList<string> AsyncSettlementFactories(string text)
  {
    const string start = "static (runtime, __expoState) =>";
    const string end = "static __expoState =>";
    var factories = new List<string>();
    for (var index = text.IndexOf(start, StringComparison.Ordinal);
        index >= 0;
        index = text.IndexOf(start, index + start.Length, StringComparison.Ordinal))
    {
      var stop = text.IndexOf(end, index, StringComparison.Ordinal);
      Assert.True(stop > index);
      factories.Add(text[index..stop]);
    }

    return factories;
  }

  [Fact]
  public void EventPayloadWithConcreteSharedObjectStaysRejected()
  {
    var result = GeneratorTestHost.Run(CompositeSharedObjectSource(
        string.Empty,
        """
        [Event] public partial Func<Entry, Task> Direct { get; }
        [Event] public partial Func<IReadOnlyList<Entry>, Task> InList { get; }
        [Event] public partial Func<EntryPair, Task> InRecord { get; }
        """
    ));

    Assert.Equal(3, result.Diagnostics.Count(item => item.Id == "EXPOJSI019"));
    Assert.DoesNotContain(result.Diagnostics, item => item.Id == "EXPOJSI023");
  }

  [Fact]
  public void UnionPayloadThatIsASharedObjectStaysRejected()
  {
    var result = GeneratorTestHost.Run(CompositeSharedObjectSource(
        "[Union] public abstract record Choice { public sealed record Held(Entry Value) : Choice; public sealed record Text(string Value) : Choice; }",
        "[JS] public Choice Echo(Choice value) => value;"
    ));

    Assert.Contains(result.Diagnostics, item => item.Id == "EXPOJSI030");
  }
}
