using System.IO;
using Xunit;

namespace Expo.ModulesCore.Tests.Errors;

public sealed class InvalidPathException(string message, Exception? innerException = null)
    : CodedException(message, innerException);

public sealed class PathException<T>(string message) : CodedException(message);

public sealed class EmptyCodeException(string message) : CodedException(string.Empty, message);

public sealed class RephrasedException(Exception? innerException = null)
    : CodedException("original", innerException)
{
  public override string Code => "ERR_REPHRASED";

  public override string Message => "rephrased";
}

public static class CodedErrorCases
{
  // kind -> expected JavaScript code (null: no code property) and message.
  public static IReadOnlyDictionary<string, (string? Code, string Message)> Expected { get; } =
      new Dictionary<string, (string?, string)>
      {
        ["derived"] = ("ERR_INVALID_PATH", "bad path"),
        ["explicit"] = ("ERR_CUSTOM", "custom code"),
        ["rephrased"] = ("ERR_REPHRASED", "rephrased"),
        ["empty"] = (null, "empty code"),
        ["plain"] = (null, "plain"),
        ["generic"] = ("ERR_PATH", "generic path"),
        // One cause.
        ["chain"] = ("ERR_CODED", "outer\n→ Caused by: inner"),
        // Two causes: plain outer, coded middle, plain root. Only the thrown exception gives a code.
        ["chain3"] = (null, "outer\n→ Caused by: middle\n→ Caused by: root"),
        // Coded outer over plain causes; the walk continues past a plain cause.
        ["codedOuter"] = ("ERR_SAVE", "outer\n→ Caused by: middle\n→ Caused by: root"),
        // An overridden Message keeps the chain.
        ["rephrasedChain"] = ("ERR_REPHRASED", "rephrased\n→ Caused by: inner"),
      };

  public static Exception Create(string kind) => kind switch
  {
    "derived" => new InvalidPathException("bad path"),
    "explicit" => new CodedException("ERR_CUSTOM", "custom code"),
    "rephrased" => new RephrasedException(),
    "empty" => new EmptyCodeException("empty code"),
    "plain" => new InvalidOperationException("plain"),
    "generic" => new PathException<int>("generic path"),
    "chain" => new CodedException("outer", new IOException("inner")),
    "chain3" => new InvalidOperationException(
        "outer",
        new InvalidPathException("middle", new IOException("root"))
    ),
    "codedOuter" => new CodedException(
        "ERR_SAVE",
        "outer",
        new InvalidOperationException("middle", new IOException("root"))
    ),
    "rephrasedChain" => new RephrasedException(new IOException("inner")),
    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown error case."),
  };

  public static TheoryData<string> Kinds()
  {
    var data = new TheoryData<string>();
    foreach (var kind in Expected.Keys)
    {
      data.Add(kind);
    }
    return data;
  }
}
