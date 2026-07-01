using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

public sealed class JavaScriptValueRepresentableTests
{
  [Fact]
  public void JavaScriptValueAsValueReturnsDisposableClone()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var original = runtime.CreateString("alive");
      IJavaScriptValueRepresentable representable = original;

      using (var clone = representable.AsValue())
      {
        Assert.Equal("alive", clone.AsString());
      }

      Assert.Equal("alive", original.AsString());
      return true;
    });
  }

  [Fact]
  public void JavaScriptValueCanBeCheckedAndWrappedAsErrorObject()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var error = runtime.CreateErrorObject("bad value");
      using var errorValue = error.AsValue();

      Assert.True(errorValue.IsError);

      using var wrappedError = errorValue.AsErrorObject();
      Assert.Equal("Error", wrappedError.Name);
      Assert.Equal("bad value", wrappedError.Message);
      return true;
    });
  }

  [Fact]
  public void JavaScriptErrorObjectAccessorsTolerateMutatedFields()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(_ =>
    {
      using var errorValue = fixture.Evaluate(
          """
          (() => {
            const error = Object.create(Error.prototype);
            error.message = 123;
            error.name = {
              toString() {
                return "CustomName";
              }
            };
            return error;
          })()
          """,
          "javascript-error-mutated-fields.js"
      );

      using var error = errorValue.AsErrorObject();
      Assert.Equal("CustomName", error.Name);
      Assert.Equal("123", error.Message);
      Assert.Null(error.Stack);
      return true;
    });
  }

  [Fact]
  public void NonErrorValueCannotBeWrappedAsErrorObject()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var notError = runtime.CreateString("not an error");

      Assert.False(notError.IsError);
      Assert.Throws<InvalidOperationException>(() => notError.AsErrorObject());
      return true;
    });
  }
}
