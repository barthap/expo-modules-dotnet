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
      messageFormat: "Module '{0}' must have a public or internal parameterless constructor or a constructor accepting DotnetRuntimeContext",
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

  public static readonly DiagnosticDescriptor InvalidEventName = new(
      id: "EXPOJSI009",
      title: "Invalid Expo module event name",
      messageFormat: "Module '{0}' declares {1} event name '{2}'",
      category: "Expo.ModulesCore",
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true
  );

  public static readonly DiagnosticDescriptor InvalidObservingHook = new(
      id: "EXPOJSI010",
      title: "Invalid Expo module observing hook",
      messageFormat: "Module '{0}' has invalid {1} observing hook '{2}': {3}",
      category: "Expo.ModulesCore",
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true
  );

  public static readonly DiagnosticDescriptor InvalidLifecycleHook = new(
      id: "EXPOJSI011",
      title: "Invalid Expo module lifecycle hook",
      messageFormat: "Module '{0}' has invalid {1} lifecycle hook '{2}': {3}",
      category: "Expo.ModulesCore",
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true
  );

  public static readonly DiagnosticDescriptor AsyncSpanParameter = new(
      id: "EXPOJSI012",
      title: "Async Expo module methods cannot borrow spans",
      messageFormat: "Method '{0}' parameter '{1}' uses '{2}', which is supported only by synchronous Expo module methods",
      category: "Expo.ModulesCore",
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true
  );

  public static readonly DiagnosticDescriptor MultipleSpanParameters = new(
      id: "EXPOJSI013",
      title: "Expo module method has multiple span parameters",
      messageFormat: "Method '{0}' declares multiple span parameters ({1}); at most one Span<byte> or ReadOnlySpan<byte> parameter is supported",
      category: "Expo.ModulesCore",
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true
  );

  public static readonly DiagnosticDescriptor UnsupportedJSPropertyShape = new(
      id: "EXPOJSI014",
      title: "Unsupported Expo module property shape",
      messageFormat: "Property '{0}' cannot be exported to JavaScript because it is {1}",
      category: "Expo.ModulesCore",
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true
  );

  public static readonly DiagnosticDescriptor UnsupportedJSPropertyType = new(
      id: "EXPOJSI015",
      title: "Unsupported Expo module property type",
      messageFormat: "Property '{0}' uses unsupported type '{1}'",
      category: "Expo.ModulesCore",
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true
  );

  public static readonly DiagnosticDescriptor DuplicateJavaScriptMemberName = new(
      id: "EXPOJSI016",
      title: "Duplicate Expo module JavaScript member name",
      messageFormat: "Module '{0}' exports duplicate JavaScript member name '{1}'",
      category: "Expo.ModulesCore",
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true
  );

  public static readonly DiagnosticDescriptor ReservedObservingPropertyName = new(
      id: "EXPOJSI017",
      title: "Reserved Expo module property name",
      messageFormat: "Property '{0}' cannot be exported to JavaScript because '{1}' is a reserved observing hook name",
      category: "Expo.ModulesCore",
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true
  );
}
