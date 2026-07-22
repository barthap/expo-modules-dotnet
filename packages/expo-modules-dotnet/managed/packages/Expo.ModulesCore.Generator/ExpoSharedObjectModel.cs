using Microsoft.CodeAnalysis;

namespace Expo.ModulesCore.Generator;

internal sealed record ExpoSharedObjectModel(
    string FullyQualifiedTypeName,
    string Namespace,
    string SimpleTypeName,
    string Accessibility,
    string JavaScriptClassName,
    Location? Location,
    bool IsValid,
    ExpoSharedObjectConstructorModel? Constructor,
    EquatableArray<ExpoEventModel> Events,
    EquatableArray<ExpoFunctionModel> Functions,
    EquatableArray<ExpoPropertyModel> Properties,
    EquatableArray<ExpoGeneratedRecordCodecModel> RecordCodecs,
    EquatableArray<ExpoDiagnosticModel> Diagnostics);

internal sealed record ExpoSharedObjectConstructorModel(
    Location? Location,
    EquatableArray<ExpoParameterModel> Parameters);
