using System;
using System.Collections.Generic;
using System.Linq;
using Expo.JSI;
using Expo.ModulesCore;
using Expo.ModulesCore.Codecs;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Codecs;

public sealed class GeneratedArrayModuleTests
{
  [Fact]
  public void GeneratedLookingCodeDecodesJavaScriptArrayIntoReadOnlyListParameter()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      GeneratedArrayModuleProvider.Register(context, modules);

      using var result = fixture.Evaluate(
          "globalThis._expoDotnet.modules.Array.sum([1, 2, 3.5])",
          "array-sum.js"
      );

      Assert.Equal(JavaScriptValueKind.Number, result.Kind);
      Assert.Equal(6.5, result.AsDouble());
      return true;
    });
  }

  [Fact]
  public void GeneratedLookingCodeEncodesReadOnlyListReturnAsJavaScriptArray()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      GeneratedArrayModuleProvider.Register(context, modules);

      using var result = fixture.Evaluate(
          "const labels = globalThis._expoDotnet.modules.Array.labels(); Array.isArray(labels) && labels.join(',')",
          "array-labels.js"
      );

      Assert.Equal(JavaScriptValueKind.String, result.Kind);
      Assert.Equal("one,two", result.AsString());
      return true;
    });
  }

  private sealed class ArrayModule
  {
    public double Sum(IReadOnlyList<double> values) => values.Sum();

    public IReadOnlyList<string> Labels() => ["one", "two"];
  }

  private static class GeneratedArrayModuleProvider
  {
    public static void Register(DotnetRuntimeContext context, JavaScriptObject modules)
    {
      using var array = context.ModuleRegistry.DefineModule(modules, "Array");

      GeneratedFunction.DefineSync(
          context,
          array,
          "sum",
          1,
          SumHostFunction,
          new ArrayModule()
      );
      GeneratedFunction.DefineSync(
          context,
          array,
          "labels",
          0,
          LabelsHostFunction,
          new ArrayModule()
      );
    }

    private static JavaScriptValue SumHostFunction(
        JavaScriptRuntime runtime,
        JavaScriptValueRef thisValue,
        JavaScriptArguments arguments,
        object context)
    {
      GeneratedFunction.RequireArgumentCount("Array.sum", arguments, 1);
      var __expoRuntimeContext = GeneratedFunction.CurrentRuntimeContext;

      var module = (ArrayModule)context;
      var values = JavaScriptArrayCodec<double, DoubleCodec>.DecodeToArray(
          arguments.GetValue(0),
          runtime,
          __expoRuntimeContext
      );
      return DoubleCodec.Encode(module.Sum(values), runtime, __expoRuntimeContext);
    }

    private static JavaScriptValue LabelsHostFunction(
        JavaScriptRuntime runtime,
        JavaScriptValueRef thisValue,
        JavaScriptArguments arguments,
        object context)
    {
      GeneratedFunction.RequireArgumentCount("Array.labels", arguments, 0);
      var __expoRuntimeContext = GeneratedFunction.CurrentRuntimeContext;

      var module = (ArrayModule)context;
      return JavaScriptArrayCodec<string, StringCodec>.Encode(module.Labels(), runtime, __expoRuntimeContext);
    }
  }
}

public sealed class JavaScriptCollectionCodecTests
{
  [Fact]
  public void ArrayCodecImplementsJavaScriptCodecForNestedLists()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      IReadOnlyList<IReadOnlyList<int>> grid = [[1, 2], [], [3]];

      var (viaRef, viaOwned) = RoundTrip<
          IReadOnlyList<IReadOnlyList<int>>,
          JavaScriptArrayCodec<IReadOnlyList<int>, JavaScriptArrayCodec<int, NumberCodec<int>>>>(
          grid,
          runtime
      );

      AssertGrid(viaRef);
      AssertGrid(viaOwned);
      return true;
    });
  }

  [Fact]
  public void DictionaryCodecImplementsBothDictionaryInterfaces()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      var source = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };

      var (mutableRef, mutableOwned) = RoundTrip<
          Dictionary<string, int>,
          JavaScriptDictionaryCodec<int, NumberCodec<int>>>(source, runtime);
      var (readOnlyRef, readOnlyOwned) = RoundTrip<
          IReadOnlyDictionary<string, int>,
          JavaScriptDictionaryCodec<int, NumberCodec<int>>>(source, runtime);

      Assert.Equal(source.OrderBy(pair => pair.Key), mutableRef.OrderBy(pair => pair.Key));
      Assert.Equal(source.OrderBy(pair => pair.Key), mutableOwned.OrderBy(pair => pair.Key));
      Assert.Equal(source.OrderBy(pair => pair.Key), readOnlyRef.OrderBy(pair => pair.Key));
      Assert.Equal(source.OrderBy(pair => pair.Key), readOnlyOwned.OrderBy(pair => pair.Key));
      return true;
    });
  }

  [Fact]
  public void ArrayCodecOwnedDecodeReturnsElementsAndLeavesInputUsable()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var input = fixture.Evaluate("['a', 'b', 'c']", "array-owned-decode.js");

      var decoded = JavaScriptArrayCodec<string, StringCodec>.Decode(input, runtime, CodecTestContext.Unused);
      var array = JavaScriptArrayCodec<string, StringCodec>.DecodeToArray(input, runtime, CodecTestContext.Unused);

      Assert.Equal(new[] { "a", "b", "c" }, decoded);
      Assert.Equal(new[] { "a", "b", "c" }, array);
      using var inputArray = input.AsArray();
      Assert.Equal(3u, inputArray.Length);
      return true;
    });
  }

  [Fact]
  public void ArrayCodecOwnedDecodeThrowsForInvalidElementAndLeavesInputUsable()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var input = fixture.Evaluate("[1, 'x']", "array-owned-decode-throws.js");

      Assert.Throws<InvalidOperationException>(() =>
          JavaScriptArrayCodec<int, NumberCodec<int>>.Decode(input, runtime, CodecTestContext.Unused));

      using var inputArray = input.AsArray();
      Assert.Equal(2u, inputArray.Length);
      return true;
    });
  }

  private static (T ViaRef, T ViaOwned) RoundTrip<T, TCodec>(T value, JavaScriptRuntime runtime)
      where TCodec : IJavaScriptCodec<T>
  {
    using var encoded = TCodec.Encode(value, runtime, CodecTestContext.Unused);
    var viaRef = TCodec.Decode(encoded.Ref, runtime, CodecTestContext.Unused);
    var viaOwned = TCodec.Decode(encoded, runtime, CodecTestContext.Unused);
    return (viaRef, viaOwned);
  }

  private static void AssertGrid(IReadOnlyList<IReadOnlyList<int>> grid)
  {
    Assert.Equal(3, grid.Count);
    Assert.Equal(new[] { 1, 2 }, grid[0]);
    Assert.Empty(grid[1]);
    Assert.Equal(new[] { 3 }, grid[2]);
  }
}
