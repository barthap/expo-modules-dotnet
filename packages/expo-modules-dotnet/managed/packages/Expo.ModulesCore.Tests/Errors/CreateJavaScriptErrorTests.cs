using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Errors;

public sealed class CreateJavaScriptErrorTests
{
  [Theory]
  [MemberData(nameof(CodedErrorCases.Kinds), MemberType = typeof(CodedErrorCases))]
  public void ConverterBuildsAnErrorWithTheCodeAndTheCauseChain(string kind)
  {
    using var fixture = HermesRuntimeFixture.Create();
    var tracked = fixture.Counters.OwnedValuesTracked;

    var described = fixture.Runtime.Execute(runtime =>
    {
      using (var error = GeneratedFunction.CreateJavaScriptError(runtime, CodedErrorCases.Create(kind)))
      using (var global = runtime.Global())
      {
        global.SetProperty("__converted", error);
      }
      using var result = fixture.Evaluate(
          """
          (e => [
            e instanceof Error,
            e.name,
            Object.prototype.hasOwnProperty.call(e, 'code'),
            Object.keys(e).indexOf('code') >= 0,
            String(e.code),
            e.message
          ].join('\u001f'))(globalThis.__converted)
          """,
          "converted-error.js"
      );
      return result.AsString().Split('\u001f');
    });

    var (code, message) = CodedErrorCases.Expected[kind];
    Assert.Equal("true", described[0]);
    Assert.Equal("Error", described[1]);
    // An own enumerable string property when the code is not empty; no property otherwise.
    Assert.Equal(code is null ? "false" : "true", described[2]);
    Assert.Equal(code is null ? "false" : "true", described[3]);
    Assert.Equal(code ?? "undefined", described[4]);
    Assert.Equal(message, described[5]);
    Assert.Equal(tracked, fixture.Counters.OwnedValuesTracked);
  }

  // The converter creates the Error before it sets code. If setting code fails, it must release the
  // value it created and let the caller fall back.
  [Fact]
  public void ConverterFailureReleasesTheCreatedValue()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var tracked = fixture.Counters.OwnedValuesTracked;

    var exception = fixture.Runtime.Execute(runtime =>
    {
      using (fixture.Evaluate(
          "Object.defineProperty(Error.prototype, 'code', { set() { throw new Error('code setter failed'); }, configurable: true }); true",
          "break-code-property.js"))
      {
      }
      return Assert.ThrowsAny<Exception>(
          () => GeneratedFunction.CreateJavaScriptError(runtime, CodedErrorCases.Create("derived"))
      );
    });

    Assert.Contains("code setter failed", exception.Message);
    Assert.Equal(tracked, fixture.Counters.OwnedValuesTracked);
  }
}
