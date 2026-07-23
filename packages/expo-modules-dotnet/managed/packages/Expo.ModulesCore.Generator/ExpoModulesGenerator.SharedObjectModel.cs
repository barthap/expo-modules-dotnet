using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Expo.ModulesCore.Generator;

public sealed partial class ExpoModulesGenerator
{
  private static ExpoSharedObjectModel? CreateSharedObjectModel(
      GeneratorAttributeSyntaxContext context,
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    if (context.TargetSymbol is not INamedTypeSymbol typeSymbol) return null;

    var location = typeSymbol.Locations.FirstOrDefault();
    var diagnostics = new List<ExpoDiagnosticModel>();
    var javaScriptClassName = typeSymbol.Name;
    void AddDeclarationDiagnostic(string reason) => diagnostics.Add(new ExpoDiagnosticModel(ExpoModulesDiagnostics.InvalidSharedObjectDeclaration.Id, location, new EquatableArray<string>(new[] { typeSymbol.Name, reason })));

    foreach (var attribute in context.Attributes)
    {
      if (attribute.ConstructorArguments.Length != 1) continue;
      if (attribute.ConstructorArguments[0].Value is string explicitName && !string.IsNullOrWhiteSpace(explicitName)) javaScriptClassName = explicitName;
      else AddDeclarationDiagnostic("its explicit JavaScript class name must be a non-empty string");
    }
    if (typeSymbol.ContainingType is not null) AddDeclarationDiagnostic("it must be a top-level class");
    if (typeSymbol.IsGenericType) AddDeclarationDiagnostic("it must be non-generic");
    if (!typeSymbol.IsSealed) AddDeclarationDiagnostic("it must be sealed");
    if (context.TargetNode is not TypeDeclarationSyntax typeDeclaration || !typeDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword)) AddDeclarationDiagnostic("it must be partial");
    if (typeSymbol.TypeKind != TypeKind.Class) AddDeclarationDiagnostic("it must be a class");
    if (!DerivesFromSharedObject(typeSymbol)) AddDeclarationDiagnostic("it must derive from Expo.ModulesCore.SharedObject");

    var isDeclarationValid = diagnostics.Count == 0;
    ExpoSharedObjectConstructorModel? constructor = null;
    var functions = new List<ExpoFunctionModel>();
    var properties = new List<ExpoPropertyModel>();
    var recordCodecs = new List<ExpoGeneratedRecordCodecModel>();
    var events = GetTypedEvents(typeSymbol, typeSymbol.Name, diagnostics, recordCodecs, ExpoModulesDiagnostics.UnsupportedSharedObjectEventProperty.Id, ExpoModulesDiagnostics.UnsupportedSharedObjectEventPayload.Id, isDeclarationValid ? null : "declared on an invalid Expo shared object class");
    if (isDeclarationValid)
    {
      var memberDiagnostics = new List<ExpoDiagnosticModel>();
      var functionCollection = GetFunctions(typeSymbol, memberDiagnostics, recordCodecs, new HashSet<string>(StringComparer.Ordinal));
      functions = functionCollection.Functions;
      properties = RemoveCollidingProperties(typeSymbol, functionCollection.ValidJavaScriptNames, GetProperties(typeSymbol, memberDiagnostics, recordCodecs, new HashSet<string>(StringComparer.Ordinal)), memberDiagnostics);
      foreach (var diagnostic in memberDiagnostics) diagnostics.Add(TranslateSharedObjectMemberDiagnostic(diagnostic, typeSymbol.Name));
      functions = RemoveInaccessibleSharedObjectMethods(typeSymbol, functions, diagnostics).Where(function => !ReportReservedSharedObjectMemberName(typeSymbol.Name, function.JavaScriptName, function.Location, diagnostics)).ToList();
      properties = properties.Where(property => !ReportReservedSharedObjectMemberName(typeSymbol.Name, property.JavaScriptName, property.Location, diagnostics)).ToList();
      ValidateSharedObjectEventNames(typeSymbol.Name, events, diagnostics);
      constructor = GetSharedObjectConstructor(typeSymbol, diagnostics, recordCodecs);
    }
    return new ExpoSharedObjectModel(typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), typeSymbol.ContainingNamespace.IsGlobalNamespace ? string.Empty : typeSymbol.ContainingNamespace.ToDisplayString(), typeSymbol.Name, typeSymbol.DeclaredAccessibility == Accessibility.Public ? "public" : "internal", GetTypeDeclarationKind(context.TargetNode), javaScriptClassName, location, isDeclarationValid, constructor, new EquatableArray<ExpoEventModel>(events), new EquatableArray<ExpoFunctionModel>(functions), new EquatableArray<ExpoPropertyModel>(properties), new EquatableArray<ExpoGeneratedRecordCodecModel>(recordCodecs), new EquatableArray<ExpoDiagnosticModel>(diagnostics));
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

}
