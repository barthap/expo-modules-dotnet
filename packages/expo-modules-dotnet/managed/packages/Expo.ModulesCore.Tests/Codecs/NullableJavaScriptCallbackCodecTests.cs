using System;
using Expo.JSI;
using Expo.ModulesCore.Codecs;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Codecs;

public sealed class NullableJavaScriptCallbackCodecTests
{
  private const string NotAFunctionMessage = "Value is not a function.";

  [Theory]
  [InlineData("null")]
  [InlineData("undefined")]
  public void DecodeReturnsNullForNullishInputWithoutRegistering(string expression)
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var value = fixture.Evaluate(expression, "nullable-callback-decode-nullish.js");

      Assert.Null(NullableJavaScriptCallbackCodec<int, NumberCodec<int>>.Decode(value.Ref, runtime, context));
      Assert.Null(
          NullableJavaScriptCallbackCodec<ValueTuple<string>, ValueTupleCodec<string, StringCodec>, string, StringCodec>
              .Decode(value.Ref, runtime, context));
      Assert.Equal(0, context.RetainedCallbackCount);
      return true;
    });
  }

  [Fact]
  public void DecodeRetainsNonNullFunctionThroughTheCallbackCodec()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);

      using var noArgs = fixture.Evaluate("(() => 5)", "nullable-callback-decode-no-args.js");
      var noArgsCallback = NullableJavaScriptCallbackCodec<int, NumberCodec<int>>.Decode(noArgs.Ref, runtime, context);
      Assert.NotNull(noArgsCallback);
      Assert.Equal(5, noArgsCallback.Invoke());
      Assert.Equal(1, context.RetainedCallbackCount);

      using var withArgs = fixture.Evaluate("((name) => 'Hi ' + name)", "nullable-callback-decode-args.js");
      var withArgsCallback =
          NullableJavaScriptCallbackCodec<ValueTuple<string>, ValueTupleCodec<string, StringCodec>, string, StringCodec>
              .Decode(withArgs.Ref, runtime, context);
      Assert.NotNull(withArgsCallback);
      Assert.Equal("Hi JS", withArgsCallback.Invoke(ValueTuple.Create("JS")));
      Assert.Equal(2, context.RetainedCallbackCount);
      return true;
    });
  }

  [Theory]
  [InlineData("5")]
  [InlineData("'x'")]
  [InlineData("({})")]
  public void DecodeRejectsNonFunctionValuesWithoutRegistering(string expression)
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var value = fixture.Evaluate(expression, "nullable-callback-decode-non-function.js");

      // Only null and undefined map to C# null. Any other non-function must still fail like the
      // non-nullable callback codec, instead of being silently dropped.
      var noArgsError = Assert.Throws<InvalidOperationException>(
          () => NullableJavaScriptCallbackCodec<int, NumberCodec<int>>.Decode(value.Ref, runtime, context));
      Assert.Contains(NotAFunctionMessage, noArgsError.Message);

      var withArgsError = Assert.Throws<InvalidOperationException>(
          () => NullableJavaScriptCallbackCodec<ValueTuple<string>, ValueTupleCodec<string, StringCodec>, string, StringCodec>
              .Decode(value.Ref, runtime, context));
      Assert.Contains(NotAFunctionMessage, withArgsError.Message);

      Assert.Equal(0, context.RetainedCallbackCount);
      return true;
    });
  }

  [Fact]
  public void RetainedCallbackCountClearsAtContextTeardown()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      var context = new DotnetRuntimeContext(runtime);
      try
      {
        using var value = fixture.Evaluate("(() => 5)", "nullable-callback-count-teardown.js");
        var callback = NullableJavaScriptCallbackCodec<int, NumberCodec<int>>.Decode(value.Ref, runtime, context);
        Assert.NotNull(callback);
        Assert.Equal(1, context.RetainedCallbackCount);

        callback.Dispose();
        Assert.Equal(1, context.RetainedCallbackCount);

        context.Dispose();
        Assert.Equal(0, context.RetainedCallbackCount);
      }
      finally
      {
        context.Dispose();
      }

      return true;
    });
  }
}
