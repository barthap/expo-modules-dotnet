using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Expo.ModulesCore.Generator;

public sealed partial class ExpoModulesGenerator
{
  private const string UnionAttributeMetadataName = "Expo.ModulesCore.UnionAttribute";

  private static bool IsUnionType(ITypeSymbol typeSymbol) =>
      typeSymbol is INamedTypeSymbol namedType &&
      namedType.GetAttributes().Any(attribute =>
          attribute.AttributeClass?.ToDisplayString() == UnionAttributeMetadataName);

  // A type whose direct base is a union: a case, or a type that only looks like one. It is never a
  // record codec, so a case never crosses as { value }.
  private static bool IsUnionCaseType(ITypeSymbol typeSymbol) =>
      typeSymbol is INamedTypeSymbol { BaseType: { } baseType } && IsUnionType(baseType);

  // The nested types that derive from the union directly, in source order. The order is only used
  // after the analysis proved that every case sits in one partial declaration.
  private static IReadOnlyList<INamedTypeSymbol> GetUnionCaseCandidates(INamedTypeSymbol union) =>
      union.GetTypeMembers()
          .Where(member => SymbolEqualityComparer.Default.Equals(member.BaseType, union))
          .OrderBy(member => member.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0)
          .ToArray();

  // The positional parameters of a source record: the parameters of the constructor that the
  // record's parameter list declares. A record without a parameter list has none.
  private static IReadOnlyList<IParameterSymbol> GetPositionalParameters(INamedTypeSymbol record)
  {
    var declaration = record.DeclaringSyntaxReferences
        .Select(reference => reference.GetSyntax())
        .OfType<RecordDeclarationSyntax>()
        .FirstOrDefault(syntax => syntax.ParameterList is not null);
    if (declaration is null)
    {
      return Array.Empty<IParameterSymbol>();
    }

    var primary = record.InstanceConstructors.FirstOrDefault(constructor =>
        constructor.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() == declaration));
    return primary is null ? Array.Empty<IParameterSymbol>() : primary.Parameters.ToArray();
  }

  private enum UnionPayloadWalk
  {
    None,
    ReachesUnion,
    Cycle,
  }

  // Walks the types a payload codec would decode, at symbol level and before any codec resolves,
  // so a recursive union or a self-referential record never reaches GetCodec, which has no
  // recursion guard. `path` holds the types on the current path only.
  private static UnionPayloadWalk WalkUnionPayload(
      ITypeSymbol typeSymbol,
      INamedTypeSymbol union,
      HashSet<ITypeSymbol> path)
  {
    if (SymbolEqualityComparer.Default.Equals(typeSymbol.OriginalDefinition, union))
    {
      return UnionPayloadWalk.ReachesUnion;
    }

    if (typeSymbol is not INamedTypeSymbol namedType)
    {
      return UnionPayloadWalk.None;
    }

    if (!path.Add(namedType))
    {
      return UnionPayloadWalk.Cycle;
    }

    try
    {
      foreach (var child in GetPayloadChildTypes(namedType))
      {
        var result = WalkUnionPayload(child, union, path);
        if (result != UnionPayloadWalk.None)
        {
          return result;
        }
      }

      return UnionPayloadWalk.None;
    }
    finally
    {
      path.Remove(namedType);
    }
  }

  // The types that the codec of `namedType` decodes: a Nullable<T> inner type, a list element, a
  // dictionary value, record constructor parameters, and the payloads of a nested union's cases.
  private static IEnumerable<ITypeSymbol> GetPayloadChildTypes(INamedTypeSymbol namedType)
  {
    var definition = namedType.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    if (namedType.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T ||
        definition == "global::System.Collections.Generic.IReadOnlyList<T>")
    {
      return namedType.TypeArguments;
    }

    if (definition is "global::System.Collections.Generic.Dictionary<TKey, TValue>" or
        "global::System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>")
    {
      return new[] { namedType.TypeArguments[1] };
    }

    if (IsUnionType(namedType))
    {
      return GetUnionCaseCandidates(namedType)
          .SelectMany(candidate => GetRecordCodecConstructor(candidate)?.Parameters.Select(parameter => parameter.Type) ??
              Enumerable.Empty<ITypeSymbol>());
    }

    if (namedType.IsRecord && GetRecordCodecConstructor(namedType) is { } constructor)
    {
      return constructor.Parameters.Select(parameter => parameter.Type);
    }

    return Array.Empty<ITypeSymbol>();
  }

  // The result of analysing one union. Diagnostics are declaration diagnostics; the declaration
  // pipeline reports them, and codec resolution only reads whether there are any.
  private sealed class ExpoUnionAnalysis
  {
    public List<ExpoDiagnosticModel> Diagnostics { get; } = new();
  }

  private static ExpoUnionAnalysis AnalyzeUnion(INamedTypeSymbol union, ExpoCodecCollector codecs)
  {
    var analysis = new ExpoUnionAnalysis();
    var declarationRejection = GetUnionDeclarationRejection(union, out var candidates);
    if (declarationRejection is not null)
    {
      analysis.Diagnostics.Add(new ExpoDiagnosticModel(
          ExpoModulesDiagnostics.InvalidUnionDeclaration.Id,
          union.Locations.FirstOrDefault(),
          new EquatableArray<string>(new[] { union.ToDisplayString(), declarationRejection })
      ));
      return analysis;
    }

    var scratch = codecs.Fork();
    var cases = new List<UnionCaseCandidate>();
    foreach (var caseType in candidates)
    {
      var rejection = GetUnionCaseRejection(union, caseType, scratch, out var candidate);
      if (rejection is not null)
      {
        analysis.Diagnostics.Add(CreateInvalidUnionCase(caseType, union, rejection));
        continue;
      }

      cases.Add(candidate!);
    }

    if (analysis.Diagnostics.Count > 0)
    {
      return analysis;
    }

    // Later tasks continue here with `cases` and `scratch`.
    return analysis;
  }

  private sealed record UnionCaseCandidate(
      INamedTypeSymbol CaseType,
      IParameterSymbol Payload,
      IPropertySymbol Property,
      ExpoCodecDescriptor Codec);

  private static ExpoDiagnosticModel CreateInvalidUnionCase(INamedTypeSymbol type, INamedTypeSymbol union, string reason) =>
      new(
          ExpoModulesDiagnostics.InvalidUnionCase.Id,
          type.Locations.FirstOrDefault(),
          new EquatableArray<string>(new[] { type.ToDisplayString(), union.ToDisplayString(), reason })
      );

  private static string? GetUnionCaseRejection(
      INamedTypeSymbol union,
      INamedTypeSymbol caseType,
      ExpoCodecCollector scratch,
      out UnionCaseCandidate? candidate)
  {
    candidate = null;
    if (!caseType.IsRecord || !caseType.IsSealed)
    {
      return "it must be a sealed record";
    }

    if (caseType.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
    {
      return "it must be public or internal";
    }

    var parameters = GetPositionalParameters(caseType);
    if (parameters.Count == 0)
    {
      return "it has no payload; give it one positional parameter, and use a C# enum payload for a fixed set of values";
    }

    if (parameters.Count > 1)
    {
      return $"it has {parameters.Count} positional parameters; group the values in a record and use that record as the payload";
    }

    var payload = parameters[0];
    if (payload.HasExplicitDefaultValue)
    {
      return "its payload has a default value";
    }

    var payloadType = payload.Type;
    if (payloadType is INamedTypeSymbol { ConstructedFrom.SpecialType: SpecialType.System_Nullable_T } ||
        (payloadType.IsReferenceType && payloadType.NullableAnnotation == NullableAnnotation.Annotated))
    {
      return "its payload is nullable; make the union nullable where it is used instead";
    }

    // Inside the union body a case named like its payload type binds the name to the case.
    if (IsUnionCaseType(payloadType) &&
        SymbolEqualityComparer.Default.Equals(((INamedTypeSymbol)payloadType).BaseType, union))
    {
      return $"its payload type binds to the case '{payloadType.Name}' of this union; rename that case or qualify the payload type";
    }

    if (WalkUnionPayload(payloadType, union, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)) ==
        UnionPayloadWalk.Cycle)
    {
      return $"its payload type '{payloadType.ToDisplayString()}' contains itself, which generated codecs do not support";
    }

    // The record-field rule: the union codec calls the payload codec directly with Decode and
    // Encode, and has no runtime context to pass. Usage-level [JSEnum] is not read.
    var codec = IsUnionCaseType(payloadType)
        ? null
        : GetCodec(payloadType, new List<ExpoDiagnosticModel>(), scratch);
    if (codec is not
      {
        Shape: ExpoCodecShape.JavaScriptCodec,
        Capabilities: ExpoCodecCapabilities.DecodeAndEncode,
        ContextRequirement: ExpoContextRequirement.None,
      })
    {
      return $"its payload type '{payloadType.ToDisplayString()}' has no union codec";
    }

    var property = caseType.GetMembers(payload.Name).OfType<IPropertySymbol>().FirstOrDefault();
    if (property?.GetMethod is not { DeclaredAccessibility: Accessibility.Public or Accessibility.Internal })
    {
      return "its payload property is not readable by generated code";
    }

    candidate = new UnionCaseCandidate(caseType, payload, property, codec);
    return null;
  }

  // A record outside a union that derives from it. The [Union] pipeline sees only the union and
  // its nested types, so these are found from the record declarations.
  private static ExpoDiagnosticModel? CreateForeignUnionCaseDiagnostic(
      GeneratorSyntaxContext context,
      CancellationToken cancellationToken)
  {
    if (context.SemanticModel.GetDeclaredSymbol(context.Node, cancellationToken) is not INamedTypeSymbol typeSymbol ||
        typeSymbol.BaseType is not { } baseType ||
        !IsUnionType(baseType) ||
        SymbolEqualityComparer.Default.Equals(typeSymbol.ContainingType, baseType) ||
        typeSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken) != context.Node)
    {
      return null;
    }

    return CreateInvalidUnionCase(typeSymbol, baseType, "it is not nested directly in the union");
  }

  private static string? GetUnionDeclarationRejection(
      INamedTypeSymbol union,
      out IReadOnlyList<INamedTypeSymbol> candidates)
  {
    candidates = Array.Empty<INamedTypeSymbol>();
    if (union.TypeKind != TypeKind.Class || !union.IsRecord || !union.IsAbstract)
    {
      return "it must be an abstract record class";
    }

    for (var type = union; type is not null; type = type.ContainingType)
    {
      if (type.TypeParameters.Length > 0)
      {
        return "it must not be generic or nested in a generic type";
      }
    }

    if (union.DeclaringSyntaxReferences
        .Select(reference => reference.GetSyntax())
        .OfType<RecordDeclarationSyntax>()
        .Any(declaration => declaration.ParameterList is not null))
    {
      return "it must not have a parameter list";
    }

    for (var type = union; type is not null; type = type.ContainingType)
    {
      if (type.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
      {
        return "it and every containing type must be public or internal";
      }
    }

    var found = GetUnionCaseCandidates(union);
    if (found.Count == 0)
    {
      return "it declares no cases; a case is a nested sealed record that derives from it";
    }

    // Case order comes from source positions, which are comparable only inside one declaration.
    if (found.Select(candidate => candidate.DeclaringSyntaxReferences[0].GetSyntax().Parent).Distinct().Count() > 1)
    {
      return "its cases must all be declared in one partial declaration";
    }

    foreach (var candidate in found)
    {
      var parameters = GetPositionalParameters(candidate);
      if (parameters.Count == 1 &&
          WalkUnionPayload(parameters[0].Type, union, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)) ==
          UnionPayloadWalk.ReachesUnion)
      {
        return $"it contains itself through case '{candidate.Name}'";
      }
    }

    candidates = found;
    return null;
  }

  private static EquatableArray<ExpoDiagnosticModel> CreateUnionDeclarationDiagnostics(
      GeneratorAttributeSyntaxContext context,
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    if (context.TargetSymbol is not INamedTypeSymbol union)
    {
      return default;
    }

    var analysis = AnalyzeUnion(union, new ExpoCodecCollector(context.SemanticModel.Compilation.Assembly));
    return new EquatableArray<ExpoDiagnosticModel>(analysis.Diagnostics);
  }
}
