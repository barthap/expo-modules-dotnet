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

  public static readonly DiagnosticDescriptor UnsupportedJSMethodShape = new(
      id: "EXPOJSI004",
      title: "Unsupported Expo module method shape",
      messageFormat: "Method '{0}' cannot be exported to JavaScript because it is {1}",
      category: "Expo.ModulesCore",
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true
  );

  public static readonly DiagnosticDescriptor DuplicateJavaScriptFunctionName = new(
      id: "EXPOJSI005",
      title: "Duplicate Expo module JavaScript function name",
      messageFormat: "Module '{0}' exports duplicate JavaScript function name '{1}'",
      category: "Expo.ModulesCore",
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true
  );

  public static readonly DiagnosticDescriptor DuplicateModuleName = new(
      id: "EXPOJSI006",
      title: "Duplicate Expo module name",
      messageFormat: "Multiple Expo modules export module name '{0}'",
      category: "Expo.ModulesCore",
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true
  );

  public static readonly DiagnosticDescriptor UnsupportedRecordField = new(
      id: "EXPOJSI007",
      title: "Unsupported Expo record field",
      messageFormat: "Record '{0}' field '{1}' uses unsupported type '{2}'",
      category: "Expo.ModulesCore",
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true
  );

  public static readonly DiagnosticDescriptor UnsupportedCallbackCodec = new(
      id: "EXPOJSI008",
      title: "Unsupported JavaScript callback codec",
      messageFormat: "Callback parameter '{0}' on '{1}' uses unsupported callback type '{2}'. Callback argument and result type codecs are required, and callback arguments must use ValueTuple shapes.",
      category: "Expo.ModulesCore",
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true
  );
}
