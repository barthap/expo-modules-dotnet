using Expo.JSI;
using Expo.ModulesCore.Codecs;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Generated;

public sealed class RuntimeSessionTests
{
  [Fact]
  public void SessionBackedFunctionFailsAfterTeardown()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var session = new RuntimeSession(runtime);
      using var modules = session.GetOrCreateDotnetModulesObject();
      RegisterLifecycleModule(session, modules, out _);

      using var beforeTeardown = fixture.Evaluate(
          "globalThis._expoDotnet.modules.Lifecycle.value()",
          "runtime-session-before-teardown.js"
      );
      Assert.Equal(42.0, beforeTeardown.AsDouble());

      session.Dispose();

      using var afterTeardown = fixture.Evaluate(
          "try { globalThis._expoDotnet.modules.Lifecycle.value(); 'no error'; } catch (e) { e.message; }",
          "runtime-session-after-teardown.js"
      );
      Assert.Equal(JavaScriptValueKind.String, afterTeardown.Kind);
      Assert.Contains("RuntimeSession", afterTeardown.AsString());

      return true;
    });
  }

  [Fact]
  public void SessionTeardownReleasesModuleInstances()
  {
    using var fixture = HermesRuntimeFixture.Create();
    WeakReference moduleReference = null!;

    fixture.Runtime.Execute(runtime =>
    {
      using var session = new RuntimeSession(runtime);
      using var modules = session.GetOrCreateDotnetModulesObject();
      RegisterLifecycleModule(session, modules, out moduleReference);

      Assert.True(moduleReference.IsAlive);
      session.Dispose();
      return true;
    });

    CollectGarbage();
    Assert.False(moduleReference.IsAlive);
  }

  private static void RegisterLifecycleModule(
      RuntimeSession session,
      JavaScriptObject modules,
      out WeakReference moduleReference
  )
  {
    using var module = ModuleRegistry.DefineModule(session.Runtime, modules, "Lifecycle");
    var lifecycleModule = new LifecycleModule();
    moduleReference = new WeakReference(lifecycleModule);
    GeneratedFunction.DefineSync(
        session,
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
