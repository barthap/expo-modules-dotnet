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

    // Set when the union is valid. Codecs is the scratch collector that holds the payload codec
    // models the union resolved, so a caller adopts it with the union model.
    public ExpoGeneratedUnionCodecModel? Model { get; set; }

    public ExpoCodecCollector? Codecs { get; set; }
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

    for (var later = 1; later < cases.Count; later++)
    {
      var earlier = cases.Take(later).FirstOrDefault(item =>
          SymbolEqualityComparer.Default.Equals(item.Payload.Type, cases[later].Payload.Type));
      if (earlier is not null)
      {
        analysis.Diagnostics.Add(new ExpoDiagnosticModel(
            ExpoModulesDiagnostics.DuplicateUnionPayload.Id,
            cases[later].CaseType.Locations.FirstOrDefault(),
            new EquatableArray<string>(new[]
            {
                union.ToDisplayString(),
                earlier.CaseType.Name,
                cases[later].CaseType.Name,
                cases[later].Payload.Type.ToDisplayString(),
            })
        ));
      }
    }

    if (analysis.Diagnostics.Count > 0)
    {
      return analysis;
    }

    var lastNumber = cases.FindLastIndex(item => (item.Kinds & ExpoJavaScriptKinds.Number) != 0);
    for (var later = 1; later < cases.Count; later++)
    {
      var laterIsGatedInteger = GetIntegerRange(cases[later].Payload.Type) is not null && later != lastNumber;
      var earlier = cases.Take(later).FirstOrDefault(item =>
          AlwaysTakesFirst(item, cases[later], laterIsGatedInteger, scratch));
      if (earlier is not null)
      {
        analysis.Diagnostics.Add(new ExpoDiagnosticModel(
            ExpoModulesDiagnostics.UnreachableUnionCase.Id,
            cases[later].CaseType.Locations.FirstOrDefault(),
            new EquatableArray<string>(new[]
            {
                union.ToDisplayString(),
                cases[later].CaseType.Name,
                earlier.CaseType.Name,
            })
        ));
      }
    }

    if (analysis.Diagnostics.Count > 0)
    {
      return analysis;
    }

    var unionTypeName = union.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    var lastNumberCase = cases.FindLastIndex(item => (item.Kinds & ExpoJavaScriptKinds.Number) != 0);
    var caseModels = cases.Select((item, index) =>
    {
      var range = GetIntegerRange(item.Payload.Type);
      var gate = range is not null && index != lastNumberCase
          ? $"global::System.Double.IsInteger(__expoNumber) && __expoNumber >= {range.Min} && __expoNumber {(range.ExclusiveMax ? "<" : "<=")} {range.Max}"
          : string.Empty;
      return new ExpoGeneratedUnionCaseModel(
          item.CaseType.Name,
          item.CaseType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
          item.Property.Name,
          item.Payload.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
          item.Codec,
          item.Kinds,
          gate);
    }).ToArray();
    analysis.Model = new ExpoGeneratedUnionCodecModel(
        $"{SanitizeIdentifier(union.Name)}UnionCodec_{EncodeTypeIdentity(unionTypeName)}",
        unionTypeName,
        union.Name,
        new EquatableArray<ExpoGeneratedUnionCaseModel>(caseModels),
        caseModels.Any(item => HoldsLeaves(item.PayloadCodec)) ? ExpoResourceOwnership.HoldsLeaves : ExpoResourceOwnership.Borrowed,
        union.Locations.FirstOrDefault());
    analysis.Codecs = scratch;
    return analysis;
  }

  private static ExpoCodecDescriptor UnionCodecDescriptor(ExpoGeneratedUnionCodecModel codec) =>
      OrdinaryCodec(codec.CodecTypeName) with { ResourceOwnership = codec.ResourceOwnership };

  // A valid union resolves to one ordinary codec. An invalid union resolves to no codec and adds
  // no diagnostic here: the declaration pipeline reports it once, and the using position reports
  // its own unsupported-type diagnostic.
  private static ExpoCodecDescriptor? TryGetUnionCodec(
      INamedTypeSymbol union,
      List<ExpoDiagnosticModel> diagnostics,
      ExpoCodecCollector codecs)
  {
    var unionTypeName = union.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    var existing = codecs.Unions.FirstOrDefault(model => model.UnionTypeName == unionTypeName);
    if (existing is not null)
    {
      return UnionCodecDescriptor(existing);
    }

    var analysis = AnalyzeUnion(union, codecs);
    if (analysis.Model is null)
    {
      return null;
    }

    codecs.Adopt(analysis.Codecs!);
    codecs.Unions.Add(analysis.Model);
    return UnionCodecDescriptor(analysis.Model);
  }

  // The JavaScript kinds a payload type decodes. None means the type is not a union payload.
  private static ExpoJavaScriptKinds GetUnionPayloadKinds(ITypeSymbol payloadType, ExpoCodecCollector codecs)
  {
    var name = payloadType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    if (name == JavaScriptValueMetadataName)
    {
      return ExpoJavaScriptKinds.All;
    }

    if (IsUnionType(payloadType))
    {
      // The nested union resolved first, so its model is in the collector.
      return codecs.Unions
          .FirstOrDefault(model => model.UnionTypeName == name)?.Cases.Values
          .Aggregate(ExpoJavaScriptKinds.None, (kinds, item) => kinds | item.Kinds) ?? ExpoJavaScriptKinds.None;
    }

    if (payloadType.TypeKind == TypeKind.Enum)
    {
      return UsesNumberEnumRepresentation(payloadType, null) ? ExpoJavaScriptKinds.Number : ExpoJavaScriptKinds.String;
    }

    if (payloadType.SpecialType == SpecialType.System_String || TryGetConvertibleCodec(payloadType) is not null)
    {
      return ExpoJavaScriptKinds.String;
    }

    if (payloadType.SpecialType == SpecialType.System_Boolean)
    {
      return ExpoJavaScriptKinds.Bool;
    }

    if (IsNumberSpecialType(payloadType.SpecialType))
    {
      return ExpoJavaScriptKinds.Number;
    }

    if (payloadType is IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Byte } ||
        name is ArrayBufferMetadataName or "global::System.Memory<byte>" or "global::System.ReadOnlyMemory<byte>")
    {
      return ExpoJavaScriptKinds.ArrayBuffer;
    }

    if (name == Uint8ArrayMetadataName || payloadType is INamedTypeSymbol { IsRecord: true })
    {
      return ExpoJavaScriptKinds.Object;
    }

    if (payloadType is INamedTypeSymbol namedType &&
        namedType.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) is
            "global::System.Collections.Generic.IReadOnlyList<T>" or
            "global::System.Collections.Generic.Dictionary<TKey, TValue>" or
            "global::System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>")
    {
      return ExpoJavaScriptKinds.Object;
    }

    return ExpoJavaScriptKinds.None;
  }

  private sealed record IntegerRange(string Min, string Max, bool ExclusiveMax, decimal Low, decimal High);

  // The range of an integer payload, or of a number enum through its underlying type. The literals
  // are emitted verbatim into the integer gate. long and ulong use an exclusive upper bound because
  // (double)long.MaxValue is 2^63, which long cannot hold.
  private static IntegerRange? GetIntegerRange(ITypeSymbol payloadType)
  {
    var type = payloadType;
    if (payloadType is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlying })
    {
      if (!UsesNumberEnumRepresentation(payloadType, null))
      {
        return null;
      }

      type = underlying;
    }

    return type.SpecialType switch
    {
      SpecialType.System_SByte => new IntegerRange("-128d", "127d", false, sbyte.MinValue, sbyte.MaxValue),
      SpecialType.System_Byte => new IntegerRange("0d", "255d", false, byte.MinValue, byte.MaxValue),
      SpecialType.System_Int16 => new IntegerRange("-32768d", "32767d", false, short.MinValue, short.MaxValue),
      SpecialType.System_UInt16 => new IntegerRange("0d", "65535d", false, ushort.MinValue, ushort.MaxValue),
      SpecialType.System_Int32 => new IntegerRange("-2147483648d", "2147483647d", false, int.MinValue, int.MaxValue),
      SpecialType.System_UInt32 => new IntegerRange("0d", "4294967295d", false, uint.MinValue, uint.MaxValue),
      SpecialType.System_Int64 => new IntegerRange("-9223372036854775808d", "9223372036854775808d", true, long.MinValue, long.MaxValue),
      SpecialType.System_UInt64 => new IntegerRange("0d", "18446744073709551616d", true, ulong.MinValue, ulong.MaxValue),
      _ => null,
    };
  }

  private const string JavaScriptValueDictionaryName =
      "global::System.Collections.Generic.Dictionary<string, global::Expo.JSI.JavaScriptValue>";
  private const string JavaScriptValueReadOnlyDictionaryName =
      "global::System.Collections.Generic.IReadOnlyDictionary<string, global::Expo.JSI.JavaScriptValue>";
  private const string JavaScriptValueListName =
      "global::System.Collections.Generic.IReadOnlyList<global::Expo.JSI.JavaScriptValue>";

  // True when `earlier` always takes every value that `later` could take, so `later` is never
  // chosen. Rules 1 to 8 of the delta spec; rule 8 also reads the record field kind, because a
  // defaulted or optional earlier field does not make the earlier record fail where a later one
  // would decode.
  private static bool AlwaysTakesFirst(
      UnionCaseCandidate earlier,
      UnionCaseCandidate later,
      bool laterIsGatedInteger,
      ExpoCodecCollector codecs)
  {
    var earlierType = earlier.Payload.Type;
    var earlierName = earlierType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    var laterType = later.Payload.Type;

    // 1. JavaScriptValue takes every value.
    if (earlierName == JavaScriptValueMetadataName)
    {
      return true;
    }

    // 2. double and float take every number.
    if (later.Kinds == ExpoJavaScriptKinds.Number &&
        earlierType.SpecialType is SpecialType.System_Double or SpecialType.System_Single)
    {
      return true;
    }

    // 3. A gated integer case is taken by an earlier (so also gated) integer type whose range holds it.
    if (laterIsGatedInteger &&
        earlierType.TypeKind != TypeKind.Enum &&
        GetIntegerRange(earlierType) is { } outer &&
        GetIntegerRange(laterType) is { } inner &&
        outer.Low <= inner.Low &&
        inner.High <= outer.High)
    {
      return true;
    }

    // 4. string takes every string.
    if (later.Kinds == ExpoJavaScriptKinds.String && earlierType.SpecialType == SpecialType.System_String)
    {
      return true;
    }

    // 5. A binary payload takes every ArrayBuffer.
    if (later.Kinds == ExpoJavaScriptKinds.ArrayBuffer &&
        earlier.Kinds == ExpoJavaScriptKinds.ArrayBuffer &&
        earlierName is ArrayBufferMetadataName or "byte[]" or "global::System.Memory<byte>" or "global::System.ReadOnlyMemory<byte>")
    {
      return true;
    }

    // 6. A JavaScriptValue dictionary, or a record of JavaScriptValue fields only, takes every object.
    var earlierRecord = codecs.Records.FirstOrDefault(model => model.RecordTypeName == earlierName);
    if (later.Kinds == ExpoJavaScriptKinds.Object &&
        (earlierName is JavaScriptValueDictionaryName or JavaScriptValueReadOnlyDictionaryName ||
         earlierRecord is not null &&
         earlierRecord.Fields.Values.All(field => field.Codec.ValueTypeName == JavaScriptValueMetadataName)))
    {
      return true;
    }

    // 7. A JavaScriptValue list takes every array.
    if (earlierName == JavaScriptValueListName &&
        laterType is INamedTypeSymbol laterNamed &&
        laterNamed.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
        "global::System.Collections.Generic.IReadOnlyList<T>")
    {
      return true;
    }

    // 8. A record takes a later record when every non-JavaScriptValue field of the earlier record
    //    has a later field with the same JavaScript name, the same type or a type it is the nullable
    //    form of, and, when the earlier field is required, a required later field.
    var laterRecord = codecs.Records.FirstOrDefault(model =>
        model.RecordTypeName == laterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    return earlierRecord is not null &&
        laterRecord is not null &&
        earlierRecord.Fields.Values
            .Where(field => field.Codec.ValueTypeName != JavaScriptValueMetadataName)
            .All(field => laterRecord.Fields.Values.Any(other =>
                other.JavaScriptName == field.JavaScriptName &&
                (other.Codec.ValueTypeName == field.Codec.ValueTypeName ||
                 field.Codec.ValueTypeName == other.Codec.ValueTypeName + "?") &&
                (field.Kind != ExpoRecordFieldKind.Required || other.Kind == ExpoRecordFieldKind.Required)));
  }

  private sealed record UnionCaseCandidate(
      INamedTypeSymbol CaseType,
      IParameterSymbol Payload,
      IPropertySymbol Property,
      ExpoCodecDescriptor Codec,
      ExpoJavaScriptKinds Kinds);

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

    var kinds = GetUnionPayloadKinds(payloadType, scratch);
    if (kinds == ExpoJavaScriptKinds.None)
    {
      return $"its payload type '{payloadType.ToDisplayString()}' has no union codec";
    }

    var property = caseType.GetMembers(payload.Name).OfType<IPropertySymbol>().FirstOrDefault();
    if (property?.GetMethod is not { DeclaredAccessibility: Accessibility.Public or Accessibility.Internal })
    {
      return "its payload property is not readable by generated code";
    }

    candidate = new UnionCaseCandidate(caseType, payload, property, codec, kinds);
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
