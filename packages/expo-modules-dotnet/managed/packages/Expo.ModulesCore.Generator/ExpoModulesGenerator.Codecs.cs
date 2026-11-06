using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Expo.ModulesCore.Generator;

public sealed partial class ExpoModulesGenerator
{
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

  // Codec type arguments keep their nullable reference annotations. A nullable inner codec
  // implements IJavaScriptCodec<T?>, so an annotation-erased type argument would leave the
  // generated code warning with CS8631 about an unsatisfied constraint.
  private static readonly SymbolDisplayFormat CodecTypeArgumentFormat =
      SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
          SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
      );

  private static string GetCodecTypeArgumentName(ITypeSymbol typeSymbol) =>
      typeSymbol.ToDisplayString(CodecTypeArgumentFormat);

  // The ordinary row of the descriptor table: a JavaScriptCodec with Decode, no context, and no
  // wrapper leaf. Branches that differ set their fields explicitly.
  private static ExpoCodecDescriptor OrdinaryCodec(string expression) => new(
      expression,
      ExpoCodecShape.JavaScriptCodec,
      ExpoCodecCapabilities.DecodeAndEncode,
      ExpoDecodeOperation.Decode,
      ExpoContextRequirement.None,
      ExpoResourceOwnership.Borrowed
  );

  // The scoped row: a codec that implements only the scoped codec protocol and holds a wrapper
  // leaf. Generated glue decodes it into the invocation's conversion scope.
  private static ExpoCodecDescriptor ScopedCodec(string expression) => new(
      expression,
      ExpoCodecShape.ScopedOnly,
      ExpoCodecCapabilities.DecodeAndEncode,
      ExpoDecodeOperation.Decode,
      ExpoContextRequirement.None,
      ExpoResourceOwnership.ScopedResource
  );

  private static bool IsScopedResource(ExpoCodecDescriptor? codec) =>
      codec?.ResourceOwnership == ExpoResourceOwnership.ScopedResource;

  // Composition rule: a list element, dictionary value, Nullable<T> inner, and nullable-reference
  // inner type are generic type arguments of an ordinary or a scoped composite codec. A
  // StaticHelper codec is a static class and cannot be one. A composite has its child's
  // capabilities, so it is decode-only over a decode-only child.
  private static ExpoCodecDescriptor? AsCompositeChildCodec(ExpoCodecDescriptor? codec) =>
      codec is { Shape: ExpoCodecShape.JavaScriptCodec or ExpoCodecShape.ScopedOnly } ? codec : null;

  // A callback result, callback argument tuple element, and typed event payload are type arguments
  // that need IJavaScriptCodec<T>. A ScopedOnly codec has no such implementation, so a leaf-bearing
  // composite is rejected there, while a direct JavaScriptValue stays accepted. A decode-only
  // JavaScriptCodec (an ordinary union) is rejected there too, because a union is a parameter type
  // only.
  private static ExpoCodecDescriptor? AsJavaScriptCodecArgument(ExpoCodecDescriptor? codec) =>
      codec is { Shape: ExpoCodecShape.JavaScriptCodec, Capabilities: ExpoCodecCapabilities.DecodeAndEncode }
          ? codec
          : null;

  // A record codec is scoped when any field holds a leaf. The cache-hit path rebuilds the
  // descriptor from the stored ownership, not from the type name alone.
  private static ExpoCodecDescriptor RecordCodecDescriptor(ExpoGeneratedRecordCodecModel codec) =>
      codec.ResourceOwnership == ExpoResourceOwnership.ScopedResource
          ? ScopedCodec(codec.CodecTypeName)
          : OrdinaryCodec(codec.CodecTypeName);

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

  private static ExpoCodecDescriptor? TryGetNullableCodec(
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
    var valueCodec = AsCompositeChildCodec(GetCodec(valueType, diagnostics, recordCodecs));
    if (valueCodec is null)
    {
      return null;
    }

    var valueTypeName = GetCodecTypeArgumentName(valueType);
    return (IsScopedResource(valueCodec)
        ? ScopedCodec($"GeneratedScopedNullableCodec<{valueTypeName}, {valueCodec.Expression}>")
        : OrdinaryCodec($"NullableCodec<{valueTypeName}, {valueCodec.Expression}>")) with
    {
      Capabilities = valueCodec.Capabilities,
    };
  }

  // JavaScriptValue keeps its non-nullable conversion rules. Its retain, transfer, and
  // release-exactly-once states are not widened with a null axis, so an annotated use is rejected
  // instead of silently converted.
  private static bool IsExcludedFromNullableReference(ITypeSymbol typeSymbol)
  {
    return typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == JavaScriptValueMetadataName;
  }

  /// <summary>
  /// Resolves the codec of an annotated nullable reference type.
  /// </summary>
  /// <returns>
  /// <see langword="false"/> when the type is not an annotated reference, so ordinary dispatch
  /// continues. <see langword="true"/> when the annotated reference was handled, either with a
  /// codec descriptor or with a <see langword="null"/> descriptor that tells the calling analysis
  /// to report its own context-specific unsupported-type diagnostic. A handled type never falls
  /// through to the codec of its non-nullable form.
  /// </returns>
  private static bool TryGetNullableReferenceCodec(
      ITypeSymbol typeSymbol,
      List<ExpoDiagnosticModel> diagnostics,
      List<ExpoGeneratedRecordCodecModel> recordCodecs,
      out ExpoCodecDescriptor? codec)
  {
    codec = null;

    // Only an explicit annotation activates nullable reference handling. A non-nullable reference
    // and an oblivious reference from a disabled nullable context keep their strict codec, and
    // Nullable<T> stays with TryGetNullableCodec because it is not a reference type.
    if (typeSymbol.NullableAnnotation != NullableAnnotation.Annotated || !typeSymbol.IsReferenceType)
    {
      return false;
    }

    // Only the top-level annotation is removed; nested annotations on type arguments and record
    // fields stay so they resolve their own nullable codecs. Resolving the inner codec from the
    // non-annotated symbol is also what stops this helper from matching its own input.
    var valueType = typeSymbol.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
    if (IsExcludedFromNullableReference(valueType))
    {
      return true;
    }

    // The callback descriptor keeps every position except a parameter rejected, so an annotated
    // callback is always handled here.
    if (IsJavaScriptCallbackType(valueType))
    {
      codec = TryGetJavaScriptCallbackCodec(valueType, diagnostics, recordCodecs, "NullableJavaScriptCallbackCodec");
      return true;
    }

    if (valueType is INamedTypeSymbol namedType)
    {
      var constructedType = namedType.ConstructedFrom.ToDisplayString(
          SymbolDisplayFormat.FullyQualifiedFormat
      );
      if (constructedType == "global::System.Collections.Generic.IReadOnlyList<T>")
      {
        var elementType = namedType.TypeArguments.Single();
        if (AsCompositeChildCodec(GetCodec(elementType, diagnostics, recordCodecs)) is { } elementCodec)
        {
          var elementTypeName = GetCodecTypeArgumentName(elementType);
          codec = (IsScopedResource(elementCodec)
              ? ScopedNullableReferenceCodec(
                  valueType,
                  $"GeneratedScopedListCodec<{elementTypeName}, {elementCodec.Expression}>"
              )
              : OrdinaryCodec($"NullableReadOnlyListCodec<{elementTypeName}, {elementCodec.Expression}>")) with
          {
            Capabilities = elementCodec.Capabilities,
          };
        }

        return true;
      }

      if (constructedType is "global::System.Collections.Generic.Dictionary<TKey, TValue>" or
          "global::System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>")
      {
        if (namedType.TypeArguments[0].SpecialType != SpecialType.System_String)
        {
          return true;
        }

        var dictionaryValueType = namedType.TypeArguments[1];
        if (AsCompositeChildCodec(GetCodec(dictionaryValueType, diagnostics, recordCodecs)) is { } dictionaryValueCodec)
        {
          var dictionaryValueTypeName = GetCodecTypeArgumentName(dictionaryValueType);
          if (IsScopedResource(dictionaryValueCodec))
          {
            codec = ScopedNullableReferenceCodec(
                valueType,
                $"GeneratedScopedDictionaryCodec<{dictionaryValueTypeName}, {dictionaryValueCodec.Expression}>"
            ) with
            {
              Capabilities = dictionaryValueCodec.Capabilities,
            };
          }
          else
          {
            var codecName =
                constructedType == "global::System.Collections.Generic.Dictionary<TKey, TValue>"
                    ? "NullableDictionaryCodec"
                    : "NullableReadOnlyDictionaryCodec";
            codec = OrdinaryCodec($"{codecName}<{dictionaryValueTypeName}, {dictionaryValueCodec.Expression}>") with
            {
              Capabilities = dictionaryValueCodec.Capabilities,
            };
          }
        }

        return true;
      }
    }

    if (AsCompositeChildCodec(GetCodec(valueType, diagnostics, recordCodecs)) is { } valueCodec)
    {
      codec = (IsScopedResource(valueCodec)
          ? ScopedNullableReferenceCodec(valueType, valueCodec.Expression)
          : OrdinaryCodec($"NullableReferenceCodec<{GetCodecTypeArgumentName(valueType)}, {valueCodec.Expression}>")) with
      {
        Capabilities = valueCodec.Capabilities,
      };
    }

    return true;
  }

  // A nullable reference whose content holds a leaf: the scoped nullable-reference codec over the
  // content codec, which is the scoped list or dictionary codec for a collection.
  private static ExpoCodecDescriptor ScopedNullableReferenceCodec(ITypeSymbol valueType, string innerCodec) =>
      ScopedCodec($"GeneratedScopedNullableReferenceCodec<{GetCodecTypeArgumentName(valueType)}, {innerCodec}>");

  private const string EitherMetadataName = "global::Expo.ModulesCore.Either<TFirst, TSecond>";

  private const string Uint8ArrayMetadataName = "global::Expo.ModulesCore.Uint8Array";

  // The ten numeric special types that NumberCodec<T> decodes.
  private static bool IsNumberSpecialType(SpecialType specialType) =>
      specialType is
          SpecialType.System_SByte or
          SpecialType.System_Byte or
          SpecialType.System_Int16 or
          SpecialType.System_UInt16 or
          SpecialType.System_Int32 or
          SpecialType.System_UInt32 or
          SpecialType.System_Int64 or
          SpecialType.System_UInt64 or
          SpecialType.System_Single or
          SpecialType.System_Double;

  // The JavaScript kind a union member decodes from. The names mirror JavaScriptValueKind and the
  // Kind of the leaf codecs, which the managed union codec compares at runtime. A type outside the
  // table, or an annotated one (null is not a kind), is not a union member.
  private static string? GetUnionMemberKind(ITypeSymbol typeSymbol)
  {
    if (typeSymbol.NullableAnnotation == NullableAnnotation.Annotated)
    {
      return null;
    }

    switch (typeSymbol.SpecialType)
    {
      case SpecialType.System_String:
        return "String";
      case SpecialType.System_Boolean:
        return "Bool";
      case var specialType when IsNumberSpecialType(specialType):
        return "Number";
    }

    if (typeSymbol is IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Byte })
    {
      return "ArrayBuffer";
    }

    return typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) is
        ArrayBufferMetadataName or
        "global::System.Memory<byte>" or
        "global::System.ReadOnlyMemory<byte>"
        ? "ArrayBuffer"
        : null;
  }

  private static ExpoCodecDescriptor? GetEitherCodec(
      INamedTypeSymbol typeSymbol,
      List<ExpoDiagnosticModel> diagnostics,
      List<ExpoGeneratedRecordCodecModel> recordCodecs)
  {
    var firstType = typeSymbol.TypeArguments[0];
    var secondType = typeSymbol.TypeArguments[1];

    // The kinds are checked before any member codec resolves, so a rejected union adds no record
    // codec and no diagnostic as a side effect.
    var firstKind = GetUnionMemberKind(firstType);
    var secondKind = GetUnionMemberKind(secondType);
    if (firstKind is null || secondKind is null || firstKind == secondKind)
    {
      return null;
    }

    var firstCodec = AsCompositeChildCodec(GetCodec(firstType, diagnostics, recordCodecs));
    var secondCodec = AsCompositeChildCodec(GetCodec(secondType, diagnostics, recordCodecs));
    if (firstCodec is null || secondCodec is null)
    {
      return null;
    }

    var firstTypeName = GetCodecTypeArgumentName(firstType);
    var secondTypeName = GetCodecTypeArgumentName(secondType);

    // A union is decode-only and accepted only at parameters, and its members must be of different
    // JavaScript kinds.
    if (!IsScopedResource(firstCodec) && !IsScopedResource(secondCodec))
    {
      return OrdinaryCodec(
          $"EitherCodec<{firstTypeName}, {firstCodec.Expression}, {secondTypeName}, {secondCodec.Expression}>"
      ) with
      {
        Capabilities = ExpoCodecCapabilities.DecodeOnly,
      };
    }

    return ScopedCodec(
        $"GeneratedScopedEitherCodec<{firstTypeName}, {GetScopedUnionMemberExpression(firstTypeName, firstCodec)}, " +
        $"{secondTypeName}, {GetScopedUnionMemberExpression(secondTypeName, secondCodec)}>"
    ) with
    {
      Capabilities = ExpoCodecCapabilities.DecodeOnly,
    };
  }

  // A scoped union decodes both members through the scoped protocol, so an ordinary member is
  // wrapped and a scoped member keeps its own codec.
  private static string GetScopedUnionMemberExpression(string typeName, ExpoCodecDescriptor codec) =>
      IsScopedResource(codec)
          ? codec.Expression
          : $"GeneratedScopedOrdinaryCodec<{typeName}, {codec.Expression}>";

  private static ExpoCodecDescriptor? TryGetReadOnlyListCodec(
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
    var elementCodec = AsCompositeChildCodec(GetCodec(elementType, diagnostics, recordCodecs));
    if (elementCodec is null)
    {
      return null;
    }

    var elementTypeName = GetCodecTypeArgumentName(elementType);
    if (IsScopedResource(elementCodec))
    {
      return ScopedCodec($"GeneratedScopedListCodec<{elementTypeName}, {elementCodec.Expression}>") with
      {
        Capabilities = elementCodec.Capabilities,
      };
    }

    return OrdinaryCodec($"JavaScriptArrayCodec<{elementTypeName}, {elementCodec.Expression}>") with
    {
      DecodeOperation = ExpoDecodeOperation.DecodeToArray,
      Capabilities = elementCodec.Capabilities,
    };
  }

  private static ExpoCodecDescriptor? TryGetDictionaryCodec(
      ITypeSymbol typeSymbol,
      List<ExpoDiagnosticModel> diagnostics,
      List<ExpoGeneratedRecordCodecModel> recordCodecs)
  {
    if (typeSymbol is not INamedTypeSymbol namedType)
    {
      return null;
    }

    var constructedType = namedType.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    if (constructedType is not "global::System.Collections.Generic.Dictionary<TKey, TValue>" and
        not "global::System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>")
    {
      return null;
    }

    if (namedType.TypeArguments[0].SpecialType != SpecialType.System_String)
    {
      return null;
    }

    var valueType = namedType.TypeArguments[1];
    var valueCodec = AsCompositeChildCodec(GetCodec(valueType, diagnostics, recordCodecs));
    if (valueCodec is null)
    {
      return null;
    }

    var valueTypeName = GetCodecTypeArgumentName(valueType);
    if (IsScopedResource(valueCodec))
    {
      // One scoped dictionary codec serves both Dictionary and IReadOnlyDictionary.
      return ScopedCodec($"GeneratedScopedDictionaryCodec<{valueTypeName}, {valueCodec.Expression}>") with
      {
        Capabilities = valueCodec.Capabilities,
      };
    }

    return OrdinaryCodec($"JavaScriptDictionaryCodec<{valueTypeName}, {valueCodec.Expression}>") with
    {
      DecodeOperation = ExpoDecodeOperation.DecodeToDictionary,
      Capabilities = valueCodec.Capabilities,
    };
  }

  private static ExpoCodecDescriptor? TryGetRecordCodec(
      INamedTypeSymbol typeSymbol,
      List<ExpoDiagnosticModel> diagnostics,
      List<ExpoGeneratedRecordCodecModel> recordCodecs)
  {
    var recordTypeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    var existing = recordCodecs.FirstOrDefault(codec => codec.RecordTypeName == recordTypeName);
    if (existing is not null)
    {
      return RecordCodecDescriptor(existing);
    }

    var constructor = GetRecordCodecConstructor(typeSymbol);
    if (constructor is null)
    {
      return null;
    }

    var fields = new List<ExpoGeneratedRecordFieldModel>();
    foreach (var parameter in constructor.Parameters)
    {
      var property = typeSymbol.GetMembers().OfType<IPropertySymbol>()
          .FirstOrDefault(item => item.Name == parameter.Name);
      var fieldCodec = GetCodec(parameter.Type, diagnostics, recordCodecs);
      // Composition rule: generated record codecs call field codecs directly with Decode and
      // Encode. A field needs both, and a context-bearing codec has no runtime context there.
      if (property is null ||
          fieldCodec is not
          {
            Capabilities: ExpoCodecCapabilities.DecodeAndEncode,
            ContextRequirement: ExpoContextRequirement.None,
          })
      {
        diagnostics.Add(new ExpoDiagnosticModel(
            ExpoModulesDiagnostics.UnsupportedRecordField.Id,
            parameter.Locations.FirstOrDefault(),
            new EquatableArray<string>(new[] { typeSymbol.Name, parameter.Name, GetDiagnosticTypeName(parameter.Type) })
        ));
        return null;
      }

      fields.Add(new ExpoGeneratedRecordFieldModel(
          LowerCamel(parameter.Name), property.Name, LowerCamel(property.Name),
          parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), fieldCodec,
          parameter.Locations.FirstOrDefault()
      ));
    }

    var codec = new ExpoGeneratedRecordCodecModel(
        $"{SanitizeIdentifier(typeSymbol.Name)}Codec_{EncodeTypeIdentity(recordTypeName)}",
        recordTypeName,
        new EquatableArray<ExpoGeneratedRecordFieldModel>(fields),
        fields.Any(field => IsScopedResource(field.Codec))
            ? ExpoResourceOwnership.ScopedResource
            : ExpoResourceOwnership.Borrowed,
        typeSymbol.IsValueType,
        typeSymbol.Locations.FirstOrDefault()
    );
    recordCodecs.Add(codec);
    return RecordCodecDescriptor(codec);
  }
  private static string GetParameterExpression(
      ExpoParameterModel parameter,
      int index,
      string runtimeParameterName)
  {
    var decodeExpression = GetDecodeExpression(
        parameter.Codec!,
        index,
        runtimeParameterName
    );
    if (!parameter.HasDefaultValue)
    {
      return decodeExpression;
    }

    return $"arguments.Count <= {index} || arguments.GetValue({index}).Kind == global::Expo.JSI.JavaScriptValueKind.Undefined ? {parameter.DefaultValueExpression} : {decodeExpression}";
  }

  private static string GetDecodeExpression(
      ExpoCodecDescriptor codec,
      int index,
      string runtimeParameterName)
  {
    if (IsScopedResource(codec))
    {
      // Scoped decode always calls the scoped Decode and registers the leaves in the scope.
      return $"{codec.Expression}.Decode(arguments.GetValue({index}), {runtimeParameterName}, __expoScope)";
    }

    var methodName = codec.DecodeOperation switch
    {
      ExpoDecodeOperation.DecodeToArray => "DecodeToArray",
      ExpoDecodeOperation.DecodeToDictionary => "DecodeToDictionary",
      _ => "Decode",
    };

    var contextArgument = codec.ContextRequirement == ExpoContextRequirement.DotnetRuntimeContext
        ? ", GeneratedFunction.CurrentRuntimeContext"
        : string.Empty;
    return $"{codec.Expression}.{methodName}(arguments.GetValue({index}), {runtimeParameterName}{contextArgument})";
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

  private static ExpoCodecDescriptor? GetCodec(
      ITypeSymbol typeSymbol,
      List<ExpoDiagnosticModel> diagnostics,
      List<ExpoGeneratedRecordCodecModel> recordCodecs,
      IEnumerable<AttributeData>? usageAttributes = null)
  {
    // An annotated reference must be classified before any concrete type match, because `string?`
    // and `string` are the same symbol and differ only by their nullable annotation.
    if (TryGetNullableReferenceCodec(
            typeSymbol, diagnostics, recordCodecs, out var nullableReferenceCodec))
    {
      return nullableReferenceCodec;
    }

    if (TryGetJavaScriptCallbackCodec(typeSymbol, diagnostics, recordCodecs, "JavaScriptCallbackCodec") is { } callbackCodec)
    {
      return callbackCodec;
    }

    if (typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == ArrayBufferMetadataName)
    {
      return ScopedCodec("ArrayBufferCodec");
    }

    if (typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == Uint8ArrayMetadataName)
    {
      return ScopedCodec("Uint8ArrayCodec");
    }

    if (typeSymbol is IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Byte })
    {
      return OrdinaryCodec("ByteArrayCodec");
    }

    if (typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
        "global::System.Memory<byte>")
    {
      return OrdinaryCodec("MemoryByteCodec");
    }

    if (typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
        "global::System.ReadOnlyMemory<byte>")
    {
      return OrdinaryCodec("ReadOnlyMemoryByteCodec");
    }

    if (typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == JavaScriptValueMetadataName)
    {
      return OrdinaryCodec("JavaScriptValueCodec") with
      {
        ResourceOwnership = ExpoResourceOwnership.ScopedResource,
      };
    }

    // A union never reaches the later branches, even when it is rejected. Either<,>? reaches this
    // branch through TryGetNullableCodec.
    if (typeSymbol is INamedTypeSymbol eitherType &&
        eitherType.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == EitherMetadataName)
    {
      return GetEitherCodec(eitherType, diagnostics, recordCodecs);
    }

    if (TryGetNullableCodec(typeSymbol, diagnostics, recordCodecs) is { } nullableCodec)
    {
      return nullableCodec;
    }

    if (TryGetConvertibleCodec(typeSymbol) is { } convertibleCodec)
    {
      return OrdinaryCodec(convertibleCodec);
    }
    if (typeSymbol.TypeKind == TypeKind.Enum)
    {
      // Usage-level enum metadata wins over enum-type metadata; string remains the default.
      var enumTypeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
      return OrdinaryCodec(
          UsesNumberEnumRepresentation(typeSymbol, usageAttributes)
              ? $"NumberEnumCodec<{enumTypeName}>"
              : $"StringEnumCodec<{enumTypeName}>"
      );
    }
    if (typeSymbol is INamedTypeSymbol { IsRecord: true } recordType)
    {
      return TryGetRecordCodec(recordType, diagnostics, recordCodecs);
    }

    return typeSymbol.SpecialType switch
    {
      SpecialType.System_Boolean => OrdinaryCodec("BoolCodec"),
      SpecialType.System_String => OrdinaryCodec("StringCodec"),
      var specialType when IsNumberSpecialType(specialType) =>
          OrdinaryCodec(GetNumberCodecExpression(typeSymbol)),
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

  private static ExpoCodecDescriptor? TryGetJavaScriptCallbackCodec(
      ITypeSymbol typeSymbol,
      List<ExpoDiagnosticModel> diagnostics,
      List<ExpoGeneratedRecordCodecModel> recordCodecs,
      string codecClassName)
  {
    if (typeSymbol is not INamedTypeSymbol namedType || !IsJavaScriptCallbackType(namedType))
    {
      return null;
    }

    if (namedType.TypeArguments.Length == 1)
    {
      var zeroArgResultType = namedType.TypeArguments[0];
      var zeroArgResultCodec = AsJavaScriptCodecArgument(GetCodec(zeroArgResultType, diagnostics, recordCodecs));
      if (zeroArgResultCodec is null)
      {
        return null;
      }

      return CallbackCodec(
          $"{codecClassName}<{GetCodecTypeArgumentName(zeroArgResultType)}, {zeroArgResultCodec.Expression}>"
      );
    }

    var argsType = namedType.TypeArguments[0];
    var resultType = namedType.TypeArguments[1];
    var argsCodec = TryGetValueTupleArgsCodec(argsType, diagnostics, recordCodecs);
    var resultCodec = AsJavaScriptCodecArgument(GetCodec(resultType, diagnostics, recordCodecs));
    if (argsCodec is null || resultCodec is null)
    {
      return null;
    }

    return CallbackCodec(
        $"{codecClassName}<{GetCodecTypeArgumentName(argsType)}, {argsCodec}, {GetCodecTypeArgumentName(resultType)}, {resultCodec.Expression}>"
    );
  }

  private static ExpoCodecDescriptor CallbackCodec(string expression) => new(
      expression,
      ExpoCodecShape.StaticHelper,
      ExpoCodecCapabilities.DecodeOnly,
      ExpoDecodeOperation.Decode,
      ExpoContextRequirement.DotnetRuntimeContext,
      ExpoResourceOwnership.ContextRetained
  );

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
      var codec = AsJavaScriptCodecArgument(GetCodec(element, diagnostics, recordCodecs));
      if (codec is null)
      {
        return null;
      }

      codecParts.Add(GetCodecTypeArgumentName(element));
      codecParts.Add(codec.Expression);
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

  private static IMethodSymbol? GetRecordCodecConstructor(INamedTypeSymbol typeSymbol) =>
      typeSymbol.InstanceConstructors
          .Where(item =>
              item.Parameters.Length > 0 &&
              item.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal)
          .OrderByDescending(item => item.Parameters.Length)
          .FirstOrDefault();

}
