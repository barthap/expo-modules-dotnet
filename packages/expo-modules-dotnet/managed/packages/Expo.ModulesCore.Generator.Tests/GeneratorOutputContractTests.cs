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
          "ExpoModulesProvider_Expo_TestModules.g.cs:7F070A3463206559C440AECDFBA7E622144B11D4ECE1B8434DC849D000F18A04",
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
          "ExpoModulesProvider_Expo_TestModules.g.cs:CF15344B9013DB57C516A0C10025EA4D853FC4FD44123B699BF41B4AA854E785",
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
}
