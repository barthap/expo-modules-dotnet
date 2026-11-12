using Xunit;

namespace Expo.ModulesCore.Tests.Errors;

// A facade ported from an upstream module branches on error.code, so the derived codes must match
// the iOS rule exactly, including its odd acronym result.
public sealed class CodedExceptionTests
{
  [Theory]
  [MemberData(nameof(DerivedCodes))]
  public void CodeIsDerivedFromTheClassNameWithTheIosRule(CodedException exception, string expected)
  {
    Assert.Equal(expected, exception.Code);
  }

  public static TheoryData<CodedException, string> DerivedCodes() => new()
  {
    { new InvalidPathException("m"), "ERR_INVALID_PATH" },
    { new NotFoundError("m"), "ERR_NOT_FOUND" },
    // Both platforms use the (.)([A-Z]) rule; keep its acronym result instead of improving it.
    { new HTTPException("m"), "ERR_H_TT_P" },
    // The generic arity suffix (`1) is removed before the Exception suffix.
    { new PathException<int>("m"), "ERR_PATH" },
    // A nested type gives its own name without the outer type.
    { new Outer.InvalidPathException("m"), "ERR_INVALID_PATH" },
    { new CodedException("m"), "ERR_CODED" },
    { new Utf8DecodeException("m"), "ERR_UTF8_DECODE" },
  };

  [Fact]
  public void ConstructorsBindAsTheSpecExpects()
  {
    var inner = new InvalidOperationException("inner");

    var messageOnly = new CodedException("m");
    var withInner = new CodedException("m", inner);
    var explicitCode = new CodedException("ERR_X", "m");
    // The tie-break prefers the constructor that needs no default argument.
    var nullSecond = new CodedException("m", null);

    Assert.Equal(("ERR_CODED", "m"), (messageOnly.Code, messageOnly.Message));
    Assert.Equal(("ERR_CODED", "m"), (withInner.Code, withInner.Message));
    Assert.Same(inner, withInner.InnerException);
    Assert.Equal(("ERR_X", "m"), (explicitCode.Code, explicitCode.Message));
    Assert.Equal(("ERR_CODED", "m"), (nullSecond.Code, nullSecond.Message));
    Assert.Null(nullSecond.InnerException);
  }

  [Fact]
  public void NullExplicitCodeDerivesAndEmptyExplicitCodeStaysEmpty()
  {
    Assert.Equal("ERR_NULL_CODE", new NullCodeException().Code);
    Assert.Equal(string.Empty, new CodedException(string.Empty, "m").Code);
  }

  [Fact]
  public void SubclassCanOverrideCodeAndMessage()
  {
    var exception = new RephrasedException();

    Assert.Equal("ERR_REPHRASED", exception.Code);
    Assert.Equal("rephrased", exception.Message);
  }

  // Message follows the .NET convention: the converter appends the chain, Message never holds it.
  [Fact]
  public void MessageHoldsNoCauseChain()
  {
    var exception = new CodedException("outer", new InvalidOperationException("inner"));

    Assert.Equal("outer", exception.Message);
  }

  private sealed class InvalidPathException(string message) : CodedException(message);

  private sealed class NotFoundError(string message) : CodedException(message);

  private sealed class HTTPException(string message) : CodedException(message);

  private sealed class PathException<T>(string message) : CodedException(message);

  private sealed class Utf8DecodeException(string message) : CodedException(message);

  private sealed class NullCodeException() : CodedException((string?)null, "m");

  private sealed class RephrasedException() : CodedException("original")
  {
    public override string Code => "ERR_REPHRASED";

    public override string Message => "rephrased";
  }

  private static class Outer
  {
    public sealed class InvalidPathException(string message) : CodedException(message);
  }
}
