using Microsoft.CodeAnalysis;
using Xunit;

namespace Expo.ModulesCore.Generator.Tests;

public sealed class ExpoModulesGeneratorTests
{
  [Theory]
  [InlineData("[JS] public JavaScriptCallback<string> Direct { get; } = null!;", "Direct")]
  [InlineData("[JS] public System.Collections.Generic.IReadOnlyList<JavaScriptCallback<string>> Nested { get; } = null!;", "Nested")]
  public void GeneratorRejectsReadableCallbackPropertyTypes(string property, string propertyName)
  {
    var result = GeneratorTestHost.Run(
        $$"""
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class PropertiesModule
        {
          {{property}}
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI015");
    Assert.Contains(propertyName, diagnostic.GetMessage());
  }

  [Fact]
  public void GeneratorAllowsRecordPropertyWithUnencodedCallbackMember()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        public sealed record Value(string Name)
        {
          public JavaScriptCallback<string>? Callback => null;
        }

        [ExpoModule]
        public sealed partial class PropertiesModule
        {
          [JS] public Value Current { get; } = new("value");
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
  }

  [Fact]
  public void GeneratorUsesPropertySymbolNamesForAccessorCallbacks()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule("Properties")]
        public sealed partial class PropertiesModule
        {
          [JS("a-b")] public bool First { get; }
          [JS("a_b")] public bool Second { get; }
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    var source = Assert.Single(result.GeneratedSources).Text;
    Assert.Contains("\"a-b\"", source);
    Assert.Contains("\"a_b\"", source);
    Assert.Contains("Properties_First_Getter", source);
    Assert.Contains("Properties_Second_Getter", source);
  }

  [Fact]
  public void GeneratorEmitsAccessorPropertiesWithLowerCamelAndExplicitNames()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule("Properties")]
        public sealed partial class PropertiesModule
        {
          [JS] public bool Ready { get; set; }
          [JS] public bool IsReadOnly => true;
          [JS("isReady")] public bool ReadyWithExplicitName => true;
          [JS] internal string InternalGetter { get; private set; } = "internal";
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    var source = Assert.Single(result.GeneratedSources).Text;
    Assert.Contains("GeneratedProperty.Define(", source);
    Assert.Contains("\"ready\"", source);
    Assert.Contains("\"isReadOnly\"", source);
    Assert.Contains("\"isReady\"", source);
    Assert.Contains("Properties_Ready_Getter", source);
    Assert.Contains("Properties_Ready_Setter", source);
    Assert.Contains("GeneratedFunction.RequireArgumentCount(\"Properties.ready\", arguments, 0);", source);
    Assert.Contains("GeneratedFunction.RequireArgumentCount(\"Properties.ready\", arguments, 1);", source);
    Assert.DoesNotContain("Properties_IsReadOnly_Setter", source);
    Assert.DoesNotContain("Properties_InternalGetter_Setter", source);
  }

  [Theory]
  [InlineData("[JS] public static bool Ready { get; } = true;", "Ready", "static")]
  [InlineData("[JS] public bool this[int index] => true;", "this[]", "indexed")]
  [InlineData("[JS] public bool Ready { private get; set; }", "Ready", "getter")]
  [InlineData("[JS] public bool Ready { set { } }", "Ready", "setter-only")]
  [InlineData("[JS] public bool Ready { get; init; }", "Ready", "init")]
  public void GeneratorReportsUnsupportedJSPropertyShape(string property, string name, string shape)
  {
    var result = GeneratorTestHost.Run(
        $$"""
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class PropertiesModule
        {
          {{property}}
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI014");
    Assert.Contains(name, diagnostic.GetMessage());
    Assert.Contains(shape, diagnostic.GetMessage(), StringComparison.OrdinalIgnoreCase);
  }

  [Theory]
  [InlineData("decimal", "System.Decimal")]
  [InlineData("System.Span<byte>", "Span")]
  [InlineData("System.ReadOnlySpan<byte>", "ReadOnlySpan")]
  public void GeneratorReportsUnsupportedJSPropertyCodec(string type, string expectedType)
  {
    var result = GeneratorTestHost.Run(
        $$"""
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class PropertiesModule
        {
          [JS] public {{type}} Value { get; set; }
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI015");
    Assert.Contains("Value", diagnostic.GetMessage());
    Assert.Contains(expectedType, diagnostic.GetMessage());
  }

  [Fact]
  public void GeneratorReportsDuplicateJavaScriptPropertyName()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule("Properties")]
        public sealed partial class PropertiesModule
        {
          [JS] public bool IsReady => true;
          [JS("isReady")] public bool Ready => true;
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI016");
    Assert.Contains("Properties", diagnostic.GetMessage());
    Assert.Contains("isReady", diagnostic.GetMessage());
  }

  [Fact]
  public void GeneratorReportsMethodAndPropertyJavaScriptNameCollision()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule("Properties")]
        public sealed partial class PropertiesModule
        {
          [JS] public bool GetReady() => true;
          [JS("getReady")] public bool Ready => true;
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI016");
    Assert.Contains("Properties", diagnostic.GetMessage());
    Assert.Contains("getReady", diagnostic.GetMessage());
  }

  [Fact]
  public void GeneratorRejectsPropertyThatCollidesWithDuplicateMethodName()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule("Properties")]
        public sealed partial class PropertiesModule
        {
          [JS("same")] public bool First() => true;
          [JS("same")] public bool Second() => true;
          [JS("same")] public bool Value => true;
        }
        """
    );

    Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI005");
    var collision = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI016");
    Assert.Contains("Properties", collision.GetMessage());
    Assert.Contains("same", collision.GetMessage());
    Assert.DoesNotContain("GeneratedProperty.Define(", Assert.Single(result.GeneratedSources).Text);
  }

  [Fact]
  public void GeneratorReportsReservedObservingPropertyName()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule("Properties")]
        [Events("change")]
        public sealed partial class PropertiesModule
        {
          [JS] public bool StartObserving => true;
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI017");
    Assert.Contains("StartObserving", diagnostic.GetMessage());
    Assert.Contains("startObserving", diagnostic.GetMessage());
  }

  [Fact]
  public void GeneratorEmitsBinaryCodecOwnershipAndSingleSpanCallbacks()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;
        using System;
        using System.Threading.Tasks;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class BinaryModule
        {
          [JS]
          public ArrayBuffer Echo(ArrayBuffer value) => value.Retain();

          [JS]
          public byte[] EchoBytes(byte[] value) => value;

          [JS]
          public int Sum(ReadOnlySpan<byte> value) => value.Length;

          [JS]
          public ArrayBuffer Transform(ReadOnlySpan<byte> value) => ArrayBuffer.CopyFrom(value);
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    var source = Assert.Single(result.GeneratedSources).Text;
    Assert.Contains("using var __expoArg0 = ArrayBufferCodec.Decode", source);
    Assert.Contains("ByteArrayCodec.Decode", source);
    Assert.Contains("__expoSpanBuffer0.WithReadOnlyBytes(__expoArg0 =>", source);
    Assert.Contains("module.Sum(__expoArg0)", source);
    Assert.Contains("using var __expoResult = module.Echo(__expoArg0);", source);
    Assert.Contains("using var __expoResult = module.Transform(__expoArg0);", source);
    Assert.Contains("return ArrayBufferCodec.Encode(__expoResult, runtime);", source);
    Assert.DoesNotContain("__expoResult.Dispose();", source);
    Assert.DoesNotContain("__expoSpanBuffer1", source);
  }

  [Fact]
  public void GeneratorReportsMultidimensionalByteArrayParameter()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class BinaryModule
        {
          [JS]
          public int Sum(byte[,] value) => value.Length;
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI001");
    Assert.Contains("value", diagnostic.GetMessage());
    Assert.Contains("byte[", diagnostic.GetMessage());
  }

  [Fact]
  public void GeneratorReportsMultidimensionalByteArrayReturn()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class BinaryModule
        {
          [JS]
          public byte[,] Create() => new byte[1, 1];
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI002");
    Assert.Contains("Create", diagnostic.GetMessage());
    Assert.Contains("byte[", diagnostic.GetMessage());
  }

  [Fact]
  public void GeneratorReportsMultipleSpanParameters()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;
        using System;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class BinaryModule
        {
          [JS]
          public int Combine(Span<byte> first, ReadOnlySpan<byte> second) => first.Length + second.Length;
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI013");
    Assert.Contains("Combine", diagnostic.GetMessage());
    Assert.Contains("first", diagnostic.GetMessage());
    Assert.Contains("second", diagnostic.GetMessage());
  }

  [Fact]
  public void GeneratorReportsAsyncSpanParameter()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;
        using System;
        using System.Threading.Tasks;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class BinaryModule
        {
          [JS]
          public Task<int> SumAsync(ReadOnlySpan<byte> value) => Task.FromResult(value.Length);
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI012");
    Assert.Contains("SumAsync", diagnostic.GetMessage());
    Assert.Contains("value", diagnostic.GetMessage());
  }

  [Fact]
  public void GeneratorUsesClaimOrAbandonForAsyncArrayBufferResults()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;
        using System.Threading.Tasks;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class BinaryModule
        {
          [JS]
          public Task<ArrayBuffer> EchoAsync(ArrayBuffer value) => Task.FromResult(value.Retain());
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    var source = Assert.Single(result.GeneratedSources).Text;
    Assert.Contains("JavaScriptPromiseResult.ResolveOwned(", source);
    Assert.Contains("ArrayBufferCodec.Encode(value, runtime)", source);
    Assert.Contains("static value => value.Dispose()", source);
  }

  [Fact]
  public void GeneratorEmitsDeterministicProviderForAssembly()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class MathModule
        {
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    var generatedSource = Assert.Single(result.GeneratedSources);
    Assert.EndsWith(".g.cs", generatedSource.HintName);
    var source = generatedSource.Text;
    Assert.Contains("// <auto-generated", source);
    Assert.Contains("public static class ExpoModulesProvider_Expo_TestModules", source);
    Assert.Contains(
        "public static void Register(global::Expo.ModulesCore.DotnetRuntimeContext context)",
        source
    );
    Assert.Contains(
        "public static void Register(global::Expo.ModulesCore.DotnetRuntimeContext context, global::Expo.JSI.JavaScriptObject modules)",
        source
    );
    Assert.DoesNotContain("public static void Register(global::Expo.JSI.JavaScriptRuntime runtime", source);
    Assert.Contains("global::System.ArgumentNullException.ThrowIfNull(context);", source);
    Assert.Contains("context.ModuleRegistry.RegisterLazyModule(", source);
    Assert.Contains("new global::Expo.ModulesCore.LazyModuleDefinition(", source);
    Assert.DoesNotContain("using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();", source);
    Assert.Contains("global::System.ArgumentNullException.ThrowIfNull(modules);", source);
    Assert.Contains("context.ModuleRegistry.DefineModule(modules, \"Math\")", source);
    Assert.Contains("context.ModuleRegistry.GetOrCreateModule(\"Math\", static () => new global::Expo.TestModules.MathModule())", source);
    Assert.Contains("RegisterMath(context, modules)", source);
  }

  [Fact]
  public void GeneratorEmitsDefaultAndExplicitFunctionNames()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule("Math")]
        public sealed partial class InternalMathModule
        {
          [JS]
          public double Add(double a, double b) => a + b;

          [JS]
          public string GetMessageAsync() => "message";

          [JS("ExactName")]
          public double Increment(double value) => value + 1.0;
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    var source = Assert.Single(result.GeneratedSources).Text;
    Assert.Contains("context.ModuleRegistry.DefineModule(modules, \"Math\")", source);
    Assert.Contains("context.ModuleRegistry.GetOrCreateModule(\"Math\", static () => new global::Expo.TestModules.InternalMathModule())", source);
    Assert.Contains("GeneratedFunction.DefineSync(", source);
    Assert.Contains("module_Math", source);
    Assert.Contains("\"add\"", source);
    Assert.Contains("\"getMessageAsync\"", source);
    Assert.Contains("\"ExactName\"", source);
    Assert.DoesNotContain("\"Add\"", source);
    Assert.Contains("module.Add(__expoArg0, __expoArg1)", source);
    Assert.Contains("module.GetMessageAsync()", source);
    Assert.Contains("module.Increment(__expoArg0)", source);
  }

  [Fact]
  public void GeneratorEmitsGenericNumberCodecs()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule("Math")]
        public sealed partial class MathModule
        {
          [JS]
          public int RoundTripInt(int value) => value;

          [JS]
          public int? RoundTripNullableInt(int? value) => value;
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    var source = Assert.Single(result.GeneratedSources).Text;
    Assert.Contains("NumberCodec<int>.Decode(arguments.GetValue(0), runtime)", source);
    Assert.Contains("NumberCodec<int>.Encode(module.RoundTripInt(__expoArg0), runtime)", source);
    Assert.Contains(
        "NullableCodec<int, NumberCodec<int>>.Decode(arguments.GetValue(0), runtime)",
        source
    );
    Assert.Contains(
        "NullableCodec<int, NumberCodec<int>>.Encode(module.RoundTripNullableInt(__expoArg0), runtime)",
        source
    );
  }

  [Fact]
  public void GeneratorEmitsAsyncFunctionSourceShape()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;
        using System.Threading.Tasks;

        namespace Expo.TestModules;

        [ExpoModule("Async")]
        public sealed partial class AsyncModule
        {
          [JS]
          public async Task CompleteAsync(int promiseValue)
          {
            await Task.Yield();
          }

          [JS]
          public async Task<int> GetValueAsync(int result)
          {
            await Task.Yield();
            return result;
          }
        }
        """
    );

    var source = Assert.Single(result.GeneratedSources).Text;
    Assert.Contains("GeneratedFunction.DefineAsync(", source);
    Assert.Contains("Async_completeAsync_HostFunction", source);
    Assert.Contains("Async_getValueAsync_HostFunction", source);
    Assert.Contains("JavaScriptPromiseResult.Resolve", source);
    Assert.Contains("runtime.CreateUndefined()", source);
    Assert.Contains("NumberCodec<int>.Encode", source);
    Assert.Contains("var __expoArg0 = NumberCodec<int>.Decode(arguments.GetValue(0), jsRuntime);", source);
    Assert.Contains("using var __expoPromiseValue = jsRuntime.CreatePromise(", source);
    Assert.Contains("var __expoTask = module.CompleteAsync(__expoArg0);", source);
    Assert.Contains("var __expoTask = module.GetValueAsync(__expoArg0);", source);
    Assert.Contains("await __expoTask.ConfigureAwait(false)", source);
    Assert.Contains("var __expoResult = await __expoTask.ConfigureAwait(false)", source);
    Assert.Contains("return GeneratedFunction.CreateRejectedPromise(jsRuntime, exception);", source);
    Assert.Contains("NumberCodec<int>.Encode(__expoResult, runtime)", source);
    Assert.DoesNotContain("var promiseValue =", source);
    Assert.DoesNotContain("using var promiseValue =", source);
    Assert.DoesNotContain("var result =", source);
  }

  [Fact]
  public void GeneratorReportsUnsupportedParameterType()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class BadModule
        {
          [JS]
          public double Bad(decimal value) => 0.0;
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI001");
    Assert.Contains("value", diagnostic.GetMessage());
    Assert.Contains("System.Decimal", diagnostic.GetMessage());
  }

  [Fact]
  public void GeneratorReportsUnsupportedCallbackArgumentCodec()
  {
    var result = GeneratorTestHost.Run(
        """
        using System;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class BadModule
        {
          [JS]
          public double Bad(JavaScriptCallback<ValueTuple<decimal>, string> callback) => 0.0;
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI008");
    Assert.Contains("callback", diagnostic.GetMessage());
    Assert.Contains("callback argument", diagnostic.GetMessage());
    Assert.Contains("decimal", diagnostic.GetMessage());
  }

  [Fact]
  public void GeneratorReportsUnsupportedCallbackResultCodec()
  {
    var result = GeneratorTestHost.Run(
        """
        using System;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class BadModule
        {
          [JS]
          public double Bad(JavaScriptCallback<ValueTuple<string>, decimal> callback) => 0.0;
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI008");
    Assert.Contains("callback", diagnostic.GetMessage());
    Assert.Contains("callback argument", diagnostic.GetMessage());
    Assert.Contains("result type", diagnostic.GetMessage());
    Assert.Contains("decimal", diagnostic.GetMessage());
  }

  [Fact]
  public void GeneratorReportsUnsupportedZeroArgumentCallbackResultCodec()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class BadModule
        {
          [JS]
          public double Bad(JavaScriptCallback<decimal> callback) => 0.0;
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI008");
    Assert.Contains("callback", diagnostic.GetMessage());
    Assert.Contains("result type", diagnostic.GetMessage());
    Assert.Contains("decimal", diagnostic.GetMessage());
  }

  [Fact]
  public void GeneratorEmitsExplicitNestedEightArgumentValueTupleCallbackCodec()
  {
    var result = GeneratorTestHost.Run(
        """
        using System;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class CallbackModule
        {
          [JS]
          public string Use(
              JavaScriptCallback<ValueTuple<string, string, string, string, string, string, string, ValueTuple<string>>, string> callback) => "";
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    var source = Assert.Single(result.GeneratedSources).Text;
    Assert.Contains("JavaScriptCallbackCodec<", source);
    Assert.Contains("ValueTupleCodec<", source);
  }

  [Fact]
  public void GeneratorReportsUnsupportedReturnType()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class BadModule
        {
          [JS]
          public decimal Bad(double value) => 0m;
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI002");
    Assert.Contains("Bad", diagnostic.GetMessage());
    Assert.Contains("System.Decimal", diagnostic.GetMessage());
  }

  [Fact]
  public void GeneratorReportsUnsupportedAsyncReturnType()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;
        using System.Threading.Tasks;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class BadModule
        {
          [JS]
          public Task<decimal> BadAsync() => Task.FromResult(1m);
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI002");
    Assert.Contains("BadAsync", diagnostic.GetMessage());
    Assert.Contains("System.Decimal", diagnostic.GetMessage());
  }

  [Fact]
  public void GeneratorReportsStaticJSMethod()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class BadModule
        {
          [JS]
          public static double Bad() => 1.0;
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI004");
    Assert.Contains("Bad", diagnostic.GetMessage());
    Assert.Contains("static", diagnostic.GetMessage());
  }

  [Fact]
  public void GeneratorReportsGenericJSMethod()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class BadModule
        {
          [JS]
          public T Bad<T>(T value) => value;
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI004");
    Assert.Contains("Bad", diagnostic.GetMessage());
    Assert.Contains("generic", diagnostic.GetMessage());
  }

  [Fact]
  public void GeneratorReportsDuplicateJavaScriptFunctionName()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule("Math")]
        public sealed partial class MathModule
        {
          [JS("same")]
          public double Add(double value) => value + 1.0;

          [JS("same")]
          public double Increment(double value) => value + 2.0;
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI005");
    Assert.Contains("Math", diagnostic.GetMessage());
    Assert.Contains("same", diagnostic.GetMessage());
  }

  [Fact]
  public void GeneratorReportsUnsupportedModuleConstructor()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class BadModule
        {
          public BadModule(double value) {}

          [JS]
          public double Value() => 1.0;
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI003");
    Assert.Contains("Bad", diagnostic.GetMessage());
    Assert.DoesNotContain("new global::Expo.TestModules.BadModule()", string.Join("\n", result.GeneratedSources.Select(source => source.Text)));
  }

  [Fact]
  public void GeneratorUsesContextConstructorWhenParameterlessConstructorIsUnavailable()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class RuntimeAwareModule
        {
          public RuntimeAwareModule(DotnetRuntimeContext context)
          {
          }

          [JS]
          public double Value() => 1.0;
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    var source = Assert.Single(result.GeneratedSources).Text;
    Assert.Contains(
        "context.ModuleRegistry.GetOrCreateModule(\"RuntimeAware\", () => new global::Expo.TestModules.RuntimeAwareModule(context))",
        source
    );
  }

  [Fact]
  public void GeneratorPrefersContextConstructorWhenBothSupportedConstructorsExist()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class DualConstructorModule
        {
          public DualConstructorModule()
          {
          }

          public DualConstructorModule(DotnetRuntimeContext context)
          {
          }

          [JS]
          public double Value() => 1.0;
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    var source = Assert.Single(result.GeneratedSources).Text;
    Assert.Contains(
        "context.ModuleRegistry.GetOrCreateModule(\"DualConstructor\", () => new global::Expo.TestModules.DualConstructorModule(context))",
        source
    );
    Assert.DoesNotContain("new global::Expo.TestModules.DualConstructorModule())", source);
  }

  [Fact]
  public void GeneratorSupportsJavaScriptValueArgumentsAndReturns()
  {
    var result = GeneratorTestHost.Run(
        """
        using System.Threading.Tasks;
        using Expo.JSI;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class ValuesModule
        {
          [JS]
          public JavaScriptValue Echo(JavaScriptValue value) => value.Retain();

          [JS]
          public async Task<JavaScriptValue> EchoAsync(JavaScriptValue value)
          {
            await Task.Yield();
            return value.Retain();
          }
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    var source = Assert.Single(result.GeneratedSources).Text;
    Assert.Contains("using var __expoArg0 = JavaScriptValueCodec.Decode(arguments.GetValue(0), runtime);", source);
    Assert.Contains("return JavaScriptValueCodec.Encode(module.Echo(__expoArg0), runtime);", source);
    Assert.DoesNotContain("using var __expoResult = module.Echo(__expoArg0);", source);
    Assert.Contains("global::Expo.JSI.JavaScriptValue? __expoArg0 = null;", source);
    Assert.Contains("__expoArg0 = JavaScriptValueCodec.Decode(arguments.GetValue(0), jsRuntime);", source);
    Assert.Contains("var __expoTask = module.EchoAsync(__expoArg0!);", source);
    Assert.DoesNotContain("__expoResult.Dispose();", source);
    Assert.Contains("__expoArg0?.Dispose();", source);
  }

  [Fact]
  public void GeneratorReportsDuplicateModuleName()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule("Math")]
        public sealed partial class FirstModule
        {
          [JS]
          public double Add(double value) => value + 1.0;
        }

        [ExpoModule("Math")]
        public sealed partial class SecondModule
        {
          [JS]
          public double Increment(double value) => value + 2.0;
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI006");
    Assert.Contains("Math", diagnostic.GetMessage());
    Assert.DoesNotContain("ModuleRegistry.DefineModule(context.Runtime, modules, \"Math\")", string.Join("\n", result.GeneratedSources.Select(source => source.Text)));
  }

  [Fact]
  public void GeneratorEmitsEnumAndDictionaryCodecs()
  {
    var result = GeneratorTestHost.Run(
        """
        using System.Collections.Generic;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        public enum Mode
        {
          Slow,
          Fast,
        }

        [ExpoModule("Codec")]
        public sealed partial class CodecModule
        {
          [JS]
          public Mode RoundTripMode(Mode mode) => mode;

          [JS]
          public double Total(Dictionary<string, double> values) => 0.0;

          [JS]
          public IReadOnlyDictionary<string, string> Labels() => new Dictionary<string, string>();
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    var source = Assert.Single(result.GeneratedSources).Text;
    Assert.Contains("StringEnumCodec<global::Expo.TestModules.Mode>.Decode(arguments.GetValue(0), runtime)", source);
    Assert.Contains("StringEnumCodec<global::Expo.TestModules.Mode>.Encode(module.RoundTripMode(__expoArg0), runtime)", source);
    Assert.Contains("JavaScriptDictionaryCodec<double, NumberCodec<double>>.DecodeToDictionary(arguments.GetValue(0), runtime)", source);
    Assert.Contains("JavaScriptDictionaryCodec<string, StringCodec>.Encode(module.Labels(), runtime)", source);
  }

  [Fact]
  public void GeneratorReportsUnsupportedDictionaryKeyType()
  {
    var result = GeneratorTestHost.Run(
        """
        using System.Collections.Generic;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule("Bad")]
        public sealed partial class BadModule
        {
          [JS]
          public double Bad(Dictionary<int, double> values) => 0.0;
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI001");
    Assert.Contains("values", diagnostic.GetMessage());
    Assert.Contains("Dictionary", diagnostic.GetMessage());
  }

  [Fact]
  public void GeneratorUsesExplicitNumberEnumRepresentation()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        public enum ParameterMode
        {
          Slow,
          Fast,
        }

        [JSEnum(EnumRepresentation.Number)]
        public enum TypeMode
        {
          Disabled,
          Enabled,
        }

        [ExpoModule("Enums")]
        public sealed partial class EnumsModule
        {
          [JS]
          [return: JSEnum(EnumRepresentation.Number)]
          public ParameterMode RoundTrip(
              [JSEnum(EnumRepresentation.Number)] ParameterMode mode) => mode;

          [JS]
          public TypeMode RoundTripTypeMode(TypeMode mode) => mode;
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    var source = Assert.Single(result.GeneratedSources).Text;
    Assert.Contains("NumberEnumCodec<global::Expo.TestModules.ParameterMode>.Decode(arguments.GetValue(0), runtime)", source);
    Assert.Contains("NumberEnumCodec<global::Expo.TestModules.ParameterMode>.Encode(module.RoundTrip(__expoArg0), runtime)", source);
    Assert.Contains("NumberEnumCodec<global::Expo.TestModules.TypeMode>.Decode(arguments.GetValue(0), runtime)", source);
    Assert.Contains("NumberEnumCodec<global::Expo.TestModules.TypeMode>.Encode(module.RoundTripTypeMode(__expoArg0), runtime)", source);
  }

  [Fact]
  public void GeneratorEmitsSimpleRecordCodecs()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        public record User(string Name, int Age);
        public record class UserClass(string Name, int Age);
        public readonly record struct UserStruct(string Name, int Age);
        public record NullableUser(string Name, int? LuckyNumber);

        [ExpoModule("Records")]
        public sealed partial class RecordsModule
        {
          [JS]
          public User RoundTripUser(User user) => user;

          [JS]
          public UserClass RoundTripUserClass(UserClass user) => user;

          [JS]
          public UserStruct RoundTripUserStruct(UserStruct user) => user;

          [JS]
          public NullableUser RoundTripNullableUser(NullableUser user) => user;
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    var source = Assert.Single(result.GeneratedSources).Text;
    Assert.Contains("private readonly struct UserCodec : global::Expo.ModulesCore.Codecs.IJavaScriptCodec<global::Expo.TestModules.User>", source);
    Assert.Contains("private readonly struct UserClassCodec : global::Expo.ModulesCore.Codecs.IJavaScriptCodec<global::Expo.TestModules.UserClass>", source);
    Assert.Contains("private readonly struct UserStructCodec : global::Expo.ModulesCore.Codecs.IJavaScriptCodec<global::Expo.TestModules.UserStruct>", source);
    Assert.Contains("return new global::Expo.TestModules.User(name, age);", source);
    Assert.Contains("return new global::Expo.TestModules.UserClass(name, age);", source);
    Assert.Contains("return new global::Expo.TestModules.UserStruct(name, age);", source);
    Assert.Contains("StringCodec.Decode(obj.GetProperty(\"name\"), runtime)", source);
    Assert.Contains("StringCodec.Encode(value.Name, runtime)", source);
    Assert.Contains("obj.SetProperty(\"name\", name);", source);
    Assert.Contains("obj.GetProperty(\"luckyNumber\")", source);
    Assert.Contains("obj.SetProperty(\"luckyNumber\", luckyNumber);", source);
    Assert.DoesNotContain("obj.GetProperty(\"Name\")", source);
    Assert.DoesNotContain("obj.GetProperty(\"LuckyNumber\")", source);
  }

  [Fact]
  public void GeneratorEmitsNestedSimpleRecordCodecs()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        public enum Status
        {
          Draft,
          Published,
        }

        public record Address(string City);
        public record User(string Name, Address Address, Status Status);

        [ExpoModule("NestedRecords")]
        public sealed partial class NestedRecordsModule
        {
          [JS]
          public User Move(User user) => user;
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    var source = Assert.Single(result.GeneratedSources).Text;
    Assert.Contains("private readonly struct AddressCodec : global::Expo.ModulesCore.Codecs.IJavaScriptCodec<global::Expo.TestModules.Address>", source);
    Assert.Contains("private readonly struct UserCodec : global::Expo.ModulesCore.Codecs.IJavaScriptCodec<global::Expo.TestModules.User>", source);
    Assert.Contains("var address = AddressCodec.Decode(obj.GetProperty(\"address\"), runtime);", source);
    Assert.Contains("var status = StringEnumCodec<global::Expo.TestModules.Status>.Decode(obj.GetProperty(\"status\"), runtime);", source);
    Assert.Contains("using var address = AddressCodec.Encode(value.Address, runtime);", source);
    Assert.Contains("using var status = StringEnumCodec<global::Expo.TestModules.Status>.Encode(value.Status, runtime);", source);
    Assert.Contains("obj.SetProperty(\"address\", address);", source);
    Assert.Contains("obj.SetProperty(\"status\", status);", source);
    Assert.Contains("return new global::Expo.TestModules.User(name, address, status);", source);
  }

  [Fact]
  public void GeneratorReportsUnsupportedRecordFieldType()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        public record Bad(decimal Value);

        [ExpoModule("BadRecords")]
        public sealed partial class BadRecordsModule
        {
          [JS]
          public Bad RoundTrip(Bad value) => value;
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI007");
    Assert.Contains("Bad", diagnostic.GetMessage());
    Assert.Contains("Value", diagnostic.GetMessage());
    Assert.Contains("System.Decimal", diagnostic.GetMessage());
  }

  [Fact]
  public void GeneratorEmitsNativeModuleRegistrationForEventModule()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule("Device")]
        [Events("onChange", "onReady")]
        public sealed partial class DeviceModule
        {
          public DeviceModule(DotnetRuntimeContext context)
          {
          }
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    var source = Assert.Single(result.GeneratedSources).Text;
    Assert.Contains("context.ModuleRegistry.DefineNativeModule(modules, \"Device\")", source);
    Assert.Contains("context.Events.Attach(", source);
    Assert.Contains("instance_Device", source);
    Assert.Contains("module_Device", source);
    Assert.Contains("new[] { \"onChange\", \"onReady\" }", source);
  }

  [Fact]
  public void GeneratorReportsInvalidEventNames()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule("Device")]
        [Events("onChange", "", "onChange")]
        public sealed partial class DeviceModule
        {
        }
        """
    );

    var diagnostics = result.Diagnostics.Where(item => item.Id == "EXPOJSI009").ToArray();
    Assert.Equal(2, diagnostics.Length);
    Assert.Contains(diagnostics, diagnostic => diagnostic.GetMessage().Contains("empty"));
    Assert.Contains(diagnostics, diagnostic => diagnostic.GetMessage().Contains("duplicate"));
  }

  [Fact]
  public void GeneratorReportsEmptyEventsAttribute()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule("Device")]
        [Events]
        public sealed partial class DeviceModule
        {
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI009");
    Assert.Contains("empty", diagnostic.GetMessage());
  }

  [Fact]
  public void GeneratorEmitsObservingHooksForEventModule()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule("Device")]
        [Events("onChange")]
        public sealed partial class DeviceModule
        {
          [OnStartObserving]
          public void Start(string eventName) {}

          [OnStopObserving("onChange")]
          public void Stop() {}
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    var source = Assert.Single(result.GeneratedSources).Text;
    Assert.Contains("\"startObserving\"", source);
    Assert.Contains("\"stopObserving\"", source);
    Assert.Contains("Device_startObserving_HostFunction", source);
    Assert.Contains("Device_stopObserving_HostFunction", source);
    Assert.Contains("module.Start(__expoEventName);", source);
    Assert.Contains("module.Stop();", source);
  }

  [Fact]
  public void GeneratorReportsInvalidObservingHook()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule("Device")]
        public sealed partial class DeviceModule
        {
          [OnStartObserving]
          public void Start(string eventName) {}
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI010");
    Assert.Contains("require an [Events] declaration", diagnostic.GetMessage());
  }

  [Fact]
  public void GeneratorRejectsReservedObservingFunctionNamesOnEventModules()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule("Device")]
        [Events("onChange")]
        public sealed partial class DeviceModule
        {
          [JS("startObserving")]
          public void Start() {}
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI004");
    Assert.Contains("reserved observing hook name", diagnostic.GetMessage());
  }

  [Fact]
  public void GeneratorEmitsLifecycleHookCallbacksForModule()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule("Lifecycle")]
        public sealed partial class LifecycleModule
        {
          [OnCreate]
          internal void Start() {}

          [OnDestroy]
          public void Stop() {}
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    var source = Assert.Single(result.GeneratedSources).Text;
    Assert.Contains(
        "context.ModuleRegistry.GetOrCreateModule(\"Lifecycle\", static () => new global::Expo.TestModules.LifecycleModule(), static module => module.Start(), static module => module.Stop())",
        source
    );
    Assert.DoesNotContain("\"onCreate\"", source);
    Assert.DoesNotContain("\"onDestroy\"", source);
  }

  [Fact]
  public void GeneratorReportsInvalidLifecycleHook()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule("Lifecycle")]
        public sealed partial class LifecycleModule
        {
          [OnCreate]
          private void Start() {}
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI011");
    Assert.Contains("method must be public or internal", diagnostic.GetMessage());
  }

  [Theory]
  [InlineData("[OnCreate] public static void Start() {}", "method is static")]
  [InlineData("[OnCreate] public void Start<T>() {}", "method is generic")]
  [InlineData("[OnCreate] public int Start() => 0;", "method must return void")]
  [InlineData("[OnCreate] public void Start(string value) {}", "method must not accept parameters")]
  [InlineData("[OnCreate] private void Start() {}", "method must be public or internal")]
  public void GeneratorReportsInvalidLifecycleHookShapes(
      string methodSource,
      string expectedReason)
  {
    var result = GeneratorTestHost.Run(
        $$"""
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule("Lifecycle")]
        public sealed partial class LifecycleModule
        {
          {{methodSource}}
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI011");
    Assert.Contains(expectedReason, diagnostic.GetMessage());
  }

  [Fact]
  public void CompilerRejectsLifecycleAttributeOnNonMethodMembers()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule("Lifecycle")]
        public sealed partial class LifecycleModule
        {
          [OnCreate]
          public int Count;
        }
        """
    );

    Assert.Contains(result.Diagnostics, item => item.Id == "CS0592");
  }

  [Fact]
  public void GeneratorReportsDuplicateLifecycleHook()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule("Lifecycle")]
        public sealed partial class LifecycleModule
        {
          [OnDestroy]
          public void First() {}

          [OnDestroy]
          public void Second() {}
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI011");
    Assert.Contains("duplicate destroy lifecycle hook", diagnostic.GetMessage());
  }
}
