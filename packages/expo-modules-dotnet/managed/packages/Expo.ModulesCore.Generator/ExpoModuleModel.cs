using Microsoft.CodeAnalysis;

namespace Expo.ModulesCore.Generator;

internal sealed record ExpoModuleModel(
    string FullyQualifiedTypeName,
    string ModuleName,
    Location? Location,
    ExpoModuleConstructorStrategy ConstructorStrategy,
    EquatableArray<ExpoFunctionModel> Functions,
    EquatableArray<ExpoGeneratedRecordCodecModel> RecordCodecs,
    EquatableArray<ExpoDiagnosticModel> Diagnostics);

internal enum ExpoModuleConstructorStrategy
{
  Unsupported,
  Parameterless,
  RuntimeContext,
}

internal sealed record ExpoFunctionModel(
    string MethodName,
    string JavaScriptName,
    Location? Location,
    string ReturnType,
    string ReturnCodecExpression,
    bool ReturnsVoid,
    bool IsAsync,
    bool AsyncReturnsVoid,
    string AsyncResultType,
    string AsyncResultCodecExpression,
    EquatableArray<ExpoParameterModel> Parameters);

internal sealed record ExpoParameterModel(
    string Name,
    string TypeName,
    string CodecExpression,
    bool RequiresRuntimeContext,
    bool HasDefaultValue,
    string DefaultValueExpression);

internal sealed record ExpoDiagnosticModel(
    string DescriptorId,
    Location? Location,
    EquatableArray<string> Arguments);

internal sealed record ExpoGeneratedRecordCodecModel(
    string CodecTypeName,
    string RecordTypeName,
    EquatableArray<ExpoGeneratedRecordFieldModel> Fields,
    Location? Location);

internal sealed record ExpoGeneratedRecordFieldModel(
    string ParameterName,
    string PropertyName,
    string TypeName,
    string CodecExpression,
    Location? Location);

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
