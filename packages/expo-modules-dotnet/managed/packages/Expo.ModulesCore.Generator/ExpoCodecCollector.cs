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

  public List<ExpoGeneratedRecordCodecModel> Records { get; } = new();

  public List<ExpoGeneratedUnionCodecModel> Unions { get; } = new();

  // A copy that resolution can fill and drop, so a rejected codec adds no model.
  public ExpoCodecCollector Fork()
  {
    var fork = new ExpoCodecCollector(Assembly);
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
