using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Expo.ModulesCore.Generator;

public sealed partial class ExpoModulesGenerator
{
  // One enum member as the generator reads it, from source or from metadata. SetsValue is true when
  // an [EnumMember] sets its Value named argument, also to null. JavaScriptName is then that Value,
  // and otherwise the member's C# name.
  private sealed record EnumMemberInfo(IFieldSymbol Field, bool SetsValue, string? JavaScriptName);

  private static bool IsEnumDeclarationDiagnostic(string descriptorId) =>
      descriptorId == ExpoModulesDiagnostics.InvalidEnumJavaScriptValue.Id ||
      descriptorId == ExpoModulesDiagnostics.DuplicateEnumJavaScriptValue.Id ||
      descriptorId == ExpoModulesDiagnostics.AliasedStringEnumMember.Id;

  // The members in declaration order. Roslyn lists source and metadata fields in declaration order
  // and does not list the value__ field of a metadata enum.
  private static IReadOnlyList<EnumMemberInfo> GetEnumMembers(INamedTypeSymbol enumType) =>
      enumType.GetMembers()
          .OfType<IFieldSymbol>()
          .Where(field => field.HasConstantValue)
          .Select(field => TryGetEnumMemberValue(field, out var value)
              ? new EnumMemberInfo(field, true, value)
              : new EnumMemberInfo(field, false, field.Name))
          .ToArray();

  // Matched by metadata name; only the Value named argument is read. [EnumMember] without Value means
  // the member name, as it does for DataContractSerializer.
  private static bool TryGetEnumMemberValue(IFieldSymbol field, out string? value)
  {
    foreach (var attribute in field.GetAttributes())
    {
      if (attribute.AttributeClass?.ToDisplayString() != EnumMemberAttributeMetadataName)
      {
        continue;
      }

      foreach (var argument in attribute.NamedArguments)
      {
        if (argument.Key == "Value")
        {
          value = argument.Value.Value as string;
          return true;
        }
      }
    }

    value = null;
    return false;
  }

  // Strings cross through a strict UTF-8 encoder, so a lone surrogate can never encode.
  private static bool HasLoneSurrogate(string value)
  {
    for (var index = 0; index < value.Length; index++)
    {
      if (char.IsHighSurrogate(value[index]) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
      {
        index++;
        continue;
      }

      if (char.IsSurrogate(value[index]))
      {
        return true;
      }
    }

    return false;
  }

  private static string? GetEnumValueRejection(EnumMemberInfo member)
  {
    if (!member.SetsValue)
    {
      return null;
    }

    if (member.JavaScriptName is null)
    {
      return "[EnumMember] Value is null; remove Value to cross as the member name";
    }

    if (member.JavaScriptName.Length == 0)
    {
      return "[EnumMember] Value is empty";
    }

    return HasLoneSurrogate(member.JavaScriptName)
        ? "[EnumMember] Value contains a lone surrogate, which cannot cross to JavaScript"
        : null;
  }

  // The enum's full C# name as the runtime error messages show it: namespaces and containing types,
  // type arguments qualified too, no `global::`, and keyword identifiers unescaped.
  private static readonly SymbolDisplayFormat EnumDisplayNameFormat = new(
      typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
      genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

  // Builds an EXPOJSI033 to 035 diagnostic: the enum's name first, then the arguments, then the
  // declaration suffix. A member of an enum declared in this compilation is reported at the member.
  // An enum from a referenced assembly is reported where it is used, and the message names the
  // assembly. The test compares assembly symbols, never location kinds: an IDE project reference
  // yields source symbols of another compilation, and Roslyn rejects a diagnostic located there.
  private static ExpoDiagnosticModel CreateEnumDiagnostic(
      DiagnosticDescriptor descriptor,
      INamedTypeSymbol enumType,
      Location? memberLocation,
      ExpoCodecCollector codecs,
      params string[] arguments)
  {
    var declaredHere = SymbolEqualityComparer.Default.Equals(enumType.ContainingAssembly, codecs.Assembly);
    var suffix = declaredHere
        ? string.Empty
        : $" (declared in referenced assembly '{enumType.ContainingAssembly.Name}')";
    return new ExpoDiagnosticModel(
        descriptor.Id,
        declaredHere ? memberLocation : codecs.UseSite,
        new EquatableArray<string>(
            new[] { enumType.ToDisplayString() }.Concat(arguments).Concat(new[] { suffix })));
  }

  // EXPOJSI033 for each invalid value, then at most one pair diagnostic per later member: EXPOJSI035
  // when it has the value of an earlier member, else EXPOJSI034 when it crosses as the string of an
  // earlier member. A pair with both reports only EXPOJSI035, because the shared value is the cause.
  private static List<ExpoDiagnosticModel> ValidateStringEnum(
      INamedTypeSymbol enumType,
      IReadOnlyList<EnumMemberInfo> members,
      ExpoCodecCollector codecs)
  {
    var errors = new List<ExpoDiagnosticModel>();
    foreach (var member in members)
    {
      if (GetEnumValueRejection(member) is { } reason)
      {
        errors.Add(CreateEnumDiagnostic(
            ExpoModulesDiagnostics.InvalidEnumJavaScriptValue,
            enumType,
            member.Field.Locations.FirstOrDefault(),
            codecs,
            member.Field.Name,
            reason));
      }
    }

    for (var later = 1; later < members.Count; later++)
    {
      var member = members[later];
      var earlierMembers = members.Take(later).ToArray();
      var alias = earlierMembers.FirstOrDefault(earlier =>
          object.Equals(earlier.Field.ConstantValue, member.Field.ConstantValue));
      if (alias is not null)
      {
        errors.Add(CreateEnumDiagnostic(
            ExpoModulesDiagnostics.AliasedStringEnumMember,
            enumType,
            member.Field.Locations.FirstOrDefault(),
            codecs,
            alias.Field.Name,
            member.Field.Name,
            Convert.ToString(member.Field.ConstantValue, CultureInfo.InvariantCulture) ?? string.Empty));
        continue;
      }

      if (GetEnumValueRejection(member) is not null)
      {
        continue;
      }

      var duplicate = earlierMembers.FirstOrDefault(earlier =>
          GetEnumValueRejection(earlier) is null &&
          string.Equals(earlier.JavaScriptName, member.JavaScriptName, StringComparison.Ordinal));
      if (duplicate is not null)
      {
        errors.Add(CreateEnumDiagnostic(
            ExpoModulesDiagnostics.DuplicateEnumJavaScriptValue,
            enumType,
            member.Field.Locations.FirstOrDefault(),
            codecs,
            duplicate.Field.Name,
            member.Field.Name,
            member.JavaScriptName!));
      }
    }

    return errors;
  }

  // Resolves an enum at one codec position. A Value on an enum whose own [JSEnum] is Number is a
  // declaration error at every position. A usage-level Number on an enum with a Value is an error at
  // that position only. The string checks run only where the enum crosses as a string, so a number
  // enum may alias members.
  private static ExpoCodecDescriptor? TryGetEnumCodec(
      INamedTypeSymbol enumType,
      List<ExpoDiagnosticModel> diagnostics,
      ExpoCodecCollector codecs,
      IEnumerable<AttributeData>? usageAttributes)
  {
    var enumTypeName = enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    var typeRepresentation = GetEnumRepresentation(enumType.GetAttributes());
    var members = GetEnumMembers(enumType);
    var valuedMembers = members.Where(member => member.SetsValue).ToArray();
    if (typeRepresentation == NumberEnumRepresentation && valuedMembers.Length > 0)
    {
      foreach (var member in valuedMembers)
      {
        diagnostics.Add(CreateEnumDiagnostic(
            ExpoModulesDiagnostics.InvalidEnumJavaScriptValue,
            enumType,
            member.Field.Locations.FirstOrDefault(),
            codecs,
            member.Field.Name,
            "it sets [EnumMember] Value, but [JSEnum(EnumRepresentation.Number)] on the enum crosses it as a number"));
      }

      return null;
    }

    if (UsesNumberEnumRepresentation(enumType, usageAttributes))
    {
      if (valuedMembers.Length > 0)
      {
        diagnostics.Add(CreateEnumDiagnostic(
            ExpoModulesDiagnostics.InvalidEnumJavaScriptValue,
            enumType,
            codecs.UseSite,
            codecs,
            valuedMembers[0].Field.Name,
            "it sets [EnumMember] Value, but [JSEnum(EnumRepresentation.Number)] at this use crosses it as a number"));
        return null;
      }

      return OrdinaryCodec($"NumberEnumCodec<{enumTypeName}>");
    }

    var existing = codecs.Enums.FirstOrDefault(model => model.EnumTypeName == enumTypeName);
    if (existing is not null)
    {
      return OrdinaryCodec(existing.CodecTypeName);
    }

    var errors = ValidateStringEnum(enumType, members, codecs);
    if (errors.Count > 0)
    {
      diagnostics.AddRange(errors);
      return null;
    }

    var codec = new ExpoGeneratedEnumCodecModel(
        $"{SanitizeIdentifier(enumType.Name)}EnumCodec_{EncodeTypeIdentity(enumTypeName)}",
        enumTypeName,
        enumType.ToDisplayString(EnumDisplayNameFormat),
        enumType.EnumUnderlyingType!.ToDisplayString(),
        new EquatableArray<ExpoGeneratedEnumMemberModel>(members.Select(member =>
            new ExpoGeneratedEnumMemberModel(member.Field.Name, member.JavaScriptName!))));
    codecs.Enums.Add(codec);
    return OrdinaryCodec(codec.CodecTypeName);
  }
}
