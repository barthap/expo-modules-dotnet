using Microsoft.CodeAnalysis;

namespace Expo.ModulesCore.Generator;

public sealed partial class ExpoModulesGenerator
{
  private static readonly string[] ReservedSharedObjectMemberNames =
  {
    "release", "constructor", "__proto__", "addListener", "removeListener",
    "removeAllListeners", "emit", "listenerCount", "removeSubscription",
  };

  private static void ValidateSharedObjectEventNames(
      string typeName,
      List<ExpoEventModel> events,
      List<ExpoDiagnosticModel> diagnostics)
  {
    var collidingIndexes = events
        .Select((@event, index) => (@event, index))
        .Where(item => item.@event.IsDispatchable)
        .GroupBy(item => item.@event.JavaScriptName, StringComparer.Ordinal)
        .Where(group => group.Count() > 1)
        .SelectMany(group => group.Select(item => item.index))
        .ToArray();
    foreach (var index in collidingIndexes)
    {
      var @event = events[index];
      diagnostics.Add(new ExpoDiagnosticModel(
          ExpoModulesDiagnostics.DuplicateSharedObjectEventName.Id,
          @event.Location,
          new EquatableArray<string>(new[] { typeName, @event.PropertyName, @event.JavaScriptName })
      ));
      events[index] = @event with { IsDispatchable = false };
    }
  }

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
          !member.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == JSAttributeMetadataName))
      {
        continue;
      }
      inaccessibleMethodSignatures.Add(GetMethodSignature(member.Name, member.Parameters.Select(parameter => parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))));
      diagnostics.Add(CreateUnsupportedSharedObjectUsage(member.Name, "it is not public or internal", member.Locations.FirstOrDefault()));
    }
    return inaccessibleMethodSignatures.Count == 0
        ? functions
        : functions.Where(function => !inaccessibleMethodSignatures.Contains(GetMethodSignature(function.MethodName, function.Parameters.Values.Select(parameter => parameter.TypeName)))).ToList();
  }

  private static string GetMethodSignature(string methodName, IEnumerable<string> parameterTypeNames) => $"{methodName}({string.Join(", ", parameterTypeNames)})";

  private static ExpoDiagnosticModel TranslateSharedObjectMemberDiagnostic(ExpoDiagnosticModel diagnostic, string typeName)
  {
    var arguments = diagnostic.Arguments.Values;
    return diagnostic.DescriptorId switch
    {
      "EXPOJSI001" => CreateUnsupportedSharedObjectUsage(arguments[1], $"parameter '{arguments[0]}' uses unsupported type '{arguments[2]}'", diagnostic.Location),
      "EXPOJSI002" => CreateUnsupportedSharedObjectUsage(arguments[0], $"it uses unsupported return type '{arguments[1]}'", diagnostic.Location),
      "EXPOJSI004" => CreateUnsupportedSharedObjectUsage(arguments[0], $"it is {arguments[1]}", diagnostic.Location),
      "EXPOJSI005" => new ExpoDiagnosticModel(ExpoModulesDiagnostics.InvalidSharedObjectMemberName.Id, diagnostic.Location, new EquatableArray<string>(new[] { typeName, arguments[1], "a duplicate" })),
      "EXPOJSI008" => CreateUnsupportedSharedObjectUsage(arguments[1], $"callback parameter '{arguments[0]}' uses unsupported callback type '{arguments[2]}'", diagnostic.Location),
      "EXPOJSI012" => CreateUnsupportedSharedObjectUsage(arguments[0], $"parameter '{arguments[1]}' uses '{arguments[2]}', which is supported only by synchronous methods", diagnostic.Location),
      "EXPOJSI013" => CreateUnsupportedSharedObjectUsage(arguments[0], $"it declares multiple span parameters ({arguments[1]})", diagnostic.Location),
      "EXPOJSI014" => CreateUnsupportedSharedObjectUsage(arguments[0], $"it is {arguments[1]}", diagnostic.Location),
      "EXPOJSI015" => CreateUnsupportedSharedObjectUsage(arguments[0], $"it uses unsupported type '{arguments[1]}'", diagnostic.Location),
      "EXPOJSI016" => new ExpoDiagnosticModel(ExpoModulesDiagnostics.InvalidSharedObjectMemberName.Id, diagnostic.Location, new EquatableArray<string>(new[] { typeName, arguments[1], "a duplicate" })),
      _ => diagnostic,
    };
  }

  private static bool DerivesFromSharedObject(INamedTypeSymbol typeSymbol)
  {
    for (var baseType = typeSymbol.BaseType; baseType is not null; baseType = baseType.BaseType)
    {
      if (baseType.ToDisplayString() == SharedObjectMetadataName) return true;
    }
    return false;
  }

  private static ExpoDiagnosticModel CreateUnsupportedSharedObjectUsage(string memberName, string reason, Location? location) => new(ExpoModulesDiagnostics.UnsupportedSharedObjectUsage.Id, location, new EquatableArray<string>(new[] { memberName, reason }));

  private static bool IsSharedObjectCodecExpression(string codecExpression) => codecExpression.StartsWith("SharedObjectCodec<", StringComparison.Ordinal);

  private static bool IsSharedObjectRelatedType(ITypeSymbol typeSymbol) => typeSymbol.ToDisplayString() == SharedObjectMetadataName || (typeSymbol is INamedTypeSymbol namedType && DerivesFromSharedObject(namedType));

  private static string? GetDirectSharedObjectBoundaryIssue(ITypeSymbol typeSymbol)
  {
    if (typeSymbol.ToDisplayString() == SharedObjectMetadataName) return "which is the polymorphic SharedObject base";
    if (typeSymbol is INamedTypeSymbol namedType && namedType.OriginalDefinition.ToDisplayString() == SharedRefMetadataName) return "which is the SharedRef<T> managed carrier base";
    if (!HasExpoSharedObjectAttribute(typeSymbol)) return "which is not marked [ExpoSharedObject]";
    if (typeSymbol.ContainingType is not null || typeSymbol is INamedTypeSymbol { IsGenericType: true } || !typeSymbol.IsSealed) return "which must be a top-level, non-generic, sealed [ExpoSharedObject] class";
    return typeSymbol.NullableAnnotation == NullableAnnotation.Annotated ? "which must be used without a nullable annotation" : null;
  }

  private static bool HasExpoSharedObjectAttribute(ITypeSymbol typeSymbol) => typeSymbol.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == ExpoSharedObjectAttributeMetadataName);

  private static bool TryAnalyzeSharedObjectBoundaryType(ITypeSymbol typeSymbol, string memberName, string positionDescription, Location? location, List<ExpoDiagnosticModel> diagnostics, out string? codecExpression)
  {
    codecExpression = null;
    if (IsSharedObjectRelatedType(typeSymbol))
    {
      var issue = GetDirectSharedObjectBoundaryIssue(typeSymbol);
      if (issue is null) { codecExpression = $"SharedObjectCodec<{typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>"; return true; }
      diagnostics.Add(CreateUnsupportedSharedObjectUsage(memberName, $"{positionDescription} uses shared-object type '{GetDiagnosticTypeName(typeSymbol)}', {issue}", location));
      return true;
    }
    if (!TryFindNestedSharedObjectType(typeSymbol, out var nestedSharedObjectType)) return false;
    diagnostics.Add(CreateUnsupportedSharedObjectUsage(memberName, $"{positionDescription} uses shared-object type '{GetDiagnosticTypeName(nestedSharedObjectType)}' inside a composed codec; shared-object types are supported only directly at the generated boundary", location));
    return true;
  }

  private static bool TryFindNestedSharedObjectType(ITypeSymbol typeSymbol, out ITypeSymbol sharedObjectType) => TryFindNestedSharedObjectType(typeSymbol, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default), out sharedObjectType);

  private static bool TryFindNestedSharedObjectType(ITypeSymbol typeSymbol, HashSet<ITypeSymbol> visitedTypes, out ITypeSymbol sharedObjectType)
  {
    sharedObjectType = typeSymbol;
    if (!visitedTypes.Add(typeSymbol) || typeSymbol is not INamedTypeSymbol namedType) return false;
    if (namedType.IsTupleType) return TryFindFirstNestedSharedObjectType(namedType.TupleElements.Select(element => element.Type), visitedTypes, out sharedObjectType);
    if (IsJavaScriptCallbackType(namedType)) return TryFindFirstNestedSharedObjectType(namedType.TypeArguments, visitedTypes, out sharedObjectType);
    if (namedType.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T || namedType.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Collections.Generic.IReadOnlyList<T>") return TryFindFirstNestedSharedObjectType(namedType.TypeArguments, visitedTypes, out sharedObjectType);
    var constructedType = namedType.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    if (constructedType is "global::System.Collections.Generic.Dictionary<TKey, TValue>" or "global::System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>") return TryFindFirstNestedSharedObjectType(new[] { namedType.TypeArguments[1] }, visitedTypes, out sharedObjectType);
    return namedType.IsRecord && GetRecordCodecConstructor(namedType) is { } constructor && TryFindFirstNestedSharedObjectType(constructor.Parameters.Select(parameter => parameter.Type), visitedTypes, out sharedObjectType);
  }

  private static bool TryFindFirstNestedSharedObjectType(IEnumerable<ITypeSymbol> typeSymbols, HashSet<ITypeSymbol> visitedTypes, out ITypeSymbol sharedObjectType)
  {
    foreach (var typeSymbol in typeSymbols)
    {
      if (IsSharedObjectRelatedType(typeSymbol)) { sharedObjectType = typeSymbol; return true; }
      if (TryFindNestedSharedObjectType(typeSymbol, visitedTypes, out sharedObjectType)) return true;
    }
    sharedObjectType = null!;
    return false;
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


}
