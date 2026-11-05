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
    EquatableArray<ExpoSharedObjectClassModel> SharedObjectClasses,
    EquatableArray<ExpoGeneratedRecordCodecModel> RecordCodecs,
    EquatableArray<ExpoDiagnosticModel> Diagnostics);

internal sealed record ExpoSharedObjectClassModel(
    string TypeName,
    Location? Location);

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
    ExpoCodecDescriptor? Codec,
    Location? Location,
    bool IsShapeValid,
    bool IsDispatchable);

internal sealed record ExpoOrphanEventModel(
    string Namespace,
    string FullyQualifiedContainerName,
    string ContainerName,
    string ContainerAccessibility,
    string PropertyName,
    string PropertyType,
    string PropertyModifiers,
    string GetterAccessor,
    Location? Location,
    bool CanReproduce);

internal enum ExpoModuleConstructorStrategy
{
  Unsupported,
  Parameterless,
  RuntimeContext,
}

// ReturnCodec and AsyncResultCodec are null when the function has no codec (void, async void, the
// other position, or a span return). A codec whose ContextRequirement is DotnetRuntimeContext
// marks a direct encode location that must receive the current DotnetRuntimeContext explicitly.
// For an async result, the generated host callback must capture
// GeneratedFunction.CurrentRuntimeContext while the host-function frame is still valid and pass
// that captured context into Promise settlement; settlement code must never consult the
// thread-static accessor after the frame exits.
internal sealed record ExpoFunctionModel(
    string MethodName,
    string JavaScriptName,
    Location? Location,
    string ReturnType,
    ExpoCodecDescriptor? ReturnCodec,
    bool ReturnsVoid,
    bool IsAsync,
    bool AsyncReturnsVoid,
    string AsyncResultType,
    ExpoCodecDescriptor? AsyncResultCodec,
    EquatableArray<ExpoParameterModel> Parameters,
    ExpoReturnPassingKind ReturnPassingKind = ExpoReturnPassingKind.Codec,
    ExpoReturnPassingKind AsyncResultPassingKind = ExpoReturnPassingKind.Codec);

// A Codec with a DotnetRuntimeContext requirement covers both direct decode/encode locations of
// the property: the generated getter encode and the generated setter decode receive the current
// DotnetRuntimeContext explicitly.
internal sealed record ExpoPropertyModel(
    string PropertyName,
    string JavaScriptName,
    Location? Location,
    string TypeName,
    ExpoCodecDescriptor Codec,
    bool HasSetter);

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
    ExpoCodecDescriptor? Codec,
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
    ExpoResourceOwnership ResourceOwnership,
    bool IsValueType,
    Location? Location);

internal sealed record ExpoGeneratedRecordFieldModel(
    string ParameterName,
    string CSharpPropertyName,
    string JavaScriptName,
    string TypeName,
    ExpoCodecDescriptor Codec,
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
