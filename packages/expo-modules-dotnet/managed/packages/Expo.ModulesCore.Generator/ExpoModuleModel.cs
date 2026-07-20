using Microsoft.CodeAnalysis;

namespace Expo.ModulesCore.Generator;

internal sealed record ExpoModuleModel(
    string FullyQualifiedTypeName,
    string Namespace,
    string SimpleTypeName,
    string Accessibility,
    string ModuleName,
    Location? Location,
    ExpoModuleConstructorStrategy ConstructorStrategy,
    ExpoLifecycleHookModel? OnCreateHook,
    ExpoLifecycleHookModel? OnDestroyHook,
    EquatableArray<string> EventNames,
    EquatableArray<ExpoEventModel> Events,
    EquatableArray<ExpoObservingHookModel> StartObservingHooks,
    EquatableArray<ExpoObservingHookModel> StopObservingHooks,
    EquatableArray<ExpoFunctionModel> Functions,
    EquatableArray<ExpoPropertyModel> Properties,
    EquatableArray<ExpoGeneratedRecordCodecModel> RecordCodecs,
    EquatableArray<ExpoDiagnosticModel> Diagnostics);

internal enum ExpoEventPayloadKind
{
  None,
  Codec,
  JavaScriptValue,
  ArrayBuffer,
}

internal sealed record ExpoEventModel(
    string PropertyName,
    string JavaScriptName,
    string Accessibility,
    string DeclarationModifiers,
    string GetterAccessor,
    string SetterAccessor,
    bool IsStatic,
    bool HasSetter,
    string DelegateTypeName,
    string PayloadTypeName,
    ExpoEventPayloadKind PayloadKind,
    string CodecExpression,
    Location? Location,
    bool IsShapeValid,
    bool IsDispatchable);

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
    EquatableArray<ExpoParameterModel> Parameters,
    ExpoReturnPassingKind ReturnPassingKind = ExpoReturnPassingKind.Codec,
    ExpoReturnPassingKind AsyncResultPassingKind = ExpoReturnPassingKind.Codec);

internal sealed record ExpoPropertyModel(
    string PropertyName,
    string JavaScriptName,
    Location? Location,
    string TypeName,
    string CodecExpression,
    bool HasSetter,
    bool OwnsDecodedValue,
    bool RequiresRuntimeContext);

internal enum ExpoParameterPassingKind
{
  Codec,
  MutableByteSpan,
  ReadOnlyByteSpan,
}

internal enum ExpoReturnPassingKind
{
  Codec,
  MutableByteSpan,
  ReadOnlyByteSpan,
}

internal sealed record ExpoObservingHookModel(
    string MethodName,
    string? EventName,
    bool PassesEventName,
    Location? Location);

internal sealed record ExpoLifecycleHookModel(
    string MethodName,
    Location? Location);

internal sealed record ExpoParameterModel(
    string Name,
    string TypeName,
    string CodecExpression,
    bool RequiresRuntimeContext,
    bool OwnsDecodedValue,
    bool HasDefaultValue,
    string DefaultValueExpression,
    ExpoParameterPassingKind PassingKind = ExpoParameterPassingKind.Codec);

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
    string CSharpPropertyName,
    string JavaScriptName,
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
