using Expo.JSI;
using Expo.ModulesCore.Codecs;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Generated;

public sealed class DotnetRuntimeContextTests
{
  [Fact]
  public void ContextBackedFunctionFailsAfterTeardown()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.GetOrCreateDotnetModulesObject();
      RegisterLifecycleModule(context, modules, out _);

      using var beforeTeardown = fixture.Evaluate(
          "globalThis._expoDotnet.modules.Lifecycle.value()",
          "runtime-context-before-teardown.js"
      );
      Assert.Equal(42.0, beforeTeardown.AsDouble());

      context.Dispose();

      using var afterTeardown = fixture.Evaluate(
          "try { globalThis._expoDotnet.modules.Lifecycle.value(); 'no error'; } catch (e) { e.message; }",
          "runtime-context-after-teardown.js"
      );
      Assert.Equal(JavaScriptValueKind.String, afterTeardown.Kind);
      Assert.Contains("DotnetRuntimeContext", afterTeardown.AsString());

      return true;
    });
  }

  [Fact]
  public void ContextTeardownReleasesModuleInstances()
  {
    using var fixture = HermesRuntimeFixture.Create();
    WeakReference moduleReference = null!;

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.GetOrCreateDotnetModulesObject();
      RegisterLifecycleModule(context, modules, out moduleReference);

      Assert.True(moduleReference.IsAlive);
      context.Dispose();
      return true;
    });

    CollectGarbage();
    Assert.False(moduleReference.IsAlive);
  }

  private static void RegisterLifecycleModule(
      DotnetRuntimeContext context,
      JavaScriptObject modules,
      out WeakReference moduleReference
  )
  {
    using var module = ModuleRegistry.DefineModule(context.Runtime, modules, "Lifecycle");
    var lifecycleModule = new LifecycleModule();
    moduleReference = new WeakReference(lifecycleModule);
    GeneratedFunction.DefineSync(
        context,
        module,
        "value",
        0,
        LifecycleValueHostFunction,
        lifecycleModule
    );
  }

  private static JavaScriptValue LifecycleValueHostFunction(
      JavaScriptRuntime runtime,
      JavaScriptValueRef thisValue,
      JavaScriptArguments arguments,
      object context
  )
  {
    GeneratedFunction.RequireArgumentCount("Lifecycle.value", arguments, 0);
    var module = (LifecycleModule)context;
    return DoubleCodec.Encode(module.Value(), runtime);
  }

  private static void CollectGarbage()
  {
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
  }

  private sealed class LifecycleModule
  {
    public double Value() => 42.0;
  }
}
