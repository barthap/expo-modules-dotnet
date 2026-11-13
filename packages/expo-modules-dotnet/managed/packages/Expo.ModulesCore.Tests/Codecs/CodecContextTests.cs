using System;
using System.Collections.Generic;
using Expo.JSI;
using Expo.ModulesCore;
using Expo.ModulesCore.Codecs;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Codecs;

// A composite codec must hand the exact context and runtime it received to its child. A child that
// resolves a registry-owned value, such as a shared object, depends on that context to find the
// registry of the runtime that owns the binding.
public sealed class CodecContextTests
{
  [Fact]
  public void ListCodecPassesItsContextAndRuntimeToEveryElementCodec()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      ContextProbeCodec.Calls.Clear();

      using var encoded = JavaScriptArrayCodec<int, ContextProbeCodec>.Encode(new[] { 1, 2 }, runtime, context);
      Assert.Equal(2, ContextProbeCodec.Calls.Count);
      JavaScriptArrayCodec<int, ContextProbeCodec>.Decode(encoded.Ref, runtime, context);
      JavaScriptArrayCodec<int, ContextProbeCodec>.Decode(encoded, runtime, context);
      JavaScriptArrayCodec<int, ContextProbeCodec>.DecodeToArray(encoded.Ref, runtime, context);
      JavaScriptArrayCodec<int, ContextProbeCodec>.DecodeToArray(encoded, runtime, context);

      Assert.Equal(10, ContextProbeCodec.Calls.Count);
      AssertEveryCallReceived(runtime, context);
      return true;
    });
  }

  [Fact]
  public void DictionaryCodecPassesItsContextAndRuntimeToEveryValueCodec()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      ContextProbeCodec.Calls.Clear();

      var source = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
      using var encoded = JavaScriptDictionaryCodec<int, ContextProbeCodec>.Encode(source, runtime, context);
      Assert.Equal(2, ContextProbeCodec.Calls.Count);
      JavaScriptDictionaryCodec<int, ContextProbeCodec>.Decode(encoded.Ref, runtime, context);
      JavaScriptDictionaryCodec<int, ContextProbeCodec>.Decode(encoded, runtime, context);
      JavaScriptDictionaryCodec<int, ContextProbeCodec>.DecodeToDictionary(encoded.Ref, runtime, context);
      JavaScriptDictionaryCodec<int, ContextProbeCodec>.DecodeToDictionary(encoded, runtime, context);

      Assert.Equal(10, ContextProbeCodec.Calls.Count);
      AssertEveryCallReceived(runtime, context);
      return true;
    });
  }

  [Fact]
  public void NullableWrappersPassTheirContextAndRuntimeToTheInnerCodec()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      ContextProbeCodec.Calls.Clear();

      using var scalar = NullableCodec<int, ContextProbeCodec>.Encode(7, runtime, context);
      NullableCodec<int, ContextProbeCodec>.Decode(scalar.Ref, runtime, context);
      NullableCodec<int, ContextProbeCodec>.Decode(scalar, runtime, context);

      using var list = NullableReadOnlyListCodec<int, ContextProbeCodec>.Encode(new[] { 1 }, runtime, context);
      NullableReadOnlyListCodec<int, ContextProbeCodec>.Decode(list.Ref, runtime, context);
      NullableReadOnlyListCodec<int, ContextProbeCodec>.Decode(list, runtime, context);

      var map = new Dictionary<string, int> { ["a"] = 1 };
      using var dictionary = NullableDictionaryCodec<int, ContextProbeCodec>.Encode(map, runtime, context);
      NullableDictionaryCodec<int, ContextProbeCodec>.Decode(dictionary.Ref, runtime, context);
      NullableDictionaryCodec<int, ContextProbeCodec>.Decode(dictionary, runtime, context);

      using var readOnlyDictionary = NullableReadOnlyDictionaryCodec<int, ContextProbeCodec>.Encode(map, runtime, context);
      NullableReadOnlyDictionaryCodec<int, ContextProbeCodec>.Decode(readOnlyDictionary.Ref, runtime, context);
      NullableReadOnlyDictionaryCodec<int, ContextProbeCodec>.Decode(readOnlyDictionary, runtime, context);

      Assert.Equal(12, ContextProbeCodec.Calls.Count);
      AssertEveryCallReceived(runtime, context);
      return true;
    });
  }

  [Fact]
  public void NullishInputNeverReachesTheInnerCodec()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      ContextProbeCodec.Calls.Clear();

      using var encoded = NullableCodec<int, ContextProbeCodec>.Encode(null, runtime, context);
      Assert.Null(NullableCodec<int, ContextProbeCodec>.Decode(encoded, runtime, context));

      Assert.Empty(ContextProbeCodec.Calls);
      return true;
    });
  }

  [Fact]
  public void ArgumentTupleCodecPassesItsContextAndRuntimeToEveryElementCodec()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      ContextProbeCodec.Calls.Clear();

      var encoded = ValueTupleCodec<int, ContextProbeCodec, int, ContextProbeCodec>.Encode((1, 2), runtime, context);
      try
      {
        Assert.Equal(2, encoded.Length);
      }
      finally
      {
        foreach (var value in encoded)
        {
          value.Dispose();
        }
      }

      Assert.Equal(2, ContextProbeCodec.Calls.Count);
      AssertEveryCallReceived(runtime, context);
      return true;
    });
  }

  [Fact]
  public void CompositesPassTheContextTheyReceiveInsteadOfAnotherOne()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var first = new DotnetRuntimeContext(runtime);
      using var second = new DotnetRuntimeContext(runtime);
      ContextProbeCodec.Calls.Clear();

      using var encoded = JavaScriptArrayCodec<int, ContextProbeCodec>.Encode(new[] { 1 }, runtime, second);

      var call = Assert.Single(ContextProbeCodec.Calls);
      Assert.Same(second, call.Context);
      Assert.NotSame(first, call.Context);
      return true;
    });
  }

  private static void AssertEveryCallReceived(JavaScriptRuntime runtime, DotnetRuntimeContext context)
  {
    Assert.All(ContextProbeCodec.Calls, call =>
    {
      Assert.Same(context, call.Context);
      Assert.Same(runtime, call.Runtime);
    });
  }

  private readonly struct ContextProbeCodec : IJavaScriptCodec<int>
  {
    public static List<(JavaScriptRuntime Runtime, DotnetRuntimeContext Context)> Calls { get; } = [];

    public static int Decode(JavaScriptValueRef value, JavaScriptRuntime runtime, DotnetRuntimeContext context)
    {
      Calls.Add((runtime, context));
      return (int)value.AsDouble();
    }

    public static int Decode(JavaScriptValue value, JavaScriptRuntime runtime, DotnetRuntimeContext context)
    {
      Calls.Add((runtime, context));
      return (int)value.AsDouble();
    }

    public static JavaScriptValue Encode(int value, JavaScriptRuntime runtime, DotnetRuntimeContext context)
    {
      Calls.Add((runtime, context));
      return runtime.CreateNumber(value);
    }
  }
}
