using Microsoft.CodeAnalysis;

namespace Expo.ModulesCore.Generator;

internal sealed record ExpoModuleModel(
    string FullyQualifiedTypeName,
    string ModuleName,
    Location? Location,
    bool CanConstruct,
    EquatableArray<ExpoFunctionModel> Functions,
    EquatableArray<ExpoDiagnosticModel> Diagnostics);

internal sealed record ExpoFunctionModel(
    string MethodName,
    string JavaScriptName,
    Location? Location,
    string ReturnType,
    string ReturnCodecExpression,
    bool ReturnsVoid,
    EquatableArray<ExpoParameterModel> Parameters);

internal sealed record ExpoParameterModel(
    string Name,
    string TypeName,
    string CodecExpression,
    bool HasDefaultValue,
    string DefaultValueExpression);

internal sealed record ExpoDiagnosticModel(
    string DescriptorId,
    Location? Location,
    EquatableArray<string> Arguments);

internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>
    where T : IEquatable<T>
{
  private readonly T[] _values;

  public EquatableArray(IEnumerable<T> values)
  {
    _values = values.ToArray();
  }

  public IReadOnlyList<T> Values => _values ?? Array.Empty<T>();

  public bool Equals(EquatableArray<T> other) =>
      (_values ?? Array.Empty<T>()).SequenceEqual(other._values ?? Array.Empty<T>());

  public override bool Equals(object? obj) =>
      obj is EquatableArray<T> other && Equals(other);

  public override int GetHashCode()
  {
    if (_values is null) return 0;
    unchecked
    {
      var hash = 17;
      foreach (var value in _values)
      {
        hash = (hash * 31) + value.GetHashCode();
      }
      return hash;
    }
  }
}
