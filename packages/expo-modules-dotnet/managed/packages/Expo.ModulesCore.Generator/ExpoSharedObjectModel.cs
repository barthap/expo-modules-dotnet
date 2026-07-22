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
    EquatableArray<ExpoDiagnosticModel> Diagnostics);
