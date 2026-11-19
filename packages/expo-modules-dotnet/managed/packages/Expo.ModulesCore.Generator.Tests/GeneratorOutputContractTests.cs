using Microsoft.CodeAnalysis;
using Xunit;

namespace Expo.ModulesCore.Generator.Tests;

public sealed class GeneratorOutputContractTests
{
  [Fact]
  public void OutputContractNormalizesLineEndings()
  {
    var unix = new GeneratedSource("Generated.g.cs", "first\nsecond\n");
    var windows = new GeneratedSource("Generated.g.cs", "first\r\nsecond\r\n");

    Assert.Equal(
        GeneratorOutputContract.GetSourceContract(unix),
        GeneratorOutputContract.GetSourceContract(windows)
    );
  }

  [Fact]
  public void DiagnosticContractIncludesWarnings()
  {
    var descriptor = new DiagnosticDescriptor(
        "EXPOJSI999",
        "Test warning",
        "Test warning message",
        "Expo.ModulesCore",
        DiagnosticSeverity.Warning,
        true
    );
    var result = new GeneratorRunResult(
        new[] { Diagnostic.Create(descriptor, Location.None) },
        Array.Empty<GeneratedSource>()
    );

    Assert.Contains("EXPOJSI999|Warning|Test warning message|0|0|0|0", result.GetDiagnosticContract());
  }

  [Fact]
  public void GeneratorPreservesModuleOutputContract()
  {
    var result = GeneratorTestHost.Run(
        """
        using System;
        using System.Threading.Tasks;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        public sealed record ContractRecord(string Label, int Count);

        [ExpoModule("Contract")]
        [Events("onTick")]
        public sealed partial class ContractModule
        {
          [JS] public ContractRecord Snapshot(ContractRecord value) => value;
          [JS] public Task<ContractRecord> SnapshotAsync(ContractRecord value) => Task.FromResult(value);
          [JS] public bool Ready { get; set; }
          [Event] public partial Func<ContractRecord, Task> Changed { get; }
        }
        """
    );

    Assert.Equal(
        new[]
        {
          "ExpoModulesProvider_Expo_TestModules.g.cs:FEAFC66BB3DA7115BEC1CA6A5840AA498917469348B59B2E17AECA7374394607",
          "global__Expo_TestModules_ContractModule_DFCB3B03.Events.g.cs:5EDF4B279587289BFFB6A548BD0C43AF76CA8EF3440A2DB17C6C973139943F79",
        },
        result.GetOutputContract()
    );
    Assert.Empty(result.GetDiagnosticContract());
  }

  [Fact]
  public void GeneratorPreservesSharedObjectOutputContract()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoSharedObject("ContractEntry")]
        public sealed partial class ContractEntry : SharedObject
        {
          [JS] public ContractEntry(string value) => Value = value;
          [JS] public string Value { get; set; }
          [JS] public string Read() => Value;
        }

        [ExpoModule(Classes = new[] { typeof(ContractEntry) })]
        public sealed partial class ContractModule
        {
          [JS] public ContractEntry Create(string value) => new(value);
        }
        """
    );

    Assert.Equal(
        new[]
        {
          "ExpoModulesProvider_Expo_TestModules.g.cs:7F1A6B1338D596556BE8134C3CFF28D1AEA5B53B30323FAD155C7643C3D00222",
        },
        result.GetOutputContract()
    );
    Assert.Empty(result.GetDiagnosticContract());
  }

  [Fact]
  public void GeneratorPreservesDiagnosticOutputContract()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class InvalidModule
        {
          [JS] public static void Invalid() { }
        }
        """
    );

    Assert.Equal(
        new[]
        {
          "ExpoModulesProvider_Expo_TestModules.g.cs:9BB7895A66C0F1AB558BE3D3FEB85243AC981C78849A918C2ABC53CDD746B350",
        },
        result.GetOutputContract()
    );
    Assert.Equal(
        new[]
        {
          "EXPOJSI004|Error|Method 'Invalid' cannot be exported to JavaScript because it is static|7|26|7|33",
        },
        result.GetDiagnosticContract()
    );
  }

  [Fact]
  public void GeneratorPreservesLeafFreeCodecPolicyModuleOutputContract()
  {
    // OnValue and OnBuffer stay here on purpose: typed-event payloads keep today's ownership,
    // so a change to their generated text must break this frozen digest.
    var result = GeneratorTestHost.Run(
        """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Expo.JSI;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        public enum PolicyMode { First, Second }

        public sealed record PolicyPayload(string Label, int Count);

        public sealed record PolicyPayloadRecord(string Label, int Count);

        [ExpoModule("PolicyLeafFree")]
        [Events("onLegacy")]
        public sealed partial class PolicyLeafFreeModule
        {
          [JS] public int Int(int value) => value;
          [JS] public string Text(string value) => value;
          [JS] public PolicyMode Mode(PolicyMode value) => value;
          [JS] public Guid Id(Guid value) => value;
          [JS] public int Bytes(byte[] value) => value.Length;
          [JS] public int Mem(Memory<byte> value) => value.Length;
          [JS] public int List(IReadOnlyList<int> value) => value.Count;
          [JS] public int Dict(Dictionary<string, int> value) => value.Count;
          [JS] public int ReadOnlyDict(IReadOnlyDictionary<string, string> value) => value.Count;
          [JS] public int? Nullable(int? value) => value;
          [JS] public int Record(PolicyPayloadRecord value) => 0;
          [JS] public int Callback(JavaScriptCallback<int> callback) => 0;
          [JS] public string TupleCallback(JavaScriptCallback<ValueTuple<string>, string> callback) => "";
          [JS] public int Optional(int value = 5) => value;

          [JS] public Task<IReadOnlyList<int>> ListResultAsync() => Task.FromResult<IReadOnlyList<int>>(new[] { 1 });

          [JS] public IReadOnlyList<int> ListReturn() => new[] { 1 };
          [JS] public PolicyPayloadRecord RecordReturn(PolicyPayloadRecord value) => value;
          [JS] public Span<byte> SpanReturn() => new byte[1];

          [JS] public string SpanToString(ReadOnlySpan<byte> value) => "";

          [JS] public IReadOnlyList<int> ListProperty { get; set; } = Array.Empty<int>();
          [JS] public string TextProperty { get; set; } = "";

          [Event] public partial Func<PolicyPayload, Task> OnRecord { get; }
          [Event] public partial Func<JavaScriptValue, Task> OnValue { get; }
          [Event] public partial Func<ArrayBuffer, Task> OnBuffer { get; }
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("CS", StringComparison.Ordinal));
    // PolicyMode crosses through a generated enum codec, and the digest covers that codec.
    Assert.Equal(
        new[]
        {
          "ExpoModulesProvider_Expo_TestModules.g.cs:2785DC4B5C08F300CA69E54C41100AE2CD990FB6DA8658B2B71D0AD5E5FEC934",
          "global__Expo_TestModules_PolicyLeafFreeModule_8D511E8D.Events.g.cs:EB0CCDFABDF861EE54188FA227D628F2A6534E012C2B43EAEF6AFB0C0CA9DE8A",
        },
        result.GetOutputContract()
    );
    Assert.Empty(result.GetDiagnosticContract());
  }

  [Fact]
  public void GeneratorPinsLeafCodecPolicyModuleOutputContract()
  {
    var result = GeneratorTestHost.Run(
        """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Expo.JSI;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        public sealed record PolicyRecord(ArrayBuffer Buffer, JavaScriptValue Value);

        [ExpoModule("PolicyLeaf")]
        public sealed partial class PolicyLeafModule
        {
          [JS] public int Record(PolicyRecord value) => 0;
          [JS] public int Value(JavaScriptValue value) => 0;
          [JS] public int Buffer(ArrayBuffer value) => 0;
          [JS] public int Values(IReadOnlyList<JavaScriptValue> value) => 0;
          [JS] public int ValueMap(Dictionary<string, JavaScriptValue> value) => 0;

          [JS] public Task ValueTask(JavaScriptValue value) => Task.CompletedTask;
          [JS] public Task BufferTask(ArrayBuffer value) => Task.CompletedTask;
          [JS] public Task<JavaScriptValue> ValueResultAsync(JavaScriptValue value) => Task.FromResult(value);
          [JS] public Task<ArrayBuffer> BufferResultAsync(ArrayBuffer value) => Task.FromResult(value);

          [JS] public JavaScriptValue ValueReturn(JavaScriptValue value) => value;
          [JS] public ArrayBuffer BufferReturn(ArrayBuffer value) => value;
          [JS] public PolicyRecord RecordReturn(PolicyRecord value) => value;
          [JS] public IReadOnlyList<JavaScriptValue> ValuesReturn() => Array.Empty<JavaScriptValue>();

          [JS] public ArrayBuffer SpanToBuffer(ReadOnlySpan<byte> value) => ArrayBuffer.CopyFrom(value);
          [JS] public JavaScriptValue SpanToValue(ReadOnlySpan<byte> value) => default;

          [JS] public ArrayBuffer BufferProperty { get; set; }
          [JS] public JavaScriptValue ValueProperty { get; set; }
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("CS", StringComparison.Ordinal));
    Assert.Equal(
        new[]
        {
          "ExpoModulesProvider_Expo_TestModules.g.cs:D11C62510C7DA44C11A823E3BC2021A5332A64ABCF8B3BEC4A173487DBCA1627",
        },
        result.GetOutputContract()
    );
    Assert.Empty(result.GetDiagnosticContract());
  }

  [Fact]
  public void GeneratorPreservesLeafFreeCodecPolicySharedObjectOutputContract()
  {
    var result = GeneratorTestHost.Run(
        """
        using System;
        using System.Threading.Tasks;
        using Expo.JSI;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        public sealed record PolicyPayload(string Label, int Count);

        [ExpoSharedObject("PolicyOther")]
        public sealed partial class PolicyOther : SharedObject
        {
          [JS] public PolicyOther() { }
        }

        [ExpoSharedObject("PolicyEntry")]
        public sealed partial class PolicyEntry : SharedObject
        {
          [JS]
          public PolicyEntry(
              string label,
              JavaScriptCallback<int> callback,
              PolicyOther other)
          {
          }

          [JS] public PolicyOther MakeOther() => new();
          [JS] public Task<PolicyOther> MakeOtherAsync() => Task.FromResult(new PolicyOther());
          [JS] public int Accept(PolicyOther other) => 0;
          [JS] public PolicyOther Related { get; set; } = new();

          [Event] public partial Func<PolicyPayload, Task> OnPayload { get; }
        }

        [ExpoModule("PolicyOwner", Classes = new[] { typeof(PolicyEntry), typeof(PolicyOther) })]
        public sealed partial class PolicyOwnerModule
        {
          [JS] public PolicyEntry MakeEntry() => new("", default, new PolicyOther());
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("CS", StringComparison.Ordinal));
    Assert.Equal(
        new[]
        {
          "ExpoModulesProvider_Expo_TestModules.g.cs:5F9E8F25496491872AF6C564A86EF64621D17305767254E6383EBB65E89FDE59",
          "global__Expo_TestModules_PolicyEntry_7B42955D.SharedObjectEvents.g.cs:7350FE94400C181F149408B9E9810B59C0B87573FD669DAB1B5D5C808A9A2A92",
        },
        result.GetOutputContract()
    );
    Assert.Empty(result.GetDiagnosticContract());
  }

  [Fact]
  public void GeneratorPinsLeafCodecPolicySharedObjectOutputContract()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.JSI;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoSharedObject("PolicyLeafEntry")]
        public sealed partial class PolicyLeafEntry : SharedObject
        {
          [JS] public PolicyLeafEntry(string label, JavaScriptValue value) { }
          [JS] public int Measure(ArrayBuffer value) => 0;
          [JS] public JavaScriptValue Echo(JavaScriptValue value) => value;
          [JS] public JavaScriptValue Tag { get; set; }
        }

        [ExpoModule("PolicyLeafOwner", Classes = new[] { typeof(PolicyLeafEntry) })]
        public sealed partial class PolicyLeafOwnerModule
        {
          [JS] public PolicyLeafEntry Make() => new("", null!);
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("CS", StringComparison.Ordinal));
    Assert.Equal(
        new[]
        {
          "ExpoModulesProvider_Expo_TestModules.g.cs:2D9269761804CF753749FE42D647B1D4CFABFE073E05CD4D4CC18B25CFD12DAB",
        },
        result.GetOutputContract()
    );
    Assert.Empty(result.GetDiagnosticContract());
  }

  [Fact]
  public void GeneratorPreservesNullableCodecOutputContract()
  {
    var result = GeneratorTestHost.Run(
        """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        public sealed record NullableRecord(string Label, int Count);

        [ExpoModule("NullableCodecs")]
        public sealed partial class NullableCodecsModule
        {
          [JS] public string? Text(string? value) => value;
          [JS] public Uri? Link(Uri? value) => value;
          [JS] public byte[]? Bytes(byte[]? value) => value;
          [JS] public NullableRecord? Record(NullableRecord? value) => value;
          [JS] public IReadOnlyList<string?>? Strings(IReadOnlyList<string?>? value) => value;
          [JS] public Dictionary<string, int?>? Counts(Dictionary<string, int?>? value) => value;
          [JS] public IReadOnlyDictionary<string, string>? Labels(IReadOnlyDictionary<string, string>? value) => value;
          [JS] public IReadOnlyList<IReadOnlyList<int>?> Nested(IReadOnlyList<IReadOnlyList<int>?> value) => value;

          [JS] public Task<string?> TextAsync(string? value) => Task.FromResult(value);
          [JS] public Task<Uri?> LinkAsync(Uri? value) => Task.FromResult(value);
          [JS] public Task<byte[]?> BytesAsync(byte[]? value) => Task.FromResult(value);
          [JS] public Task<NullableRecord?> RecordAsync(NullableRecord? value) => Task.FromResult(value);
          [JS] public Task<IReadOnlyList<string?>?> StringsAsync(IReadOnlyList<string?>? value) => Task.FromResult(value);
          [JS] public Task<Dictionary<string, int?>?> CountsAsync(Dictionary<string, int?>? value) => Task.FromResult(value);
          [JS] public Task<IReadOnlyDictionary<string, string>?> LabelsAsync(IReadOnlyDictionary<string, string>? value) => Task.FromResult(value);
          [JS] public Task<IReadOnlyList<IReadOnlyList<int>?>> NestedAsync(IReadOnlyList<IReadOnlyList<int>?> value) => Task.FromResult(value);

          [JS] public string? TextProperty { get; set; }
          [JS] public Uri? LinkProperty { get; set; }
          [JS] public byte[]? BytesProperty { get; set; }
          [JS] public NullableRecord? RecordProperty { get; set; }
          [JS] public IReadOnlyList<string?>? StringsProperty { get; set; }
          [JS] public Dictionary<string, int?>? CountsProperty { get; set; }
          [JS] public IReadOnlyDictionary<string, string>? LabelsProperty { get; set; }
          [JS] public IReadOnlyList<IReadOnlyList<int>?> NestedProperty { get; set; } = Array.Empty<IReadOnlyList<int>?>();
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("CS", StringComparison.Ordinal));
    Assert.Equal(
        new[]
        {
          "ExpoModulesProvider_Expo_TestModules.g.cs:46954C1FAC8AD7D9A04DEDE4B787FCCF8B9A70ED5611148416921FFAE199DB3A",
        },
        result.GetOutputContract()
    );
    Assert.Empty(result.GetDiagnosticContract());
  }

  [Fact]
  public void GeneratorPreservesCodecPolicyDiagnosticContract()
  {
    var result = GeneratorTestHost.Run(
        """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Expo.JSI;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule("PolicyDiagnostics", Classes = new[] { typeof(PolicyEntry) })]
        public sealed partial class PolicyDiagnosticsModule
        {
          [JS] public int OptionalValue(JavaScriptValue? value) => 0;
          [JS] public int OptionalBuffer(ArrayBuffer? value) => 0;
          [JS] public int OptionalCallback(JavaScriptCallback<int>? callback) => 0;
          [JS] public int OptionalSharedObject(SharedObject? value) => 0;
          [JS] public int SharedObjects(IReadOnlyList<SharedObject> value) => 0;
          [JS] public int MutableList(List<int> value) => 0;
          [JS] public JavaScriptCallback<int> CallbackProperty { get; set; }
          [JS] public int TwoSpans(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second) => 0;
          [JS] public Task<int> AsyncSpan(ReadOnlySpan<byte> value) => Task.FromResult(0);
          [JS] public int ConcreteList(IReadOnlyList<PolicyEntry?> value) => 0;
          [JS] public IReadOnlyDictionary<string, PolicyEntry> ConcreteDictionary(Dictionary<string, PolicyEntry> value) => value;
          [JS] public PolicyHolder ConcreteRecord(PolicyHolder value) => value;
        }

        [ExpoSharedObject]
        public sealed partial class PolicyEntry : SharedObject
        {
        }

        public sealed record PolicyHolder(PolicyEntry First, PolicyEntry? Second);
        """
    );

    Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("CS", StringComparison.Ordinal));
    Assert.Equal(
        new[]
        {
          "ExpoModulesProvider_Expo_TestModules.g.cs:270D74E33F40E9C229D0219993094663608D204CB8879D970341FF57DCE63885",
        },
        result.GetOutputContract()
    );
    Assert.Equal(
        new[]
        {
          "EXPOJSI015|Error|Property 'CallbackProperty' uses unsupported type 'global::Expo.ModulesCore.JavaScriptCallback<int>'|0|0|0|0",
          "EXPOJSI001|Error|Parameter 'value' on 'OptionalValue' uses unsupported type 'global::Expo.JSI.JavaScriptValue'|12|49|12|54",
          "EXPOJSI023|Error|'OptionalSharedObject' cannot be exported to JavaScript: parameter 'value' uses shared-object type 'global::Expo.ModulesCore.SharedObject', which is the polymorphic SharedObject base|15|53|15|58",
          "EXPOJSI023|Error|'SharedObjects' cannot be exported to JavaScript: parameter 'value' uses shared-object type 'global::Expo.ModulesCore.SharedObject' inside a composed codec, which is the polymorphic SharedObject base|16|60|16|65",
          "EXPOJSI001|Error|Parameter 'value' on 'MutableList' uses unsupported type 'global::System.Collections.Generic.List<int>'|17|40|17|45",
          "EXPOJSI013|Error|Method 'TwoSpans' declares multiple span parameters (first, second); at most one Span<byte> or ReadOnlySpan<byte> parameter is supported|19|72|19|78",
          "EXPOJSI012|Error|Method 'AsyncSpan' parameter 'value' uses 'global::System.ReadOnlySpan<byte>', which is supported only by synchronous Expo module methods|20|53|20|58",
        },
        result.GetDiagnosticContract()
    );
  }
}
