using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Expo.ModulesCore.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class ExpoModulesGenerator : IIncrementalGenerator
{
  private const string ExpoModuleAttributeMetadataName = "Expo.ModulesCore.ExpoModuleAttribute";
  private const string EventsAttributeMetadataName = "Expo.ModulesCore.EventsAttribute";
  private const string OnCreateAttributeMetadataName = "Expo.ModulesCore.OnCreateAttribute";
  private const string OnDestroyAttributeMetadataName = "Expo.ModulesCore.OnDestroyAttribute";
  private const string OnStartObservingAttributeMetadataName = "Expo.ModulesCore.OnStartObservingAttribute";
  private const string OnStopObservingAttributeMetadataName = "Expo.ModulesCore.OnStopObservingAttribute";
  private const string JSEnumAttributeMetadataName = "Expo.ModulesCore.JSEnumAttribute";
  private const string JSAttributeMetadataName = "Expo.ModulesCore.JSAttribute";
  private const string DotnetRuntimeContextMetadataName = "Expo.ModulesCore.DotnetRuntimeContext";
  private const string JavaScriptValueMetadataName = "Expo.JSI.JavaScriptValue";
  private const string ArrayBufferMetadataName = "global::Expo.ModulesCore.ArrayBuffer";

  public void Initialize(IncrementalGeneratorInitializationContext context)
  {
    var modules = context.SyntaxProvider.ForAttributeWithMetadataName(
        ExpoModuleAttributeMetadataName,
        static (node, _) => node is ClassDeclarationSyntax,
        static (syntaxContext, cancellationToken) =>
            CreateModuleModel(syntaxContext, cancellationToken)
    );

    var compilationAndModules = context.CompilationProvider.Combine(modules.Collect());

    context.RegisterSourceOutput(
        compilationAndModules,
        static (sourceContext, value) =>
        {
          var assemblyName = value.Left.AssemblyName ?? "ExpoModules";
          EmitProvider(
              sourceContext,
              assemblyName,
              value.Right.Where(module => module is not null).Select(module => module!)
          );
        }
    );
  }

  private static ExpoModuleModel? CreateModuleModel(
      GeneratorAttributeSyntaxContext context,
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();

    if (context.TargetSymbol is not INamedTypeSymbol typeSymbol)
    {
      return null;
    }

    var moduleName = typeSymbol.Name.EndsWith("Module", StringComparison.Ordinal)
        ? typeSymbol.Name.Substring(0, typeSymbol.Name.Length - "Module".Length)
        : typeSymbol.Name;

    foreach (var attribute in context.Attributes)
    {
      if (attribute.ConstructorArguments.Length == 1 &&
          attribute.ConstructorArguments[0].Value is string explicitName)
      {
        moduleName = explicitName;
      }
    }

    var diagnostics = new List<ExpoDiagnosticModel>();
    var constructorStrategy = GetConstructorStrategy(typeSymbol);
    if (constructorStrategy == ExpoModuleConstructorStrategy.Unsupported)
    {
      diagnostics.Add(new ExpoDiagnosticModel(
          ExpoModulesDiagnostics.UnsupportedModuleConstructor.Id,
          typeSymbol.Locations.FirstOrDefault(),
          new EquatableArray<string>(new[] { moduleName })
      ));
    }

    var recordCodecs = new List<ExpoGeneratedRecordCodecModel>();
    var onCreateHook = GetLifecycleHook(
        typeSymbol,
        moduleName,
        "create",
        OnCreateAttributeMetadataName,
        diagnostics
    );
    var onDestroyHook = GetLifecycleHook(
        typeSymbol,
        moduleName,
        "destroy",
        OnDestroyAttributeMetadataName,
        diagnostics
    );
    var eventNames = GetEventNames(typeSymbol, moduleName, diagnostics);
    var eventNameSet = new HashSet<string>(eventNames, StringComparer.Ordinal);
    var startObservingHooks = GetObservingHooks(
        typeSymbol,
        moduleName,
        "start",
        OnStartObservingAttributeMetadataName,
        eventNameSet,
        diagnostics
    );
    var stopObservingHooks = GetObservingHooks(
        typeSymbol,
        moduleName,
        "stop",
        OnStopObservingAttributeMetadataName,
        eventNameSet,
        diagnostics
    );
    HashSet<string> reservedJavaScriptNames = eventNameSet.Count == 0
        ? []
        : new HashSet<string>(["startObserving", "stopObserving"], StringComparer.Ordinal);
    var functions = GetFunctions(typeSymbol, diagnostics, recordCodecs, reservedJavaScriptNames);

    return new ExpoModuleModel(
        typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        moduleName,
        typeSymbol.Locations.FirstOrDefault(),
        constructorStrategy,
        onCreateHook,
        onDestroyHook,
        new EquatableArray<string>(eventNames),
        new EquatableArray<ExpoObservingHookModel>(startObservingHooks),
        new EquatableArray<ExpoObservingHookModel>(stopObservingHooks),
        new EquatableArray<ExpoFunctionModel>(functions),
        new EquatableArray<ExpoGeneratedRecordCodecModel>(recordCodecs),
        new EquatableArray<ExpoDiagnosticModel>(diagnostics)
    );
  }

  private static IEnumerable<string> GetEventNames(
      INamedTypeSymbol typeSymbol,
      string moduleName,
      List<ExpoDiagnosticModel> diagnostics)
  {
    var eventAttribute = typeSymbol.GetAttributes().FirstOrDefault(attribute =>
        attribute.AttributeClass?.ToDisplayString() == EventsAttributeMetadataName);
    if (eventAttribute is null)
    {
      return [];
    }

    var eventNames = new List<string>();
    var seen = new HashSet<string>(StringComparer.Ordinal);
    foreach (var argument in eventAttribute.ConstructorArguments)
    {
      foreach (var value in GetEventNameValues(argument))
      {
        if (string.IsNullOrWhiteSpace(value))
        {
          diagnostics.Add(new ExpoDiagnosticModel(
              ExpoModulesDiagnostics.InvalidEventName.Id,
              typeSymbol.Locations.FirstOrDefault(),
              new EquatableArray<string>(new[] { moduleName, "empty", value ?? string.Empty })
          ));
          continue;
        }

        var eventName = value!;
        if (!seen.Add(eventName))
        {
          diagnostics.Add(new ExpoDiagnosticModel(
              ExpoModulesDiagnostics.InvalidEventName.Id,
              typeSymbol.Locations.FirstOrDefault(),
              new EquatableArray<string>(new[] { moduleName, "duplicate", eventName })
          ));
          continue;
        }

        eventNames.Add(eventName);
      }
    }
    if (eventNames.Count == 0)
    {
      diagnostics.Add(new ExpoDiagnosticModel(
          ExpoModulesDiagnostics.InvalidEventName.Id,
          typeSymbol.Locations.FirstOrDefault(),
          new EquatableArray<string>(new[] { moduleName, "empty", string.Empty })
      ));
    }

    return eventNames;
  }

  private static IEnumerable<string?> GetEventNameValues(TypedConstant argument)
  {
    if (argument.Kind == TypedConstantKind.Array)
    {
      return argument.Values.Select(value => value.Value as string);
    }

    return new[] { argument.Value as string };
  }

  private static IEnumerable<ExpoObservingHookModel> GetObservingHooks(
      INamedTypeSymbol typeSymbol,
      string moduleName,
      string hookKind,
      string attributeMetadataName,
      HashSet<string> eventNames,
      List<ExpoDiagnosticModel> diagnostics)
  {
    var hooks = new List<ExpoObservingHookModel>();
    foreach (var member in typeSymbol.GetMembers().OfType<IMethodSymbol>())
    {
      if (member.MethodKind != MethodKind.Ordinary)
      {
        continue;
      }

      var attribute = member.GetAttributes().FirstOrDefault(item =>
          item.AttributeClass?.ToDisplayString() == attributeMetadataName);
      if (attribute is null)
      {
        continue;
      }

      if (eventNames.Count == 0)
      {
        diagnostics.Add(CreateInvalidObservingHook(
            moduleName,
            hookKind,
            member,
            "observing hooks require an [Events] declaration"
        ));
        continue;
      }

      if (member.IsStatic)
      {
        diagnostics.Add(CreateInvalidObservingHook(moduleName, hookKind, member, "method is static"));
        continue;
      }

      if (member.IsGenericMethod)
      {
        diagnostics.Add(CreateInvalidObservingHook(moduleName, hookKind, member, "method is generic"));
        continue;
      }

      if (!member.ReturnsVoid &&
          member.ReturnType.SpecialType != SpecialType.System_Void)
      {
        diagnostics.Add(CreateInvalidObservingHook(moduleName, hookKind, member, "method must return void"));
        continue;
      }

      var eventName = attribute.ConstructorArguments.Length == 1
          ? attribute.ConstructorArguments[0].Value as string
          : null;
      if (eventName is not null && !eventNames.Contains(eventName))
      {
        diagnostics.Add(CreateInvalidObservingHook(
            moduleName,
            hookKind,
            member,
            $"event '{eventName}' is not declared"
        ));
        continue;
      }

      var passesEventName = eventName is null;
      if (passesEventName)
      {
        if (member.Parameters.Length != 1 ||
            member.Parameters[0].Type.SpecialType != SpecialType.System_String)
        {
          diagnostics.Add(CreateInvalidObservingHook(
              moduleName,
              hookKind,
              member,
              "method must accept one string eventName parameter"
          ));
          continue;
        }
      }
      else if (member.Parameters.Length != 0)
      {
        diagnostics.Add(CreateInvalidObservingHook(
            moduleName,
            hookKind,
            member,
            "event-specific method must not accept parameters"
        ));
        continue;
      }

      hooks.Add(new ExpoObservingHookModel(
          member.Name,
          eventName,
          passesEventName,
          member.Locations.FirstOrDefault()
      ));
    }

    return hooks;
  }

  private static ExpoLifecycleHookModel? GetLifecycleHook(
      INamedTypeSymbol typeSymbol,
      string moduleName,
      string hookKind,
      string attributeMetadataName,
      List<ExpoDiagnosticModel> diagnostics)
  {
    ExpoLifecycleHookModel? hook = null;
    foreach (var member in typeSymbol.GetMembers())
    {
      var attribute = member.GetAttributes().FirstOrDefault(item =>
          item.AttributeClass?.ToDisplayString() == attributeMetadataName);
      if (attribute is null)
      {
        continue;
      }

      if (hook is not null)
      {
        diagnostics.Add(CreateInvalidLifecycleHook(
            moduleName,
            hookKind,
            member,
            $"duplicate {hookKind} lifecycle hook"
        ));
        continue;
      }

      if (member is not IMethodSymbol method || method.MethodKind != MethodKind.Ordinary)
      {
        diagnostics.Add(CreateInvalidLifecycleHook(
            moduleName,
            hookKind,
            member,
            "attribute must be applied to a method"
        ));
        continue;
      }

      if (method.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
      {
        diagnostics.Add(CreateInvalidLifecycleHook(
            moduleName,
            hookKind,
            method,
            "method must be public or internal"
        ));
        continue;
      }

      if (method.IsStatic)
      {
        diagnostics.Add(CreateInvalidLifecycleHook(moduleName, hookKind, method, "method is static"));
        continue;
      }

      if (method.IsGenericMethod)
      {
        diagnostics.Add(CreateInvalidLifecycleHook(moduleName, hookKind, method, "method is generic"));
        continue;
      }

      if (!method.ReturnsVoid &&
          method.ReturnType.SpecialType != SpecialType.System_Void)
      {
        diagnostics.Add(CreateInvalidLifecycleHook(moduleName, hookKind, method, "method must return void"));
        continue;
      }

      if (method.Parameters.Length != 0)
      {
        diagnostics.Add(CreateInvalidLifecycleHook(
            moduleName,
            hookKind,
            method,
            "method must not accept parameters"
        ));
        continue;
      }

      hook = new ExpoLifecycleHookModel(method.Name, method.Locations.FirstOrDefault());
    }

    return hook;
  }

  private static ExpoDiagnosticModel CreateInvalidLifecycleHook(
      string moduleName,
      string hookKind,
      ISymbol member,
      string reason)
  {
    return new ExpoDiagnosticModel(
        ExpoModulesDiagnostics.InvalidLifecycleHook.Id,
        member.Locations.FirstOrDefault(),
        new EquatableArray<string>(new[] { moduleName, hookKind, member.Name, reason })
    );
  }

  private static ExpoDiagnosticModel CreateInvalidObservingHook(
      string moduleName,
      string hookKind,
      IMethodSymbol member,
      string reason) =>
      new(
          ExpoModulesDiagnostics.InvalidObservingHook.Id,
          member.Locations.FirstOrDefault(),
          new EquatableArray<string>(new[] { moduleName, hookKind, member.Name, reason })
      );

  private static ExpoModuleConstructorStrategy GetConstructorStrategy(INamedTypeSymbol typeSymbol)
  {
    var hasParameterlessConstructor = false;

    foreach (var constructor in typeSymbol.InstanceConstructors)
    {
      if (!IsSupportedConstructorAccessibility(constructor))
      {
        continue;
      }

      if (constructor.Parameters.Length == 1 &&
          constructor.Parameters[0].Type.ToDisplayString() == DotnetRuntimeContextMetadataName)
      {
        return ExpoModuleConstructorStrategy.RuntimeContext;
      }

      if (constructor.Parameters.Length == 0)
      {
        hasParameterlessConstructor = true;
      }
    }

    return hasParameterlessConstructor
        ? ExpoModuleConstructorStrategy.Parameterless
        : ExpoModuleConstructorStrategy.Unsupported;
  }

  private static bool IsSupportedConstructorAccessibility(IMethodSymbol constructor) =>
      constructor.DeclaredAccessibility == Accessibility.Public ||
      constructor.DeclaredAccessibility == Accessibility.Internal;

  private static IEnumerable<ExpoFunctionModel> GetFunctions(
      INamedTypeSymbol typeSymbol,
      List<ExpoDiagnosticModel> diagnostics,
      List<ExpoGeneratedRecordCodecModel> recordCodecs,
      HashSet<string> reservedJavaScriptNames)
  {
    var functions = new List<ExpoFunctionModel>();

    foreach (var member in typeSymbol.GetMembers().OfType<IMethodSymbol>())
    {
      if (member.MethodKind != MethodKind.Ordinary)
      {
        continue;
      }

      var jsAttribute = member.GetAttributes().FirstOrDefault(attribute =>
          attribute.AttributeClass?.ToDisplayString() == JSAttributeMetadataName);
      if (jsAttribute is null)
      {
        continue;
      }

      if (member.IsStatic)
      {
        diagnostics.Add(new ExpoDiagnosticModel(
            ExpoModulesDiagnostics.UnsupportedJSMethodShape.Id,
            member.Locations.FirstOrDefault(),
            new EquatableArray<string>(
                new[]
                {
                    member.Name,
                    "static",
                }
            )
        ));
        continue;
      }

      if (member.IsGenericMethod)
      {
        diagnostics.Add(new ExpoDiagnosticModel(
            ExpoModulesDiagnostics.UnsupportedJSMethodShape.Id,
            member.Locations.FirstOrDefault(),
            new EquatableArray<string>(
                new[]
                {
                    member.Name,
                    "generic",
                }
            )
        ));
        continue;
      }

      var javaScriptName = member.Name;
      if (jsAttribute.ConstructorArguments.Length == 1 &&
          jsAttribute.ConstructorArguments[0].Value is string explicitName)
      {
        javaScriptName = explicitName;
      }
      if (reservedJavaScriptNames.Contains(javaScriptName))
      {
        diagnostics.Add(new ExpoDiagnosticModel(
            ExpoModulesDiagnostics.UnsupportedJSMethodShape.Id,
            member.Locations.FirstOrDefault(),
            new EquatableArray<string>(new[] { member.Name, "reserved observing hook name" })
        ));
        continue;
      }

      // Validate the generated return path before collecting parameters.
      var isAsync = TryGetTaskResultType(member.ReturnType, out var asyncResultType);
      var asyncReturnsVoid = isAsync && asyncResultType is null;
      var asyncResultTypeName = asyncResultType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty;
      var asyncResultPassingKind = GetReturnPassingKind(asyncResultType);
      var asyncResultCodec = asyncResultType is null || asyncResultPassingKind != ExpoReturnPassingKind.Codec
          ? string.Empty
          : GetCodecExpression(
              asyncResultType,
              diagnostics,
              recordCodecs,
              member.GetReturnTypeAttributes()
          );

      if (isAsync && !asyncReturnsVoid && asyncResultPassingKind == ExpoReturnPassingKind.Codec && asyncResultCodec is null)
      {
        diagnostics.Add(new ExpoDiagnosticModel(
            ExpoModulesDiagnostics.UnsupportedReturnType.Id,
            asyncResultType?.Locations.FirstOrDefault() ?? member.ReturnType.Locations.FirstOrDefault(),
            new EquatableArray<string>(
                new[]
                {
                    member.Name,
                    asyncResultType is null ? GetDiagnosticTypeName(member.ReturnType) : GetDiagnosticTypeName(asyncResultType),
                }
            )
        ));
        continue;
      }

      var returnsVoid = !isAsync &&
          (member.ReturnsVoid || member.ReturnType.SpecialType == SpecialType.System_Void);
      var returnPassingKind = returnsVoid || isAsync
          ? ExpoReturnPassingKind.Codec
          : GetReturnPassingKind(member.ReturnType);
      var returnCodec = returnsVoid || isAsync || returnPassingKind != ExpoReturnPassingKind.Codec
          ? string.Empty
          : GetCodecExpression(
              member.ReturnType,
              diagnostics,
              recordCodecs,
              member.GetReturnTypeAttributes()
          );
      if (!isAsync && !returnsVoid && returnPassingKind == ExpoReturnPassingKind.Codec && returnCodec is null)
      {
        diagnostics.Add(new ExpoDiagnosticModel(
            ExpoModulesDiagnostics.UnsupportedReturnType.Id,
            member.ReturnType.Locations.FirstOrDefault(),
            new EquatableArray<string>(
                new[]
                {
                    member.Name,
                    GetDiagnosticTypeName(member.ReturnType),
                }
            )
        ));
        continue;
      }

      // Validate each generated argument path and preserve authored defaults.
      var spanParameters = member.Parameters
          .Select(parameter => (parameter, kind: GetParameterPassingKind(parameter.Type)))
          .Where(item => item.kind != ExpoParameterPassingKind.Codec)
          .ToArray();
      if (isAsync && spanParameters.Length > 0)
      {
        foreach (var (parameter, kind) in spanParameters)
        {
          diagnostics.Add(new ExpoDiagnosticModel(
              ExpoModulesDiagnostics.AsyncSpanParameter.Id,
              parameter.Locations.FirstOrDefault(),
              new EquatableArray<string>(new[]
              {
                  member.Name,
                  parameter.Name,
                  GetDiagnosticTypeName(parameter.Type),
              })
          ));
        }
        continue;
      }
      if (!isAsync && spanParameters.Length > 1)
      {
        // Multiple Span<byte>/ReadOnlySpan<byte> parameters need a grouped access
        // primitive. Nesting the current callbacks would make the inner lambda capture
        // the outer ref-struct parameter, which C# rejects with CS9108. Keep this
        // diagnostic until one callback can receive all requested spans together.
        diagnostics.Add(new ExpoDiagnosticModel(
            ExpoModulesDiagnostics.MultipleSpanParameters.Id,
            spanParameters[1].parameter.Locations.FirstOrDefault(),
            new EquatableArray<string>(new[]
            {
                member.Name,
                string.Join(", ", spanParameters.Select(item => item.parameter.Name)),
            })
        ));
        continue;
      }

      var parameters = new List<ExpoParameterModel>();
      foreach (var parameter in member.Parameters)
      {
        var passingKind = GetParameterPassingKind(parameter.Type);
        var parameterCodec = GetCodecExpression(
            parameter.Type,
            diagnostics,
            recordCodecs,
            parameter.GetAttributes()
        );
        if (passingKind == ExpoParameterPassingKind.Codec && parameterCodec is null)
        {
          var descriptor = IsJavaScriptCallbackType(parameter.Type)
              ? ExpoModulesDiagnostics.UnsupportedCallbackCodec
              : ExpoModulesDiagnostics.UnsupportedParameterType;
          diagnostics.Add(new ExpoDiagnosticModel(
              descriptor.Id,
              parameter.Locations.FirstOrDefault(),
              new EquatableArray<string>(
                  new[]
                  {
                      parameter.Name,
                      member.Name,
                      GetDiagnosticTypeName(parameter.Type),
                  }
              )
          ));
          continue;
        }

        parameters.Add(new ExpoParameterModel(
            parameter.Name,
            parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            parameterCodec ?? string.Empty,
            IsJavaScriptCallbackType(parameter.Type),
            parameterCodec is "JavaScriptValueCodec" or "ArrayBufferCodec",
            parameter.HasExplicitDefaultValue,
            parameter.HasExplicitDefaultValue
                ? GetDefaultValueExpression(parameter.Type, parameter.ExplicitDefaultValue)
                : string.Empty,
            passingKind
        ));
      }

      if (parameters.Count != member.Parameters.Length)
      {
        continue;
      }

      functions.Add(new ExpoFunctionModel(
          member.Name,
          javaScriptName,
          member.Locations.FirstOrDefault(),
          member.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
          returnCodec ?? string.Empty,
          returnsVoid,
          isAsync,
          asyncReturnsVoid,
          asyncResultTypeName,
          asyncResultCodec ?? string.Empty,
          new EquatableArray<ExpoParameterModel>(parameters),
          returnPassingKind,
          asyncResultPassingKind
      ));
    }

    foreach (var group in functions.GroupBy(function => function.JavaScriptName))
    {
      var duplicateFunctions = group.ToArray();
      if (duplicateFunctions.Length <= 1)
      {
        continue;
      }

      diagnostics.Add(new ExpoDiagnosticModel(
          ExpoModulesDiagnostics.DuplicateJavaScriptFunctionName.Id,
          duplicateFunctions[1].Location,
          new EquatableArray<string>(
              new[]
              {
                  typeSymbol.Name,
                  group.Key,
              }
          )
      ));
    }

    var duplicateNames = new HashSet<string>(
        functions
            .GroupBy(function => function.JavaScriptName)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key),
        StringComparer.Ordinal
    );

    foreach (var function in functions)
    {
      if (!duplicateNames.Contains(function.JavaScriptName))
      {
        yield return function;
      }
    }
  }

  private static void EmitProvider(
      SourceProductionContext context,
      string assemblyName,
      IEnumerable<ExpoModuleModel> modules)
  {
    var moduleModels = modules.ToArray();
    var duplicateModuleNames = new HashSet<string>(
        moduleModels
            .GroupBy(module => module.ModuleName)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key),
        StringComparer.Ordinal
    );

    foreach (var diagnostic in moduleModels.SelectMany(module => module.Diagnostics.Values))
    {
      context.ReportDiagnostic(ToDiagnostic(diagnostic));
    }

    foreach (var group in moduleModels.GroupBy(module => module.ModuleName))
    {
      var duplicateModules = group.ToArray();
      if (duplicateModules.Length <= 1)
      {
        continue;
      }

      context.ReportDiagnostic(ToDiagnostic(new ExpoDiagnosticModel(
          ExpoModulesDiagnostics.DuplicateModuleName.Id,
          duplicateModules[1].Location,
          new EquatableArray<string>(new[] { group.Key })
      )));
    }

    moduleModels = moduleModels
        .Where(module =>
            module.ConstructorStrategy != ExpoModuleConstructorStrategy.Unsupported &&
            !duplicateModuleNames.Contains(module.ModuleName))
        .ToArray();

    var providerTypeName = $"ExpoModulesProvider_{SanitizeIdentifier(assemblyName)}";
    var builder = new StringBuilder();
    builder.AppendLine("// <auto-generated/>");
    builder.AppendLine("#nullable enable");
    builder.AppendLine("using Expo.ModulesCore;");
    builder.AppendLine("using Expo.ModulesCore.Codecs;");
    builder.AppendLine();
    builder.AppendLine("namespace Expo.ModulesCore.Generated;");
    builder.AppendLine();
    builder.AppendLine($"public static class {providerTypeName}");
    builder.AppendLine("{");
    foreach (var recordCodec in moduleModels.SelectMany(module => module.RecordCodecs.Values))
    {
      EmitRecordCodec(builder, recordCodec);
    }

    builder.AppendLine("  public static void Register(global::Expo.ModulesCore.DotnetRuntimeContext context)");
    builder.AppendLine("  {");
    builder.AppendLine("    global::System.ArgumentNullException.ThrowIfNull(context);");
    foreach (var module in moduleModels)
    {
      builder.AppendLine("    context.ModuleRegistry.RegisterLazyModule(");
      builder.AppendLine("        new global::Expo.ModulesCore.LazyModuleDefinition(");
      builder.AppendLine($"            \"{EscapeString(module.ModuleName)}\",");
      builder.AppendLine($"            static (context, modules) => {GetModuleRegistrationFunctionName(module)}(context, modules)");
      builder.AppendLine("        )");
      builder.AppendLine("    );");
    }
    builder.AppendLine("  }");
    builder.AppendLine();
    builder.AppendLine("  public static void Register(global::Expo.ModulesCore.DotnetRuntimeContext context, global::Expo.JSI.JavaScriptObject modules)");
    builder.AppendLine("  {");
    builder.AppendLine("    global::System.ArgumentNullException.ThrowIfNull(context);");
    builder.AppendLine("    global::System.ArgumentNullException.ThrowIfNull(modules);");
    foreach (var module in moduleModels)
    {
      builder.AppendLine($"    using var module_{SanitizeIdentifier(module.ModuleName)} = {GetModuleRegistrationFunctionName(module)}(context, modules);");
    }
    builder.AppendLine("  }");
    foreach (var module in moduleModels)
    {
      EmitModuleRegistrationFunction(builder, module);
    }
    foreach (var module in moduleModels)
    {
      foreach (var function in module.Functions.Values)
      {
        EmitHostFunction(builder, module, function);
      }
      if (module.StartObservingHooks.Values.Count > 0)
      {
        EmitObservingHookFunction(builder, module, "startObserving", module.StartObservingHooks.Values);
      }
      if (module.StopObservingHooks.Values.Count > 0)
      {
        EmitObservingHookFunction(builder, module, "stopObserving", module.StopObservingHooks.Values);
      }
    }
    builder.AppendLine("}");

    context.AddSource($"{providerTypeName}.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
  }

  private static void EmitModuleRegistrationFunction(StringBuilder builder, ExpoModuleModel module)
  {
    var moduleVariable = $"module_{SanitizeIdentifier(module.ModuleName)}";
    var moduleInstanceVariable = $"instance_{SanitizeIdentifier(module.ModuleName)}";
    var hasEvents = module.EventNames.Values.Count > 0;
    var factoryExpression = module.ConstructorStrategy == ExpoModuleConstructorStrategy.RuntimeContext
        ? $"() => new {module.FullyQualifiedTypeName}(context)"
        : $"static () => new {module.FullyQualifiedTypeName}()";
    var onCreateExpression = GetLifecycleHookExpression(module.OnCreateHook);
    var onDestroyExpression = GetLifecycleHookExpression(module.OnDestroyHook);

    builder.AppendLine();
    builder.AppendLine($"  private static global::Expo.JSI.JavaScriptObject {GetModuleRegistrationFunctionName(module)}(");
    builder.AppendLine("      global::Expo.ModulesCore.DotnetRuntimeContext context,");
    builder.AppendLine("      global::Expo.JSI.JavaScriptObject modules)");
    builder.AppendLine("  {");
    builder.AppendLine(hasEvents
        ? $"    var {moduleVariable} = context.ModuleRegistry.DefineNativeModule(modules, \"{EscapeString(module.ModuleName)}\");"
        : $"    var {moduleVariable} = context.ModuleRegistry.DefineModule(modules, \"{EscapeString(module.ModuleName)}\");");
    builder.AppendLine("    try");
    builder.AppendLine("    {");
    builder.AppendLine(module.OnCreateHook is null && module.OnDestroyHook is null
        ? $"      var {moduleInstanceVariable} = context.ModuleRegistry.GetOrCreateModule(\"{EscapeString(module.ModuleName)}\", {factoryExpression});"
        : $"      var {moduleInstanceVariable} = context.ModuleRegistry.GetOrCreateModule(\"{EscapeString(module.ModuleName)}\", {factoryExpression}, {onCreateExpression}, {onDestroyExpression});");
    if (hasEvents)
    {
      builder.AppendLine(
          $"      context.Events.Attach({moduleInstanceVariable}, {moduleVariable}, \"{EscapeString(module.ModuleName)}\", new[] {{ {string.Join(", ", module.EventNames.Values.Select(name => $"\"{EscapeString(name)}\""))} }});"
      );
    }
    if (module.StartObservingHooks.Values.Count > 0)
    {
      EmitObservingHookRegistration(builder, module, moduleVariable, moduleInstanceVariable, "startObserving", "      ");
    }
    if (module.StopObservingHooks.Values.Count > 0)
    {
      EmitObservingHookRegistration(builder, module, moduleVariable, moduleInstanceVariable, "stopObserving", "      ");
    }
    foreach (var function in module.Functions.Values)
    {
      builder.AppendLine(function.IsAsync
          ? "      GeneratedFunction.DefineAsync("
          : "      GeneratedFunction.DefineSync(");
      builder.AppendLine("          context,");
      builder.AppendLine($"          {moduleVariable},");
      builder.AppendLine($"          \"{EscapeString(function.JavaScriptName)}\",");
      builder.AppendLine($"          {GetRequiredParameterCount(function)},");
      builder.AppendLine($"          {GetHostFunctionName(module, function)},");
      builder.AppendLine($"          {moduleInstanceVariable}");
      builder.AppendLine("      );");
    }
    builder.AppendLine($"      return {moduleVariable};");
    builder.AppendLine("    }");
    builder.AppendLine("    catch");
    builder.AppendLine("    {");
    builder.AppendLine($"      {moduleVariable}.Dispose();");
    builder.AppendLine("      throw;");
    builder.AppendLine("    }");
    builder.AppendLine("  }");
  }

  private static Diagnostic ToDiagnostic(ExpoDiagnosticModel model)
  {
    var descriptor = model.DescriptorId switch
    {
      "EXPOJSI001" => ExpoModulesDiagnostics.UnsupportedParameterType,
      "EXPOJSI002" => ExpoModulesDiagnostics.UnsupportedReturnType,
      "EXPOJSI003" => ExpoModulesDiagnostics.UnsupportedModuleConstructor,
      "EXPOJSI004" => ExpoModulesDiagnostics.UnsupportedJSMethodShape,
      "EXPOJSI005" => ExpoModulesDiagnostics.DuplicateJavaScriptFunctionName,
      "EXPOJSI006" => ExpoModulesDiagnostics.DuplicateModuleName,
      "EXPOJSI007" => ExpoModulesDiagnostics.UnsupportedRecordField,
      "EXPOJSI008" => ExpoModulesDiagnostics.UnsupportedCallbackCodec,
      "EXPOJSI009" => ExpoModulesDiagnostics.InvalidEventName,
      "EXPOJSI010" => ExpoModulesDiagnostics.InvalidObservingHook,
      "EXPOJSI011" => ExpoModulesDiagnostics.InvalidLifecycleHook,
      "EXPOJSI012" => ExpoModulesDiagnostics.AsyncSpanParameter,
      "EXPOJSI013" => ExpoModulesDiagnostics.MultipleSpanParameters,
      _ => throw new InvalidOperationException($"Unknown diagnostic descriptor: {model.DescriptorId}"),
    };
    return Diagnostic.Create(descriptor, model.Location, model.Arguments.Values.Cast<object>().ToArray());
  }

  private static string GetLifecycleHookExpression(ExpoLifecycleHookModel? hook) =>
      hook is null ? "null" : $"static module => module.{hook.MethodName}()";

  private static void EmitObservingHookRegistration(
      StringBuilder builder,
      ExpoModuleModel module,
      string moduleVariable,
      string moduleInstanceVariable,
      string javaScriptName,
      string indent = "    ")
  {
    builder.AppendLine($"{indent}GeneratedFunction.DefineSync(");
    builder.AppendLine($"{indent}    context,");
    builder.AppendLine($"{indent}    {moduleVariable},");
    builder.AppendLine($"{indent}    \"{javaScriptName}\",");
    builder.AppendLine($"{indent}    1,");
    builder.AppendLine($"{indent}    {GetObservingHookFunctionName(module, javaScriptName)},");
    builder.AppendLine($"{indent}    {moduleInstanceVariable}");
    builder.AppendLine($"{indent});");
  }

  private static void EmitHostFunction(
      StringBuilder builder,
      ExpoModuleModel module,
      ExpoFunctionModel function)
  {
    if (!function.IsAsync && function.Parameters.Values.Any(parameter =>
            parameter.PassingKind != ExpoParameterPassingKind.Codec))
    {
      EmitSpanHostFunction(builder, module, function);
      return;
    }

    builder.AppendLine();
    builder.AppendLine($"  private static global::Expo.JSI.JavaScriptValue {GetHostFunctionName(module, function)}(");
    var runtimeParameterName = function.IsAsync ? "jsRuntime" : "runtime";
    builder.AppendLine($"      global::Expo.JSI.JavaScriptRuntime {runtimeParameterName},");
    builder.AppendLine("      global::Expo.JSI.JavaScriptValueRef thisValue,");
    builder.AppendLine("      global::Expo.JSI.JavaScriptArguments arguments,");
    builder.AppendLine("      object context)");
    builder.AppendLine("  {");
    if (function.IsAsync)
    {
      for (var index = 0; index < function.Parameters.Values.Count; index++)
      {
        if (function.Parameters.Values[index].OwnsDecodedValue)
        {
          builder.AppendLine($"    {function.Parameters.Values[index].TypeName}? {GetParameterLocalName(index)} = null;");
        }
      }
    }

    if (function.IsAsync)
    {
      builder.AppendLine("    try");
      builder.AppendLine("    {");
      builder.AppendLine($"      GeneratedFunction.RequireArgumentCount(\"{EscapeString(module.ModuleName)}.{EscapeString(function.JavaScriptName)}\", arguments, {GetRequiredParameterCount(function)}, {function.Parameters.Values.Count});");
      builder.AppendLine($"      var module = ({module.FullyQualifiedTypeName})context;");
    }
    else
    {
      builder.AppendLine($"    GeneratedFunction.RequireArgumentCount(\"{EscapeString(module.ModuleName)}.{EscapeString(function.JavaScriptName)}\", arguments, {GetRequiredParameterCount(function)}, {function.Parameters.Values.Count});");
      builder.AppendLine();
      builder.AppendLine($"    var module = ({module.FullyQualifiedTypeName})context;");
    }

    for (var index = 0; index < function.Parameters.Values.Count; index++)
    {
      var parameter = function.Parameters.Values[index];
      var parameterLocalName = GetParameterLocalName(index);
      if (function.IsAsync && parameter.OwnsDecodedValue)
      {
        builder.AppendLine($"      {parameterLocalName} = {GetParameterExpression(parameter, index, runtimeParameterName)};");
      }
      else
      {
        var declaration = parameter.OwnsDecodedValue ? "using var" : "var";
        builder.AppendLine(function.IsAsync
            ? $"      {declaration} {parameterLocalName} = {GetParameterExpression(parameter, index, runtimeParameterName)};"
            : $"    {declaration} {parameterLocalName} = {GetParameterExpression(parameter, index, runtimeParameterName)};");
      }
    }

    var argumentList = string.Join(
        ", ",
        function.Parameters.Values.Select((parameter, index) =>
            function.IsAsync && parameter.OwnsDecodedValue
                ? $"{GetParameterLocalName(index)}!"
                : GetParameterLocalName(index))
    );
    var disposeAsyncDecodedValues = function.Parameters.Values.Any(parameter => parameter.OwnsDecodedValue);
    if (function.IsAsync)
    {
      builder.AppendLine($"      var __expoTask = module.{function.MethodName}({argumentList});");
      builder.AppendLine($"      using var __expoPromiseValue = {runtimeParameterName}.CreatePromise(");
      builder.AppendLine("          async _ =>");
      builder.AppendLine("          {");
      if (disposeAsyncDecodedValues)
      {
        builder.AppendLine("            try");
        builder.AppendLine("            {");
      }

      var asyncIndent = disposeAsyncDecodedValues ? "              " : "            ";
      if (function.AsyncReturnsVoid)
      {
        builder.AppendLine($"{asyncIndent}await __expoTask.ConfigureAwait(false);");
        builder.AppendLine($"{asyncIndent}return global::Expo.JSI.JavaScriptPromiseResult.Resolve(static runtime => runtime.CreateUndefined());");
      }
      else if (function.AsyncResultCodecExpression == "JavaScriptValueCodec")
      {
        builder.AppendLine($"{asyncIndent}var __expoResult = await __expoTask.ConfigureAwait(false);");
        builder.AppendLine($"{asyncIndent}return global::Expo.JSI.JavaScriptPromiseResult.ResolveOwned(");
        builder.AppendLine($"{asyncIndent}    __expoResult,");
        builder.AppendLine($"{asyncIndent}    static (_, value) => value,");
        builder.AppendLine($"{asyncIndent}    static value => value.Dispose()");
        builder.AppendLine($"{asyncIndent});");
      }
      else if (function.AsyncResultCodecExpression == "ArrayBufferCodec")
      {
        builder.AppendLine($"{asyncIndent}var __expoResult = await __expoTask.ConfigureAwait(false);");
        builder.AppendLine($"{asyncIndent}return global::Expo.JSI.JavaScriptPromiseResult.ResolveOwned(");
        builder.AppendLine($"{asyncIndent}    __expoResult,");
        builder.AppendLine($"{asyncIndent}    static (runtime, value) =>");
        builder.AppendLine($"{asyncIndent}    {{");
        builder.AppendLine($"{asyncIndent}      try {{ return ArrayBufferCodec.Encode(value, runtime); }}");
        builder.AppendLine($"{asyncIndent}      finally {{ value.Dispose(); }}");
        builder.AppendLine($"{asyncIndent}    }},");
        builder.AppendLine($"{asyncIndent}    static value => value.Dispose()");
        builder.AppendLine($"{asyncIndent});");
      }
      else
      {
        builder.AppendLine($"{asyncIndent}var __expoResult = await __expoTask.ConfigureAwait(false);");
        builder.AppendLine($"{asyncIndent}return global::Expo.JSI.JavaScriptPromiseResult.Resolve(runtime => {function.AsyncResultCodecExpression}.Encode(__expoResult, runtime));");
      }

      if (disposeAsyncDecodedValues)
      {
        builder.AppendLine("            }");
        builder.AppendLine("            finally");
        builder.AppendLine("            {");
        EmitDisposeDecodedValues(builder, function, "              ");
        builder.AppendLine("            }");
      }
      builder.AppendLine("          }");
      builder.AppendLine("      );");
      builder.AppendLine("      return __expoPromiseValue.AsValue();");
      builder.AppendLine("    }");
      builder.AppendLine("    catch (global::System.Exception exception)");
      builder.AppendLine("    {");
      if (disposeAsyncDecodedValues)
      {
        EmitDisposeDecodedValues(builder, function, "      ");
      }
      builder.AppendLine($"      return GeneratedFunction.CreateRejectedPromise({runtimeParameterName}, exception);");
      builder.AppendLine("    }");
    }
    else if (function.ReturnsVoid)
    {
      builder.AppendLine($"    module.{function.MethodName}({argumentList});");
      builder.AppendLine("    return runtime.CreateUndefined();");
    }
    else
    {
      if (function.ReturnPassingKind != ExpoReturnPassingKind.Codec)
      {
        builder.AppendLine($"    return ArrayBufferCodec.EncodeCopy(module.{function.MethodName}({argumentList}), runtime);");
      }
      else if (function.ReturnCodecExpression == "ArrayBufferCodec")
      {
        builder.AppendLine($"    using var __expoResult = module.{function.MethodName}({argumentList});");
        builder.AppendLine("    return ArrayBufferCodec.Encode(__expoResult, runtime);");
      }
      else if (function.ReturnCodecExpression == "JavaScriptValueCodec")
      {
        builder.AppendLine($"    return JavaScriptValueCodec.Encode(module.{function.MethodName}({argumentList}), runtime);");
      }
      else
      {
        builder.AppendLine($"    return {function.ReturnCodecExpression}.Encode(module.{function.MethodName}({argumentList}), runtime);");
      }
    }
    builder.AppendLine("  }");
  }

  private static void EmitSpanHostFunction(
      StringBuilder builder,
      ExpoModuleModel module,
      ExpoFunctionModel function)
  {
    var spanParameter = function.Parameters.Values.Single(parameter =>
        parameter.PassingKind != ExpoParameterPassingKind.Codec);
    var spanIndex = Enumerable.Range(0, function.Parameters.Values.Count)
        .Single(index => ReferenceEquals(function.Parameters.Values[index], spanParameter));
    var spanBuffer = $"__expoSpanBuffer{spanIndex}";
    var spanMethod = spanParameter.PassingKind == ExpoParameterPassingKind.MutableByteSpan
        ? "WithBytes"
        : "WithReadOnlyBytes";

    builder.AppendLine();
    builder.AppendLine($"  private static global::Expo.JSI.JavaScriptValue {GetHostFunctionName(module, function)}(");
    builder.AppendLine("      global::Expo.JSI.JavaScriptRuntime runtime,");
    builder.AppendLine("      global::Expo.JSI.JavaScriptValueRef thisValue,");
    builder.AppendLine("      global::Expo.JSI.JavaScriptArguments arguments,");
    builder.AppendLine("      object context)");
    builder.AppendLine("  {");
    builder.AppendLine($"    GeneratedFunction.RequireArgumentCount(\"{EscapeString(module.ModuleName)}.{EscapeString(function.JavaScriptName)}\", arguments, {GetRequiredParameterCount(function)}, {function.Parameters.Values.Count});");
    builder.AppendLine($"    var module = ({module.FullyQualifiedTypeName})context;");
    for (var index = 0; index < function.Parameters.Values.Count; index++)
    {
      var parameter = function.Parameters.Values[index];
      if (parameter.PassingKind != ExpoParameterPassingKind.Codec)
      {
        continue;
      }
      var declaration = parameter.OwnsDecodedValue ? "using var" : "var";
      builder.AppendLine($"    {declaration} {GetParameterLocalName(index)} = {GetParameterExpression(parameter, index, "runtime")};");
    }
    builder.AppendLine($"    using var {spanBuffer} = ArrayBufferCodec.Decode(arguments.GetValue({spanIndex}), runtime);");
    builder.AppendLine($"    return {spanBuffer}.{spanMethod}(__expoArg{spanIndex} =>");
    builder.AppendLine("    {");
    var argumentList = string.Join(", ", function.Parameters.Values.Select((_, index) => GetParameterLocalName(index)));
    var invocation = $"module.{function.MethodName}({argumentList})";
    if (function.ReturnsVoid)
    {
      builder.AppendLine($"      {invocation};");
      builder.AppendLine("      return runtime.CreateUndefined();");
    }
    else if (function.ReturnPassingKind != ExpoReturnPassingKind.Codec)
    {
      builder.AppendLine($"      return ArrayBufferCodec.EncodeCopy({invocation}, runtime);");
    }
    else if (function.ReturnCodecExpression == "ArrayBufferCodec")
    {
      builder.AppendLine($"      using var __expoResult = {invocation};");
      builder.AppendLine("      return ArrayBufferCodec.Encode(__expoResult, runtime);");
    }
    else if (function.ReturnCodecExpression == "JavaScriptValueCodec")
    {
      builder.AppendLine($"      return JavaScriptValueCodec.Encode({invocation}, runtime);");
    }
    else
    {
      builder.AppendLine($"      return {function.ReturnCodecExpression}.Encode({invocation}, runtime);");
    }
    builder.AppendLine("    });");
    builder.AppendLine("  }");
  }

  private static void EmitObservingHookFunction(
      StringBuilder builder,
      ExpoModuleModel module,
      string javaScriptName,
      IReadOnlyList<ExpoObservingHookModel> hooks)
  {
    builder.AppendLine();
    builder.AppendLine($"  private static global::Expo.JSI.JavaScriptValue {GetObservingHookFunctionName(module, javaScriptName)}(");
    builder.AppendLine("      global::Expo.JSI.JavaScriptRuntime runtime,");
    builder.AppendLine("      global::Expo.JSI.JavaScriptValueRef thisValue,");
    builder.AppendLine("      global::Expo.JSI.JavaScriptArguments arguments,");
    builder.AppendLine("      object context)");
    builder.AppendLine("  {");
    builder.AppendLine($"    GeneratedFunction.RequireArgumentCount(\"{EscapeString(module.ModuleName)}.{javaScriptName}\", arguments, 1);");
    builder.AppendLine();
    builder.AppendLine($"    var module = ({module.FullyQualifiedTypeName})context;");
    builder.AppendLine("    var __expoEventName = StringCodec.Decode(arguments.GetValue(0), runtime);");
    foreach (var hook in hooks)
    {
      if (hook.EventName is not null)
      {
        builder.AppendLine($"    if (__expoEventName == \"{EscapeString(hook.EventName)}\")");
        builder.AppendLine("    {");
        builder.AppendLine($"      module.{hook.MethodName}();");
        builder.AppendLine("    }");
      }
      else
      {
        builder.AppendLine($"    module.{hook.MethodName}(__expoEventName);");
      }
    }
    builder.AppendLine("    return runtime.CreateUndefined();");
    builder.AppendLine("  }");
  }

  private static void EmitDisposeDecodedValues(
      StringBuilder builder,
      ExpoFunctionModel function,
      string indent)
  {
    for (var index = 0; index < function.Parameters.Values.Count; index++)
    {
      if (function.Parameters.Values[index].OwnsDecodedValue)
      {
        builder.AppendLine($"{indent}{GetParameterLocalName(index)}?.Dispose();");
      }
    }
  }

  private static void EmitRecordCodec(StringBuilder builder, ExpoGeneratedRecordCodecModel codec)
  {
    builder.AppendLine($"  private readonly struct {codec.CodecTypeName} : global::Expo.ModulesCore.Codecs.IJavaScriptCodec<{codec.RecordTypeName}>");
    builder.AppendLine("  {");
    builder.AppendLine($"    public static {codec.RecordTypeName} Decode(global::Expo.JSI.JavaScriptValueRef value, global::Expo.JSI.JavaScriptRuntime runtime)");
    builder.AppendLine("    {");
    builder.AppendLine("      var obj = value.AsObject();");
    foreach (var field in codec.Fields.Values)
    {
      builder.AppendLine($"      var {field.ParameterName} = {field.CodecExpression}.Decode(obj.GetProperty(\"{EscapeString(field.PropertyName)}\"), runtime);");
    }
    builder.AppendLine($"      return new {codec.RecordTypeName}({string.Join(", ", codec.Fields.Values.Select(field => field.ParameterName))});");
    builder.AppendLine("    }");
    builder.AppendLine();
    builder.AppendLine($"    public static {codec.RecordTypeName} Decode(global::Expo.JSI.JavaScriptValue value, global::Expo.JSI.JavaScriptRuntime runtime)");
    builder.AppendLine("    {");
    builder.AppendLine("      using var obj = value.AsObject();");
    foreach (var field in codec.Fields.Values)
    {
      builder.AppendLine($"      using var {field.ParameterName}Value = obj.GetProperty(\"{EscapeString(field.PropertyName)}\");");
      builder.AppendLine($"      var {field.ParameterName} = {field.CodecExpression}.Decode({field.ParameterName}Value, runtime);");
    }
    builder.AppendLine($"      return new {codec.RecordTypeName}({string.Join(", ", codec.Fields.Values.Select(field => field.ParameterName))});");
    builder.AppendLine("    }");
    builder.AppendLine();
    builder.AppendLine($"    public static global::Expo.JSI.JavaScriptValue Encode({codec.RecordTypeName} value, global::Expo.JSI.JavaScriptRuntime runtime)");
    builder.AppendLine("    {");
    builder.AppendLine("      using var obj = runtime.CreateObject();");
    foreach (var field in codec.Fields.Values)
    {
      builder.AppendLine($"      using var {field.ParameterName} = {field.CodecExpression}.Encode(value.{field.PropertyName}, runtime);");
      builder.AppendLine($"      obj.SetProperty(\"{EscapeString(field.PropertyName)}\", {field.ParameterName});");
    }
    builder.AppendLine("      return obj.AsValue();");
    builder.AppendLine("    }");
    builder.AppendLine("  }");
    builder.AppendLine();
  }

  private static string SanitizeIdentifier(string value)
  {
    var builder = new StringBuilder(value.Length);
    foreach (var character in value)
    {
      builder.Append(char.IsLetterOrDigit(character) ? character : '_');
    }
    return builder.Length == 0 ? "ExpoModules" : builder.ToString();
  }

  private static string GetHostFunctionName(ExpoModuleModel module, ExpoFunctionModel function) =>
      $"{SanitizeIdentifier(module.ModuleName)}_{SanitizeIdentifier(function.JavaScriptName)}_HostFunction";

  private static string GetModuleRegistrationFunctionName(ExpoModuleModel module) =>
      $"Register{SanitizeIdentifier(module.ModuleName)}";

  private static string GetObservingHookFunctionName(ExpoModuleModel module, string javaScriptName) =>
      $"{SanitizeIdentifier(module.ModuleName)}_{SanitizeIdentifier(javaScriptName)}_HostFunction";

  private static int GetRequiredParameterCount(ExpoFunctionModel function) =>
      function.Parameters.Values.Count(parameter => !parameter.HasDefaultValue);

  private static string GetParameterLocalName(int index) =>
      $"__expoArg{index.ToString(CultureInfo.InvariantCulture)}";

  private static string GetParameterExpression(
      ExpoParameterModel parameter,
      int index,
      string runtimeParameterName)
  {
    var decodeExpression = GetDecodeExpression(
        parameter.CodecExpression,
        index,
        runtimeParameterName,
        parameter.RequiresRuntimeContext
    );
    if (!parameter.HasDefaultValue)
    {
      return decodeExpression;
    }

    return $"arguments.Count <= {index} || arguments.GetValue({index}).Kind == global::Expo.JSI.JavaScriptValueKind.Undefined ? {parameter.DefaultValueExpression} : {decodeExpression}";
  }

  private static string GetDecodeExpression(
      string codecExpression,
      int index,
      string runtimeParameterName,
      bool requiresRuntimeContext)
  {
    var methodName = "Decode";
    if (codecExpression.StartsWith("JavaScriptArrayCodec<", StringComparison.Ordinal))
    {
      methodName = "DecodeToArray";
    }
    else if (codecExpression.StartsWith("JavaScriptDictionaryCodec<", StringComparison.Ordinal))
    {
      methodName = "DecodeToDictionary";
    }

    var contextArgument = requiresRuntimeContext ? ", GeneratedFunction.CurrentRuntimeContext" : string.Empty;
    return $"{codecExpression}.{methodName}(arguments.GetValue({index}), {runtimeParameterName}{contextArgument})";
  }

  private static bool TryGetTaskResultType(
      ITypeSymbol typeSymbol,
      out ITypeSymbol? resultType)
  {
    resultType = null;
    if (typeSymbol is not INamedTypeSymbol namedType)
    {
      return false;
    }

    var originalDefinition = namedType.OriginalDefinition.ToDisplayString(
        SymbolDisplayFormat.FullyQualifiedFormat
    );
    if (originalDefinition == "global::System.Threading.Tasks.Task")
    {
      return true;
    }

    if (originalDefinition == "global::System.Threading.Tasks.Task<TResult>")
    {
      resultType = namedType.TypeArguments.Single();
      return true;
    }

    return false;
  }

  private static string? GetCodecExpression(
      ITypeSymbol typeSymbol,
      List<ExpoDiagnosticModel> diagnostics,
      List<ExpoGeneratedRecordCodecModel> recordCodecs,
      IEnumerable<AttributeData>? usageAttributes = null)
  {
    if (TryGetJavaScriptCallbackCodec(typeSymbol, diagnostics, recordCodecs) is { } callbackCodec)
    {
      return callbackCodec;
    }

    if (typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == ArrayBufferMetadataName)
    {
      return "ArrayBufferCodec";
    }

    if (typeSymbol is IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Byte })
    {
      return "ByteArrayCodec";
    }

    if (typeSymbol.ToDisplayString() == JavaScriptValueMetadataName)
    {
      return "JavaScriptValueCodec";
    }

    if (TryGetNullableCodec(typeSymbol, diagnostics, recordCodecs) is { } nullableCodec)
    {
      return nullableCodec;
    }

    if (TryGetConvertibleCodec(typeSymbol) is { } convertibleCodec)
    {
      return convertibleCodec;
    }
    if (typeSymbol.TypeKind == TypeKind.Enum)
    {
      // Usage-level enum metadata wins over enum-type metadata; string remains the default.
      var enumTypeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
      return UsesNumberEnumRepresentation(typeSymbol, usageAttributes)
          ? $"NumberEnumCodec<{enumTypeName}>"
          : $"StringEnumCodec<{enumTypeName}>";
    }
    if (typeSymbol is INamedTypeSymbol { IsRecord: true } recordType)
    {
      return TryGetRecordCodec(recordType, diagnostics, recordCodecs);
    }

    return typeSymbol.SpecialType switch
    {
      SpecialType.System_Boolean => "BoolCodec",
      SpecialType.System_String => "StringCodec",
      SpecialType.System_SByte or
      SpecialType.System_Byte or
      SpecialType.System_Int16 or
      SpecialType.System_UInt16 or
      SpecialType.System_Int32 or
      SpecialType.System_UInt32 or
      SpecialType.System_Int64 or
      SpecialType.System_UInt64 or
      SpecialType.System_Single or
      SpecialType.System_Double => GetNumberCodecExpression(typeSymbol),
      _ => TryGetReadOnlyListCodec(typeSymbol, diagnostics, recordCodecs) ??
          TryGetDictionaryCodec(typeSymbol, diagnostics, recordCodecs),
    };
  }

  private static ExpoParameterPassingKind GetParameterPassingKind(ITypeSymbol typeSymbol)
  {
    var displayName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    if (displayName == "global::System.Span<byte>")
    {
      return ExpoParameterPassingKind.MutableByteSpan;
    }
    if (displayName == "global::System.ReadOnlySpan<byte>")
    {
      return ExpoParameterPassingKind.ReadOnlyByteSpan;
    }
    if (typeSymbol is not INamedTypeSymbol namedType)
    {
      return ExpoParameterPassingKind.Codec;
    }

    if (namedType.ContainingNamespace.ToDisplayString() != "System")
    {
      return ExpoParameterPassingKind.Codec;
    }
    if (namedType.TypeArguments.Length != 1 ||
        namedType.TypeArguments[0].SpecialType != SpecialType.System_Byte)
    {
      return ExpoParameterPassingKind.Codec;
    }
    return namedType.Name switch
    {
      "Span" => ExpoParameterPassingKind.MutableByteSpan,
      "ReadOnlySpan" => ExpoParameterPassingKind.ReadOnlyByteSpan,
      _ => ExpoParameterPassingKind.Codec,
    };
  }

  private static ExpoReturnPassingKind GetReturnPassingKind(ITypeSymbol? typeSymbol)
  {
    return typeSymbol is null ? ExpoReturnPassingKind.Codec : GetParameterPassingKind(typeSymbol) switch
    {
      ExpoParameterPassingKind.MutableByteSpan => ExpoReturnPassingKind.MutableByteSpan,
      ExpoParameterPassingKind.ReadOnlyByteSpan => ExpoReturnPassingKind.ReadOnlyByteSpan,
      _ => ExpoReturnPassingKind.Codec,
    };
  }

  private static bool IsJavaScriptCallbackType(ITypeSymbol typeSymbol)
  {
    if (typeSymbol is not INamedTypeSymbol namedType)
    {
      return false;
    }

    return namedType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
        is "global::Expo.ModulesCore.JavaScriptCallback<TResult>"
        or "global::Expo.ModulesCore.JavaScriptCallback<TArgs, TResult>";
  }

  private static string? TryGetJavaScriptCallbackCodec(
      ITypeSymbol typeSymbol,
      List<ExpoDiagnosticModel> diagnostics,
      List<ExpoGeneratedRecordCodecModel> recordCodecs)
  {
    if (typeSymbol is not INamedTypeSymbol namedType || !IsJavaScriptCallbackType(namedType))
    {
      return null;
    }

    if (namedType.TypeArguments.Length == 1)
    {
      var zeroArgResultType = namedType.TypeArguments[0];
      var zeroArgResultCodec = GetCodecExpression(zeroArgResultType, diagnostics, recordCodecs);
      if (zeroArgResultCodec is null)
      {
        return null;
      }

      return $"JavaScriptCallbackCodec<{zeroArgResultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}, {zeroArgResultCodec}>";
    }

    var argsType = namedType.TypeArguments[0];
    var resultType = namedType.TypeArguments[1];
    var argsCodec = TryGetValueTupleArgsCodec(argsType, diagnostics, recordCodecs);
    var resultCodec = GetCodecExpression(resultType, diagnostics, recordCodecs);
    if (argsCodec is null || resultCodec is null)
    {
      return null;
    }

    return $"JavaScriptCallbackCodec<{argsType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}, {argsCodec}, {resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}, {resultCodec}>";
  }

  private static string? TryGetValueTupleArgsCodec(
      ITypeSymbol typeSymbol,
      List<ExpoDiagnosticModel> diagnostics,
      List<ExpoGeneratedRecordCodecModel> recordCodecs)
  {
    var elements = GetValueTupleElements(typeSymbol);
    if (elements is null || elements.Count > 8)
    {
      return null;
    }

    if (elements.Count == 0)
    {
      return "ValueTupleCodec";
    }

    var codecParts = new List<string>();
    foreach (var element in elements)
    {
      var codec = GetCodecExpression(element, diagnostics, recordCodecs);
      if (codec is null)
      {
        return null;
      }

      codecParts.Add(element.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
      codecParts.Add(codec);
    }

    return $"ValueTupleCodec<{string.Join(", ", codecParts)}>";
  }

  private static IReadOnlyList<ITypeSymbol>? GetValueTupleElements(ITypeSymbol typeSymbol)
  {
    if (typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.ValueTuple")
    {
      return Array.Empty<ITypeSymbol>();
    }

    if (typeSymbol is not INamedTypeSymbol namedType)
    {
      return null;
    }

    if (namedType.IsTupleType)
    {
      return namedType.TupleElements.Select(element => element.Type).ToArray();
    }

    var definition = namedType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    if (definition is "global::System.ValueTuple<T1>"
        or "global::System.ValueTuple<T1, T2>"
        or "global::System.ValueTuple<T1, T2, T3>"
        or "global::System.ValueTuple<T1, T2, T3, T4>"
        or "global::System.ValueTuple<T1, T2, T3, T4, T5>"
        or "global::System.ValueTuple<T1, T2, T3, T4, T5, T6>"
        or "global::System.ValueTuple<T1, T2, T3, T4, T5, T6, T7>")
    {
      return namedType.TypeArguments.ToArray();
    }

    if (definition == "global::System.ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest>")
    {
      var restElements = GetValueTupleElements(namedType.TypeArguments[7]);
      if (restElements is not { Count: 1 })
      {
        return null;
      }

      return namedType.TypeArguments.Take(7).Concat(restElements).ToArray();
    }

    return null;
  }

  private static string GetDefaultValueExpression(ITypeSymbol typeSymbol, object? value)
  {
    if (value is null)
    {
      return "null";
    }

    if (typeSymbol is INamedTypeSymbol namedType &&
        namedType.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
    {
      typeSymbol = namedType.TypeArguments.Single();
    }

    return typeSymbol.SpecialType switch
    {
      SpecialType.System_Boolean => (bool)value ? "true" : "false",
      SpecialType.System_String => $"\"{EscapeString((string)value)}\"",
      SpecialType.System_SByte => ((sbyte)value).ToString(CultureInfo.InvariantCulture),
      SpecialType.System_Byte => ((byte)value).ToString(CultureInfo.InvariantCulture),
      SpecialType.System_Int16 => ((short)value).ToString(CultureInfo.InvariantCulture),
      SpecialType.System_UInt16 => ((ushort)value).ToString(CultureInfo.InvariantCulture),
      SpecialType.System_Int32 => ((int)value).ToString(CultureInfo.InvariantCulture),
      SpecialType.System_UInt32 => ((uint)value).ToString(CultureInfo.InvariantCulture),
      SpecialType.System_Int64 => ((long)value).ToString(CultureInfo.InvariantCulture) + "L",
      SpecialType.System_UInt64 => ((ulong)value).ToString(CultureInfo.InvariantCulture) + "UL",
      SpecialType.System_Single => ((float)value).ToString("R", CultureInfo.InvariantCulture) + "F",
      SpecialType.System_Double => ((double)value).ToString("R", CultureInfo.InvariantCulture),
      _ => throw new InvalidOperationException(
          $"Unsupported default value type: {typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}"
      ),
    };
  }

  private static string GetNumberCodecExpression(ITypeSymbol typeSymbol) =>
      $"NumberCodec<{typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>";

  private static string? TryGetConvertibleCodec(ITypeSymbol typeSymbol)
  {
    return typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) switch
    {
      "global::System.Guid" => "GuidCodec",
      "global::System.Uri" => "UriCodec",
      "global::System.DateTimeOffset" => "DateTimeOffsetCodec",
      "global::System.TimeSpan" => "TimeSpanCodec",
      _ => null,
    };
  }

  private static bool UsesNumberEnumRepresentation(
      ITypeSymbol enumType,
      IEnumerable<AttributeData>? usageAttributes)
  {
    return GetEnumRepresentation(usageAttributes) == 1 ||
        GetEnumRepresentation(enumType.GetAttributes()) == 1;
  }

  private static int? GetEnumRepresentation(IEnumerable<AttributeData>? attributes)
  {
    if (attributes is null)
    {
      return null;
    }

    foreach (var attribute in attributes)
    {
      if (attribute.AttributeClass?.ToDisplayString() != JSEnumAttributeMetadataName ||
          attribute.ConstructorArguments.Length != 1 ||
          attribute.ConstructorArguments[0].Value is not int representation)
      {
        continue;
      }

      return representation;
    }

    return null;
  }

  private static string GetDiagnosticTypeName(ITypeSymbol typeSymbol)
  {
    return typeSymbol.SpecialType switch
    {
      SpecialType.System_Boolean => "System.Boolean",
      SpecialType.System_Double => "System.Double",
      SpecialType.System_String => "System.String",
      SpecialType.System_Decimal => "System.Decimal",
      _ => typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
    };
  }

  private static string? TryGetNullableCodec(
      ITypeSymbol typeSymbol,
      List<ExpoDiagnosticModel> diagnostics,
      List<ExpoGeneratedRecordCodecModel> recordCodecs)
  {
    if (typeSymbol is not INamedTypeSymbol namedType ||
        namedType.ConstructedFrom.SpecialType != SpecialType.System_Nullable_T)
    {
      return null;
    }

    var valueType = namedType.TypeArguments.Single();
    var valueCodec = GetCodecExpression(valueType, diagnostics, recordCodecs);
    if (valueCodec is null)
    {
      return null;
    }

    var valueTypeName = valueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    return $"NullableCodec<{valueTypeName}, {valueCodec}>";
  }

  private static string? TryGetReadOnlyListCodec(
      ITypeSymbol typeSymbol,
      List<ExpoDiagnosticModel> diagnostics,
      List<ExpoGeneratedRecordCodecModel> recordCodecs)
  {
    if (typeSymbol is not INamedTypeSymbol namedType ||
        namedType.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) !=
        "global::System.Collections.Generic.IReadOnlyList<T>")
    {
      return null;
    }

    var elementType = namedType.TypeArguments.Single();
    var elementCodec = GetCodecExpression(elementType, diagnostics, recordCodecs);
    if (elementCodec is null)
    {
      return null;
    }

    var elementTypeName = elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    return $"JavaScriptArrayCodec<{elementTypeName}, {elementCodec}>";
  }

  private static string? TryGetDictionaryCodec(
      ITypeSymbol typeSymbol,
      List<ExpoDiagnosticModel> diagnostics,
      List<ExpoGeneratedRecordCodecModel> recordCodecs)
  {
    if (typeSymbol is not INamedTypeSymbol namedType)
    {
      return null;
    }

    var constructedType = namedType.ConstructedFrom.ToDisplayString(
        SymbolDisplayFormat.FullyQualifiedFormat
    );
    if (constructedType is not "global::System.Collections.Generic.Dictionary<TKey, TValue>" and
        not "global::System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>")
    {
      return null;
    }

    var keyType = namedType.TypeArguments[0];
    if (keyType.SpecialType != SpecialType.System_String)
    {
      return null;
    }

    var valueType = namedType.TypeArguments[1];
    var valueCodec = GetCodecExpression(valueType, diagnostics, recordCodecs);
    if (valueCodec is null)
    {
      return null;
    }

    var valueTypeName = valueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    return $"JavaScriptDictionaryCodec<{valueTypeName}, {valueCodec}>";
  }

  private static string? TryGetRecordCodec(
      INamedTypeSymbol typeSymbol,
      List<ExpoDiagnosticModel> diagnostics,
      List<ExpoGeneratedRecordCodecModel> recordCodecs)
  {
    var recordTypeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    var existing = recordCodecs.FirstOrDefault(codec => codec.RecordTypeName == recordTypeName);
    if (existing is not null)
    {
      return existing.CodecTypeName;
    }

    var constructor = typeSymbol.InstanceConstructors
        .Where(item =>
            item.Parameters.Length > 0 &&
            item.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal)
        .OrderByDescending(item => item.Parameters.Length)
        .FirstOrDefault();
    if (constructor is null)
    {
      return null;
    }

    var fields = new List<ExpoGeneratedRecordFieldModel>();
    foreach (var parameter in constructor.Parameters)
    {
      var property = typeSymbol
          .GetMembers()
          .OfType<IPropertySymbol>()
          .FirstOrDefault(item => item.Name == parameter.Name);
      var fieldCodec = GetCodecExpression(parameter.Type, diagnostics, recordCodecs);
      if (property is null || fieldCodec is null)
      {
        diagnostics.Add(new ExpoDiagnosticModel(
            ExpoModulesDiagnostics.UnsupportedRecordField.Id,
            parameter.Locations.FirstOrDefault(),
            new EquatableArray<string>(
                new[]
                {
                    typeSymbol.Name,
                    parameter.Name,
                    GetDiagnosticTypeName(parameter.Type),
                }
            )
        ));
        return null;
      }

      fields.Add(new ExpoGeneratedRecordFieldModel(
          LowerCamel(parameter.Name),
          property.Name,
          parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
          fieldCodec,
          parameter.Locations.FirstOrDefault()
      ));
    }

    var codec = new ExpoGeneratedRecordCodecModel(
        $"{SanitizeIdentifier(typeSymbol.Name)}Codec",
        recordTypeName,
        new EquatableArray<ExpoGeneratedRecordFieldModel>(fields),
        typeSymbol.Locations.FirstOrDefault()
    );
    recordCodecs.Add(codec);
    return codec.CodecTypeName;
  }

  private static string LowerCamel(string value) =>
      value.Length == 0 ? value : char.ToLowerInvariant(value[0]) + value.Substring(1);

  private static string EscapeString(string value) =>
      value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
