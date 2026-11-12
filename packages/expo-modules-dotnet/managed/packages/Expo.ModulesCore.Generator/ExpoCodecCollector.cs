using Microsoft.CodeAnalysis;

namespace Expo.ModulesCore.Generator;

// The codec models that one module or shared-object model collects while its codecs resolve. It
// holds symbols, so it never leaves model creation: the models copy its lists into EquatableArray.
internal sealed class ExpoCodecCollector
{
  public ExpoCodecCollector(IAssemblySymbol assembly)
  {
    Assembly = assembly;
  }

  // The assembly being compiled. A type from another assembly has a different ContainingAssembly.
  public IAssemblySymbol Assembly { get; }

  // Where the codec being resolved is used, for diagnostics about a type that has no codec.
  public Location? UseSite { get; set; }

  public List<ExpoGeneratedRecordCodecModel> Records { get; } = new();

  public List<ExpoGeneratedUnionCodecModel> Unions { get; } = new();

  // The unions whose analysis is running, and the ones that analysis reached again. Forks share
  // this state, so a union that reaches itself through any codec path is caught on re-entry.
  public UnionAnalysisState UnionAnalysis { get; private set; } = new();

  // A copy that resolution can fill and drop, so a rejected codec adds no model.
  public ExpoCodecCollector Fork()
  {
    var fork = new ExpoCodecCollector(Assembly) { UseSite = UseSite, UnionAnalysis = UnionAnalysis };
    fork.Records.AddRange(Records);
    fork.Unions.AddRange(Unions);
    return fork;
  }

  // Takes the models of a fork that resolved successfully.
  public void Adopt(ExpoCodecCollector fork)
  {
    Records.Clear();
    Records.AddRange(fork.Records);
    Unions.Clear();
    Unions.AddRange(fork.Unions);
  }
}

internal sealed class UnionAnalysisState
{
  public HashSet<INamedTypeSymbol> InProgress { get; } = new(SymbolEqualityComparer.Default);

  public HashSet<INamedTypeSymbol> Reentered { get; } = new(SymbolEqualityComparer.Default);
}
