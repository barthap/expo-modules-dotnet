using Expo.JSI;

namespace Expo.ModulesCore;

/// <summary>Thrown when no case of a <c>[Union]</c> type takes a JavaScript value.</summary>
/// <remarks>
/// The message is <c>'&lt;Union&gt;' expected &lt;T1&gt; or &lt;T2&gt;, but received &lt;kind&gt;</c>, as in
/// upstream iOS. The code derives from the class name: <c>ERR_UNION_CASE_MISMATCH</c>. The inner
/// exception is the failure of the last attempted case, or <c>null</c> when every case was skipped
/// by its JavaScript kind.
/// </remarks>
public sealed class UnionCaseMismatchException : CodedException
{
  /// <summary>Creates the error for a value of kind <paramref name="received"/>.</summary>
  public UnionCaseMismatchException(
      string unionName,
      IReadOnlyList<string> expected,
      JavaScriptValueKind received,
      Exception? innerException = null)
      : this(
          unionName ?? throw new ArgumentNullException(nameof(unionName)),
          (expected ?? throw new ArgumentNullException(nameof(expected))).ToArray(),
          GetReceivedName(received),
          innerException)
  {
  }

  private UnionCaseMismatchException(string unionName, string[] expected, string received, Exception? innerException)
      : base($"'{unionName}' expected {string.Join(" or ", expected)}, but received {received}", innerException)
  {
    UnionName = unionName;
    Expected = expected;
    Received = received;
  }

  /// <summary>Gets the simple type name of the union.</summary>
  public string UnionName { get; }

  /// <summary>Gets the payload types of the cases, in declaration order.</summary>
  public IReadOnlyList<string> Expected { get; }

  /// <summary>Gets the JavaScript <c>typeof</c> name of the value, with <c>null</c> and <c>ArrayBuffer</c>.</summary>
  public string Received { get; }

  private static string GetReceivedName(JavaScriptValueKind kind) => kind switch
  {
    JavaScriptValueKind.Undefined => "undefined",
    JavaScriptValueKind.Null => "null",
    JavaScriptValueKind.Bool => "boolean",
    JavaScriptValueKind.Number => "number",
    JavaScriptValueKind.String => "string",
    JavaScriptValueKind.Object => "object",
    JavaScriptValueKind.Function => "function",
    JavaScriptValueKind.ArrayBuffer => "ArrayBuffer",
    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown JavaScript value kind."),
  };
}
