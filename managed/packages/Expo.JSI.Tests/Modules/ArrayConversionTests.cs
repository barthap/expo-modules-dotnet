using System.Collections.Generic;
using System.Linq;
using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Modules;

public sealed class ArrayConversionTests
{
  [Fact]
  public void GeneratedLookingCodeDecodesJavaScriptArrayIntoReadOnlyListParameter()
  {
    using var fixture = HermesRuntimeFixture.Create();
    GeneratedArrayModuleProvider.Register(fixture.Runtime);

    using var result = fixture.Evaluate(
        "globalThis.expo.modules.Array.sum([1, 2, 3.5])",
        "array-sum.js"
    );

    Assert.Equal(JavaScriptValueKind.Number, result.Kind);
    Assert.Equal(6.5, result.AsDouble());
  }

  [Fact]
  public void GeneratedLookingCodeEncodesReadOnlyListReturnAsJavaScriptArray()
  {
    using var fixture = HermesRuntimeFixture.Create();
    GeneratedArrayModuleProvider.Register(fixture.Runtime);

    using var result = fixture.Evaluate(
        "const labels = globalThis.expo.modules.Array.labels(); Array.isArray(labels) && labels.join(',')",
        "array-labels.js"
    );

    Assert.Equal(JavaScriptValueKind.String, result.Kind);
    Assert.Equal("one,two", result.AsString());
  }

  private sealed class ArrayModule
  {
    public double Sum(IReadOnlyList<double> values) => values.Sum();

    public IReadOnlyList<string> Labels() => ["one", "two"];
  }

  private interface IJavaScriptCodec<T>
  {
    static abstract T Decode(JavaScriptBorrowedValue value, JavaScriptRuntime runtime);
    static abstract T Decode(JavaScriptValue value, JavaScriptRuntime runtime);
    static abstract JavaScriptValue Encode(T value, JavaScriptRuntime runtime);
  }

  private readonly struct DoubleCodec : IJavaScriptCodec<double>
  {
    public static double Decode(JavaScriptBorrowedValue value, JavaScriptRuntime runtime) =>
        value.AsDouble();

    public static double Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
        value.AsDouble();

    public static JavaScriptValue Encode(double value, JavaScriptRuntime runtime) =>
        runtime.CreateNumber(value);
  }

  private readonly struct StringCodec : IJavaScriptCodec<string>
  {
    public static string Decode(JavaScriptBorrowedValue value, JavaScriptRuntime runtime) =>
        value.AsString();

    public static string Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
        value.AsString();

    public static JavaScriptValue Encode(string value, JavaScriptRuntime runtime) =>
        runtime.CreateString(value);
  }

  private static class JavaScriptArrayCodec<T, TCodec>
      where TCodec : IJavaScriptCodec<T>
  {
    public static T[] DecodeToArray(JavaScriptBorrowedValue value, JavaScriptRuntime runtime)
    {
      using var array = value.AsArray();
      var length = checked((int)array.Length);
      var result = new T[length];

      for (var index = 0; index < length; index++)
      {
        using var element = array.GetValue((uint)index);
        result[index] = TCodec.Decode(element, runtime);
      }

      return result;
    }

    public static JavaScriptValue Encode(IReadOnlyList<T> values, JavaScriptRuntime runtime)
    {
      using var array = runtime.CreateArray((uint)values.Count);
      for (var index = 0; index < values.Count; index++)
      {
        using var element = TCodec.Encode(values[index], runtime);
        array.SetValue((uint)index, element);
      }
      return array.AsValue();
    }
  }

  private static class GeneratedArrayModuleProvider
  {
    public static void Register(JavaScriptRuntime runtime)
    {
      using var global = runtime.Global();
      using var expo = runtime.CreateObject();
      using var modules = runtime.CreateObject();
      using var array = runtime.CreateObject();

      var module = new ArrayModule();
      using var sum = runtime.CreateHostFunction("sum", 1, SumHostFunction, module);
      using var labels = runtime.CreateHostFunction("labels", 0, LabelsHostFunction, module);
      using var sumValue = sum.AsValue();
      using var labelsValue = labels.AsValue();
      array.SetProperty("sum", sumValue);
      array.SetProperty("labels", labelsValue);

      using var arrayValue = array.AsValue();
      modules.SetProperty("Array", arrayValue);
      using var modulesValue = modules.AsValue();
      expo.SetProperty("modules", modulesValue);
      using var expoValue = expo.AsValue();
      global.SetProperty("expo", expoValue);
    }

    private static JavaScriptValue SumHostFunction(
        JavaScriptRuntime runtime,
        JavaScriptBorrowedValue thisValue,
        JavaScriptArguments arguments,
        object context)
    {
      var module = (ArrayModule)context;
      var values = JavaScriptArrayCodec<double, DoubleCodec>.DecodeToArray(
          arguments.GetBorrowedValue(0),
          runtime
      );
      return DoubleCodec.Encode(module.Sum(values), runtime);
    }

    private static JavaScriptValue LabelsHostFunction(
        JavaScriptRuntime runtime,
        JavaScriptBorrowedValue thisValue,
        JavaScriptArguments arguments,
        object context)
    {
      var module = (ArrayModule)context;
      return JavaScriptArrayCodec<string, StringCodec>.Encode(module.Labels(), runtime);
    }
  }
}
