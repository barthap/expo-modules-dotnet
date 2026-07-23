using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Expo.ModulesCore.Generator;

public sealed partial class ExpoModulesGenerator
{
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

    var sharedObjectClasses = new List<ExpoSharedObjectClassModel>();
    foreach (var attribute in context.Attributes)
    {
      if (attribute.ConstructorArguments.Length == 1 &&
          attribute.ConstructorArguments[0].Value is string explicitName)
      {
        moduleName = explicitName;
      }

      foreach (var namedArgument in attribute.NamedArguments)
      {
        if (namedArgument.Key != "Classes" || namedArgument.Value.Kind != TypedConstantKind.Array)
        {
          continue;
        }

        for (var index = 0; index < namedArgument.Value.Values.Length; index++)
        {
          var entry = namedArgument.Value.Values[index];
          var entryTypeName = entry.Value is ITypeSymbol entryType
              ? entryType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
              : entry.Value?.ToString() ?? "null";
          sharedObjectClasses.Add(new ExpoSharedObjectClassModel(
              entryTypeName,
              GetClassesEntryLocation(attribute, index) ?? typeSymbol.Locations.FirstOrDefault()
          ));
        }
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
    var typedEvents = GetTypedEvents(typeSymbol, moduleName, diagnostics, recordCodecs);
    var legacyEventNames = GetEventNames(typeSymbol, moduleName, diagnostics).ToList();
    var eventNames = MergeEventNames(moduleName, legacyEventNames, typedEvents, diagnostics);
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
    var functionCollection = GetFunctions(typeSymbol, diagnostics, recordCodecs, reservedJavaScriptNames);
    var functions = functionCollection.Functions;
    var properties = GetProperties(typeSymbol, diagnostics, recordCodecs, reservedJavaScriptNames);
    properties = RemoveCollidingProperties(
        typeSymbol,
        functionCollection.ValidJavaScriptNames,
        properties,
        diagnostics
    );

    return new ExpoModuleModel(
        typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        typeSymbol.ContainingNamespace.IsGlobalNamespace ? string.Empty : typeSymbol.ContainingNamespace.ToDisplayString(),
        typeSymbol.Name,
        typeSymbol.DeclaredAccessibility == Accessibility.Public ? "public" : "internal",
        moduleName,
        typeSymbol.Locations.FirstOrDefault(),
        constructorStrategy,
        onCreateHook,
        onDestroyHook,
        new EquatableArray<string>(eventNames),
        new EquatableArray<ExpoEventModel>(typedEvents),
        new EquatableArray<ExpoObservingHookModel>(startObservingHooks),
        new EquatableArray<ExpoObservingHookModel>(stopObservingHooks),
        new EquatableArray<ExpoFunctionModel>(functions),
        new EquatableArray<ExpoPropertyModel>(properties),
        new EquatableArray<ExpoSharedObjectClassModel>(sharedObjectClasses),
        new EquatableArray<ExpoGeneratedRecordCodecModel>(recordCodecs),
        new EquatableArray<ExpoDiagnosticModel>(diagnostics)
    );
  }

  private static Location? GetClassesEntryLocation(AttributeData attribute, int entryIndex)
  {
    if (attribute.ApplicationSyntaxReference?.GetSyntax() is not AttributeSyntax attributeSyntax)
    {
      return null;
    }

    var classesArgument = attributeSyntax.ArgumentList?.Arguments.FirstOrDefault(argument =>
        argument.NameEquals?.Name.Identifier.ValueText == "Classes");
    if (classesArgument is null)
    {
      return null;
    }

    var typeOfExpressions = classesArgument.Expression
        .DescendantNodesAndSelf()
        .OfType<TypeOfExpressionSyntax>()
        .ToArray();
    return entryIndex < typeOfExpressions.Length ? typeOfExpressions[entryIndex].Type.GetLocation() : null;
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

  private static (List<ExpoFunctionModel> Functions, HashSet<string> ValidJavaScriptNames) GetFunctions(
      INamedTypeSymbol typeSymbol,
      List<ExpoDiagnosticModel> diagnostics,
      List<ExpoGeneratedRecordCodecModel> recordCodecs,
      HashSet<string> reservedJavaScriptNames)
  {
    var functions = new List<ExpoFunctionModel>();
    var validJavaScriptNames = new HashSet<string>(StringComparer.Ordinal);

    foreach (var member in typeSymbol.GetMembers().OfType<IMethodSymbol>())
    {
      if (member.MethodKind != MethodKind.Ordinary)
      {
        continue;
      }

      var jsAttribute = member.GetAttributes().FirstOrDefault(attribute =>
          attribute.AttributeClass?.ToDisplayString() == JSAttributeMetadataName);
      if (jsAttribute is null || member.GetAttributes().Any(attribute =>
              attribute.AttributeClass?.ToDisplayString() == EventAttributeMetadataName))
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

      var javaScriptName = LowerCamel(member.Name);
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
      string? asyncResultCodec;
      if (asyncResultType is not null &&
          asyncResultPassingKind == ExpoReturnPassingKind.Codec &&
          TryAnalyzeSharedObjectBoundaryType(
              asyncResultType,
              member.Name,
              "async result type",
              member.Locations.FirstOrDefault(),
              diagnostics,
              out var sharedAsyncResultCodec))
      {
        if (sharedAsyncResultCodec is null)
        {
          continue;
        }
        asyncResultCodec = sharedAsyncResultCodec;
      }
      else
      {
        asyncResultCodec = asyncResultType is null || asyncResultPassingKind != ExpoReturnPassingKind.Codec
            ? string.Empty
            : GetCodecExpression(
                asyncResultType,
                diagnostics,
                recordCodecs,
                member.GetReturnTypeAttributes()
            );
      }

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
      string? returnCodec;
      if (!isAsync && !returnsVoid &&
          returnPassingKind == ExpoReturnPassingKind.Codec &&
          TryAnalyzeSharedObjectBoundaryType(
              member.ReturnType,
              member.Name,
              "return type",
              member.Locations.FirstOrDefault(),
              diagnostics,
              out var sharedReturnCodec))
      {
        if (sharedReturnCodec is null)
        {
          continue;
        }
        returnCodec = sharedReturnCodec;
      }
      else
      {
        returnCodec = returnsVoid || isAsync || returnPassingKind != ExpoReturnPassingKind.Codec
            ? string.Empty
            : GetCodecExpression(
                member.ReturnType,
                diagnostics,
                recordCodecs,
                member.GetReturnTypeAttributes()
            );
      }
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
        string? parameterCodec;
        if (passingKind == ExpoParameterPassingKind.Codec &&
            TryAnalyzeSharedObjectBoundaryType(
                parameter.Type,
                member.Name,
                $"parameter '{parameter.Name}'",
                parameter.Locations.FirstOrDefault(),
                diagnostics,
                out var sharedParameterCodec))
        {
          if (sharedParameterCodec is null)
          {
            continue;
          }
          parameterCodec = sharedParameterCodec;
        }
        else
        {
          parameterCodec = GetCodecExpression(
              parameter.Type,
              diagnostics,
              recordCodecs,
              parameter.GetAttributes()
          );
        }
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
            IsJavaScriptCallbackType(parameter.Type) ||
                IsSharedObjectCodecExpression(parameterCodec ?? string.Empty),
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

      validJavaScriptNames.Add(javaScriptName);

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
          asyncResultPassingKind,
          IsSharedObjectCodecExpression(returnCodec ?? string.Empty),
          IsSharedObjectCodecExpression(asyncResultCodec ?? string.Empty)
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

    return (
        functions.Where(function => !duplicateNames.Contains(function.JavaScriptName)).ToList(),
        validJavaScriptNames
    );
  }

  private static List<ExpoPropertyModel> GetProperties(
      INamedTypeSymbol typeSymbol,
      List<ExpoDiagnosticModel> diagnostics,
      List<ExpoGeneratedRecordCodecModel> recordCodecs,
      HashSet<string> reservedJavaScriptNames)
  {
    var properties = new List<ExpoPropertyModel>();

    foreach (var member in typeSymbol.GetMembers().OfType<IPropertySymbol>())
    {
      var jsAttribute = member.GetAttributes().FirstOrDefault(attribute =>
          attribute.AttributeClass?.ToDisplayString() == JSAttributeMetadataName);
      if (jsAttribute is null || member.GetAttributes().Any(attribute =>
              attribute.AttributeClass?.ToDisplayString() == EventAttributeMetadataName))
      {
        continue;
      }

      var unsupportedShape = GetUnsupportedPropertyShape(member);
      if (unsupportedShape is not null)
      {
        diagnostics.Add(new ExpoDiagnosticModel(
            ExpoModulesDiagnostics.UnsupportedJSPropertyShape.Id,
            member.Locations.FirstOrDefault(),
            new EquatableArray<string>(new[] { member.Name, unsupportedShape })
        ));
        continue;
      }

      if (ContainsJavaScriptCallback(member.Type))
      {
        diagnostics.Add(new ExpoDiagnosticModel(
            ExpoModulesDiagnostics.UnsupportedJSPropertyType.Id,
            member.Type.Locations.FirstOrDefault() ?? member.Locations.FirstOrDefault(),
            new EquatableArray<string>(new[] { member.Name, GetDiagnosticTypeName(member.Type) })
        ));
        continue;
      }

      string? codecExpression;
      if (TryAnalyzeSharedObjectBoundaryType(
          member.Type,
          member.Name,
          "property type",
          member.Locations.FirstOrDefault(),
          diagnostics,
          out var sharedPropertyCodec))
      {
        if (sharedPropertyCodec is null)
        {
          continue;
        }
        codecExpression = sharedPropertyCodec;
      }
      else
      {
        codecExpression = GetCodecExpression(member.Type, diagnostics, recordCodecs, member.GetAttributes());
      }
      if (codecExpression is null)
      {
        diagnostics.Add(new ExpoDiagnosticModel(
            ExpoModulesDiagnostics.UnsupportedJSPropertyType.Id,
            member.Type.Locations.FirstOrDefault() ?? member.Locations.FirstOrDefault(),
            new EquatableArray<string>(new[] { member.Name, GetDiagnosticTypeName(member.Type) })
        ));
        continue;
      }

      var javaScriptName = GetJavaScriptName(member.Name, jsAttribute);
      if (reservedJavaScriptNames.Contains(javaScriptName))
      {
        diagnostics.Add(new ExpoDiagnosticModel(
            ExpoModulesDiagnostics.ReservedObservingPropertyName.Id,
            member.Locations.FirstOrDefault(),
            new EquatableArray<string>(new[] { member.Name, javaScriptName })
        ));
        continue;
      }

      properties.Add(new ExpoPropertyModel(
          member.Name,
          javaScriptName,
          member.Locations.FirstOrDefault(),
          member.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
          codecExpression,
          IsSupportedPropertySetter(member.SetMethod),
          codecExpression is "JavaScriptValueCodec" or "ArrayBufferCodec",
          IsJavaScriptCallbackType(member.Type) || IsSharedObjectCodecExpression(codecExpression)
      ));
    }

    return properties;
  }

  private static List<ExpoPropertyModel> RemoveCollidingProperties(
      INamedTypeSymbol typeSymbol,
      HashSet<string> validMethodJavaScriptNames,
      List<ExpoPropertyModel> properties,
      List<ExpoDiagnosticModel> diagnostics)
  {
    var collidingProperties = new HashSet<string>(StringComparer.Ordinal);
    foreach (var group in properties.GroupBy(property => property.JavaScriptName))
    {
      if (group.Count() <= 1)
      {
        continue;
      }

      var duplicate = group.Skip(1).First();
      diagnostics.Add(new ExpoDiagnosticModel(
          ExpoModulesDiagnostics.DuplicateJavaScriptMemberName.Id,
          duplicate.Location,
          new EquatableArray<string>(new[] { typeSymbol.Name, group.Key })
      ));
      collidingProperties.Add(group.Key);
    }

    foreach (var property in properties)
    {
      if (!validMethodJavaScriptNames.Contains(property.JavaScriptName))
      {
        continue;
      }

      diagnostics.Add(new ExpoDiagnosticModel(
          ExpoModulesDiagnostics.DuplicateJavaScriptMemberName.Id,
          property.Location,
          new EquatableArray<string>(new[] { typeSymbol.Name, property.JavaScriptName })
      ));
      collidingProperties.Add(property.JavaScriptName);
    }

    return properties
        .Where(property => !collidingProperties.Contains(property.JavaScriptName))
        .ToList();
  }

  private static string? GetUnsupportedPropertyShape(IPropertySymbol property)
  {
    if (property.IsStatic)
    {
      return "static";
    }
    if (property.IsIndexer)
    {
      return "indexed";
    }
    if (property.GetMethod is null)
    {
      return "setter-only";
    }
    if (!IsSupportedPropertyAccessibility(property.GetMethod))
    {
      return "an inaccessible getter";
    }
    if (property.SetMethod?.IsInitOnly == true)
    {
      return "an init accessor";
    }
    return null;
  }

  private static bool IsSupportedPropertyAccessibility(IMethodSymbol accessor) =>
      accessor.DeclaredAccessibility == Accessibility.Public ||
      accessor.DeclaredAccessibility == Accessibility.Internal;

  private static bool IsSupportedPropertySetter(IMethodSymbol? accessor) =>
      accessor is not null &&
      !accessor.IsInitOnly &&
      IsSupportedPropertyAccessibility(accessor);

  private static string GetJavaScriptName(string memberName, AttributeData jsAttribute)
  {
    if (jsAttribute.ConstructorArguments.Length == 1 &&
        jsAttribute.ConstructorArguments[0].Value is string explicitName)
    {
      return explicitName;
    }
    return LowerCamel(memberName);
  }

}
