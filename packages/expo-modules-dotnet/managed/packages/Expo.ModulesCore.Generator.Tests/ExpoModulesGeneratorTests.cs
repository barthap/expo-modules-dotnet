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
        using System.Diagnostics.CodeAnalysis;
        using System.Threading.Tasks;
        using System.Diagnostics.CodeAnalysis;

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

  [Fact]
  public void TypedEventPropertiesGenerateCachedDelegatesAndProviderInitialization()
  {
    var result = GeneratorTestHost.Run(
        """
        using System;
        using System.Threading.Tasks;
        using Expo.JSI;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        public sealed record Progress(int Value);

        [ExpoModule("Device")]
        [Events("legacy")]
        public sealed partial class DeviceModule
        {
          public DeviceModule(DotnetRuntimeContext context) {}

          [OnCreate]
          internal void Initialize() {}

          [Event]
          public partial Func<Task> OnReady { get; }

          [Event("StatusChanged")]
          internal partial Func<Progress, Task> OnProgress { get; }

          [Event]
          public partial Func<string, Task> OnText { get; }

          [Event]
          public partial Func<JavaScriptValue, Task> OnValue { get; }

          [Event]
          public partial Func<ArrayBuffer, Task> OnBuffer { get; }
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    Assert.Equal(2, result.GeneratedSources.Count);
    var provider = Assert.Single(result.GeneratedSources, source => source.HintName.Contains("Provider"));
    var partial = Assert.Single(result.GeneratedSources, source => source.HintName.Contains("Events"));
    Assert.Contains("private static global::Expo.TestModules.DeviceModule CreateDevice", provider.Text);
    Assert.Contains("InitializeDeviceEvents(context, module);", provider.Text);
    Assert.Contains("var emitter = context.Events;", provider.Text);
    Assert.Contains("() => emitter.EmitAsync(module, \"onReady\")", provider.Text);
    Assert.Contains("emitter.EmitAsync<ProgressCodec, global::Expo.TestModules.Progress>(module, \"StatusChanged\", onProgressValue)", provider.Text);
    Assert.Contains("emitter.EmitAsync(module, \"onValue\", onValueValue)", provider.Text);
    Assert.Contains("emitter.EmitAsync(module, \"onBuffer\", onBufferValue)", provider.Text);
    Assert.Contains("GetOrCreateModule(\"Device\", () => CreateDevice(context), static module => module.Initialize(), null)", provider.Text);
    Assert.Contains("InitializeDeviceEvents(context, instance_Device);", provider.Text);
    Assert.True(
        provider.Text.IndexOf("() => CreateDevice(context)", StringComparison.Ordinal) <
        provider.Text.IndexOf("static module => module.Initialize()", StringComparison.Ordinal)
    );
    Assert.Contains("new[] { \"legacy\", \"onReady\", \"StatusChanged\", \"onText\", \"onValue\", \"onBuffer\" }", provider.Text);
    Assert.Contains("private global::System.Func<global::System.Threading.Tasks.Task>? __expoEvent_OnReady;", partial.Text);
    Assert.Contains("Event member 'DeviceModule.OnReady' is unavailable before module registration.", partial.Text);
    Assert.Contains("public partial global::System.Func<global::System.Threading.Tasks.Task> OnReady", partial.Text);
    Assert.Contains("internal partial global::System.Func<global::Expo.TestModules.Progress, global::System.Threading.Tasks.Task> OnProgress", partial.Text);
    Assert.DoesNotContain("ProgressCodec", partial.Text);
  }

  [Theory]
  [InlineData("[Event(null!)] public partial Func<Task> OnReady { get; }", "null")]
  [InlineData("[Event(\"\")] public partial Func<Task> OnReady { get; }", "empty")]
  [InlineData("[Event(\" \")] public partial Func<Task> OnReady { get; }", "blank")]
  [InlineData("[Event] public partial Action OnReady { get; }", "Func<Task>")]
  [InlineData("[Event] public partial Func<string> OnReady { get; }", "Func<Task>")]
  [InlineData("[Event] [JS] public partial Func<Task> OnReady { get; }", "[JS]")]
  public void TypedEventPropertyShapeFailuresRemainCompilable(string property, string reason)
  {
    var result = GeneratorTestHost.Run(
        $$"""
        using System;
        using System.Threading.Tasks;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class DeviceModule
        {
          {{property}}
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI018");
    Assert.Contains("OnReady", diagnostic.GetMessage());
    Assert.Contains(reason, diagnostic.GetMessage(), StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain(result.Diagnostics, item =>
        item.Id.StartsWith("CS", StringComparison.Ordinal) && item.Severity == DiagnosticSeverity.Error);
    Assert.Contains("partial", GeneratedText(result));
  }

  [Theory]
  [InlineData("[Event] public static partial Func<Task> OnReady { get; }", "static")]
  [InlineData("[Event] public partial Func<Task> this[int index] { get; }", "indexed")]
  [InlineData("[Event] public Func<Task> OnReady { get; }", "non-partial")]
  [InlineData("[Event] public partial Func<Task> OnReady { get; set; }", "setter")]
  [InlineData("[Event] public partial Func<Task> OnReady => null!;", "implementation")]
  [InlineData("[Event] public virtual partial Func<Task> OnReady { get; }", "virtual")]
  public void TypedEventUnsupportedSyntaxReportsEventPropertyDiagnostic(string property, string reason)
  {
    var result = GeneratorTestHost.Run(
        $$"""
        using System;
        using System.Threading.Tasks;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class DeviceModule
        {
          {{property}}
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI018");
    Assert.Contains(reason, diagnostic.GetMessage(), StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void TypedEventWithAuthoredPartialImplementationIsRejectedWithoutGeneratedEventPartial()
  {
    var result = GeneratorTestHost.Run(
        """
        using System;
        using System.Threading.Tasks;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class DeviceModule
        {
          [Event] public partial Func<Task> OnReady { get; }

          public partial Func<Task> OnReady { get => static () => Task.CompletedTask; }
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI018");
    Assert.Contains("implementation", diagnostic.GetMessage(), StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain(result.Diagnostics, item =>
        item.Id.StartsWith("CS", StringComparison.Ordinal) && item.Severity == DiagnosticSeverity.Error);
    Assert.DoesNotContain("__expoEvent_OnReady", GeneratedText(result));
  }

  [Theory]
  [InlineData("JavaScriptCallback<string>")]
  [InlineData("System.Collections.Generic.IReadOnlyList<JavaScriptCallback<string>>")]
  [InlineData("System.Collections.Generic.Dictionary<string, JavaScriptCallback<string>>")]
  [InlineData("System.Collections.Generic.IReadOnlyList<JavaScriptValue>")]
  public void TypedEventPayloadFailuresDoNotGenerateCallbackCodecs(string payloadType)
  {
    var result = GeneratorTestHost.Run(
        $$"""
        using System;
        using System.Threading.Tasks;
        using Expo.JSI;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class DeviceModule
        {
          [Event] public partial Func<{{payloadType}}, Task> OnPayload { get; }
        }
        """
    );

    Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI019");
    Assert.DoesNotContain("JavaScriptCallbackCodec", GeneratedText(result));
    Assert.DoesNotContain(result.Diagnostics, item =>
        item.Id.StartsWith("CS", StringComparison.Ordinal) && item.Severity == DiagnosticSeverity.Error);
  }

  [Fact]
  public void TypedEventJavaScriptObjectPayloadIsRejectedWithoutCodecOrEventState()
  {
    var result = GeneratorTestHost.Run(
        """
        using System;
        using System.Threading.Tasks;
        using Expo.JSI;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class DeviceModule
        {
          [Event] public partial Func<JavaScriptObject, Task> OnPayload { get; }
        }
        """
    );

    Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI019");
    Assert.DoesNotContain("JavaScriptObjectCodec", GeneratedText(result));
    Assert.DoesNotContain("__expoEvent_OnPayload", GeneratedText(result));
    Assert.DoesNotContain(result.Diagnostics, item =>
        item.Id.StartsWith("CS", StringComparison.Ordinal) && item.Severity == DiagnosticSeverity.Error);
  }

  [Fact]
  public void TypedEventRecordPayloadInspectsOnlySelectedConstructorParameters()
  {
    var result = GeneratorTestHost.Run(
        """
        using System;
        using System.Threading.Tasks;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        public sealed record SafePayload(string Value)
        {
          public JavaScriptCallback<string>? Ignored => null;
        }

        public sealed record UnsafePayload(JavaScriptCallback<string> Callback);

        [ExpoModule]
        public sealed partial class DeviceModule
        {
          [Event] public partial Func<SafePayload, Task> OnSafe { get; }
          [Event] public partial Func<UnsafePayload, Task> OnUnsafe { get; }
        }
        """
    );

    Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI019");
    var generated = GeneratedText(result);
    Assert.Contains("SafePayloadCodec", generated);
    Assert.DoesNotContain("JavaScriptCallbackCodec", generated);
    Assert.DoesNotContain(result.Diagnostics, item =>
        item.Id.StartsWith("CS", StringComparison.Ordinal) && item.Severity == DiagnosticSeverity.Error);
  }

  [Theory]
  [InlineData("[Event] public partial Func<Task> OnReady { get; }\n[Event(\"onReady\")] public partial Func<Task> OnReadyAgain { get; }")]
  [InlineData("[Event(\"legacy\")] public partial Func<Task> OnReady { get; }")]
  public void TypedEventDuplicateNamesUseTypedEventDiagnostics(string properties)
  {
    var legacy = properties.Contains("legacy", StringComparison.Ordinal) ? "[Events(\"legacy\")]" : string.Empty;
    var result = GeneratorTestHost.Run(
        $$"""
        using System;
        using System.Threading.Tasks;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        {{legacy}}
        public sealed partial class DeviceModule
        {
          {{properties}}
        }
        """
    );

    Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI020");
    Assert.DoesNotContain(result.Diagnostics, item =>
        item.Id.StartsWith("CS", StringComparison.Ordinal) && item.Severity == DiagnosticSeverity.Error);
  }

  [Fact]
  public void TypedEventRecursiveRecordPayloadIsRejectedBeforeCodecGeneration()
  {
    var result = GeneratorTestHost.Run(
        """
        using System;
        using System.Threading.Tasks;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        public sealed record Node(Branch Next);
        public sealed record Branch(Node Parent);

        [ExpoModule]
        public sealed partial class DeviceModule
        {
          [Event] public partial Func<Node, Task> OnNode { get; }
        }
        """
    );

    Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI019");
    Assert.DoesNotContain("NodeCodec", GeneratedText(result));
    Assert.DoesNotContain(result.Diagnostics, item =>
        item.Id.StartsWith("CS", StringComparison.Ordinal) && item.Severity == DiagnosticSeverity.Error);
  }

  [Fact]
  public void TypedEventRepeatedNonrecursiveRecordPayloadsRemainSupported()
  {
    var result = GeneratorTestHost.Run(
        """
        using System;
        using System.Threading.Tasks;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        public sealed record Leaf(string Value);
        public sealed record Pair(Leaf First, Leaf Second);

        [ExpoModule]
        public sealed partial class DeviceModule
        {
          [Event] public partial Func<Pair, Task> OnPair { get; }
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, item => item.Severity == DiagnosticSeverity.Error);
    Assert.Contains("PairCodec", GeneratedText(result));
    Assert.Contains("LeafCodec", GeneratedText(result));
  }

  [Fact]
  public void TypedEventPartialsUseQualifiedHintNamesAndEscapeAuthoredIdentifiers()
  {
    var result = GeneratorTestHost.Run(
        """
        using System;
        using System.Threading.Tasks;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule("Other")]
        public sealed partial class @class
        {
          [Event("line\nbreak")] public partial Func<Task> @event { get; }
        }

        [ExpoModule("Same")]
        public sealed partial class OtherModule
        {
          [Event] public partial Func<Task> OnReady { get; }
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, item => item.Severity == DiagnosticSeverity.Error);
    Assert.Equal(3, result.GeneratedSources.Count);
    var partial = Assert.Single(result.GeneratedSources, source => source.Text.Contains("partial class @class"));
    Assert.Contains("partial global::System.Func<global::System.Threading.Tasks.Task> @event", partial.Text);
    Assert.Contains("line\\nbreak", GeneratedText(result));
    Assert.DoesNotContain(result.Diagnostics, item =>
        item.Id.StartsWith("CS", StringComparison.Ordinal) && item.Severity == DiagnosticSeverity.Error);
  }

  [Fact]
  public void TypedEventDuplicateModuleNamesStillEmitDistinctPartialHints()
  {
    var result = GeneratorTestHost.Run(
        """
        using System;
        using System.Threading.Tasks;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule("Same")] public sealed partial class FirstModule { [Event] public partial Func<Task> OnFirst { get; } }
        [ExpoModule("Same")] public sealed partial class SecondModule { [Event] public partial Func<Task> OnSecond { get; } }
        """
    );

    Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI006");
    Assert.Equal(3, result.GeneratedSources.Count);
    Assert.Equal(2, result.GeneratedSources.Count(source => source.HintName.EndsWith(".Events.g.cs", StringComparison.Ordinal)));
    Assert.DoesNotContain(result.Diagnostics, item =>
        item.Id.StartsWith("CS", StringComparison.Ordinal) && item.Severity == DiagnosticSeverity.Error);
  }

  [Theory]
  [InlineData("public partial Func<Task> OnReady { get; set; }", "get", "set")]
  [InlineData("public partial Func<Task> OnReady { get; private set; }", "get", "private set")]
  [InlineData("public partial Func<Task> OnReady { get; init; }", "get", "init")]
  [InlineData("public partial Func<Task> OnReady { set; }", "", "set")]
  [InlineData("public partial Func<Task> OnReady { private get; set; }", "private get", "set")]
  public void TypedEventInertPartialPreservesAuthoredAccessorSyntax(string property, string getter, string setter)
  {
    var result = GeneratorTestHost.Run(
        $$"""
        using System;
        using System.Threading.Tasks;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public partial class DeviceModule
        {
          [Event] {{property}}
        }
        """
    );

    Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI018");
    var partial = Assert.Single(result.GeneratedSources, source => source.HintName.EndsWith(".Events.g.cs", StringComparison.Ordinal));
    if (getter.Length > 0) Assert.Contains(getter + " =>", partial.Text);
    else Assert.DoesNotContain("get =>", partial.Text);
    Assert.Contains(setter + " =>", partial.Text);
    Assert.DoesNotContain(result.Diagnostics, item =>
        item.Id.StartsWith("CS", StringComparison.Ordinal) && item.Severity == DiagnosticSeverity.Error);
  }

  [Fact]
  public void TypedEventHintsRemainUniqueForSanitizedQualifiedTypeCollisions()
  {
    var result = GeneratorTestHost.Run(
        """
        using System;
        using System.Threading.Tasks;
        using Expo.ModulesCore;

        namespace A { [ExpoModule] public partial class B_C { [Event] public partial Func<Task> OnReady { get; } } }
        namespace A_B { [ExpoModule] public partial class C { [Event] public partial Func<Task> OnReady { get; } } }
        """
    );

    var hints = result.GeneratedSources.Where(source => source.HintName.EndsWith(".Events.g.cs", StringComparison.Ordinal)).ToArray();
    Assert.Equal(2, hints.Length);
    Assert.NotEqual(hints[0].HintName, hints[1].HintName);
    Assert.DoesNotContain(result.Diagnostics, item => item.Severity == DiagnosticSeverity.Error);
  }

  [Fact]
  public void TypedEventExplicitNamesEscapeControlAndSurrogateCharacters()
  {
    var result = GeneratorTestHost.Run(
        """
        using System;
        using System.Threading.Tasks;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class DeviceModule
        {
          [Event("line\nbreak")] public partial Func<Task> OnNewline { get; }
          [Event("line\u2028break")] public partial Func<Task> OnLineSeparator { get; }
          [Event("line\u2029break")] public partial Func<Task> OnParagraphSeparator { get; }
          [Event("\uD800")] public partial Func<Task> OnSurrogate { get; }
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, item => item.Severity == DiagnosticSeverity.Error);
    var provider = Assert.Single(result.GeneratedSources, source => source.HintName.Contains("Provider"));
    Assert.Contains("line\\nbreak", provider.Text);
    Assert.Contains("line\\u2028break", provider.Text);
    Assert.Contains("line\\u2029break", provider.Text);
    Assert.Contains("\\uD800", provider.Text);
  }

  [Fact]
  public void TypedEventUnsupportedOrdinaryCodecUsesEventPayloadDiagnostic()
  {
    var result = GeneratorTestHost.Run(
        """
        using System;
        using System.Threading.Tasks;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class DeviceModule
        {
          [Event] public partial Func<decimal, Task> OnAmount { get; }
        }
        """
    );

    Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI019");
    Assert.DoesNotContain(result.Diagnostics, item =>
        item.Id.StartsWith("CS", StringComparison.Ordinal) && item.Severity == DiagnosticSeverity.Error);
  }

  [Fact]
  public void TypedEventParameterlessConstructorUsesCreateAndInitializeHelper()
  {
    var result = GeneratorTestHost.Run(
        """
        using System;
        using System.Threading.Tasks;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class DeviceModule
        {
          public DeviceModule() {}
          [OnCreate] internal void Initialize() {}
          [Event] public partial Func<Task> OnReady { get; }
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, item => item.Severity == DiagnosticSeverity.Error);
    var provider = Assert.Single(result.GeneratedSources, source => source.HintName.Contains("Provider"));
    Assert.Contains("var module = new global::Expo.TestModules.DeviceModule();", provider.Text);
    Assert.Contains("InitializeDeviceEvents(context, module);", provider.Text);
    Assert.True(provider.Text.IndexOf("() => CreateDevice(context)", StringComparison.Ordinal) <
        provider.Text.IndexOf("static module => module.Initialize()", StringComparison.Ordinal));
  }

  [Theory]
  [InlineData("abstract")]
  [InlineData("extern")]
  public void TypedEventBodylessModifiersReportOnlyEventDiagnosticWithoutGeneratedPartial(string modifier)
  {
    var result = GeneratorTestHost.Run(
        $$"""
        using System;
        using System.Threading.Tasks;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public abstract partial class DeviceModule
        {
          [Event] public {{modifier}} partial Func<Task> OnReady { get; }
        }
        """
    );

    Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI018");
    Assert.DoesNotContain("__expoEvent_OnReady", GeneratedText(result));
  }

  [Theory]
  [InlineData("virtual")]
  [InlineData("abstract")]
  [InlineData("override")]
  [InlineData("sealed")]
  [InlineData("required")]
  [InlineData("extern")]
  public void TypedEventUnsupportedModifierMatrixReportsEventDiagnostic(string modifier)
  {
    var result = GeneratorTestHost.Run(
        $$"""
        using System;
        using System.Threading.Tasks;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public partial class DeviceModule
        {
          [Event] public {{modifier}} partial Func<Task> OnReady { get; }
        }
        """
    );

    Assert.Contains(result.Diagnostics, item => item.Id == "EXPOJSI018");
  }

  [Theory]
  [InlineData("new", false)]
  [InlineData("unsafe", true)]
  public void TypedEventNewAndUnsafeModifiersReceiveMatchingInertImplementations(string modifier, bool allowUnsafe)
  {
    var result = GeneratorTestHost.Run(
        $$"""
        using System;
        using System.Threading.Tasks;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public partial class DeviceModule
        {
          [Event] public {{modifier}} partial Func<Task> OnReady { get; }
        }
        """,
        assemblyName: "Expo.TestModules",
        allowUnsafe: allowUnsafe
    );

    Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI018");
    var partial = Assert.Single(result.GeneratedSources, source => source.HintName.EndsWith(".Events.g.cs", StringComparison.Ordinal));
    Assert.Contains($"public {modifier} partial", partial.Text);
    Assert.DoesNotContain(result.Diagnostics, item =>
        item.Id.StartsWith("CS", StringComparison.Ordinal) && item.Severity == DiagnosticSeverity.Error);
  }

  [Theory]
  [InlineData("""
    public interface IEvents { Func<Task> OnReady { get; } }
    [ExpoModule] public partial class DeviceModule : IEvents { [Event] Func<Task> IEvents.OnReady { get; } }
    """)]
  [InlineData("""
    [ExpoModule] public partial class DeviceModule { [Event] public ref Func<Task> OnReady => throw null!; }
    """)]
  [InlineData("""
    [ExpoModule] file partial class DeviceModule { [Event] public partial Func<Task> OnReady { get; } }
    """)]
  [InlineData("""
    public partial class Outer { [ExpoModule] public partial class DeviceModule { [Event] public partial Func<Task> OnReady { get; } } }
    """)]
  [InlineData("""
    [ExpoModule] public partial class DeviceModule<T> { [Event] public partial Func<Task> OnReady { get; } }
    """)]
  [InlineData("""
    [ExpoModule] public class DeviceModule { [Event] public partial Func<Task> OnReady { get; } }
    """)]
  public void TypedEventNonreproducibleShapesReportDiagnosticWithoutGeneratedPartial(string declaration)
  {
    var result = GeneratorTestHost.Run(
        $$"""
        using System;
        using System.Threading.Tasks;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        {{declaration}}
        """
    );

    Assert.Contains(result.Diagnostics, item => item.Id == "EXPOJSI018");
    Assert.DoesNotContain("__expoEvent_OnReady", GeneratedText(result));
  }

  [Theory]
  [InlineData("override")]
  [InlineData("sealed override")]
  public void TypedEventOverridingModifiersReceiveMatchingInertImplementations(string modifier)
  {
    var result = GeneratorTestHost.Run(
        $$"""
        using System;
        using System.Threading.Tasks;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        public abstract class BaseModule
        {
          public virtual Func<Task> OnReady => null!;
        }

        [ExpoModule]
        public partial class DeviceModule : BaseModule
        {
          [Event] public {{modifier}} partial Func<Task> OnReady { get; }
        }
        """
    );

    Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI018");
    var partial = Assert.Single(result.GeneratedSources, source => source.HintName.EndsWith(".Events.g.cs", StringComparison.Ordinal));
    Assert.Contains($"public {modifier} partial", partial.Text);
    Assert.DoesNotContain(result.Diagnostics, item =>
        item.Id.StartsWith("CS", StringComparison.Ordinal) && item.Severity == DiagnosticSeverity.Error);
  }

  [Fact]
  public void TypedEventRequiredModifierReceivesMatchingInertImplementation()
  {
    var result = GeneratorTestHost.Run(
        """
        using System;
        using System.Threading.Tasks;
        using System.Diagnostics.CodeAnalysis;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public partial class DeviceModule
        {
          [SetsRequiredMembers] public DeviceModule() {}
          [Event] public required partial Func<Task> OnReady { get; set; }
        }
        """
    );

    Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI018");
    Assert.Contains("public required partial", GeneratedText(result));
    Assert.DoesNotContain(result.Diagnostics, item =>
        item.Id.StartsWith("CS", StringComparison.Ordinal) && item.Severity == DiagnosticSeverity.Error);
  }

  [Theory]
  [InlineData("[Event] public static partial Func<Task> OnStatic { get; }")]
  [InlineData("[Event] public partial Func<Task> OnSetter { get; private set; }")]
  [InlineData("[Event] public virtual partial Func<Task> OnVirtual { get; }")]
  public void TypedEventReproducibleRejectedPropertiesReceiveInertMatchingImplementations(string property)
  {
    var result = GeneratorTestHost.Run(
        $$"""
        using System;
        using System.Threading.Tasks;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public partial class DeviceModule
        {
          {{property}}
        }
        """
    );

    Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI018");
    Assert.Contains("partial", GeneratedText(result));
    Assert.DoesNotContain(result.Diagnostics, item =>
        item.Id.StartsWith("CS", StringComparison.Ordinal) && item.Severity == DiagnosticSeverity.Error);
  }

  [Fact]
  public void GeneratorAcceptsSharedObjectDeclarationWithImplicitName()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoSharedObject]
        public sealed partial class CacheEntry : SharedObject
        {
        }

        [ExpoModule(Classes = new[] { typeof(CacheEntry) })]
        public sealed partial class CacheModule
        {
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
  }

  [Fact]
  public void GeneratorAcceptsSharedObjectDeclarationWithExplicitName()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoSharedObject("NativeCache")]
        public sealed partial class CacheEntry : SharedObject
        {
        }

        [ExpoModule(Classes = new[] { typeof(CacheEntry) })]
        public sealed partial class CacheModule
        {
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
  }

  [Fact]
  public void GeneratorAcceptsSharedObjectDerivedIndirectlyThroughSharedRef()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoSharedObject]
        public sealed partial class ImageRef : SharedRef<string>
        {
          public ImageRef(string reference) : base(reference)
          {
          }
        }

        [ExpoModule(Classes = new[] { typeof(ImageRef) })]
        public sealed partial class ImageModule
        {
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
  }

  [Fact]
  public void SharedObjectAuthoringApiSurfaceCompiles()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoSharedObject]
        public sealed partial class CacheEntry : SharedObject
        {
          [JS]
          public CacheEntry()
          {
          }

          protected override void OnRelease()
          {
          }
        }

        [ExpoSharedObject]
        public sealed partial class ImageRef : SharedRef<string>
        {
          public ImageRef(string reference) : base(reference)
          {
          }

          public string Reference => Ref;
        }

        [ExpoModule(Classes = new[] { typeof(CacheEntry), typeof(ImageRef) })]
        public sealed partial class CacheModule
        {
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
  }

  [Fact]
  public void SharedObjectSharedRefRefPropertyIsReadOnly()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoSharedObject]
        public sealed partial class ImageRef : SharedRef<string>
        {
          public ImageRef(string reference) : base(reference)
          {
          }
        }

        public static class Mutator
        {
          public static void Mutate(ImageRef image) => image.Ref = "other";
        }
        """
    );

    Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "CS0200");
  }

  [Theory]
  [InlineData(
      """
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      public sealed class Container
      {
        [ExpoSharedObject]
        public sealed partial class CacheEntry : SharedObject
        {
        }
      }
      """,
      "CacheEntry",
      "top-level")]
  [InlineData(
      """
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      [ExpoSharedObject]
      public sealed partial class CacheEntry<T> : SharedObject
      {
      }
      """,
      "CacheEntry",
      "non-generic")]
  [InlineData(
      """
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      [ExpoSharedObject]
      public partial class CacheEntry : SharedObject
      {
      }
      """,
      "CacheEntry",
      "sealed")]
  [InlineData(
      """
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      [ExpoSharedObject]
      public sealed class CacheEntry : SharedObject
      {
      }
      """,
      "CacheEntry",
      "partial")]
  [InlineData(
      """
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      [ExpoSharedObject]
      public sealed partial class CacheEntry
      {
      }
      """,
      "CacheEntry",
      "derive from Expo.ModulesCore.SharedObject")]
  [InlineData(
      """
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      [ExpoSharedObject(null)]
      public sealed partial class CacheEntry : SharedObject
      {
      }
      """,
      "CacheEntry",
      "non-empty")]
  [InlineData(
      """
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      [ExpoSharedObject("")]
      public sealed partial class CacheEntry : SharedObject
      {
      }
      """,
      "CacheEntry",
      "non-empty")]
  [InlineData(
      """
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      [ExpoSharedObject("   ")]
      public sealed partial class CacheEntry : SharedObject
      {
      }
      """,
      "CacheEntry",
      "non-empty")]
  public void GeneratorReportsInvalidSharedObjectDeclaration(
      string source,
      string typeName,
      string expectedReason)
  {
    var result = GeneratorTestHost.Run(source);

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI021");
    Assert.Contains(typeName, diagnostic.GetMessage());
    Assert.Contains(expectedReason, diagnostic.GetMessage());
    Assert.NotEqual(Location.None, diagnostic.Location);
    var locatedText = diagnostic.Location.SourceTree!
        .GetText(TestContext.Current.CancellationToken)
        .ToString(diagnostic.Location.SourceSpan);
    Assert.Contains(typeName, locatedText);
    Assert.DoesNotContain(result.Diagnostics, item =>
        item.Id.StartsWith("CS", StringComparison.Ordinal) && item.Severity == DiagnosticSeverity.Error);
  }

  [Theory]
  [InlineData(
      """
        [JS]
        public CacheEntry()
        {
        }

        [JS]
        public CacheEntry(int size)
        {
        }
      """,
      "declares multiple [JS] constructors")]
  [InlineData(
      """
        [JS]
        private CacheEntry()
        {
        }
      """,
      "must be public or internal")]
  [InlineData(
      """
        [JS("create")]
        public CacheEntry()
        {
        }
      """,
      "must not declare an explicit JavaScript name")]
  [InlineData(
      """
        [JS]
        static CacheEntry()
        {
        }
      """,
      "must be an instance constructor")]
  public void GeneratorReportsInvalidSharedObjectConstructor(string constructor, string expectedReason)
  {
    var result = GeneratorTestHost.Run(
        $$"""
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoSharedObject]
        public sealed partial class CacheEntry : SharedObject
        {
        {{constructor}}
        }

        [ExpoModule(Classes = new[] { typeof(CacheEntry) })]
        public sealed partial class CacheModule
        {
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI022");
    Assert.Contains("CacheEntry", diagnostic.GetMessage());
    Assert.Contains(expectedReason, diagnostic.GetMessage());
    Assert.NotEqual(Location.None, diagnostic.Location);
    var locatedText = diagnostic.Location.SourceTree!
        .GetText(TestContext.Current.CancellationToken)
        .ToString(diagnostic.Location.SourceSpan);
    Assert.Contains("CacheEntry", locatedText);
    Assert.DoesNotContain(result.Diagnostics, item =>
        item.Id.StartsWith("CS", StringComparison.Ordinal) && item.Severity == DiagnosticSeverity.Error);
  }

  [Theory]
  [InlineData(
      "[JS] public CacheEntry(decimal size) { }",
      "CacheEntry",
      "size",
      "System.Decimal")]
  [InlineData(
      "[JS] public CacheEntry(System.Span<byte> data) { }",
      "CacheEntry",
      "data",
      "Span")]
  public void GeneratorReportsSharedObjectConstructorParameterWithoutCodec(
      string constructor,
      string typeName,
      string parameterName,
      string expectedType)
  {
    var result = GeneratorTestHost.Run(
        $$"""
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoSharedObject]
        public sealed partial class CacheEntry : SharedObject
        {
          {{constructor}}
        }

        [ExpoModule(Classes = new[] { typeof(CacheEntry) })]
        public sealed partial class CacheModule
        {
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI023");
    Assert.Contains(typeName, diagnostic.GetMessage());
    Assert.Contains($"constructor parameter '{parameterName}'", diagnostic.GetMessage());
    Assert.Contains(expectedType, diagnostic.GetMessage());
    Assert.NotEqual(Location.None, diagnostic.Location);
    Assert.DoesNotContain(result.Diagnostics, item =>
        item.Id.StartsWith("CS", StringComparison.Ordinal) && item.Severity == DiagnosticSeverity.Error);
  }

  [Theory]
  [InlineData(
      "[JS] public CacheEntry(SharedObject value) { }",
      "polymorphic SharedObject base")]
  [InlineData(
      "[JS] public CacheEntry(SharedRef<string> value) { }",
      "managed carrier base")]
  [InlineData(
      "[JS] public CacheEntry(UnattributedEntry value) { }",
      "not marked [ExpoSharedObject]")]
  public void GeneratorReportsSharedObjectConstructorParameterBoundaryUse(
      string constructor,
      string expectedReason)
  {
    var result = GeneratorTestHost.Run(
        $$"""
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        public sealed class UnattributedEntry : SharedObject
        {
        }

        [ExpoSharedObject]
        public sealed partial class CacheEntry : SharedObject
        {
          {{constructor}}
        }

        [ExpoModule(Classes = new[] { typeof(CacheEntry) })]
        public sealed partial class CacheModule
        {
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI023");
    Assert.Contains("CacheEntry", diagnostic.GetMessage());
    Assert.Contains("constructor parameter 'value'", diagnostic.GetMessage());
    Assert.Contains(expectedReason, diagnostic.GetMessage());
    Assert.NotEqual(Location.None, diagnostic.Location);
    Assert.DoesNotContain(result.Diagnostics, item =>
        item.Id.StartsWith("CS", StringComparison.Ordinal) && item.Severity == DiagnosticSeverity.Error);
  }

  [Fact]
  public void GeneratorAcceptsOwnedSharedObjectConstructorParameter()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoSharedObject]
        public sealed partial class CacheEntry : SharedObject
        {
          [JS]
          public CacheEntry()
          {
          }
        }

        [ExpoSharedObject]
        public sealed partial class Snapshot : SharedObject
        {
          [JS]
          public Snapshot(CacheEntry entry)
          {
          }
        }

        [ExpoModule(Classes = new[] { typeof(CacheEntry), typeof(Snapshot) })]
        public sealed partial class CacheModule
        {
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
  }

  [Theory]
  [InlineData("[JS] public static int GetSize() => 1;", "GetSize", "static")]
  [InlineData("[JS] public T Identity<T>(T value) => value;", "Identity", "generic")]
  [InlineData("[JS] internal protected int GetSize() => 1;", "GetSize", "public or internal")]
  [InlineData("[JS] public static bool Ready => true;", "Ready", "static")]
  [InlineData("[JS] public bool this[int index] => true;", "this[]", "indexed")]
  [InlineData("[JS] public bool Ready { set { } }", "Ready", "setter-only")]
  [InlineData("[JS] public bool Ready { get; init; }", "Ready", "init")]
  [InlineData("[JS] public decimal Total => 0m;", "Total", "System.Decimal")]
  [InlineData("[JS] public decimal GetTotal() => 0m;", "GetTotal", "System.Decimal")]
  [InlineData("[JS] public void Store(decimal value) { }", "Store", "System.Decimal")]
  public void GeneratorReportsInvalidSharedObjectMember(
      string member,
      string memberName,
      string expectedReason)
  {
    var result = GeneratorTestHost.Run(
        $$"""
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoSharedObject]
        public sealed partial class CacheEntry : SharedObject
        {
          {{member}}
        }

        [ExpoModule(Classes = new[] { typeof(CacheEntry) })]
        public sealed partial class CacheModule
        {
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI023");
    Assert.Contains(memberName, diagnostic.GetMessage());
    Assert.Contains(expectedReason, diagnostic.GetMessage());
    Assert.NotEqual(Location.None, diagnostic.Location);
    Assert.DoesNotContain(result.Diagnostics, item =>
        item.Id.StartsWith("CS", StringComparison.Ordinal) && item.Severity == DiagnosticSeverity.Error);
  }

  [Fact]
  public void GeneratorReportsNonPartialSharedObjectEvent()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoSharedObject]
        public sealed partial class CacheEntry : SharedObject
        {
          [Event] public System.Func<System.Threading.Tasks.Task> OnChange { get; } = null!;
        }

        [ExpoModule(Classes = new[] { typeof(CacheEntry) })]
        public sealed partial class CacheModule
        {
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI026");
    Assert.Contains("OnChange", diagnostic.GetMessage());
    Assert.Contains("non-partial", diagnostic.GetMessage());
    Assert.DoesNotContain(result.Diagnostics, item =>
        item.Id.StartsWith("CS", StringComparison.Ordinal) && item.Severity == DiagnosticSeverity.Error);
  }

  [Theory]
  [InlineData(
      "[JS] public void Store(SharedObject value) { }",
      "Store",
      "polymorphic SharedObject base")]
  [InlineData(
      "[JS] public void StoreRef(SharedRef<string> value) { }",
      "StoreRef",
      "managed carrier base")]
  [InlineData(
      "[JS] public SharedObject Load() => null!;",
      "Load",
      "polymorphic SharedObject base")]
  [InlineData(
      "[JS] public System.Threading.Tasks.Task<SharedObject> LoadAsync() => null!;",
      "LoadAsync",
      "polymorphic SharedObject base")]
  [InlineData(
      "[JS] public UnattributedEntry LoadCustom() => null!;",
      "LoadCustom",
      "not marked [ExpoSharedObject]")]
  [InlineData(
      "[JS] public SharedObject Value => null!;",
      "Value",
      "polymorphic SharedObject base")]
  public void GeneratorReportsDirectSharedObjectBaseBoundaryUse(
      string member,
      string memberName,
      string expectedReason)
  {
    var result = GeneratorTestHost.Run(
        $$"""
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        public sealed class UnattributedEntry : SharedObject
        {
        }

        [ExpoModule]
        public sealed partial class CacheModule
        {
          {{member}}
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI023");
    Assert.Contains(memberName, diagnostic.GetMessage());
    Assert.Contains(expectedReason, diagnostic.GetMessage());
    Assert.NotEqual(Location.None, diagnostic.Location);
    Assert.DoesNotContain(result.Diagnostics, item =>
        item.Id.StartsWith("CS", StringComparison.Ordinal) && item.Severity == DiagnosticSeverity.Error);
  }

  [Theory]
  [InlineData(
      "[JS] public void Store(CacheEntry? entry) { }",
      "Store",
      "parameter 'entry'")]
  [InlineData(
      "[JS] public CacheEntry? Load() => null;",
      "Load",
      "return type")]
  [InlineData(
      "[JS] public System.Threading.Tasks.Task<CacheEntry?> LoadAsync() => null!;",
      "LoadAsync",
      "async result type")]
  [InlineData(
      "[JS] public CacheEntry? Latest => null;",
      "Latest",
      "property type")]
  public void GeneratorReportsNullableAnnotatedSharedObjectBoundaryUse(
      string member,
      string memberName,
      string expectedPosition)
  {
    var result = GeneratorTestHost.Run(
        $$"""
        #nullable enable
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoSharedObject]
        public sealed partial class CacheEntry : SharedObject
        {
        }

        [ExpoModule(Classes = new[] { typeof(CacheEntry) })]
        public sealed partial class CacheModule
        {
          {{member}}
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI023");
    Assert.Contains(memberName, diagnostic.GetMessage());
    Assert.Contains(expectedPosition, diagnostic.GetMessage());
    Assert.Contains("without a nullable annotation", diagnostic.GetMessage());
    Assert.NotEqual(Location.None, diagnostic.Location);
    Assert.DoesNotContain(result.Diagnostics, item =>
        item.Id.StartsWith("CS", StringComparison.Ordinal) && item.Severity == DiagnosticSeverity.Error);
  }

  [Fact]
  public void GeneratorReportsNullableAnnotatedSharedObjectConstructorParameter()
  {
    var result = GeneratorTestHost.Run(
        """
        #nullable enable
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoSharedObject]
        public sealed partial class CacheEntry : SharedObject
        {
          [JS]
          public CacheEntry(CacheEntry? source)
          {
          }
        }

        [ExpoModule(Classes = new[] { typeof(CacheEntry) })]
        public sealed partial class CacheModule
        {
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI023");
    Assert.Contains("CacheEntry", diagnostic.GetMessage());
    Assert.Contains("constructor parameter 'source'", diagnostic.GetMessage());
    Assert.Contains("without a nullable annotation", diagnostic.GetMessage());
    Assert.NotEqual(Location.None, diagnostic.Location);
    Assert.DoesNotContain(result.Diagnostics, item =>
        item.Id.StartsWith("CS", StringComparison.Ordinal) && item.Severity == DiagnosticSeverity.Error);
  }

  // Only the inaccessible overload may be removed from the model; the accessible overload
  // keeps its own signature. Shared-object members are validated but not emitted in this
  // slice, so survival is observed through the reserved-name validation that runs after
  // inaccessible members are filtered out: the accessible overload must still reach it.
  [Fact]
  public void GeneratorKeepsAccessibleSharedObjectOverloadOfInaccessibleMethod()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoSharedObject]
        public sealed partial class CacheEntry : SharedObject
        {
          [JS("releaseNumber")] private void Release(int value) { }
          [JS("release")] public void Release(string value) { }
        }

        [ExpoModule(Classes = new[] { typeof(CacheEntry) })]
        public sealed partial class CacheModule
        {
        }
        """
    );

    var inaccessible = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI023");
    Assert.Contains("Release", inaccessible.GetMessage());
    Assert.Contains("not public or internal", inaccessible.GetMessage());
    var reserved = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI025");
    Assert.Contains("'release'", reserved.GetMessage());
    Assert.Contains("reserved for the shared object prototype", reserved.GetMessage());
    Assert.DoesNotContain(result.Diagnostics, item =>
        item.Id.StartsWith("CS", StringComparison.Ordinal) && item.Severity == DiagnosticSeverity.Error);
  }

  // Duplicate JavaScript-name detection runs over all authored [JS] members before
  // accessibility filtering, so a private overload that also collides on the JavaScript
  // name keeps reporting both problems: EXPOJSI023 for the inaccessible member and
  // EXPOJSI025 for the authored name collision. Each diagnostic points at a distinct
  // authored issue, and neither member is emitted.
  [Fact]
  public void GeneratorReportsInaccessibleAndDuplicateSharedObjectOverloadsSeparately()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoSharedObject]
        public sealed partial class CacheEntry : SharedObject
        {
          [JS] private void Store(int value) { }
          [JS] public void Store(string value) { }
        }

        [ExpoModule(Classes = new[] { typeof(CacheEntry) })]
        public sealed partial class CacheModule
        {
        }
        """
    );

    var inaccessible = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI023");
    Assert.Contains("Store", inaccessible.GetMessage());
    Assert.Contains("not public or internal", inaccessible.GetMessage());
    var duplicate = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI025");
    Assert.Contains("'store'", duplicate.GetMessage());
    Assert.Contains("a duplicate", duplicate.GetMessage());
    Assert.DoesNotContain(result.Diagnostics, item =>
        item.Id.StartsWith("CS", StringComparison.Ordinal) && item.Severity == DiagnosticSeverity.Error);
  }

  [Fact]
  public void GeneratorReportsDirectSharedObjectBaseUseOnSharedObjectMember()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoSharedObject]
        public sealed partial class CacheEntry : SharedObject
        {
          [JS] public void Store(SharedRef<string> value) { }
        }

        [ExpoModule(Classes = new[] { typeof(CacheEntry) })]
        public sealed partial class CacheModule
        {
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI023");
    Assert.Contains("Store", diagnostic.GetMessage());
    Assert.Contains("managed carrier base", diagnostic.GetMessage());
  }

  [Theory]
  [InlineData(
      "[JS] public void StoreAll(System.Collections.Generic.IReadOnlyList<CacheEntry> entries) { }",
      "StoreAll")]
  [InlineData(
      "[JS] public void StoreMap(System.Collections.Generic.Dictionary<string, CacheEntry> entries) { }",
      "StoreMap")]
  [InlineData(
      "[JS] public void StoreHolder(Holder holder) { }",
      "StoreHolder")]
  [InlineData(
      "[JS] public void Subscribe(JavaScriptCallback<CacheEntry> callback) { }",
      "Subscribe")]
  [InlineData(
      "[JS] public System.Collections.Generic.IReadOnlyList<CacheEntry> LoadAll() => null!;",
      "LoadAll")]
  public void GeneratorReportsNestedSharedObjectComposition(string member, string memberName)
  {
    var result = GeneratorTestHost.Run(
        $$"""
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoSharedObject]
        public sealed partial class CacheEntry : SharedObject
        {
        }

        public sealed record Holder(CacheEntry Entry);

        [ExpoModule(Classes = new[] { typeof(CacheEntry) })]
        public sealed partial class CacheModule
        {
          {{member}}
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI023");
    Assert.Contains(memberName, diagnostic.GetMessage());
    Assert.Contains("CacheEntry", diagnostic.GetMessage());
    Assert.Contains("composed codec", diagnostic.GetMessage());
    Assert.NotEqual(Location.None, diagnostic.Location);
    Assert.DoesNotContain(result.Diagnostics, item =>
        item.Id.StartsWith("CS", StringComparison.Ordinal) && item.Severity == DiagnosticSeverity.Error);
  }

  [Theory]
  [InlineData(
      """
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      [ExpoModule(Classes = new[] { typeof(string) })]
      public sealed partial class CacheModule
      {
      }
      """,
      "string",
      "not an [ExpoSharedObject] class")]
  [InlineData(
      """
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      public sealed class UnattributedEntry : SharedObject
      {
      }

      [ExpoModule(Classes = new[] { typeof(UnattributedEntry) })]
      public sealed partial class CacheModule
      {
      }
      """,
      "UnattributedEntry",
      "not an [ExpoSharedObject] class")]
  [InlineData(
      """
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      [ExpoSharedObject]
      public sealed partial class CacheEntry : SharedObject
      {
      }

      [ExpoModule(Classes = new[] { typeof(CacheEntry), typeof(CacheEntry) })]
      public sealed partial class CacheModule
      {
      }
      """,
      "CacheEntry",
      "more than once")]
  [InlineData(
      """
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      [ExpoSharedObject]
      public sealed partial class CacheEntry : SharedObject
      {
      }
      """,
      "CacheEntry",
      "no module lists it")]
  [InlineData(
      """
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      [ExpoSharedObject]
      public sealed partial class CacheEntry : SharedObject
      {
      }

      [ExpoModule(Classes = new[] { typeof(CacheEntry) })]
      public sealed partial class CacheModule
      {
      }

      [ExpoModule(Classes = new[] { typeof(CacheEntry) })]
      public sealed partial class BackupModule
      {
      }
      """,
      "CacheEntry",
      "multiple modules")]
  [InlineData(
      """
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      [ExpoSharedObject("Entry")]
      public sealed partial class CacheEntry : SharedObject
      {
      }

      [ExpoSharedObject("Entry")]
      public sealed partial class DiskEntry : SharedObject
      {
      }

      [ExpoModule(Classes = new[] { typeof(CacheEntry), typeof(DiskEntry) })]
      public sealed partial class CacheModule
      {
      }
      """,
      "Entry",
      "already used")]
  [InlineData(
      """
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      [ExpoSharedObject("getSize")]
      public sealed partial class CacheEntry : SharedObject
      {
        [JS]
        public CacheEntry()
        {
        }
      }

      [ExpoModule(Classes = new[] { typeof(CacheEntry) })]
      public sealed partial class CacheModule
      {
        [JS] public int GetSize() => 1;
      }
      """,
      "getSize",
      "generated function 'getSize'")]
  [InlineData(
      """
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      [ExpoSharedObject("ready")]
      public sealed partial class CacheEntry : SharedObject
      {
        [JS]
        public CacheEntry()
        {
        }
      }

      [ExpoModule(Classes = new[] { typeof(CacheEntry) })]
      public sealed partial class CacheModule
      {
        [JS] public bool Ready => true;
      }
      """,
      "ready",
      "generated property 'ready'")]
  [InlineData(
      """
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      [ExpoSharedObject("Entry")]
      public sealed partial class CacheEntry : SharedObject
      {
        [JS]
        public CacheEntry()
        {
        }
      }

      [ExpoSharedObject("Entry")]
      public sealed partial class DiskEntry : SharedObject
      {
        [JS]
        public DiskEntry()
        {
        }
      }

      [ExpoModule(Classes = new[] { typeof(CacheEntry), typeof(DiskEntry) })]
      public sealed partial class CacheModule
      {
      }
      """,
      "Entry",
      "already used")]
  [InlineData(
      """
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      [ExpoSharedObject("startObserving")]
      public sealed partial class CacheEntry : SharedObject
      {
        [JS]
        public CacheEntry()
        {
        }
      }

      [ExpoModule(Classes = new[] { typeof(CacheEntry) })]
      [Events("change")]
      public sealed partial class CacheModule
      {
        [OnStartObserving] public void Start(string eventName) { }
      }
      """,
      "startObserving",
      "observing hook 'startObserving'")]
  [InlineData(
      """
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      [ExpoSharedObject("addListener")]
      public sealed partial class CacheEntry : SharedObject
      {
        [JS]
        public CacheEntry()
        {
        }
      }

      [ExpoModule(Classes = new[] { typeof(CacheEntry) })]
      [Events("change")]
      public sealed partial class CacheModule
      {
      }
      """,
      "addListener",
      "reserved event-runtime member 'addListener'")]
  public void GeneratorReportsInvalidSharedObjectOwnership(
      string source,
      string expectedName,
      string expectedReason)
  {
    var result = GeneratorTestHost.Run(source);

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI024");
    Assert.Contains(expectedName, diagnostic.GetMessage());
    Assert.Contains(expectedReason, diagnostic.GetMessage());
    Assert.NotEqual(Location.None, diagnostic.Location);
    Assert.DoesNotContain(result.Diagnostics, item =>
        item.Id.StartsWith("CS", StringComparison.Ordinal) && item.Severity == DiagnosticSeverity.Error);
  }

  [Theory]
  [InlineData(
      """
        [JS] public int GetSize() => 1;
        [JS("getSize")] public int Size() => 2;
      """,
      "getSize",
      "a duplicate")]
  [InlineData(
      """
        [JS] public int GetSize() => 1;
        [JS("getSize")] public bool Ready => true;
      """,
      "getSize",
      "a duplicate")]
  [InlineData(
      "[JS] public void Release() { }",
      "release",
      "reserved for the shared object prototype")]
  [InlineData(
      "[JS(\"constructor\")] public void Rebuild() { }",
      "constructor",
      "reserved for the shared object prototype")]
  [InlineData(
      "[JS(\"__proto__\")] public bool Proto => true;",
      "__proto__",
      "reserved for the shared object prototype")]
  public void GeneratorReportsDuplicateOrReservedSharedObjectMemberName(
      string members,
      string javaScriptName,
      string expectedReason)
  {
    var result = GeneratorTestHost.Run(
        $$"""
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoSharedObject]
        public sealed partial class CacheEntry : SharedObject
        {
        {{members}}
        }

        [ExpoModule(Classes = new[] { typeof(CacheEntry) })]
        public sealed partial class CacheModule
        {
        }
        """
    );

    var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI025");
    Assert.Contains("CacheEntry", diagnostic.GetMessage());
    Assert.Contains($"'{javaScriptName}'", diagnostic.GetMessage());
    Assert.Contains(expectedReason, diagnostic.GetMessage());
    Assert.NotEqual(Location.None, diagnostic.Location);
    Assert.DoesNotContain(result.Diagnostics, item =>
        item.Id.StartsWith("CS", StringComparison.Ordinal) && item.Severity == DiagnosticSeverity.Error);
  }

  [Fact]
  public void GeneratorAcceptsSharedObjectMembersAndModuleBoundaries()
  {
    var result = GeneratorTestHost.Run(
        """
        using System.Threading.Tasks;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoSharedObject]
        public sealed partial class CacheEntry : SharedObject
        {
          [JS]
          public CacheEntry(string key, int size = 16)
          {
            Key = key;
            Size = size;
          }

          [JS] public string Key { get; }
          [JS] public int Size { get; set; }
          [JS] public string Tag { get; internal set; } = "";
          [JS] public int GetCost() => Size;
          [JS("resetNow")] public void Reset() { }
          [JS] public Task Refresh() => Task.CompletedTask;
          [JS] public Task<int> LoadAsync() => Task.FromResult(Size);
          [JS] public CacheEntry Clone(CacheEntry other) => other;
        }

        [ExpoSharedObject("NativeSnapshot")]
        public sealed partial class Snapshot : SharedObject
        {
          [JS] public int Version => 1;
        }

        [ExpoModule(Classes = new[] { typeof(CacheEntry), typeof(Snapshot) })]
        public sealed partial class CacheModule
        {
          [JS] public CacheEntry MakeEntry(CacheEntry template) => template;
          [JS] public Task<Snapshot> CaptureAsync() => Task.FromResult<Snapshot>(null!);
          [JS] public CacheEntry Latest { get; set; } = null!;
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
  }

  [Fact]
  public void GeneratorEmitsAwaitableSharedObjectEventBinding()
  {
    var result = GeneratorTestHost.Run(
        """
        using System;
        using System.Threading.Tasks;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        public sealed record Progress(double Value);

        [ExpoSharedObject]
        public sealed partial class CacheEntry : SharedObject
        {
          [Event]
          public partial Func<Progress, Task> OnProgress { get; }
        }

        [ExpoModule(Classes = new[] { typeof(CacheEntry) })]
        public sealed partial class CacheModule
        {
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    var provider = Assert.Single(result.GeneratedSources, source => source.HintName.Contains("Provider"));
    var partial = Assert.Single(result.GeneratedSources, source => source.HintName.Contains("SharedObjectEvents"));
    Assert.Contains("private readonly struct ProgressCodec", provider.Text);
    Assert.Contains(
        "GeneratedSharedObjectEvents.EmitAsync<ProgressCodec, global::Expo.TestModules.Progress>(context, sharedObject, \"onProgress\", onProgressValue)",
        provider.Text
    );
    Assert.Contains("__ExpoModulesCoreInitializeSharedObjectEvents", partial.Text);
  }

  [Theory]
  [InlineData("[Event] public partial Action OnProgress { get; }", "EXPOJSI026")]
  [InlineData("[Event] public partial Func<JavaScriptCallback<string>, Task> OnProgress { get; }", "EXPOJSI027")]
  [InlineData("[Event(\"release\")] public partial Func<Task> OnProgress { get; }", "EXPOJSI028")]
  public void GeneratorReportsInvalidSharedObjectEvent(string member, string diagnosticId)
  {
    var result = GeneratorTestHost.Run(
        $$"""
        using System;
        using System.Threading.Tasks;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoSharedObject]
        public sealed partial class CacheEntry : SharedObject
        {
          {{member}}
        }

        [ExpoModule(Classes = new[] { typeof(CacheEntry) })]
        public sealed partial class CacheModule
        {
        }
        """
    );

    Assert.Single(result.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
    Assert.DoesNotContain(result.Diagnostics, diagnostic =>
        diagnostic.Id.StartsWith("CS", StringComparison.Ordinal) && diagnostic.Severity == DiagnosticSeverity.Error);
  }

  [Fact]
  public void GeneratorAcceptsConcreteSharedRefSubclassAtBoundary()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoSharedObject]
        public sealed partial class ImageRef : SharedRef<string>
        {
          public ImageRef(string reference) : base(reference)
          {
          }
        }

        [ExpoModule(Classes = new[] { typeof(ImageRef) })]
        public sealed partial class ImageModule
        {
          [JS] public ImageRef Snapshot(ImageRef source) => source;
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
  }

  [Fact]
  public void GeneratorEmitsSharedObjectClassInstallationAndDirectBindings()
  {
    var result = GeneratorTestHost.Run("""
        using System.Threading.Tasks;
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoSharedObject]
        public sealed partial class CacheEntry : SharedObject
        {
          [JS]
          public CacheEntry(double start)
          {
            Total = start;
          }

          [JS]
          public double Total { get; set; }

          [JS]
          public double Increment(double by)
          {
            Total += by;
            return Total;
          }
        }

        [ExpoModule(Classes = new[] { typeof(CacheEntry) })]
        public sealed partial class CacheModule
        {
          [JS]
          public CacheEntry MakeEntry(double start) => new(start);

          [JS]
          public double ReadEntry(CacheEntry entry) => entry.Total;

          [JS]
          public async Task<CacheEntry> MakeEntryLater(double start)
          {
            await Task.Yield();
            return new CacheEntry(start);
          }
        }
        """);

    Assert.DoesNotContain(
        result.Diagnostics,
        diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
    );
    var text = GeneratedText(result);

    // Class installation happens inside the module registration function, after the module is
    // materialized, and not in the lazy provider metadata registration.
    var installIndex = text.IndexOf("GeneratedSharedObjectClass.Install(", StringComparison.Ordinal);
    var materializeIndex = text.IndexOf("GetOrCreateModule(", StringComparison.Ordinal);
    var lazyMetadataIndex = text.IndexOf("RegisterLazyModule(", StringComparison.Ordinal);
    Assert.True(installIndex > materializeIndex && materializeIndex > 0);
    Assert.True(lazyMetadataIndex > 0 && installIndex > lazyMetadataIndex);
    Assert.DoesNotContain(
        "GeneratedSharedObjectClass.Install(",
        text.Substring(lazyMetadataIndex, materializeIndex - lazyMetadataIndex)
    );

    // The receiver resolves through SharedObjectCodec<T> and the current runtime context before
    // authored code runs, and the authored constructor/method/property are called directly.
    Assert.Contains(
        "SharedObjectCodec<global::Expo.TestModules.CacheEntry>.Decode(thisValue, runtime, GeneratedFunction.CurrentRuntimeContext)",
        text
    );
    Assert.Contains("new global::Expo.TestModules.CacheEntry(", text);
    Assert.Contains("module.Increment(", text);
    Assert.Contains("module.Total", text);
    Assert.Contains("typeof(global::Expo.TestModules.CacheEntry)", text);

    // Shared decode and encode pass the runtime context explicitly.
    Assert.Contains(
        "SharedObjectCodec<global::Expo.TestModules.CacheEntry>.Decode(arguments.GetValue(0), runtime, GeneratedFunction.CurrentRuntimeContext)",
        text
    );
    Assert.Contains(
        "SharedObjectCodec<global::Expo.TestModules.CacheEntry>.Encode(module.MakeEntry(__expoArg0), runtime, GeneratedFunction.CurrentRuntimeContext)",
        text
    );

    // Asynchronous shared results capture the exact runtime context inside the host-function
    // frame and settle with the captured context, never the thread-static accessor.
    Assert.Contains("var __expoRuntimeContext = GeneratedFunction.CurrentRuntimeContext;", text);
    Assert.Contains(
        "SharedObjectCodec<global::Expo.TestModules.CacheEntry>.Encode(__expoResult, runtime, __expoRuntimeContext)",
        text
    );
    Assert.DoesNotContain(
        "SharedObjectCodec<global::Expo.TestModules.CacheEntry>.Encode(__expoResult, runtime, GeneratedFunction.CurrentRuntimeContext)",
        text
    );

    // Generated paths stay free of reflection, dynamic dispatch, JSON, and boxed argument arrays.
    Assert.DoesNotContain("System.Reflection", text);
    Assert.DoesNotContain("dynamic ", text);
    Assert.DoesNotContain("Json", text);
    Assert.DoesNotContain("object?[]", text);
  }

  [Fact]
  public void GeneratorEmitsNativeCreatedOnlyClassWithoutConstructorExposure()
  {
    var result = GeneratorTestHost.Run("""
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoSharedObject("NativeSnapshot")]
        public sealed partial class SnapshotEntry : SharedObject
        {
          [JS]
          public double Stamp => 7;
        }

        [ExpoModule(Classes = new[] { typeof(SnapshotEntry) })]
        public sealed partial class SnapshotModule
        {
          [JS]
          public SnapshotEntry MakeSnapshot() => new();
        }
        """);

    Assert.DoesNotContain(
        result.Diagnostics,
        diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
    );
    var text = GeneratedText(result);

    // The class still installs its internal prototype under the explicit name, but no
    // constructor factory is exposed for a native-created-only class.
    Assert.Contains("GeneratedSharedObjectClass.Install(", text);
    Assert.Contains("\"NativeSnapshot\",", text);
    var installIndex = text.IndexOf("GeneratedSharedObjectClass.Install(", StringComparison.Ordinal);
    var installCall = text.Substring(installIndex, text.IndexOf(");", installIndex, StringComparison.Ordinal) - installIndex);
    Assert.Contains("null", installCall);
    Assert.DoesNotContain("ConstructSharedObject_", text);
  }

  private static string GeneratedText(GeneratorRunResult result) =>
      string.Join("\n", result.GeneratedSources.Select(source => source.Text));
}
