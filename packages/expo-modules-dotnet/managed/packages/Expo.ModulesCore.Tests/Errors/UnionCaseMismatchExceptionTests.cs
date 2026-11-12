using Expo.JSI;
using Expo.ModulesCore.Codecs;
using Xunit;

namespace Expo.ModulesCore.Tests.Errors;

// The union mismatch is what JavaScript sees when no case takes a value. Its text is the iOS
// UnionCaseMismatch reason, and its code is the iOS code, so a ported module reads the same error.
public sealed class UnionCaseMismatchExceptionTests
{
  [Theory]
  [InlineData(JavaScriptValueKind.Undefined, "undefined")]
  [InlineData(JavaScriptValueKind.Null, "null")]
  [InlineData(JavaScriptValueKind.Bool, "boolean")]
  [InlineData(JavaScriptValueKind.Number, "number")]
  [InlineData(JavaScriptValueKind.String, "string")]
  [InlineData(JavaScriptValueKind.Object, "object")]
  [InlineData(JavaScriptValueKind.Function, "function")]
  [InlineData(JavaScriptValueKind.ArrayBuffer, "ArrayBuffer")]
  public void MessageNamesTheReceivedKindLikeJavaScriptTypeof(JavaScriptValueKind kind, string name)
  {
    var error = new UnionCaseMismatchException("UnionSource", new[] { "string", "UnionSourceOptions" }, kind);

    Assert.Equal($"'UnionSource' expected string or UnionSourceOptions, but received {name}", error.Message);
    Assert.Equal(name, error.Received);
  }

  [Fact]
  public void CodeIsTheUpstreamUnionCaseMismatchCode()
  {
    var error = new UnionCaseMismatchException("U", new[] { "int" }, JavaScriptValueKind.String);

    Assert.IsAssignableFrom<CodedException>(error);
    Assert.Equal("ERR_UNION_CASE_MISMATCH", error.Code);
  }

  [Fact]
  public void ExposesTheUnionTheExpectedTypesAndTheLastAttemptFailure()
  {
    var expected = new[] { "string", "byte[]", "int" };
    var attempt = new InvalidOperationException("attempt");
    var error = new UnionCaseMismatchException("NonceInput", expected, JavaScriptValueKind.Object, attempt);
    expected[0] = "changed";

    Assert.Equal("NonceInput", error.UnionName);
    // The exception keeps its own copy, so a caller cannot change the reported alternatives.
    Assert.Equal(new[] { "string", "byte[]", "int" }, error.Expected);
    Assert.Same(attempt, error.InnerException);
    Assert.Null(new UnionCaseMismatchException("U", new[] { "int" }, JavaScriptValueKind.Null).InnerException);
  }

  [Fact]
  public void RejectsNullArguments()
  {
    Assert.Throws<ArgumentNullException>(() => new UnionCaseMismatchException(null!, new[] { "int" }, JavaScriptValueKind.Null));
    Assert.Throws<ArgumentNullException>(() => new UnionCaseMismatchException("U", null!, JavaScriptValueKind.Null));
  }

  private sealed record Rogue(string Value);

  [Fact]
  public void NotACaseNamesTheRuntimeTypeAndTheUnion()
  {
    var error = UnionCodecErrors.NotACase(new Rogue("x"), "Expo.TestModules.Payload");

    Assert.IsType<InvalidOperationException>(error);
    Assert.Equal(
        "Type 'Expo.ModulesCore.Tests.Errors.UnionCaseMismatchExceptionTests+Rogue' is not a case of union 'Expo.TestModules.Payload'.",
        error.Message);
  }
}
