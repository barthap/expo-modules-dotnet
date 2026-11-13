using System.Globalization;
using System.Text;

namespace Expo.ModulesCore;

/// <summary>
/// An exception that JavaScript receives as an <c>Error</c> with a stable <c>code</c> property.
/// </summary>
/// <remarks>
/// Throw a subclass from a module member. JavaScript gets <c>error.code</c> from <see cref="Code" />
/// and <c>error.message</c> from <see cref="Exception.Message" /> followed by the inner exception
/// chain. Without an explicit code, the code comes from the class name:
/// <c>InvalidPathException</c> gives <c>ERR_INVALID_PATH</c>. A null or empty code gives an
/// <c>Error</c> without a <c>code</c> property.
/// </remarks>
public class CodedException : Exception
{
  private readonly string? explicitCode;

  /// <summary>Creates an exception whose code is derived from the class name.</summary>
  public CodedException(string message, Exception? innerException = null)
      : base(message, innerException)
  {
  }

  /// <summary>
  /// Creates an exception with an explicit code. A null <paramref name="code" /> derives the code
  /// from the class name; an empty one gives no <c>code</c> property.
  /// </summary>
  public CodedException(string? code, string message, Exception? innerException = null)
      : base(message, innerException)
  {
    explicitCode = code;
  }

  /// <summary>Gets the code that JavaScript reads as <c>error.code</c>.</summary>
  public virtual string Code => explicitCode ?? DeriveCode(GetType().Name);

  // The iOS rule (CodedError.swift errorCodeFromString) on the CLR type name: drop the generic
  // arity suffix, drop one trailing Exception or Error, apply (.)([A-Z]) -> $1_$2 left to right
  // without overlapping pairs, uppercase invariantly, prefix ERR_. Runs only when Code is read.
  // @ref docs/specs/modules-core-functions-and-properties.md "Coded Exceptions"
  internal static string DeriveCode(string typeName)
  {
    var name = typeName;
    var arity = name.IndexOf('`');
    if (arity >= 0)
    {
      name = name[..arity];
    }
    if (name.EndsWith("Exception", StringComparison.Ordinal))
    {
      name = name[..^"Exception".Length];
    }
    else if (name.EndsWith("Error", StringComparison.Ordinal))
    {
      name = name[..^"Error".Length];
    }

    var builder = new StringBuilder("ERR_", 4 + (name.Length * 2));
    var index = 0;
    while (index < name.Length)
    {
      if (index + 1 < name.Length && char.IsAsciiLetterUpper(name[index + 1]))
      {
        builder.Append(name[index]).Append('_').Append(name[index + 1]);
        index += 2;
      }
      else
      {
        builder.Append(name[index]);
        index++;
      }
    }
    return builder.ToString().ToUpper(CultureInfo.InvariantCulture);
  }
}
