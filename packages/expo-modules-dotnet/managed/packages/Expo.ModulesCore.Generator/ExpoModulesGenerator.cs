using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Expo.ModulesCore.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class ExpoModulesGenerator : IIncrementalGenerator
{
  private const string ExpoModuleAttributeMetadataName = "Expo.ModulesCore.ExpoModuleAttribute";
  private const string ExpoSharedObjectAttributeMetadataName = "Expo.ModulesCore.ExpoSharedObjectAttribute";
  private const string SharedObjectMetadataName = "Expo.ModulesCore.SharedObject";
  private const string SharedRefMetadataName = "Expo.ModulesCore.SharedRef<T>";
  private const string EventsAttributeMetadataName = "Expo.ModulesCore.EventsAttribute";
  private const string EventAttributeMetadataName = "Expo.ModulesCore.EventAttribute";
  private const string OnCreateAttributeMetadataName = "Expo.ModulesCore.OnCreateAttribute";
  private const string OnDestroyAttributeMetadataName = "Expo.ModulesCore.OnDestroyAttribute";
  private const string OnStartObservingAttributeMetadataName = "Expo.ModulesCore.OnStartObservingAttribute";
  private const string OnStopObservingAttributeMetadataName = "Expo.ModulesCore.OnStopObservingAttribute";
  private const string JSEnumAttributeMetadataName = "Expo.ModulesCore.JSEnumAttribute";
  private const string JSAttributeMetadataName = "Expo.ModulesCore.JSAttribute";
  private const string DotnetRuntimeContextMetadataName = "Expo.ModulesCore.DotnetRuntimeContext";
  private const string JavaScriptValueMetadataName = "global::Expo.JSI.JavaScriptValue";
  private const string ArrayBufferMetadataName = "global::Expo.ModulesCore.ArrayBuffer";

  public void Initialize(IncrementalGeneratorInitializationContext context)
  {
    var modules = context.SyntaxProvider.ForAttributeWithMetadataName(
        ExpoModuleAttributeMetadataName,
        static (node, _) => node is ClassDeclarationSyntax,
        static (syntaxContext, cancellationToken) =>
            CreateModuleModel(syntaxContext, cancellationToken)
    );

    var sharedObjects = context.SyntaxProvider.ForAttributeWithMetadataName(
        ExpoSharedObjectAttributeMetadataName,
        static (node, _) => node is ClassDeclarationSyntax,
        static (syntaxContext, cancellationToken) =>
            CreateSharedObjectModel(syntaxContext, cancellationToken)
    );

    var compilationModulesAndSharedObjects = context.CompilationProvider
        .Combine(modules.Collect())
        .Combine(sharedObjects.Collect());

    context.RegisterSourceOutput(
        compilationModulesAndSharedObjects,
        static (sourceContext, value) =>
        {
          var assemblyName = value.Left.Left.AssemblyName ?? "ExpoModules";
          EmitProvider(
              sourceContext,
              assemblyName,
              value.Left.Right.Where(module => module is not null).Select(module => module!),
              value.Right.Where(sharedObject => sharedObject is not null).Select(sharedObject => sharedObject!)
          );
        }
    );
  }

  private static ExpoSharedObjectModel? CreateSharedObjectModel(
      GeneratorAttributeSyntaxContext context,
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();

    if (context.TargetSymbol is not INamedTypeSymbol typeSymbol)
    {
      return null;
    }

    var location = typeSymbol.Locations.FirstOrDefault();
    var diagnostics = new List<ExpoDiagnosticModel>();
    var javaScriptClassName = typeSymbol.Name;

    void AddDeclarationDiagnostic(string reason) =>
        diagnostics.Add(new ExpoDiagnosticModel(
            ExpoModulesDiagnostics.InvalidSharedObjectDeclaration.Id,
            location,
            new EquatableArray<string>(new[] { typeSymbol.Name, reason })
        ));

    foreach (var attribute in context.Attributes)
    {
      if (attribute.ConstructorArguments.Length != 1)
      {
        continue;
      }

      if (attribute.ConstructorArguments[0].Value is string explicitName &&
          !string.IsNullOrWhiteSpace(explicitName))
      {
        javaScriptClassName = explicitName;
      }
      else
      {
        AddDeclarationDiagnostic("its explicit JavaScript class name must be a non-empty string");
      }
    }

    if (typeSymbol.ContainingType is not null)
    {
      AddDeclarationDiagnostic("it must be a top-level class");
    }

    if (typeSymbol.IsGenericType)
    {
      AddDeclarationDiagnostic("it must be non-generic");
    }

    if (!typeSymbol.IsSealed)
    {
      AddDeclarationDiagnostic("it must be sealed");
    }

    if (context.TargetNode is not ClassDeclarationSyntax classDeclaration ||
        !classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
    {
      AddDeclarationDiagnostic("it must be partial");
    }

    if (!DerivesFromSharedObject(typeSymbol))
    {
      AddDeclarationDiagnostic("it must derive from Expo.ModulesCore.SharedObject");
    }

    var isDeclarationValid = diagnostics.Count == 0;
    ExpoSharedObjectConstructorModel? constructor = null;
    var functions = new List<ExpoFunctionModel>();
    var properties = new List<ExpoPropertyModel>();
    var events = new List<ExpoEventModel>();
    var recordCodecs = new List<ExpoGeneratedRecordCodecModel>();
    if (isDeclarationValid)
    {
      var memberDiagnostics = new List<ExpoDiagnosticModel>();
      var functionCollection = GetFunctions(
          typeSymbol,
          memberDiagnostics,
          recordCodecs,
          new HashSet<string>(StringComparer.Ordinal)
      );
      functions = functionCollection.Functions;
      properties = GetProperties(typeSymbol, memberDiagnostics, recordCodecs, new HashSet<string>(StringComparer.Ordinal));
      properties = RemoveCollidingProperties(
          typeSymbol,
          functionCollection.ValidJavaScriptNames,
          properties,
          memberDiagnostics
      );
      foreach (var diagnostic in memberDiagnostics)
      {
        diagnostics.Add(TranslateSharedObjectMemberDiagnostic(diagnostic, typeSymbol.Name));
      }

      functions = RemoveInaccessibleSharedObjectMethods(typeSymbol, functions, diagnostics);
      functions = functions
          .Where(function => !ReportReservedSharedObjectMemberName(
              typeSymbol.Name, function.JavaScriptName, function.Location, diagnostics))
          .ToList();
      properties = properties
          .Where(property => !ReportReservedSharedObjectMemberName(
              typeSymbol.Name, property.JavaScriptName, property.Location, diagnostics))
          .ToList();
      events = GetTypedEvents(
          typeSymbol,
          typeSymbol.Name,
          diagnostics,
          recordCodecs,
          ExpoModulesDiagnostics.UnsupportedSharedObjectEventProperty.Id,
          ExpoModulesDiagnostics.UnsupportedSharedObjectEventPayload.Id
      );
      ValidateSharedObjectEventNames(typeSymbol.Name, functions, properties, events, diagnostics);

      constructor = GetSharedObjectConstructor(typeSymbol, diagnostics, recordCodecs);
    }

    return new ExpoSharedObjectModel(
        typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        typeSymbol.ContainingNamespace.IsGlobalNamespace ? string.Empty : typeSymbol.ContainingNamespace.ToDisplayString(),
        typeSymbol.Name,
        typeSymbol.DeclaredAccessibility == Accessibility.Public ? "public" : "internal",
        javaScriptClassName,
        location,
        isDeclarationValid,
        constructor,
        new EquatableArray<ExpoEventModel>(events),
        new EquatableArray<ExpoFunctionModel>(functions),
        new EquatableArray<ExpoPropertyModel>(properties),
        new EquatableArray<ExpoGeneratedRecordCodecModel>(recordCodecs),
        new EquatableArray<ExpoDiagnosticModel>(diagnostics)
    );
  }

  private static readonly string[] ReservedSharedObjectMemberNames = { "release", "constructor", "__proto__" };

  private static bool ReportReservedSharedObjectMemberName(
      string typeName,
      string javaScriptName,
      Location? location,
      List<ExpoDiagnosticModel> diagnostics)
  {
    if (!ReservedSharedObjectMemberNames.Contains(javaScriptName, StringComparer.Ordinal))
    {
      return false;
    }

    diagnostics.Add(new ExpoDiagnosticModel(
        ExpoModulesDiagnostics.InvalidSharedObjectMemberName.Id,
        location,
        new EquatableArray<string>(new[] { typeName, javaScriptName, "reserved for the shared object prototype" })
    ));
    return true;
  }

  private static void ValidateSharedObjectEventNames(
      string typeName,
      IReadOnlyList<ExpoFunctionModel> functions,
      IReadOnlyList<ExpoPropertyModel> properties,
      List<ExpoEventModel> events,
      List<ExpoDiagnosticModel> diagnostics)
  {
    var memberNames = new HashSet<string>(
        functions.Select(function => function.JavaScriptName)
            .Concat(properties.Select(property => property.JavaScriptName)),
        StringComparer.Ordinal
    );
    var eventNames = new HashSet<string>(StringComparer.Ordinal);
    for (var index = 0; index < events.Count; index++)
    {
      var @event = events[index];
      if (!@event.IsDispatchable)
      {
        continue;
      }

      string? reason = null;
      if (ReservedSharedObjectMemberNames.Contains(@event.JavaScriptName, StringComparer.Ordinal))
      {
        reason = "reserved for the shared object prototype";
      }
      else if (memberNames.Contains(@event.JavaScriptName))
      {
        reason = "already used by a generated shared-object member";
      }
      else if (!eventNames.Add(@event.JavaScriptName))
      {
        reason = "duplicated by another shared-object event";
      }

      if (reason is null)
      {
        continue;
      }

      diagnostics.Add(new ExpoDiagnosticModel(
          ExpoModulesDiagnostics.InvalidSharedObjectEventName.Id,
          @event.Location,
          new EquatableArray<string>(new[] { typeName, @event.PropertyName, @event.JavaScriptName, reason })
      ));
      events[index] = @event with { IsDispatchable = false };
    }
  }

  private static List<ExpoFunctionModel> RemoveInaccessibleSharedObjectMethods(
      INamedTypeSymbol typeSymbol,
      List<ExpoFunctionModel> functions,
      List<ExpoDiagnosticModel> diagnostics)
  {
    var inaccessibleMethodSignatures = new HashSet<string>(StringComparer.Ordinal);
    foreach (var member in typeSymbol.GetMembers().OfType<IMethodSymbol>())
    {
      if (member.MethodKind != MethodKind.Ordinary ||
          member.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal ||
          !member.GetAttributes().Any(attribute =>
              attribute.AttributeClass?.ToDisplayString() == JSAttributeMetadataName))
      {
        continue;
      }

      inaccessibleMethodSignatures.Add(GetMethodSignature(
          member.Name,
          member.Parameters.Select(parameter =>
              parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
      ));
      diagnostics.Add(CreateUnsupportedSharedObjectUsage(
          member.Name,
          "it is not public or internal",
          member.Locations.FirstOrDefault()
      ));
    }

    return inaccessibleMethodSignatures.Count == 0
        ? functions
        : functions
            .Where(function => !inaccessibleMethodSignatures.Contains(GetMethodSignature(
                function.MethodName,
                function.Parameters.Values.Select(parameter => parameter.TypeName))))
            .ToList();
  }

  private static string GetMethodSignature(string methodName, IEnumerable<string> parameterTypeNames) =>
      $"{methodName}({string.Join(", ", parameterTypeNames)})";

  private static ExpoDiagnosticModel TranslateSharedObjectMemberDiagnostic(
      ExpoDiagnosticModel diagnostic,
      string typeName)
  {
    var arguments = diagnostic.Arguments.Values;
    return diagnostic.DescriptorId switch
    {
      "EXPOJSI001" => CreateUnsupportedSharedObjectUsage(
          arguments[1], $"parameter '{arguments[0]}' uses unsupported type '{arguments[2]}'", diagnostic.Location),
      "EXPOJSI002" => CreateUnsupportedSharedObjectUsage(
          arguments[0], $"it uses unsupported return type '{arguments[1]}'", diagnostic.Location),
      "EXPOJSI004" => CreateUnsupportedSharedObjectUsage(
          arguments[0], $"it is {arguments[1]}", diagnostic.Location),
      "EXPOJSI005" => new ExpoDiagnosticModel(
          ExpoModulesDiagnostics.InvalidSharedObjectMemberName.Id,
          diagnostic.Location,
          new EquatableArray<string>(new[] { typeName, arguments[1], "a duplicate" })),
      "EXPOJSI008" => CreateUnsupportedSharedObjectUsage(
          arguments[1], $"callback parameter '{arguments[0]}' uses unsupported callback type '{arguments[2]}'", diagnostic.Location),
      "EXPOJSI012" => CreateUnsupportedSharedObjectUsage(
          arguments[0], $"parameter '{arguments[1]}' uses '{arguments[2]}', which is supported only by synchronous methods", diagnostic.Location),
      "EXPOJSI013" => CreateUnsupportedSharedObjectUsage(
          arguments[0], $"it declares multiple span parameters ({arguments[1]})", diagnostic.Location),
      "EXPOJSI014" => CreateUnsupportedSharedObjectUsage(
          arguments[0], $"it is {arguments[1]}", diagnostic.Location),
      "EXPOJSI015" => CreateUnsupportedSharedObjectUsage(
          arguments[0], $"it uses unsupported type '{arguments[1]}'", diagnostic.Location),
      "EXPOJSI016" => new ExpoDiagnosticModel(
          ExpoModulesDiagnostics.InvalidSharedObjectMemberName.Id,
          diagnostic.Location,
          new EquatableArray<string>(new[] { typeName, arguments[1], "a duplicate" })),
      _ => diagnostic,
    };
  }

  private static ExpoSharedObjectConstructorModel? GetSharedObjectConstructor(
      INamedTypeSymbol typeSymbol,
      List<ExpoDiagnosticModel> diagnostics,
      List<ExpoGeneratedRecordCodecModel> recordCodecs)
  {
    void AddConstructorDiagnostic(Location? location, string reason) =>
        diagnostics.Add(new ExpoDiagnosticModel(
            ExpoModulesDiagnostics.InvalidSharedObjectConstructor.Id,
            location ?? typeSymbol.Locations.FirstOrDefault(),
            new EquatableArray<string>(new[] { typeSymbol.Name, reason })
        ));

    var attributedConstructors = new List<(IMethodSymbol Constructor, AttributeData Attribute)>();
    foreach (var constructor in typeSymbol.Constructors)
    {
      var jsAttribute = constructor.GetAttributes().FirstOrDefault(attribute =>
          attribute.AttributeClass?.ToDisplayString() == JSAttributeMetadataName);
      if (jsAttribute is null)
      {
        continue;
      }

      if (constructor.IsStatic)
      {
        AddConstructorDiagnostic(constructor.Locations.FirstOrDefault(), "it must be an instance constructor");
        continue;
      }

      attributedConstructors.Add((constructor, jsAttribute));
    }

    if (attributedConstructors.Count == 0)
    {
      return null;
    }

    if (attributedConstructors.Count > 1)
    {
      AddConstructorDiagnostic(
          attributedConstructors[1].Constructor.Locations.FirstOrDefault(),
          "it declares multiple [JS] constructors"
      );
      return null;
    }

    var (target, attribute) = attributedConstructors[0];
    if (attribute.ConstructorArguments.Length == 1)
    {
      AddConstructorDiagnostic(target.Locations.FirstOrDefault(), "it must not declare an explicit JavaScript name");
      return null;
    }

    if (!IsSupportedConstructorAccessibility(target))
    {
      AddConstructorDiagnostic(target.Locations.FirstOrDefault(), "it must be public or internal");
      return null;
    }

    var parameters = new List<ExpoParameterModel>();
    foreach (var parameter in target.Parameters)
    {
      var parameterLocation = parameter.Locations.FirstOrDefault() ?? target.Locations.FirstOrDefault();
      var positionDescription = $"its [JS] constructor parameter '{parameter.Name}'";
      string? parameterCodec;
      if (GetParameterPassingKind(parameter.Type) != ExpoParameterPassingKind.Codec)
      {
        parameterCodec = null;
      }
      else if (TryAnalyzeSharedObjectBoundaryType(
          parameter.Type,
          typeSymbol.Name,
          positionDescription,
          parameterLocation,
          diagnostics,
          out var sharedParameterCodec))
      {
        if (sharedParameterCodec is null)
        {
          return null;
        }
        parameterCodec = sharedParameterCodec;
      }
      else
      {
        parameterCodec = GetCodecExpression(parameter.Type, diagnostics, recordCodecs, parameter.GetAttributes());
      }

      if (parameterCodec is null)
      {
        diagnostics.Add(CreateUnsupportedSharedObjectUsage(
            typeSymbol.Name,
            $"{positionDescription} uses unsupported type '{GetDiagnosticTypeName(parameter.Type)}'",
            parameterLocation
        ));
        return null;
      }

      parameters.Add(new ExpoParameterModel(
          parameter.Name,
          parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
          parameterCodec,
          IsJavaScriptCallbackType(parameter.Type) || IsSharedObjectCodecExpression(parameterCodec),
          parameterCodec is "JavaScriptValueCodec" or "ArrayBufferCodec",
          parameter.HasExplicitDefaultValue,
          parameter.HasExplicitDefaultValue
              ? GetDefaultValueExpression(parameter.Type, parameter.ExplicitDefaultValue)
              : string.Empty
      ));
    }

    return new ExpoSharedObjectConstructorModel(
        target.Locations.FirstOrDefault(),
        new EquatableArray<ExpoParameterModel>(parameters)
    );
  }

  private static bool DerivesFromSharedObject(INamedTypeSymbol typeSymbol)
  {
    for (var baseType = typeSymbol.BaseType; baseType is not null; baseType = baseType.BaseType)
    {
      if (baseType.ToDisplayString() == SharedObjectMetadataName)
      {
        return true;
      }
    }
    return false;
  }

  private static ExpoDiagnosticModel CreateUnsupportedSharedObjectUsage(
      string memberName,
      string reason,
      Location? location) =>
      new(
          ExpoModulesDiagnostics.UnsupportedSharedObjectUsage.Id,
          location,
          new EquatableArray<string>(new[] { memberName, reason })
      );

  private static bool IsSharedObjectCodecExpression(string codecExpression) =>
      codecExpression.StartsWith("SharedObjectCodec<", StringComparison.Ordinal);

  private static bool IsSharedObjectRelatedType(ITypeSymbol typeSymbol) =>
      typeSymbol.ToDisplayString() == SharedObjectMetadataName ||
      (typeSymbol is INamedTypeSymbol namedType && DerivesFromSharedObject(namedType));

  private static string? GetDirectSharedObjectBoundaryIssue(ITypeSymbol typeSymbol)
  {
    if (typeSymbol.ToDisplayString() == SharedObjectMetadataName)
    {
      return "which is the polymorphic SharedObject base";
    }
    if (typeSymbol is INamedTypeSymbol namedType &&
        namedType.OriginalDefinition.ToDisplayString() == SharedRefMetadataName)
    {
      return "which is the SharedRef<T> managed carrier base";
    }
    if (!HasExpoSharedObjectAttribute(typeSymbol))
    {
      return "which is not marked [ExpoSharedObject]";
    }
    if (typeSymbol.ContainingType is not null ||
        typeSymbol is INamedTypeSymbol { IsGenericType: true } ||
        !typeSymbol.IsSealed)
    {
      return "which must be a top-level, non-generic, sealed [ExpoSharedObject] class";
    }
    if (typeSymbol.NullableAnnotation == NullableAnnotation.Annotated)
    {
      return "which must be used without a nullable annotation";
    }
    return null;
  }

  private static bool HasExpoSharedObjectAttribute(ITypeSymbol typeSymbol) =>
      typeSymbol.GetAttributes().Any(attribute =>
          attribute.AttributeClass?.ToDisplayString() == ExpoSharedObjectAttributeMetadataName);

  // Reports EXPOJSI023 and returns true when the boundary type is shared-object related.
  // On success codecExpression is the exact shared-object codec; on failure it stays null.
  // Ownership of the exact attributed type is validated once per compilation before emission.
  private static bool TryAnalyzeSharedObjectBoundaryType(
      ITypeSymbol typeSymbol,
      string memberName,
      string positionDescription,
      Location? location,
      List<ExpoDiagnosticModel> diagnostics,
      out string? codecExpression)
  {
    codecExpression = null;
    if (IsSharedObjectRelatedType(typeSymbol))
    {
      var issue = GetDirectSharedObjectBoundaryIssue(typeSymbol);
      if (issue is null)
      {
        codecExpression =
            $"SharedObjectCodec<{typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>";
        return true;
      }

      diagnostics.Add(CreateUnsupportedSharedObjectUsage(
          memberName,
          $"{positionDescription} uses shared-object type '{GetDiagnosticTypeName(typeSymbol)}', {issue}",
          location
      ));
      return true;
    }

    if (TryFindNestedSharedObjectType(typeSymbol, out var nestedSharedObjectType))
    {
      diagnostics.Add(CreateUnsupportedSharedObjectUsage(
          memberName,
          $"{positionDescription} uses shared-object type '{GetDiagnosticTypeName(nestedSharedObjectType)}' inside a composed codec; shared-object types are supported only directly at the generated boundary",
          location
      ));
      return true;
    }

    return false;
  }

  private static bool TryFindNestedSharedObjectType(ITypeSymbol typeSymbol, out ITypeSymbol sharedObjectType)
  {
    return TryFindNestedSharedObjectType(
        typeSymbol,
        new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default),
        out sharedObjectType
    );
  }

  private static bool TryFindNestedSharedObjectType(
      ITypeSymbol typeSymbol,
      HashSet<ITypeSymbol> visitedTypes,
      out ITypeSymbol sharedObjectType)
  {
    sharedObjectType = typeSymbol;
    if (!visitedTypes.Add(typeSymbol))
    {
      return false;
    }

    if (typeSymbol is not INamedTypeSymbol namedType)
    {
      return false;
    }

    if (namedType.IsTupleType)
    {
      return TryFindFirstNestedSharedObjectType(
          namedType.TupleElements.Select(element => element.Type), visitedTypes, out sharedObjectType);
    }

    if (IsJavaScriptCallbackType(namedType))
    {
      return TryFindFirstNestedSharedObjectType(namedType.TypeArguments, visitedTypes, out sharedObjectType);
    }

    if (namedType.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T ||
        namedType.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            "global::System.Collections.Generic.IReadOnlyList<T>")
    {
      return TryFindFirstNestedSharedObjectType(namedType.TypeArguments, visitedTypes, out sharedObjectType);
    }

    var constructedType = namedType.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    if (constructedType is "global::System.Collections.Generic.Dictionary<TKey, TValue>" or
        "global::System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>")
    {
      return TryFindFirstNestedSharedObjectType(
          new[] { namedType.TypeArguments[1] }, visitedTypes, out sharedObjectType);
    }

    if (namedType.IsRecord && GetRecordCodecConstructor(namedType) is { } constructor)
    {
      return TryFindFirstNestedSharedObjectType(
          constructor.Parameters.Select(parameter => parameter.Type), visitedTypes, out sharedObjectType);
    }

    return false;
  }

  private static bool TryFindFirstNestedSharedObjectType(
      IEnumerable<ITypeSymbol> typeSymbols,
      HashSet<ITypeSymbol> visitedTypes,
      out ITypeSymbol sharedObjectType)
  {
    foreach (var typeSymbol in typeSymbols)
    {
      if (IsSharedObjectRelatedType(typeSymbol))
      {
        sharedObjectType = typeSymbol;
        return true;
      }
      if (TryFindNestedSharedObjectType(typeSymbol, visitedTypes, out sharedObjectType))
      {
        return true;
      }
    }

    sharedObjectType = null!;
    return false;
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

  private static List<ExpoEventModel> GetTypedEvents(
      INamedTypeSymbol typeSymbol,
      string moduleName,
      List<ExpoDiagnosticModel> diagnostics,
      List<ExpoGeneratedRecordCodecModel> recordCodecs,
      string unsupportedPropertyDiagnosticId = "EXPOJSI018",
      string unsupportedPayloadDiagnosticId = "EXPOJSI019")
  {
    var events = new List<ExpoEventModel>();
    var containerReason = GetUnsupportedEventContainerShape(typeSymbol);
    foreach (var property in typeSymbol.GetMembers().OfType<IPropertySymbol>()
                 .OrderBy(member => member.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue))
    {
      var eventAttribute = property.GetAttributes().FirstOrDefault(attribute =>
          attribute.AttributeClass?.ToDisplayString() == EventAttributeMetadataName);
      if (eventAttribute is null)
      {
        continue;
      }

      var declaration = property.DeclaringSyntaxReferences
          .Select(reference => reference.GetSyntax())
          .OfType<PropertyDeclarationSyntax>()
          .FirstOrDefault();
      var propertyReason = containerReason ?? GetUnsupportedEventPropertyShape(property, declaration);
      var canReproduce = containerReason is null && CanReproduceEventProperty(property, declaration);
      var javaScriptName = LowerCamel(property.Name);
      ITypeSymbol? payloadType = null;
      var hasUnsupportedPayload = false;
      if (eventAttribute.ConstructorArguments.Length == 1)
      {
        var explicitName = eventAttribute.ConstructorArguments[0].Value as string;
        if (string.IsNullOrWhiteSpace(explicitName))
        {
          propertyReason ??= explicitName is null ? "a null explicit name" : "an empty or blank explicit name";
        }
        else
        {
          javaScriptName = explicitName!;
        }
      }

      var payloadKind = ExpoEventPayloadKind.None;
      var payloadTypeName = string.Empty;
      var codecExpression = string.Empty;
      if (propertyReason is null && !TryGetEventDelegatePayload(
              property.Type,
              out payloadType,
              out payloadKind,
              out var delegateReason))
      {
        propertyReason = delegateReason;
      }

      if (payloadType is not null)
      {
        payloadTypeName = payloadType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
      }

      if (propertyReason is null && payloadType is not null && payloadKind == ExpoEventPayloadKind.Codec)
      {
        var payloadReason = GetUnsupportedEventPayload(payloadType);
        if (payloadReason is not null)
        {
          diagnostics.Add(CreateUnsupportedEventPayload(moduleName, property, payloadType, payloadReason, unsupportedPayloadDiagnosticId));
          hasUnsupportedPayload = true;
          propertyReason = "an unsupported payload";
        }
        else
        {
          var scratchRecordCodecs = recordCodecs.ToList();
          var scratchDiagnostics = new List<ExpoDiagnosticModel>();
          codecExpression = GetCodecExpression(payloadType, scratchDiagnostics, scratchRecordCodecs) ?? string.Empty;
          if (codecExpression.Length == 0)
          {
            diagnostics.Add(CreateUnsupportedEventPayload(
                moduleName,
                property,
                payloadType,
                "no encode-capable codec is available",
                unsupportedPayloadDiagnosticId
            ));
            hasUnsupportedPayload = true;
            propertyReason = "an unsupported payload";
          }
          else
          {
            recordCodecs.Clear();
            recordCodecs.AddRange(scratchRecordCodecs);
          }
        }
      }
      else if (propertyReason is null && payloadType is not null)
      {
        var payloadReason = GetUnsupportedEventPayload(payloadType);
        if (payloadReason is not null)
        {
          diagnostics.Add(CreateUnsupportedEventPayload(moduleName, property, payloadType, payloadReason, unsupportedPayloadDiagnosticId));
          hasUnsupportedPayload = true;
          propertyReason = "an unsupported payload";
        }
      }

      if (propertyReason is not null && !hasUnsupportedPayload)
      {
        diagnostics.Add(new ExpoDiagnosticModel(
            unsupportedPropertyDiagnosticId,
            property.Locations.FirstOrDefault(),
            new EquatableArray<string>(new[] { moduleName, property.Name, propertyReason })
        ));
      }

      events.Add(new ExpoEventModel(
          property.Name,
          javaScriptName,
          GetAccessibilityText(property.DeclaredAccessibility),
          GetEventDeclarationModifiers(declaration, property),
          GetEventAccessorText(declaration, SyntaxKind.GetAccessorDeclaration),
          GetEventAccessorText(declaration, SyntaxKind.SetAccessorDeclaration, SyntaxKind.InitAccessorDeclaration),
          property.IsStatic,
          property.SetMethod is not null,
          property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
          payloadTypeName,
          payloadKind,
          codecExpression,
          property.Locations.FirstOrDefault(),
          canReproduce,
          propertyReason is null
      ));
    }

    return events;
  }

  private static IReadOnlyList<string> MergeEventNames(
      string moduleName,
      List<string> legacyEventNames,
      List<ExpoEventModel> typedEvents,
      List<ExpoDiagnosticModel> diagnostics)
  {
    var names = new List<string>(legacyEventNames);
    var seen = new HashSet<string>(legacyEventNames, StringComparer.Ordinal);
    for (var index = 0; index < typedEvents.Count; index++)
    {
      var typedEvent = typedEvents[index];
      if (!typedEvent.IsDispatchable)
      {
        continue;
      }
      if (!seen.Add(typedEvent.JavaScriptName))
      {
        diagnostics.Add(new ExpoDiagnosticModel(
            ExpoModulesDiagnostics.DuplicateEventName.Id,
            typedEvent.Location,
            new EquatableArray<string>(new[] { moduleName, typedEvent.PropertyName, typedEvent.JavaScriptName })
        ));
        typedEvents[index] = typedEvent with { IsDispatchable = false };
        continue;
      }
      names.Add(typedEvent.JavaScriptName);
    }
    return names;
  }

  private static ExpoDiagnosticModel CreateUnsupportedEventPayload(
      string moduleName,
      IPropertySymbol property,
      ITypeSymbol payloadType,
      string reason,
      string diagnosticId = "EXPOJSI019") =>
      new(
          diagnosticId,
          payloadType.Locations.FirstOrDefault() ?? property.Locations.FirstOrDefault(),
          new EquatableArray<string>(new[] { moduleName, property.Name, GetDiagnosticTypeName(payloadType), reason })
      );

  private static bool TryGetEventDelegatePayload(
      ITypeSymbol delegateType,
      out ITypeSymbol? payloadType,
      out ExpoEventPayloadKind payloadKind,
      out string reason)
  {
    payloadType = null;
    payloadKind = ExpoEventPayloadKind.None;
    reason = "a non-awaitable delegate; events must use Func<Task> or Func<T, Task>";
    if (delegateType is not INamedTypeSymbol namedDelegate ||
        namedDelegate.DelegateInvokeMethod is null)
    {
      return false;
    }

    var definition = namedDelegate.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    if (definition == "global::System.Func<TResult>" &&
        IsTaskType(namedDelegate.TypeArguments.Single()))
    {
      return true;
    }
    if (definition == "global::System.Func<T, TResult>" &&
        IsTaskType(namedDelegate.TypeArguments[1]))
    {
      payloadType = namedDelegate.TypeArguments[0];
      var payloadName = payloadType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
      payloadKind = payloadName == JavaScriptValueMetadataName
          ? ExpoEventPayloadKind.JavaScriptValue
          : payloadName == ArrayBufferMetadataName
              ? ExpoEventPayloadKind.ArrayBuffer
              : ExpoEventPayloadKind.Codec;
      return true;
    }
    return false;
  }

  private static bool IsTaskType(ITypeSymbol typeSymbol) =>
      typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
      "global::System.Threading.Tasks.Task";

  private static string? GetUnsupportedEventPayload(ITypeSymbol payloadType) =>
      GetUnsupportedEventPayload(payloadType, true, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));

  private static string? GetUnsupportedEventPayload(
      ITypeSymbol typeSymbol,
      bool isTopLevel,
      HashSet<ITypeSymbol> visitedTypes)
  {
    if (!visitedTypes.Add(typeSymbol))
    {
      return "it contains a recursive record codec";
    }
    try
    {
      if (IsJavaScriptCallbackType(typeSymbol)) return "it contains JavaScriptCallback, which is decode-only";
      var typeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
      if (typeName is JavaScriptValueMetadataName or ArrayBufferMetadataName)
        return isTopLevel ? null : "it contains a nested transfer-sensitive wrapper";
      if (typeSymbol is not INamedTypeSymbol namedType) return null;
      if (namedType.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
        return GetUnsupportedEventPayload(namedType.TypeArguments.Single(), false, visitedTypes);
      if (namedType.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
          "global::System.Collections.Generic.IReadOnlyList<T>")
        return GetUnsupportedEventPayload(namedType.TypeArguments.Single(), false, visitedTypes);
      var definition = namedType.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
      if (definition is "global::System.Collections.Generic.Dictionary<TKey, TValue>" or
          "global::System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>")
        return namedType.TypeArguments[0].SpecialType == SpecialType.System_String
            ? GetUnsupportedEventPayload(namedType.TypeArguments[1], false, visitedTypes)
            : null;
      if (!namedType.IsRecord) return null;
      var constructor = GetRecordCodecConstructor(namedType);
      return constructor is null
          ? null
          : constructor.Parameters.Select(parameter => GetUnsupportedEventPayload(parameter.Type, false, visitedTypes))
              .FirstOrDefault(reason => reason is not null);
    }
    finally
    {
      visitedTypes.Remove(typeSymbol);
    }
  }

  private static string? GetUnsupportedEventContainerShape(INamedTypeSymbol typeSymbol)
  {
    if (typeSymbol.ContainingType is not null) return "declared in a nested module container";
    if (typeSymbol.TypeParameters.Length != 0) return "declared in a generic module container";
    if (typeSymbol.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax() is ClassDeclarationSyntax declaration &&
            declaration.Modifiers.Any(SyntaxKind.FileKeyword)))
    {
      return "declared in a file-local module container";
    }
    if (typeSymbol.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax() is ClassDeclarationSyntax declaration &&
            !declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
    {
      return "declared in a non-partial module container";
    }
    return null;
  }

  private static string? GetUnsupportedEventPropertyShape(
      IPropertySymbol property,
      PropertyDeclarationSyntax? declaration)
  {
    if (property.IsStatic) return "static";
    if (property.IsIndexer) return "indexed";
    if (property.ExplicitInterfaceImplementations.Length != 0 || declaration?.ExplicitInterfaceSpecifier is not null)
      return "an explicit-interface property";
    if (property.RefKind != RefKind.None) return "a ref-return property";
    if (declaration is null || !declaration.Modifiers.Any(SyntaxKind.PartialKeyword)) return "non-partial";
    if (property.SetMethod is not null) return "a setter";
    if (HasAuthoredPartialImplementation(property)) return "an authored implementation";
    if (declaration.ExpressionBody is not null || declaration.AccessorList?.Accessors.Any(accessor =>
            accessor.Body is not null || accessor.ExpressionBody is not null) == true)
      return "an authored implementation";
    if (property.GetMethod is null) return "getter-less";
    if (property.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
      return "not public or internal";
    foreach (var modifier in declaration.Modifiers)
    {
      if (modifier.IsKind(SyntaxKind.PartialKeyword) ||
          modifier.IsKind(SyntaxKind.PublicKeyword) ||
          modifier.IsKind(SyntaxKind.InternalKeyword))
      {
        continue;
      }
      return $"modified with '{modifier.Text}'";
    }
    if (property.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == JSAttributeMetadataName))
      return "also marked [JS]";
    return null;
  }

  private static bool CanReproduceEventProperty(
      IPropertySymbol property,
      PropertyDeclarationSyntax? declaration)
  {
    if (HasAuthoredPartialImplementation(property) ||
        property.IsIndexer ||
        property.ExplicitInterfaceImplementations.Length != 0 ||
        property.RefKind != RefKind.None ||
        declaration is null ||
        !declaration.Modifiers.Any(SyntaxKind.PartialKeyword) ||
        declaration.ExpressionBody is not null ||
        declaration.AccessorList?.Accessors.Any(accessor =>
            accessor.Body is not null || accessor.ExpressionBody is not null) != false)
    {
      return false;
    }

    return !declaration.Modifiers.Any(modifier =>
        modifier.IsKind(SyntaxKind.AbstractKeyword) || modifier.IsKind(SyntaxKind.ExternKeyword));
  }

  private static bool HasAuthoredPartialImplementation(IPropertySymbol property) =>
      property.PartialImplementationPart is not null ||
      property.PartialDefinitionPart?.PartialImplementationPart is not null;

  private static string GetEventDeclarationModifiers(
      PropertyDeclarationSyntax? declaration,
      IPropertySymbol property) => declaration is null
      ? $"{GetAccessibilityText(property.DeclaredAccessibility)} partial"
      : string.Join(" ", declaration.Modifiers.Select(modifier => modifier.Text));

  private static string GetEventAccessorText(
      PropertyDeclarationSyntax? declaration,
      params SyntaxKind[] kinds)
  {
    var accessor = declaration?.AccessorList?.Accessors.FirstOrDefault(item => kinds.Contains(item.Kind()));
    return accessor is null
        ? string.Empty
        : string.Join(" ", accessor.Modifiers.Select(modifier => modifier.Text).Append(accessor.Keyword.Text));
  }

  private static string GetAccessibilityText(Accessibility accessibility) => accessibility switch
  {
    Accessibility.Public => "public",
    Accessibility.Internal => "internal",
    Accessibility.Private => "private",
    Accessibility.Protected => "protected",
    Accessibility.ProtectedOrInternal => "protected internal",
    Accessibility.ProtectedAndInternal => "private protected",
    _ => "private",
  };

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

  private static void EmitProvider(
      SourceProductionContext context,
      string assemblyName,
      IEnumerable<ExpoModuleModel> modules,
      IEnumerable<ExpoSharedObjectModel> sharedObjects)
  {
    var sharedObjectModels = sharedObjects.ToArray();
    foreach (var diagnostic in sharedObjectModels.SelectMany(sharedObject => sharedObject.Diagnostics.Values))
    {
      context.ReportDiagnostic(ToDiagnostic(diagnostic));
    }

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

    // The owning module's full JavaScript namespace is validated across the whole
    // compilation before any module is emitted.
    var ownershipDiagnostics = ValidateSharedObjectOwnership(
        moduleModels,
        sharedObjectModels,
        out var emittableClassesByModuleName);
    foreach (var diagnostic in ownershipDiagnostics)
    {
      context.ReportDiagnostic(ToDiagnostic(diagnostic));
    }

    foreach (var module in moduleModels)
    {
      EmitEventPartial(context, module);
    }
    foreach (var sharedObject in sharedObjectModels.Where(sharedObject => sharedObject.IsValid))
    {
      EmitSharedObjectEventPartial(context, sharedObject);
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

    IReadOnlyList<ExpoSharedObjectModel> GetEmittableClasses(ExpoModuleModel module) =>
        emittableClassesByModuleName.TryGetValue(module.ModuleName, out var classes)
            ? classes
            : Array.Empty<ExpoSharedObjectModel>();
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
    var emittedRecordCodecNames = new HashSet<string>(StringComparer.Ordinal);
    foreach (var recordCodec in moduleModels
                 .SelectMany(module => module.RecordCodecs.Values)
                 .Concat(moduleModels.SelectMany(module =>
                     GetEmittableClasses(module).SelectMany(sharedObject => sharedObject.RecordCodecs.Values))))
    {
      if (emittedRecordCodecNames.Add(recordCodec.CodecTypeName))
      {
        EmitRecordCodec(builder, recordCodec);
      }
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
      EmitModuleRegistrationFunction(builder, module, GetEmittableClasses(module));
    }
    foreach (var module in moduleModels)
    {
      EmitTypedEventProviderHelpers(builder, module);
    }
    foreach (var module in moduleModels)
    {
      foreach (var function in module.Functions.Values)
      {
        EmitHostFunction(builder, module, function);
      }
      foreach (var property in module.Properties.Values)
      {
        EmitPropertyGetter(builder, module, property);
        if (property.HasSetter)
        {
          EmitPropertySetter(builder, module, property);
        }
      }
      if (module.StartObservingHooks.Values.Count > 0)
      {
        EmitObservingHookFunction(builder, module, "startObserving", module.StartObservingHooks.Values);
      }
      if (module.StopObservingHooks.Values.Count > 0)
      {
        EmitObservingHookFunction(builder, module, "stopObserving", module.StopObservingHooks.Values);
      }
      foreach (var sharedObject in GetEmittableClasses(module))
      {
        EmitSharedObjectClassGlue(builder, module, sharedObject);
      }
    }
    builder.AppendLine("}");

    context.AddSource($"{providerTypeName}.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
  }

  /// <summary>
  /// Names one emitted shared-object member host function: its generated method name, the
  /// diagnostic label used in argument-count errors, and the exact receiver type resolved through
  /// <c>SharedObjectCodec&lt;T&gt;</c> before authored code runs.
  /// </summary>
  private sealed record SharedObjectHostTarget(
      string HostFunctionName,
      string Label,
      string ReceiverTypeName);

  private static string GetSharedObjectIdentifier(
      ExpoModuleModel module,
      ExpoSharedObjectModel sharedObject) =>
      $"{SanitizeIdentifier(module.ModuleName)}_{SanitizeIdentifier(sharedObject.JavaScriptClassName)}";

  private static string GetSharedObjectFactoryName(
      ExpoModuleModel module,
      ExpoSharedObjectModel sharedObject) =>
      $"ConstructSharedObject_{GetSharedObjectIdentifier(module, sharedObject)}";

  private static string GetSharedObjectMemberInstallerName(
      ExpoModuleModel module,
      ExpoSharedObjectModel sharedObject) =>
      $"InstallSharedObjectMembers_{GetSharedObjectIdentifier(module, sharedObject)}";

  private static string GetSharedObjectFunctionName(
      ExpoModuleModel module,
      ExpoSharedObjectModel sharedObject,
      ExpoFunctionModel function) =>
      $"InvokeSharedObject_{GetSharedObjectIdentifier(module, sharedObject)}_{SanitizeIdentifier(function.JavaScriptName)}";

  private static string GetSharedObjectPropertyGetterName(
      ExpoModuleModel module,
      ExpoSharedObjectModel sharedObject,
      ExpoPropertyModel property) =>
      $"GetSharedObjectProperty_{GetSharedObjectIdentifier(module, sharedObject)}_{SanitizeIdentifier(property.JavaScriptName)}";

  private static string GetSharedObjectPropertySetterName(
      ExpoModuleModel module,
      ExpoSharedObjectModel sharedObject,
      ExpoPropertyModel property) =>
      $"SetSharedObjectProperty_{GetSharedObjectIdentifier(module, sharedObject)}_{SanitizeIdentifier(property.JavaScriptName)}";

  private static string GetSharedObjectEventInitializerName(
      ExpoModuleModel module,
      ExpoSharedObjectModel sharedObject) =>
      $"InitializeSharedObjectEvents_{GetSharedObjectIdentifier(module, sharedObject)}";

  private static void EmitSharedObjectClassGlue(
      StringBuilder builder,
      ExpoModuleModel module,
      ExpoSharedObjectModel sharedObject)
  {
    if (sharedObject.Constructor is not null)
    {
      EmitSharedObjectFactory(builder, module, sharedObject);
    }

    if (sharedObject.Events.Values.Any(@event => @event.IsDispatchable))
    {
      EmitSharedObjectEventProviderHelper(builder, module, sharedObject);
    }

    var hasMembers = sharedObject.Functions.Values.Count > 0 ||
        sharedObject.Properties.Values.Count > 0 ||
        sharedObject.Events.Values.Any(@event => @event.IsDispatchable);
    if (hasMembers)
    {
      EmitSharedObjectMemberInstaller(builder, module, sharedObject);
    }

    foreach (var function in sharedObject.Functions.Values)
    {
      EmitHostFunction(
          builder,
          module,
          function,
          new SharedObjectHostTarget(
              GetSharedObjectFunctionName(module, sharedObject, function),
              sharedObject.JavaScriptClassName,
              sharedObject.FullyQualifiedTypeName
          )
      );
    }
    foreach (var property in sharedObject.Properties.Values)
    {
      EmitPropertyGetter(
          builder,
          module,
          property,
          new SharedObjectHostTarget(
              GetSharedObjectPropertyGetterName(module, sharedObject, property),
              sharedObject.JavaScriptClassName,
              sharedObject.FullyQualifiedTypeName
          )
      );
      if (property.HasSetter)
      {
        EmitPropertySetter(
            builder,
            module,
            property,
            new SharedObjectHostTarget(
                GetSharedObjectPropertySetterName(module, sharedObject, property),
                sharedObject.JavaScriptClassName,
                sharedObject.FullyQualifiedTypeName
            )
        );
      }
    }
  }

  private static void EmitSharedObjectEventProviderHelper(
      StringBuilder builder,
      ExpoModuleModel module,
      ExpoSharedObjectModel sharedObject)
  {
    var events = sharedObject.Events.Values.Where(@event => @event.IsDispatchable).ToArray();
    builder.AppendLine();
    builder.AppendLine($"  private static void {GetSharedObjectEventInitializerName(module, sharedObject)}(");
    builder.AppendLine("      global::Expo.ModulesCore.DotnetRuntimeContext context,");
    builder.AppendLine($"      {sharedObject.FullyQualifiedTypeName} sharedObject)");
    builder.AppendLine("  {");
    builder.AppendLine("    sharedObject.__ExpoModulesCoreInitializeSharedObjectEvents(");
    builder.AppendLine("        context,");
    for (var index = 0; index < events.Length; index++)
    {
      var @event = events[index];
      var valueParameter = GetEventValueParameterName(@event);
      var emitExpression = @event.PayloadKind switch
      {
        ExpoEventPayloadKind.None => $"() => global::Expo.ModulesCore.GeneratedSharedObjectEvents.EmitAsync(context, sharedObject, \"{EscapeString(@event.JavaScriptName)}\")",
        ExpoEventPayloadKind.Codec => $"{valueParameter} => global::Expo.ModulesCore.GeneratedSharedObjectEvents.EmitAsync<{@event.CodecExpression}, {@event.PayloadTypeName}>(context, sharedObject, \"{EscapeString(@event.JavaScriptName)}\", {valueParameter})",
        ExpoEventPayloadKind.JavaScriptValue or ExpoEventPayloadKind.ArrayBuffer => $"{valueParameter} => global::Expo.ModulesCore.GeneratedSharedObjectEvents.EmitAsync(context, sharedObject, \"{EscapeString(@event.JavaScriptName)}\", {valueParameter})",
        _ => throw new InvalidOperationException($"Unknown event payload kind: {@event.PayloadKind}"),
      };
      builder.AppendLine($"        {emitExpression}{(index == events.Length - 1 ? ");" : ",")}");
    }
    builder.AppendLine("  }");
  }

  private static void EmitSharedObjectFactory(
      StringBuilder builder,
      ExpoModuleModel module,
      ExpoSharedObjectModel sharedObject)
  {
    var constructor = sharedObject.Constructor!;
    var parameters = constructor.Parameters.Values;
    var requiredCount = parameters.Count(parameter => !parameter.HasDefaultValue);

    builder.AppendLine();
    builder.AppendLine($"  private static global::Expo.ModulesCore.SharedObject {GetSharedObjectFactoryName(module, sharedObject)}(");
    builder.AppendLine("      global::Expo.JSI.JavaScriptRuntime runtime,");
    builder.AppendLine("      global::Expo.JSI.JavaScriptArrayRef arguments,");
    builder.AppendLine("      object context)");
    builder.AppendLine("  {");
    builder.AppendLine($"    GeneratedSharedObjectClass.RequireArgumentCount(\"{EscapeString(sharedObject.JavaScriptClassName)}\", arguments, {requiredCount}, {parameters.Count});");
    for (var index = 0; index < parameters.Count; index++)
    {
      var parameter = parameters[index];
      var decodeExpression = GetDecodeExpression(
          parameter.CodecExpression,
          index,
          "runtime",
          parameter.RequiresRuntimeContext
      );
      if (parameter.HasDefaultValue)
      {
        decodeExpression =
            $"arguments.Length <= {index} || arguments.GetValue({index}).Kind == global::Expo.JSI.JavaScriptValueKind.Undefined ? {parameter.DefaultValueExpression} : {decodeExpression}";
      }
      var declaration = parameter.OwnsDecodedValue ? "using var" : "var";
      builder.AppendLine($"    {declaration} {GetParameterLocalName(index)} = {decodeExpression};");
    }

    var argumentList = string.Join(
        ", ",
        parameters.Select((_, index) => GetParameterLocalName(index))
    );
    builder.AppendLine($"    return new {sharedObject.FullyQualifiedTypeName}({argumentList});");
    builder.AppendLine("  }");
  }

  private static void EmitSharedObjectMemberInstaller(
      StringBuilder builder,
      ExpoModuleModel module,
      ExpoSharedObjectModel sharedObject)
  {
    builder.AppendLine();
    builder.AppendLine($"  private static void {GetSharedObjectMemberInstallerName(module, sharedObject)}(");
    builder.AppendLine("      global::Expo.ModulesCore.DotnetRuntimeContext context,");
    builder.AppendLine("      global::Expo.JSI.JavaScriptObject prototype)");
    builder.AppendLine("  {");
    if (sharedObject.Events.Values.Any(@event => @event.IsDispatchable))
    {
      builder.AppendLine("    global::Expo.ModulesCore.GeneratedSharedObjectEvents.InstallPrototype(context, prototype);");
    }
    foreach (var function in sharedObject.Functions.Values)
    {
      builder.AppendLine(function.IsAsync
          ? "    GeneratedFunction.DefineAsync("
          : "    GeneratedFunction.DefineSync(");
      builder.AppendLine("        context,");
      builder.AppendLine("        prototype,");
      builder.AppendLine($"        \"{EscapeString(function.JavaScriptName)}\",");
      builder.AppendLine($"        {GetRequiredParameterCount(function)},");
      builder.AppendLine($"        {GetSharedObjectFunctionName(module, sharedObject, function)},");
      builder.AppendLine($"        typeof({sharedObject.FullyQualifiedTypeName})");
      builder.AppendLine("    );");
    }
    foreach (var property in sharedObject.Properties.Values)
    {
      builder.AppendLine("    GeneratedProperty.Define(");
      builder.AppendLine("        context,");
      builder.AppendLine("        prototype,");
      builder.AppendLine($"        \"{EscapeString(property.JavaScriptName)}\",");
      builder.AppendLine($"        {GetSharedObjectPropertyGetterName(module, sharedObject, property)},");
      builder.AppendLine(property.HasSetter
          ? $"        {GetSharedObjectPropertySetterName(module, sharedObject, property)},"
          : "        null,");
      builder.AppendLine($"        typeof({sharedObject.FullyQualifiedTypeName})");
      builder.AppendLine("    );");
    }
    builder.AppendLine("  }");
  }

  private static List<ExpoDiagnosticModel> ValidateSharedObjectOwnership(
      IReadOnlyList<ExpoModuleModel> moduleModels,
      IReadOnlyList<ExpoSharedObjectModel> sharedObjectModels,
      out Dictionary<string, List<ExpoSharedObjectModel>> emittableClassesByModuleName)
  {
    var diagnostics = new List<ExpoDiagnosticModel>();
    var excludedTypeNames = new HashSet<string>(StringComparer.Ordinal);

    ExpoDiagnosticModel CreateOwnershipDiagnostic(string typeName, string reason, Location? location) =>
        new(
            ExpoModulesDiagnostics.InvalidSharedObjectOwnership.Id,
            location,
            new EquatableArray<string>(new[] { typeName, reason })
        );

    var sharedObjectsByTypeName = new Dictionary<string, ExpoSharedObjectModel>(StringComparer.Ordinal);
    foreach (var sharedObject in sharedObjectModels)
    {
      if (!sharedObjectsByTypeName.ContainsKey(sharedObject.FullyQualifiedTypeName))
      {
        sharedObjectsByTypeName.Add(sharedObject.FullyQualifiedTypeName, sharedObject);
      }
    }

    var ownersByTypeName = new Dictionary<string, List<ExpoModuleModel>>(StringComparer.Ordinal);
    var moduleOwnedClasses = new List<(ExpoModuleModel Module, List<ExpoSharedObjectModel> OwnedClasses)>();
    foreach (var module in moduleModels)
    {
      var ownedClasses = new List<ExpoSharedObjectModel>();
      var seenEntryTypeNames = new HashSet<string>(StringComparer.Ordinal);
      foreach (var entry in module.SharedObjectClasses.Values)
      {
        if (!sharedObjectsByTypeName.TryGetValue(entry.TypeName, out var sharedObject))
        {
          diagnostics.Add(CreateOwnershipDiagnostic(
              GetShortTypeName(entry.TypeName),
              $"module '{module.ModuleName}' lists it in Classes, but it is not an [ExpoSharedObject] class deriving from SharedObject",
              entry.Location ?? module.Location
          ));
          continue;
        }

        if (!seenEntryTypeNames.Add(entry.TypeName))
        {
          diagnostics.Add(CreateOwnershipDiagnostic(
              sharedObject.SimpleTypeName,
              $"module '{module.ModuleName}' lists it more than once in Classes",
              entry.Location ?? module.Location
          ));
          continue;
        }

        if (!ownersByTypeName.TryGetValue(entry.TypeName, out var owners))
        {
          owners = new List<ExpoModuleModel>();
          ownersByTypeName.Add(entry.TypeName, owners);
        }
        owners.Add(module);
        if (sharedObject.IsValid)
        {
          ownedClasses.Add(sharedObject);
        }
      }
      moduleOwnedClasses.Add((module, ownedClasses));
    }

    foreach (var sharedObject in sharedObjectModels)
    {
      ownersByTypeName.TryGetValue(sharedObject.FullyQualifiedTypeName, out var owners);
      if (owners is { Count: > 1 })
      {
        excludedTypeNames.Add(sharedObject.FullyQualifiedTypeName);
        diagnostics.Add(CreateOwnershipDiagnostic(
            sharedObject.SimpleTypeName,
            $"it is listed in Classes by multiple modules ({string.Join(", ", owners.Select(owner => $"'{owner.ModuleName}'"))})",
            sharedObject.Location
        ));
      }
      else if (owners is null or { Count: 0 } && sharedObject.IsValid)
      {
        diagnostics.Add(CreateOwnershipDiagnostic(
            sharedObject.SimpleTypeName,
            "no module lists it in [ExpoModule(Classes = ...)]",
            sharedObject.Location
        ));
      }
    }

    foreach (var (module, ownedClasses) in moduleOwnedClasses)
    {
      // Effective class names stay unique for every owned class, including
      // native-created-only classes, because the generated prototype and codec
      // identity table is keyed by those names.
      var classesByJavaScriptName = new Dictionary<string, ExpoSharedObjectModel>(StringComparer.Ordinal);
      foreach (var sharedObject in ownedClasses)
      {
        if (classesByJavaScriptName.TryGetValue(sharedObject.JavaScriptClassName, out var existingClass))
        {
          excludedTypeNames.Add(sharedObject.FullyQualifiedTypeName);
          diagnostics.Add(CreateOwnershipDiagnostic(
              sharedObject.SimpleTypeName,
              $"its JavaScript class name '{sharedObject.JavaScriptClassName}' is already used by class '{existingClass.SimpleTypeName}' in module '{module.ModuleName}'",
              sharedObject.Location
          ));
          continue;
        }
        classesByJavaScriptName.Add(sharedObject.JavaScriptClassName, sharedObject);
      }

      var namespaceSurfaces = GetModuleNamespaceSurfaces(module);
      foreach (var sharedObject in ownedClasses)
      {
        if (sharedObject.Constructor is null)
        {
          continue;
        }
        if (namespaceSurfaces.TryGetValue(sharedObject.JavaScriptClassName, out var surface))
        {
          excludedTypeNames.Add(sharedObject.FullyQualifiedTypeName);
          diagnostics.Add(CreateOwnershipDiagnostic(
              sharedObject.SimpleTypeName,
              $"its JavaScript class name '{sharedObject.JavaScriptClassName}' collides with {surface} on module '{module.ModuleName}'",
              sharedObject.Location
          ));
        }
      }
    }

    // Only classes that passed every ownership and namespace rule are emitted; a class with any
    // EXPOJSI024 finding produces its diagnostic and no binding glue.
    emittableClassesByModuleName = new Dictionary<string, List<ExpoSharedObjectModel>>(StringComparer.Ordinal);
    foreach (var (module, ownedClasses) in moduleOwnedClasses)
    {
      var emittable = ownedClasses
          .Where(sharedObject =>
              !excludedTypeNames.Contains(sharedObject.FullyQualifiedTypeName) &&
              ownersByTypeName.TryGetValue(sharedObject.FullyQualifiedTypeName, out var classOwners) &&
              classOwners.Count == 1)
          .ToList();
      if (emittable.Count > 0 && !emittableClassesByModuleName.ContainsKey(module.ModuleName))
      {
        emittableClassesByModuleName.Add(module.ModuleName, emittable);
      }
    }

    return diagnostics;
  }

  private static Dictionary<string, string> GetModuleNamespaceSurfaces(ExpoModuleModel module)
  {
    var surfaces = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var function in module.Functions.Values)
    {
      surfaces[function.JavaScriptName] = $"generated function '{function.JavaScriptName}'";
    }
    foreach (var property in module.Properties.Values)
    {
      surfaces[property.JavaScriptName] = $"generated property '{property.JavaScriptName}'";
    }

    var hasEvents = module.EventNames.Values.Count > 0;
    if (hasEvents ||
        module.StartObservingHooks.Values.Count > 0 ||
        module.StopObservingHooks.Values.Count > 0)
    {
      surfaces["startObserving"] = "the observing hook 'startObserving'";
      surfaces["stopObserving"] = "the observing hook 'stopObserving'";
    }
    if (hasEvents)
    {
      foreach (var name in new[] { "addListener", "removeListener", "removeAllListeners", "emit", "listenerCount" })
      {
        surfaces[name] = $"the reserved event-runtime member '{name}'";
      }
    }
    return surfaces;
  }

  private static string GetShortTypeName(string fullyQualifiedTypeName) =>
      fullyQualifiedTypeName.StartsWith("global::", StringComparison.Ordinal)
          ? fullyQualifiedTypeName.Substring("global::".Length)
          : fullyQualifiedTypeName;

  private static void EmitModuleRegistrationFunction(
      StringBuilder builder,
      ExpoModuleModel module,
      IReadOnlyList<ExpoSharedObjectModel> ownedClasses)
  {
    var moduleVariable = $"module_{SanitizeIdentifier(module.ModuleName)}";
    var moduleInstanceVariable = $"instance_{SanitizeIdentifier(module.ModuleName)}";
    var hasEvents = module.EventNames.Values.Count > 0;
    var typedEvents = module.Events.Values.Where(@event => @event.IsDispatchable).ToArray();
    var factoryExpression = typedEvents.Length > 0
        ? $"() => {GetEventFactoryName(module)}(context)"
        : module.ConstructorStrategy == ExpoModuleConstructorStrategy.RuntimeContext
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
    if (typedEvents.Length > 0)
    {
      builder.AppendLine($"      {GetEventInitializerName(module)}(context, {moduleInstanceVariable});");
    }
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
    foreach (var property in module.Properties.Values)
    {
      builder.AppendLine("      GeneratedProperty.Define(");
      builder.AppendLine("          context,");
      builder.AppendLine($"          {moduleVariable},");
      builder.AppendLine($"          \"{EscapeString(property.JavaScriptName)}\",");
      builder.AppendLine($"          {GetPropertyGetterFunctionName(module, property)},");
      builder.AppendLine(property.HasSetter
          ? $"          {GetPropertySetterFunctionName(module, property)},"
          : "          null,");
      builder.AppendLine($"          {moduleInstanceVariable}");
      builder.AppendLine("      );");
    }
    foreach (var sharedObject in ownedClasses)
    {
      var hasMembers = sharedObject.Functions.Values.Count > 0 ||
          sharedObject.Properties.Values.Count > 0 ||
          sharedObject.Events.Values.Any(@event => @event.IsDispatchable);
      builder.AppendLine("      GeneratedSharedObjectClass.Install(");
      builder.AppendLine("          context,");
      builder.AppendLine($"          {moduleVariable},");
      builder.AppendLine($"          typeof({sharedObject.FullyQualifiedTypeName}),");
      builder.AppendLine($"          \"{EscapeString(sharedObject.JavaScriptClassName)}\",");
      builder.AppendLine($"          {(sharedObject.Constructor?.Parameters.Values.Count ?? 0)},");
      builder.AppendLine(sharedObject.Constructor is null
          ? "          null,"
          : $"          {GetSharedObjectFactoryName(module, sharedObject)},");
      builder.AppendLine(hasMembers
          ? $"          {GetSharedObjectMemberInstallerName(module, sharedObject)},"
          : "          null,");
      builder.AppendLine(sharedObject.Events.Values.Any(@event => @event.IsDispatchable)
          ? $"          static (context, sharedObject) => {GetSharedObjectEventInitializerName(module, sharedObject)}(context, ({sharedObject.FullyQualifiedTypeName})sharedObject)"
          : "          null");
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

  private static void EmitEventPartial(SourceProductionContext context, ExpoModuleModel module)
  {
    var events = module.Events.Values.Where(@event => @event.IsShapeValid).ToArray();
    if (events.Length == 0)
    {
      return;
    }

    var dispatchableEvents = events.Where(@event => @event.IsDispatchable).ToArray();
    var builder = new StringBuilder();
    builder.AppendLine("// <auto-generated/>");
    builder.AppendLine("#nullable enable");
    if (module.Namespace.Length > 0)
    {
      builder.AppendLine($"namespace {module.Namespace};");
      builder.AppendLine();
    }
    builder.AppendLine($"{module.Accessibility} partial class {EscapeIdentifier(module.SimpleTypeName)}");
    builder.AppendLine("{");
    if (dispatchableEvents.Length > 0)
    {
      builder.AppendLine("  private readonly object __expoEventInitializationGate = new();");
      builder.AppendLine("  private global::Expo.ModulesCore.DotnetRuntimeContext? __expoEventContext;");
    }
    foreach (var @event in dispatchableEvents)
    {
      builder.AppendLine($"  private {(@event.IsStatic ? "static " : string.Empty)}{@event.DelegateTypeName}? {GetEventBackingFieldName(@event)};");
    }
    foreach (var @event in events)
    {
      builder.AppendLine();
      builder.AppendLine($"  {@event.DeclarationModifiers} {@event.DelegateTypeName} {EscapeIdentifier(@event.PropertyName)}");
      builder.AppendLine("  {");
      if (@event.GetterAccessor.Length > 0)
      {
        var getterExpression = @event.IsDispatchable
            ? $"{GetEventBackingFieldName(@event)} ?? throw new global::System.InvalidOperationException(\"Event member '{module.SimpleTypeName}.{@event.PropertyName}' is unavailable before module registration.\")"
            : $"throw new global::System.InvalidOperationException(\"Event member '{module.SimpleTypeName}.{@event.PropertyName}' cannot be used because its declaration is invalid.\")";
        builder.AppendLine($"    {@event.GetterAccessor} => {getterExpression};");
      }
      if (@event.SetterAccessor.Length > 0)
      {
        builder.AppendLine($"    {@event.SetterAccessor} => throw new global::System.InvalidOperationException(\"Event member '{module.SimpleTypeName}.{@event.PropertyName}' cannot be assigned.\");");
      }
      builder.AppendLine("  }");
    }
    if (dispatchableEvents.Length > 0)
    {
      builder.AppendLine();
      builder.AppendLine("  internal void __ExpoModulesCoreInitializeEvents(");
      builder.AppendLine("      global::Expo.ModulesCore.DotnetRuntimeContext context,");
      for (var index = 0; index < dispatchableEvents.Length; index++)
      {
        var @event = dispatchableEvents[index];
        builder.AppendLine($"      {@event.DelegateTypeName} {GetEventParameterName(@event)}{(index == dispatchableEvents.Length - 1 ? ")" : ",")}");
      }
      builder.AppendLine("  {");
      builder.AppendLine("    lock (__expoEventInitializationGate)");
      builder.AppendLine("    {");
      builder.AppendLine("      if (__expoEventContext is not null)");
      builder.AppendLine("      {");
      builder.AppendLine("        if (!global::System.Object.ReferenceEquals(__expoEventContext, context))");
      builder.AppendLine("          throw new global::System.InvalidOperationException(\"Module event members cannot be rebound to a different runtime context.\");");
      builder.AppendLine("        return;");
      builder.AppendLine("      }");
      foreach (var @event in dispatchableEvents)
      {
        builder.AppendLine($"      {GetEventBackingFieldName(@event)} = {GetEventParameterName(@event)} ?? throw new global::System.ArgumentNullException(nameof({GetEventParameterName(@event)}));");
      }
      builder.AppendLine("      __expoEventContext = context ?? throw new global::System.ArgumentNullException(nameof(context));");
      builder.AppendLine("    }");
      builder.AppendLine("  }");
    }
    builder.AppendLine("}");
    context.AddSource(GetEventHintName(module), SourceText.From(builder.ToString(), Encoding.UTF8));
  }

  private static void EmitSharedObjectEventPartial(
      SourceProductionContext context,
      ExpoSharedObjectModel sharedObject)
  {
    var events = sharedObject.Events.Values.Where(@event => @event.IsShapeValid).ToArray();
    if (events.Length == 0)
    {
      return;
    }

    var dispatchableEvents = events.Where(@event => @event.IsDispatchable).ToArray();
    var builder = new StringBuilder();
    builder.AppendLine("// <auto-generated/>");
    builder.AppendLine("#nullable enable");
    if (sharedObject.Namespace.Length > 0)
    {
      builder.AppendLine($"namespace {sharedObject.Namespace};");
      builder.AppendLine();
    }
    builder.AppendLine($"{sharedObject.Accessibility} partial class {EscapeIdentifier(sharedObject.SimpleTypeName)}");
    builder.AppendLine("{");
    if (dispatchableEvents.Length > 0)
    {
      builder.AppendLine("  private readonly object __expoSharedObjectEventInitializationGate = new();");
      builder.AppendLine("  private global::Expo.ModulesCore.DotnetRuntimeContext? __expoSharedObjectEventContext;");
    }
    foreach (var @event in dispatchableEvents)
    {
      builder.AppendLine($"  private {@event.DelegateTypeName}? __expoSharedObjectEvent_{SanitizeIdentifier(@event.PropertyName)};");
    }
    foreach (var @event in events)
    {
      builder.AppendLine();
      builder.AppendLine($"  {@event.DeclarationModifiers} {@event.DelegateTypeName} {EscapeIdentifier(@event.PropertyName)}");
      builder.AppendLine("  {");
      if (@event.GetterAccessor.Length > 0)
      {
        var getterExpression = @event.IsDispatchable
            ? $"__expoSharedObjectEvent_{SanitizeIdentifier(@event.PropertyName)} ?? throw new global::System.InvalidOperationException(\"Event member '{sharedObject.SimpleTypeName}.{@event.PropertyName}' is unavailable before shared-object pairing.\")"
            : $"throw new global::System.InvalidOperationException(\"Event member '{sharedObject.SimpleTypeName}.{@event.PropertyName}' cannot be used because its declaration is invalid.\")";
        builder.AppendLine($"    {@event.GetterAccessor} => {getterExpression};");
      }
      if (@event.SetterAccessor.Length > 0)
      {
        builder.AppendLine($"    {@event.SetterAccessor} => throw new global::System.InvalidOperationException(\"Event member '{sharedObject.SimpleTypeName}.{@event.PropertyName}' cannot be assigned.\");");
      }
      builder.AppendLine("  }");
    }
    if (dispatchableEvents.Length > 0)
    {
      builder.AppendLine();
      builder.AppendLine("  internal void __ExpoModulesCoreInitializeSharedObjectEvents(");
      builder.AppendLine("      global::Expo.ModulesCore.DotnetRuntimeContext context,");
      for (var index = 0; index < dispatchableEvents.Length; index++)
      {
        var @event = dispatchableEvents[index];
        builder.AppendLine($"      {@event.DelegateTypeName} {GetEventParameterName(@event)}{(index == dispatchableEvents.Length - 1 ? ")" : ",")}");
      }
      builder.AppendLine("  {");
      builder.AppendLine("    lock (__expoSharedObjectEventInitializationGate)");
      builder.AppendLine("    {");
      builder.AppendLine("      if (__expoSharedObjectEventContext is not null)");
      builder.AppendLine("      {");
      builder.AppendLine("        if (!global::System.Object.ReferenceEquals(__expoSharedObjectEventContext, context))");
      builder.AppendLine("          throw new global::System.InvalidOperationException(\"Shared-object event members cannot be rebound to a different runtime context.\");");
      builder.AppendLine("        return;");
      builder.AppendLine("      }");
      builder.AppendLine($"      global::Expo.ModulesCore.GeneratedSharedObjectEvents.Attach(context, this, new[] {{ {string.Join(", ", dispatchableEvents.Select(@event => $"\"{EscapeString(@event.JavaScriptName)}\""))} }});");
      foreach (var @event in dispatchableEvents)
      {
        builder.AppendLine($"      __expoSharedObjectEvent_{SanitizeIdentifier(@event.PropertyName)} = {GetEventParameterName(@event)} ?? throw new global::System.ArgumentNullException(nameof({GetEventParameterName(@event)}));");
      }
      builder.AppendLine("      __expoSharedObjectEventContext = context ?? throw new global::System.ArgumentNullException(nameof(context));");
      builder.AppendLine("    }");
      builder.AppendLine("  }");
    }
    builder.AppendLine("}");
    context.AddSource(
        $"{SanitizeIdentifier(sharedObject.FullyQualifiedTypeName)}_{GetStableHash(sharedObject.FullyQualifiedTypeName):X8}.SharedObjectEvents.g.cs",
        SourceText.From(builder.ToString(), Encoding.UTF8)
    );
  }

  private static void EmitTypedEventProviderHelpers(StringBuilder builder, ExpoModuleModel module)
  {
    var events = module.Events.Values.Where(@event => @event.IsDispatchable).ToArray();
    if (events.Length == 0)
    {
      return;
    }
    builder.AppendLine();
    builder.AppendLine($"  private static {module.FullyQualifiedTypeName} {GetEventFactoryName(module)}(global::Expo.ModulesCore.DotnetRuntimeContext context)");
    builder.AppendLine("  {");
    var creation = module.ConstructorStrategy == ExpoModuleConstructorStrategy.RuntimeContext
        ? $"new {module.FullyQualifiedTypeName}(context)"
        : $"new {module.FullyQualifiedTypeName}()";
    builder.AppendLine($"    var module = {creation};");
    builder.AppendLine($"    {GetEventInitializerName(module)}(context, module);");
    builder.AppendLine("    return module;");
    builder.AppendLine("  }");
    builder.AppendLine();
    builder.AppendLine($"  private static void {GetEventInitializerName(module)}(");
    builder.AppendLine("      global::Expo.ModulesCore.DotnetRuntimeContext context,");
    builder.AppendLine($"      {module.FullyQualifiedTypeName} module)");
    builder.AppendLine("  {");
    builder.AppendLine("    var emitter = context.Events;");
    builder.AppendLine("    module.__ExpoModulesCoreInitializeEvents(");
    builder.AppendLine("        context,");
    for (var index = 0; index < events.Length; index++)
    {
      var @event = events[index];
      builder.AppendLine($"        {GetEventDelegateExpression(@event)}{(index == events.Length - 1 ? ");" : ",")}");
    }
    builder.AppendLine("  }");
  }

  private static string GetEventDelegateExpression(ExpoEventModel @event) => @event.PayloadKind switch
  {
    ExpoEventPayloadKind.None => $"() => emitter.EmitAsync(module, \"{EscapeString(@event.JavaScriptName)}\")",
    ExpoEventPayloadKind.Codec => $"{GetEventValueParameterName(@event)} => emitter.EmitAsync<{@event.CodecExpression}, {@event.PayloadTypeName}>(module, \"{EscapeString(@event.JavaScriptName)}\", {GetEventValueParameterName(@event)})",
    ExpoEventPayloadKind.JavaScriptValue or ExpoEventPayloadKind.ArrayBuffer =>
        $"{GetEventValueParameterName(@event)} => emitter.EmitAsync(module, \"{EscapeString(@event.JavaScriptName)}\", {GetEventValueParameterName(@event)})",
    _ => throw new InvalidOperationException($"Unknown event payload kind: {@event.PayloadKind}"),
  };

  private static string GetEventFactoryName(ExpoModuleModel module) =>
      $"Create{SanitizeIdentifier(module.ModuleName)}";

  private static string GetEventInitializerName(ExpoModuleModel module) =>
      $"Initialize{SanitizeIdentifier(module.ModuleName)}Events";

  private static string GetEventBackingFieldName(ExpoEventModel @event) =>
      $"__expoEvent_{SanitizeIdentifier(@event.PropertyName)}";

  private static string GetEventParameterName(ExpoEventModel @event) =>
      $"on{SanitizeIdentifier(@event.PropertyName)}";

  private static string GetEventValueParameterName(ExpoEventModel @event) =>
      $"{LowerCamel(SanitizeIdentifier(@event.PropertyName))}Value";

  private static string GetEventHintName(ExpoModuleModel module) =>
      $"{SanitizeIdentifier(module.FullyQualifiedTypeName)}_{GetStableHash(module.FullyQualifiedTypeName):X8}.Events.g.cs";

  private static uint GetStableHash(string value)
  {
    const uint offsetBasis = 2166136261;
    const uint prime = 16777619;
    var hash = offsetBasis;
    foreach (var character in value)
    {
      hash = (hash ^ character) * prime;
    }
    return hash;
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
      "EXPOJSI014" => ExpoModulesDiagnostics.UnsupportedJSPropertyShape,
      "EXPOJSI015" => ExpoModulesDiagnostics.UnsupportedJSPropertyType,
      "EXPOJSI016" => ExpoModulesDiagnostics.DuplicateJavaScriptMemberName,
      "EXPOJSI017" => ExpoModulesDiagnostics.ReservedObservingPropertyName,
      "EXPOJSI018" => ExpoModulesDiagnostics.UnsupportedEventProperty,
      "EXPOJSI019" => ExpoModulesDiagnostics.UnsupportedEventPayload,
      "EXPOJSI020" => ExpoModulesDiagnostics.DuplicateEventName,
      "EXPOJSI021" => ExpoModulesDiagnostics.InvalidSharedObjectDeclaration,
      "EXPOJSI022" => ExpoModulesDiagnostics.InvalidSharedObjectConstructor,
      "EXPOJSI023" => ExpoModulesDiagnostics.UnsupportedSharedObjectUsage,
      "EXPOJSI024" => ExpoModulesDiagnostics.InvalidSharedObjectOwnership,
      "EXPOJSI025" => ExpoModulesDiagnostics.InvalidSharedObjectMemberName,
      "EXPOJSI026" => ExpoModulesDiagnostics.UnsupportedSharedObjectEventProperty,
      "EXPOJSI027" => ExpoModulesDiagnostics.UnsupportedSharedObjectEventPayload,
      "EXPOJSI028" => ExpoModulesDiagnostics.InvalidSharedObjectEventName,
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
      ExpoFunctionModel function,
      SharedObjectHostTarget? shared = null)
  {
    if (!function.IsAsync && function.Parameters.Values.Any(parameter =>
            parameter.PassingKind != ExpoParameterPassingKind.Codec))
    {
      EmitSpanHostFunction(builder, module, function, shared);
      return;
    }

    var hostFunctionName = shared?.HostFunctionName ?? GetHostFunctionName(module, function);
    var label = shared?.Label ?? module.ModuleName;

    builder.AppendLine();
    builder.AppendLine($"  private static global::Expo.JSI.JavaScriptValue {hostFunctionName}(");
    var runtimeParameterName = function.IsAsync ? "jsRuntime" : "runtime";
    // The shared receiver resolves through the codec (registry NativeState identity) before any
    // authored code runs; the local keeps the `module` name so the shared body emission below
    // stays identical for module-level and prototype-level members.
    var targetDeclaration = shared is null
        ? $"var module = ({module.FullyQualifiedTypeName})context;"
        : $"var module = SharedObjectCodec<{shared.ReceiverTypeName}>.Decode(thisValue, {runtimeParameterName}, GeneratedFunction.CurrentRuntimeContext);";
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
      builder.AppendLine($"      GeneratedFunction.RequireArgumentCount(\"{EscapeString(label)}.{EscapeString(function.JavaScriptName)}\", arguments, {GetRequiredParameterCount(function)}, {function.Parameters.Values.Count});");
      builder.AppendLine($"      {targetDeclaration}");
      if (function.AsyncResultRequiresRuntimeContext)
      {
        // Capture the exact runtime context while the host-function frame is active; Promise
        // settlement runs after the frame exits and must never read the thread-static accessor.
        builder.AppendLine("      var __expoRuntimeContext = GeneratedFunction.CurrentRuntimeContext;");
      }
    }
    else
    {
      builder.AppendLine($"    GeneratedFunction.RequireArgumentCount(\"{EscapeString(label)}.{EscapeString(function.JavaScriptName)}\", arguments, {GetRequiredParameterCount(function)}, {function.Parameters.Values.Count});");
      builder.AppendLine();
      builder.AppendLine($"    {targetDeclaration}");
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
        var asyncEncodeContextArgument = function.AsyncResultRequiresRuntimeContext
            ? ", __expoRuntimeContext"
            : string.Empty;
        builder.AppendLine($"{asyncIndent}var __expoResult = await __expoTask.ConfigureAwait(false);");
        builder.AppendLine($"{asyncIndent}return global::Expo.JSI.JavaScriptPromiseResult.Resolve(runtime => {function.AsyncResultCodecExpression}.Encode(__expoResult, runtime{asyncEncodeContextArgument}));");
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
        var returnEncodeContextArgument = function.ReturnRequiresRuntimeContext
            ? ", GeneratedFunction.CurrentRuntimeContext"
            : string.Empty;
        builder.AppendLine($"    return {function.ReturnCodecExpression}.Encode(module.{function.MethodName}({argumentList}), runtime{returnEncodeContextArgument});");
      }
    }
    builder.AppendLine("  }");
  }

  private static void EmitPropertyGetter(
      StringBuilder builder,
      ExpoModuleModel module,
      ExpoPropertyModel property,
      SharedObjectHostTarget? shared = null)
  {
    var getterName = shared?.HostFunctionName ?? GetPropertyGetterFunctionName(module, property);
    var label = shared?.Label ?? module.ModuleName;
    var targetDeclaration = shared is null
        ? $"var module = ({module.FullyQualifiedTypeName})context;"
        : $"var module = SharedObjectCodec<{shared.ReceiverTypeName}>.Decode(thisValue, runtime, GeneratedFunction.CurrentRuntimeContext);";
    var encodeContextArgument = property.RequiresRuntimeContext
        ? ", GeneratedFunction.CurrentRuntimeContext"
        : string.Empty;

    builder.AppendLine();
    builder.AppendLine($"  private static global::Expo.JSI.JavaScriptValue {getterName}(");
    builder.AppendLine("      global::Expo.JSI.JavaScriptRuntime runtime,");
    builder.AppendLine("      global::Expo.JSI.JavaScriptValueRef thisValue,");
    builder.AppendLine("      global::Expo.JSI.JavaScriptArguments arguments,");
    builder.AppendLine("      object context)");
    builder.AppendLine("  {");
    builder.AppendLine($"    GeneratedFunction.RequireArgumentCount(\"{EscapeString(label)}.{EscapeString(property.JavaScriptName)}\", arguments, 0);");
    builder.AppendLine($"    {targetDeclaration}");
    if (property.CodecExpression == "ArrayBufferCodec")
    {
      builder.AppendLine($"    using var __expoResult = module.{property.PropertyName};");
      builder.AppendLine("    return ArrayBufferCodec.Encode(__expoResult, runtime);");
    }
    else if (property.CodecExpression == "JavaScriptValueCodec")
    {
      builder.AppendLine($"    return JavaScriptValueCodec.Encode(module.{property.PropertyName}, runtime);");
    }
    else
    {
      builder.AppendLine($"    return {property.CodecExpression}.Encode(module.{property.PropertyName}, runtime{encodeContextArgument});");
    }
    builder.AppendLine("  }");
  }

  private static void EmitPropertySetter(
      StringBuilder builder,
      ExpoModuleModel module,
      ExpoPropertyModel property,
      SharedObjectHostTarget? shared = null)
  {
    var setterName = shared?.HostFunctionName ?? GetPropertySetterFunctionName(module, property);
    var label = shared?.Label ?? module.ModuleName;
    var targetDeclaration = shared is null
        ? $"var module = ({module.FullyQualifiedTypeName})context;"
        : $"var module = SharedObjectCodec<{shared.ReceiverTypeName}>.Decode(thisValue, runtime, GeneratedFunction.CurrentRuntimeContext);";

    builder.AppendLine();
    builder.AppendLine($"  private static global::Expo.JSI.JavaScriptValue {setterName}(");
    builder.AppendLine("      global::Expo.JSI.JavaScriptRuntime runtime,");
    builder.AppendLine("      global::Expo.JSI.JavaScriptValueRef thisValue,");
    builder.AppendLine("      global::Expo.JSI.JavaScriptArguments arguments,");
    builder.AppendLine("      object context)");
    builder.AppendLine("  {");
    builder.AppendLine($"    GeneratedFunction.RequireArgumentCount(\"{EscapeString(label)}.{EscapeString(property.JavaScriptName)}\", arguments, 1);");
    builder.AppendLine($"    {targetDeclaration}");
    var decode = GetDecodeExpression(
        property.CodecExpression,
        0,
        "runtime",
        property.RequiresRuntimeContext
    );
    builder.AppendLine(property.OwnsDecodedValue
        ? $"    using var __expoValue = {decode};"
        : $"    var __expoValue = {decode};");
    builder.AppendLine($"    module.{property.PropertyName} = __expoValue;");
    builder.AppendLine("    return runtime.CreateUndefined();");
    builder.AppendLine("  }");
  }

  private static void EmitSpanHostFunction(
      StringBuilder builder,
      ExpoModuleModel module,
      ExpoFunctionModel function,
      SharedObjectHostTarget? shared = null)
  {
    var spanHostFunctionName = shared?.HostFunctionName ?? GetHostFunctionName(module, function);
    var spanLabel = shared?.Label ?? module.ModuleName;
    var spanTargetDeclaration = shared is null
        ? $"var module = ({module.FullyQualifiedTypeName})context;"
        : $"var module = SharedObjectCodec<{shared.ReceiverTypeName}>.Decode(thisValue, runtime, GeneratedFunction.CurrentRuntimeContext);";
    var spanParameter = function.Parameters.Values.Single(parameter =>
        parameter.PassingKind != ExpoParameterPassingKind.Codec);
    var spanIndex = Enumerable.Range(0, function.Parameters.Values.Count)
        .Single(index => ReferenceEquals(function.Parameters.Values[index], spanParameter));
    var spanBuffer = $"__expoSpanBuffer{spanIndex}";
    var spanMethod = spanParameter.PassingKind == ExpoParameterPassingKind.MutableByteSpan
        ? "WithBytes"
        : "WithReadOnlyBytes";

    builder.AppendLine();
    builder.AppendLine($"  private static global::Expo.JSI.JavaScriptValue {spanHostFunctionName}(");
    builder.AppendLine("      global::Expo.JSI.JavaScriptRuntime runtime,");
    builder.AppendLine("      global::Expo.JSI.JavaScriptValueRef thisValue,");
    builder.AppendLine("      global::Expo.JSI.JavaScriptArguments arguments,");
    builder.AppendLine("      object context)");
    builder.AppendLine("  {");
    builder.AppendLine($"    GeneratedFunction.RequireArgumentCount(\"{EscapeString(spanLabel)}.{EscapeString(function.JavaScriptName)}\", arguments, {GetRequiredParameterCount(function)}, {function.Parameters.Values.Count});");
    builder.AppendLine($"    {spanTargetDeclaration}");
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
      builder.AppendLine($"      var {GetRecordFieldLocalName(field)} = {field.CodecExpression}.Decode(obj.GetProperty(\"{EscapeString(field.JavaScriptName)}\"), runtime);");
    }
    builder.AppendLine($"      return new {codec.RecordTypeName}({string.Join(", ", codec.Fields.Values.Select(GetRecordFieldLocalName))});");
    builder.AppendLine("    }");
    builder.AppendLine();
    builder.AppendLine($"    public static {codec.RecordTypeName} Decode(global::Expo.JSI.JavaScriptValue value, global::Expo.JSI.JavaScriptRuntime runtime)");
    builder.AppendLine("    {");
    builder.AppendLine("      using var obj = value.AsObject();");
    foreach (var field in codec.Fields.Values)
    {
      builder.AppendLine($"      using var {field.ParameterName}Value = obj.GetProperty(\"{EscapeString(field.JavaScriptName)}\");");
      builder.AppendLine($"      var {GetRecordFieldLocalName(field)} = {field.CodecExpression}.Decode({field.ParameterName}Value, runtime);");
    }
    builder.AppendLine($"      return new {codec.RecordTypeName}({string.Join(", ", codec.Fields.Values.Select(GetRecordFieldLocalName))});");
    builder.AppendLine("    }");
    builder.AppendLine();
    builder.AppendLine($"    public static global::Expo.JSI.JavaScriptValue Encode({codec.RecordTypeName} value, global::Expo.JSI.JavaScriptRuntime runtime)");
    builder.AppendLine("    {");
    builder.AppendLine("      using var obj = runtime.CreateObject();");
    foreach (var field in codec.Fields.Values)
    {
      builder.AppendLine($"      using var {GetRecordFieldLocalName(field)} = {field.CodecExpression}.Encode(value.{field.CSharpPropertyName}, runtime);");
      builder.AppendLine($"      obj.SetProperty(\"{EscapeString(field.JavaScriptName)}\", {GetRecordFieldLocalName(field)});");
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

  private static string GetPropertyGetterFunctionName(ExpoModuleModel module, ExpoPropertyModel property) =>
      $"{SanitizeIdentifier(module.ModuleName)}_{SanitizeIdentifier(property.PropertyName)}_Getter";

  private static string GetPropertySetterFunctionName(ExpoModuleModel module, ExpoPropertyModel property) =>
      $"{SanitizeIdentifier(module.ModuleName)}_{SanitizeIdentifier(property.PropertyName)}_Setter";

  private static string GetModuleRegistrationFunctionName(ExpoModuleModel module) =>
      $"Register{SanitizeIdentifier(module.ModuleName)}";

  private static string GetObservingHookFunctionName(ExpoModuleModel module, string javaScriptName) =>
      $"{SanitizeIdentifier(module.ModuleName)}_{SanitizeIdentifier(javaScriptName)}_HostFunction";

  private static string GetRecordFieldLocalName(ExpoGeneratedRecordFieldModel field) =>
      field.ParameterName == "value" ? "__expoValue" : field.ParameterName;

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

    if (typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == JavaScriptValueMetadataName)
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

  private static bool ContainsJavaScriptCallback(ITypeSymbol typeSymbol) =>
      ContainsJavaScriptCallback(typeSymbol, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));

  private static bool ContainsJavaScriptCallback(
      ITypeSymbol typeSymbol,
      HashSet<ITypeSymbol> visitedTypes)
  {
    if (!visitedTypes.Add(typeSymbol))
    {
      return false;
    }

    if (IsJavaScriptCallbackType(typeSymbol))
    {
      return true;
    }

    if (typeSymbol is not INamedTypeSymbol namedType)
    {
      return false;
    }

    if (namedType.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
    {
      return ContainsJavaScriptCallback(namedType.TypeArguments.Single(), visitedTypes);
    }

    if (namedType.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
        "global::System.Collections.Generic.IReadOnlyList<T>")
    {
      return ContainsJavaScriptCallback(namedType.TypeArguments.Single(), visitedTypes);
    }

    var constructedType = namedType.ConstructedFrom.ToDisplayString(
        SymbolDisplayFormat.FullyQualifiedFormat
    );
    if (constructedType is "global::System.Collections.Generic.Dictionary<TKey, TValue>" or
        "global::System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>")
    {
      return namedType.TypeArguments[0].SpecialType == SpecialType.System_String &&
          ContainsJavaScriptCallback(namedType.TypeArguments[1], visitedTypes);
    }

    if (!namedType.IsRecord)
    {
      return false;
    }

    var constructor = GetRecordCodecConstructor(namedType);
    return constructor is not null && constructor.Parameters.Any(parameter =>
        ContainsJavaScriptCallback(parameter.Type, visitedTypes));
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

    var constructor = GetRecordCodecConstructor(typeSymbol);
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
          LowerCamel(property.Name),
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

  private static IMethodSymbol? GetRecordCodecConstructor(INamedTypeSymbol typeSymbol) =>
      typeSymbol.InstanceConstructors
          .Where(item =>
              item.Parameters.Length > 0 &&
              item.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal)
          .OrderByDescending(item => item.Parameters.Length)
          .FirstOrDefault();

  private static string LowerCamel(string value) =>
      value.Length == 0 ? value : char.ToLowerInvariant(value[0]) + value.Substring(1);

  private static string EscapeString(string value)
  {
    var builder = new StringBuilder(value.Length);
    foreach (var character in value)
    {
      builder.Append(character switch
      {
        '\\' => "\\\\",
        '\"' => "\\\"",
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        '\0' => "\\0",
        '\b' => "\\b",
        '\f' => "\\f",
        '\v' => "\\v",
        '\u2028' => "\\u2028",
        '\u2029' => "\\u2029",
        _ when char.IsSurrogate(character) => $"\\u{(int)character:X4}",
        _ when char.IsControl(character) => $"\\u{(int)character:X4}",
        _ => character.ToString(),
      });
    }
    return builder.ToString();
  }

  private static string EscapeIdentifier(string value) =>
      SyntaxFacts.GetKeywordKind(value) != SyntaxKind.None ||
      SyntaxFacts.GetContextualKeywordKind(value) != SyntaxKind.None
          ? "@" + value
          : value;
}
