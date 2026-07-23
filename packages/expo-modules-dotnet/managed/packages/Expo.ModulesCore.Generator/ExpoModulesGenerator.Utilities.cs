using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Expo.ModulesCore.Generator;

public sealed partial class ExpoModulesGenerator
{
  private static string GetTypeDeclarationKind(SyntaxNode node) => node switch
  {
    ClassDeclarationSyntax => "class",
    RecordDeclarationSyntax record when record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) =>
        "record struct",
    RecordDeclarationSyntax => "record",
    _ => "class",
  };

  private static string EncodeTypeIdentity(string value)
  {
    var builder = new StringBuilder(value.Length * 4);
    foreach (var character in value)
    {
      builder.Append(((ushort)character).ToString("X4", CultureInfo.InvariantCulture));
    }
    return builder.ToString();
  }

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
