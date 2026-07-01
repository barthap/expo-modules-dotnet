using Microsoft.CodeAnalysis;

namespace Expo.ModulesCore.Generator;

internal static class ExpoModulesDiagnostics
{
  public static readonly DiagnosticDescriptor UnsupportedParameterType = new(
      id: "EXPOJSI001",
      title: "Unsupported Expo module parameter type",
      messageFormat: "Parameter '{0}' on '{1}' uses unsupported type '{2}'",
      category: "Expo.ModulesCore",
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true
  );

  public static readonly DiagnosticDescriptor UnsupportedReturnType = new(
      id: "EXPOJSI002",
      title: "Unsupported Expo module return type",
      messageFormat: "Method '{0}' uses unsupported return type '{1}'",
      category: "Expo.ModulesCore",
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true
  );

  public static readonly DiagnosticDescriptor UnsupportedModuleConstructor = new(
      id: "EXPOJSI003",
      title: "Unsupported Expo module constructor",
      messageFormat: "Module '{0}' must have a public or internal parameterless constructor",
      category: "Expo.ModulesCore",
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true
  );
}
