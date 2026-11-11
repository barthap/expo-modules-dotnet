using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Expo.ModulesCore.Generator;

public sealed partial class ExpoModulesGenerator
{
  // Renders a parameter default as a C# expression of the parameter's own type, so a conditional
  // `undefined ? <default> : <decode>` has that type. The text depends only on the constant, not
  // on the culture or the runtime that hosts the compiler. Codec resolution rejects every type
  // whose constant this does not cover (char, decimal, nint) before a default is rendered.
  private static string GetDefaultValueExpression(ITypeSymbol typeSymbol, object? value)
  {
    var isNullableValueType = typeSymbol is INamedTypeSymbol
    {
      ConstructedFrom.SpecialType: SpecialType.System_Nullable_T,
    };
    if (value is null)
    {
      if (typeSymbol.IsValueType && !isNullableValueType)
      {
        return $"default({typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})";
      }

      // `string Name = null` is accepted (the author gets CS8625). The null-forgiving operator keeps
      // CS8604 out of the generated glue, where the default reaches a non-nullable parameter.
      return typeSymbol.IsReferenceType && typeSymbol.NullableAnnotation != NullableAnnotation.Annotated
          ? "null!"
          : "null";
    }

    if (isNullableValueType)
    {
      typeSymbol = ((INamedTypeSymbol)typeSymbol).TypeArguments.Single();
    }

    if (typeSymbol is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlyingType })
    {
      return $"({typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})({GetEnumUnderlyingLiteral(underlyingType.SpecialType, value)})";
    }

    return typeSymbol.SpecialType switch
    {
      SpecialType.System_Boolean => (bool)value ? "true" : "false",
      SpecialType.System_String => $"\"{EscapeString((string)value)}\"",
      SpecialType.System_SByte => $"(sbyte)({((sbyte)value).ToString(CultureInfo.InvariantCulture)})",
      SpecialType.System_Byte => $"(byte)({((byte)value).ToString(CultureInfo.InvariantCulture)})",
      SpecialType.System_Int16 => $"(short)({((short)value).ToString(CultureInfo.InvariantCulture)})",
      SpecialType.System_UInt16 => $"(ushort)({((ushort)value).ToString(CultureInfo.InvariantCulture)})",
      SpecialType.System_Int32 => ((int)value).ToString(CultureInfo.InvariantCulture),
      SpecialType.System_UInt32 => ((uint)value).ToString(CultureInfo.InvariantCulture) + "U",
      SpecialType.System_Int64 => ((long)value).ToString(CultureInfo.InvariantCulture) + "L",
      SpecialType.System_UInt64 => ((ulong)value).ToString(CultureInfo.InvariantCulture) + "UL",
      SpecialType.System_Single => GetSingleLiteral((float)value),
      SpecialType.System_Double => GetDoubleLiteral((double)value),
      // Unreachable: a type outside this table has no codec, and the caller reports that first.
      _ => throw new InvalidOperationException(
          $"Unsupported default value type: {typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}"
      ),
    };
  }

  // An enum value of any underlying type fits the literal of that type, and an explicit enum cast
  // from it always compiles, also for a [Flags] combination or a value that names no member.
  private static string GetEnumUnderlyingLiteral(SpecialType underlyingType, object value) => underlyingType switch
  {
    SpecialType.System_UInt32 => ((uint)value).ToString(CultureInfo.InvariantCulture) + "U",
    SpecialType.System_Int64 => ((long)value).ToString(CultureInfo.InvariantCulture) + "L",
    SpecialType.System_UInt64 => ((ulong)value).ToString(CultureInfo.InvariantCulture) + "UL",
    _ => Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
  };

  private static string GetDoubleLiteral(double value)
  {
    if (double.IsNaN(value)) return "global::System.Double.NaN";
    if (double.IsPositiveInfinity(value)) return "global::System.Double.PositiveInfinity";
    if (double.IsNegativeInfinity(value)) return "global::System.Double.NegativeInfinity";
    if (value == 0 && double.IsNegativeInfinity(1 / value)) return "-0.0";
    return value.ToString("G17", CultureInfo.InvariantCulture);
  }

  private static string GetSingleLiteral(float value)
  {
    if (float.IsNaN(value)) return "global::System.Single.NaN";
    if (float.IsPositiveInfinity(value)) return "global::System.Single.PositiveInfinity";
    if (float.IsNegativeInfinity(value)) return "global::System.Single.NegativeInfinity";
    if (value == 0 && float.IsNegativeInfinity(1 / value)) return "-0.0F";
    return value.ToString("G9", CultureInfo.InvariantCulture) + "F";
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

  private static bool HoldsLeaves(ExpoCodecDescriptor? codec) =>
      codec?.ResourceOwnership == ExpoResourceOwnership.HoldsLeaves;

  // A list, dictionary, nullable, or union codec holds leaves when a child does, and releases them
  // through the child codec.
  private static ExpoCodecDescriptor CompositeCodec(ExpoCodecDescriptor codec, ExpoCodecDescriptor child) =>
      HoldsLeaves(child) ? codec with { ResourceOwnership = ExpoResourceOwnership.HoldsLeaves } : codec;

  // Composition rule: a list element, dictionary value, Nullable<T> inner, nullable-reference
  // inner type, callback result, callback argument tuple element, and typed event payload are
  // generic type arguments that need IJavaScriptCodec<T>. A StaticHelper codec is a static class
  // and cannot be one.
  private static ExpoCodecDescriptor? AsCompositeChildCodec(ExpoCodecDescriptor? codec) =>
      codec is { Shape: ExpoCodecShape.JavaScriptCodec } ? codec : null;

  // A record codec holds leaves when any field does. The cache-hit path rebuilds the descriptor
  // from the stored ownership, not from the type name alone.
  private static ExpoCodecDescriptor RecordCodecDescriptor(ExpoGeneratedRecordCodecModel codec) =>
      OrdinaryCodec(codec.CodecTypeName) with { ResourceOwnership = codec.ResourceOwnership };

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
      ExpoCodecCollector codecs)
  {
    if (typeSymbol is not INamedTypeSymbol namedType ||
        namedType.ConstructedFrom.SpecialType != SpecialType.System_Nullable_T)
    {
      return null;
    }

    var valueType = namedType.TypeArguments.Single();
    var valueCodec = AsCompositeChildCodec(GetCodec(valueType, diagnostics, codecs));
    if (valueCodec is null)
    {
      return null;
    }

    return CompositeCodec(
        OrdinaryCodec($"NullableCodec<{GetCodecTypeArgumentName(valueType)}, {valueCodec.Expression}>"),
        valueCodec
    );
  }

  // JavaScriptValue already represents JavaScript null and undefined, so an annotated use is
  // rejected instead of silently converted.
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
      ExpoCodecCollector codecs,
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
      codec = TryGetJavaScriptCallbackCodec(valueType, diagnostics, codecs, "NullableJavaScriptCallbackCodec");
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
        if (AsCompositeChildCodec(GetCodec(elementType, diagnostics, codecs)) is { } elementCodec)
        {
          codec = CompositeCodec(
              OrdinaryCodec($"NullableReadOnlyListCodec<{GetCodecTypeArgumentName(elementType)}, {elementCodec.Expression}>"),
              elementCodec
          );
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
        if (AsCompositeChildCodec(GetCodec(dictionaryValueType, diagnostics, codecs)) is { } dictionaryValueCodec)
        {
          var codecName =
              constructedType == "global::System.Collections.Generic.Dictionary<TKey, TValue>"
                  ? "NullableDictionaryCodec"
                  : "NullableReadOnlyDictionaryCodec";
          codec = CompositeCodec(
              OrdinaryCodec($"{codecName}<{GetCodecTypeArgumentName(dictionaryValueType)}, {dictionaryValueCodec.Expression}>"),
              dictionaryValueCodec
          );
        }

        return true;
      }
    }

    if (AsCompositeChildCodec(GetCodec(valueType, diagnostics, codecs)) is { } valueCodec)
    {
      codec = CompositeCodec(
          OrdinaryCodec($"NullableReferenceCodec<{GetCodecTypeArgumentName(valueType)}, {valueCodec.Expression}>"),
          valueCodec
      );
    }

    return true;
  }

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
  // table, or an annotated one (null is not a kind), is not a union member. A Uint8Array is an
  // Object, and its codec then proves that the object is a Uint8Array.
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

    return typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) switch
    {
      ArrayBufferMetadataName or
      "global::System.Memory<byte>" or
      "global::System.ReadOnlyMemory<byte>" => "ArrayBuffer",
      Uint8ArrayMetadataName => "Object",
      _ => null,
    };
  }

  // A union is one ordinary codec. Its members must be of different JavaScript kinds, and it holds
  // leaves when a member does.
  private static ExpoCodecDescriptor? GetEitherCodec(
      INamedTypeSymbol typeSymbol,
      List<ExpoDiagnosticModel> diagnostics,
      ExpoCodecCollector codecs)
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

    var firstCodec = AsCompositeChildCodec(GetCodec(firstType, diagnostics, codecs));
    var secondCodec = AsCompositeChildCodec(GetCodec(secondType, diagnostics, codecs));
    if (firstCodec is null || secondCodec is null)
    {
      return null;
    }

    var codec = OrdinaryCodec(
        $"EitherCodec<{GetCodecTypeArgumentName(firstType)}, {firstCodec.Expression}, " +
        $"{GetCodecTypeArgumentName(secondType)}, {secondCodec.Expression}>"
    );
    return CompositeCodec(CompositeCodec(codec, firstCodec), secondCodec);
  }

  private static ExpoCodecDescriptor? TryGetReadOnlyListCodec(
      ITypeSymbol typeSymbol,
      List<ExpoDiagnosticModel> diagnostics,
      ExpoCodecCollector codecs)
  {
    if (typeSymbol is not INamedTypeSymbol namedType ||
        namedType.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) !=
        "global::System.Collections.Generic.IReadOnlyList<T>")
    {
      return null;
    }

    var elementType = namedType.TypeArguments.Single();
    var elementCodec = AsCompositeChildCodec(GetCodec(elementType, diagnostics, codecs));
    if (elementCodec is null)
    {
      return null;
    }

    return CompositeCodec(
        OrdinaryCodec($"JavaScriptArrayCodec<{GetCodecTypeArgumentName(elementType)}, {elementCodec.Expression}>") with
        {
          DecodeOperation = ExpoDecodeOperation.DecodeToArray,
        },
        elementCodec
    );
  }

  private static ExpoCodecDescriptor? TryGetDictionaryCodec(
      ITypeSymbol typeSymbol,
      List<ExpoDiagnosticModel> diagnostics,
      ExpoCodecCollector codecs)
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
    var valueCodec = AsCompositeChildCodec(GetCodec(valueType, diagnostics, codecs));
    if (valueCodec is null)
    {
      return null;
    }

    return CompositeCodec(
        OrdinaryCodec($"JavaScriptDictionaryCodec<{GetCodecTypeArgumentName(valueType)}, {valueCodec.Expression}>") with
        {
          DecodeOperation = ExpoDecodeOperation.DecodeToDictionary,
        },
        valueCodec
    );
  }

  private static ExpoCodecDescriptor? TryGetRecordCodec(
      INamedTypeSymbol typeSymbol,
      List<ExpoDiagnosticModel> diagnostics,
      ExpoCodecCollector codecs)
  {
    var recordTypeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    var existing = codecs.Records.FirstOrDefault(codec => codec.RecordTypeName == recordTypeName);
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
      // Checked from the symbol before the field codec resolves, so a rejected record adds no
      // codec for the struct it holds.
      if (GetRecordFieldDefaultRejection(parameter) is { } rejection)
      {
        diagnostics.Add(new ExpoDiagnosticModel(
            ExpoModulesDiagnostics.UnsupportedRecordField.Id,
            parameter.Locations.FirstOrDefault(),
            new EquatableArray<string>(new[] { typeSymbol.Name, parameter.Name, rejection })
        ));
        return null;
      }

      var property = typeSymbol.GetMembers().OfType<IPropertySymbol>()
          .FirstOrDefault(item => item.Name == parameter.Name);
      var fieldCodec = GetCodecAt(parameter.Locations.FirstOrDefault(), parameter.Type, diagnostics, codecs);
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
            new EquatableArray<string>(new[]
            {
                typeSymbol.Name,
                parameter.Name,
                $"uses unsupported type '{GetDiagnosticTypeName(parameter.Type)}'",
            })
        ));
        return null;
      }

      // The default is rendered only after the codec check above, so a field type without a
      // codec keeps its unsupported-type diagnostic.
      fields.Add(new ExpoGeneratedRecordFieldModel(
          LowerCamel(parameter.Name), property.Name, LowerCamel(property.Name),
          parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), fieldCodec,
          GetRecordFieldKind(parameter),
          parameter.HasExplicitDefaultValue
              ? GetDefaultValueExpression(parameter.Type, parameter.ExplicitDefaultValue)
              : string.Empty,
          parameter.Locations.FirstOrDefault()
      ));
    }

    var codec = new ExpoGeneratedRecordCodecModel(
        $"{SanitizeIdentifier(typeSymbol.Name)}Codec_{EncodeTypeIdentity(recordTypeName)}",
        recordTypeName,
        new EquatableArray<ExpoGeneratedRecordFieldModel>(fields),
        fields.Any(field => HoldsLeaves(field.Codec))
            ? ExpoResourceOwnership.HoldsLeaves
            : ExpoResourceOwnership.Borrowed,
        typeSymbol.IsValueType,
        typeSymbol.Locations.FirstOrDefault()
    );
    codecs.Records.Add(codec);
    return RecordCodecDescriptor(codec);
  }

  // The upstream @Record rule: a default makes a field optional, an optional type makes it
  // nullable and optional, and every other field is required. JavaScriptValue already represents
  // null and undefined, and JavaScriptValue? is rejected, so it is optional by type. An oblivious
  // reference (nullable context disabled) is not annotated and stays required.
  private static ExpoRecordFieldKind GetRecordFieldKind(IParameterSymbol parameter)
  {
    if (parameter.HasExplicitDefaultValue)
    {
      return ExpoRecordFieldKind.Defaulted;
    }

    var type = parameter.Type;
    var isOptionalByType =
        type is INamedTypeSymbol { ConstructedFrom.SpecialType: SpecialType.System_Nullable_T } ||
        (type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.Annotated) ||
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == JavaScriptValueMetadataName;
    return isOptionalByType ? ExpoRecordFieldKind.OptionalByType : ExpoRecordFieldKind.Required;
  }

  // A default on a JavaScriptValue field, or on a record struct field that holds one, can only be
  // C# null or a struct holding null. That is not a JavaScript undefined value, and
  // JavaScriptValueCodec.Encode rejects it. A method parameter default is not a record field and
  // keeps its behavior.
  private static string? GetRecordFieldDefaultRejection(IParameterSymbol parameter)
  {
    if (!parameter.HasExplicitDefaultValue)
    {
      return null;
    }

    var typeName = GetDiagnosticTypeName(parameter.Type);
    if (parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == JavaScriptValueMetadataName)
    {
      return $"of type '{typeName}' cannot have a default value";
    }

    return HoldsJavaScriptValueByValue(parameter.Type)
        ? $"of type '{typeName}' cannot have a default value because the type holds a JavaScriptValue"
        : null;
  }

  // A record struct holds a JavaScriptValue when a constructor field is one, or is a record struct
  // that holds one. A struct cannot contain itself by value (CS0523), so the recursion ends.
  // Nullable<T> is not followed: its default is null, which encodes as JavaScript null.
  private static bool HoldsJavaScriptValueByValue(ITypeSymbol typeSymbol) =>
      typeSymbol is INamedTypeSymbol { IsRecord: true, IsValueType: true } recordType &&
      GetRecordCodecConstructor(recordType) is { } constructor &&
      constructor.Parameters.Any(parameter =>
          parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == JavaScriptValueMetadataName ||
          HoldsJavaScriptValueByValue(parameter.Type));
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

  // Every resolved codec carries its value type with the nullable annotation, so generated glue
  // can declare leaf-bearing locals and release them without re-deriving the type.
  private static ExpoCodecDescriptor? GetCodec(
      ITypeSymbol typeSymbol,
      List<ExpoDiagnosticModel> diagnostics,
      ExpoCodecCollector codecs,
      IEnumerable<AttributeData>? usageAttributes = null) =>
      ResolveCodec(typeSymbol, diagnostics, codecs, usageAttributes) is { } codec
          ? codec with { ValueTypeName = GetCodecTypeArgumentName(typeSymbol) }
          : null;

  // Resolves the codec of a boundary position and records where it is used, so a union declared in
  // a referenced assembly is reported at that position.
  private static ExpoCodecDescriptor? GetCodecAt(
      Location? useSite,
      ITypeSymbol typeSymbol,
      List<ExpoDiagnosticModel> diagnostics,
      ExpoCodecCollector codecs,
      IEnumerable<AttributeData>? usageAttributes = null)
  {
    var previous = codecs.UseSite;
    codecs.UseSite = useSite;
    try
    {
      return GetCodec(typeSymbol, diagnostics, codecs, usageAttributes);
    }
    finally
    {
      codecs.UseSite = previous;
    }
  }

  private static ExpoCodecDescriptor? ResolveCodec(
      ITypeSymbol typeSymbol,
      List<ExpoDiagnosticModel> diagnostics,
      ExpoCodecCollector codecs,
      IEnumerable<AttributeData>? usageAttributes)
  {
    // An annotated reference must be classified before any concrete type match, because `string?`
    // and `string` are the same symbol and differ only by their nullable annotation.
    if (TryGetNullableReferenceCodec(
            typeSymbol, diagnostics, codecs, out var nullableReferenceCodec))
    {
      return nullableReferenceCodec;
    }

    if (TryGetJavaScriptCallbackCodec(typeSymbol, diagnostics, codecs, "JavaScriptCallbackCodec") is { } callbackCodec)
    {
      return callbackCodec;
    }

    if (typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == ArrayBufferMetadataName)
    {
      return OrdinaryCodec("ArrayBufferCodec") with { ResourceOwnership = ExpoResourceOwnership.HoldsLeaves };
    }

    if (typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == Uint8ArrayMetadataName)
    {
      return OrdinaryCodec("Uint8ArrayCodec") with { ResourceOwnership = ExpoResourceOwnership.HoldsLeaves };
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
        ResourceOwnership = ExpoResourceOwnership.HoldsLeaves,
      };
    }

    // A union is classified before the record branch: it is an abstract record, and its cases are
    // positional records that must never become record codecs.
    if (typeSymbol is INamedTypeSymbol unionType && IsUnionType(unionType))
    {
      return TryGetUnionCodec(unionType, diagnostics, codecs);
    }

    if (IsUnionCaseType(typeSymbol))
    {
      return null;
    }

    // A union never reaches the later branches, even when it is rejected. Either<,>? reaches this
    // branch through TryGetNullableCodec.
    if (typeSymbol is INamedTypeSymbol eitherType &&
        eitherType.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == EitherMetadataName)
    {
      return GetEitherCodec(eitherType, diagnostics, codecs);
    }

    if (TryGetNullableCodec(typeSymbol, diagnostics, codecs) is { } nullableCodec)
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
      return TryGetRecordCodec(recordType, diagnostics, codecs);
    }

    return typeSymbol.SpecialType switch
    {
      SpecialType.System_Boolean => OrdinaryCodec("BoolCodec"),
      SpecialType.System_String => OrdinaryCodec("StringCodec"),
      var specialType when IsNumberSpecialType(specialType) =>
          OrdinaryCodec(GetNumberCodecExpression(typeSymbol)),
      _ => TryGetReadOnlyListCodec(typeSymbol, diagnostics, codecs) ??
          TryGetDictionaryCodec(typeSymbol, diagnostics, codecs),
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

    if (IsUnionType(namedType))
    {
      // A union is an abstract record with no record constructor; walk its case payloads.
      return GetUnionCaseCandidates(namedType)
          .Select(candidate => GetRecordCodecConstructor(candidate))
          .Where(caseConstructor => caseConstructor is not null)
          .SelectMany(caseConstructor => caseConstructor!.Parameters)
          .Any(parameter => ContainsJavaScriptCallback(parameter.Type, visitedTypes));
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
      ExpoCodecCollector codecs,
      string codecClassName)
  {
    if (typeSymbol is not INamedTypeSymbol namedType || !IsJavaScriptCallbackType(namedType))
    {
      return null;
    }

    if (namedType.TypeArguments.Length == 1)
    {
      var zeroArgResultType = namedType.TypeArguments[0];
      var zeroArgResultCodec = AsCompositeChildCodec(GetCodec(zeroArgResultType, diagnostics, codecs));
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
    var argsCodec = TryGetValueTupleArgsCodec(argsType, diagnostics, codecs);
    var resultCodec = AsCompositeChildCodec(GetCodec(resultType, diagnostics, codecs));
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
      ExpoCodecCollector codecs)
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
      var codec = AsCompositeChildCodec(GetCodec(element, diagnostics, codecs));
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
