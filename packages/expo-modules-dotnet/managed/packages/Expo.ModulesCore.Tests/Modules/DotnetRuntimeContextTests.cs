using Expo.JSI;
using Expo.ModulesCore.Codecs;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Modules;

public sealed class DotnetRuntimeContextTests
{
  [Fact]
  public void ContextBackedFunctionFailsAfterTeardown()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
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
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      RegisterLifecycleModule(context, modules, out moduleReference);

      Assert.True(moduleReference.IsAlive);
      context.Dispose();
      return true;
    });

    CollectGarbage();
    Assert.False(moduleReference.IsAlive);
  }

  [Fact]
  public void ContextOwnedModuleRegistryReusesModuleInstance()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      var createCount = 0;

      var first = context.ModuleRegistry.GetOrCreateModule(
          "Reusable",
          () =>
          {
            createCount++;
            return new LifecycleModule();
          }
      );
      var second = context.ModuleRegistry.GetOrCreateModule(
          "Reusable",
          () =>
          {
            createCount++;
            return new LifecycleModule();
          }
      );

      Assert.Same(first, second);
      Assert.Equal(1, createCount);
      return true;
    });
  }

  [Fact]
  public void ModuleRegistryRunsCreateHookOnceForNewInstance()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      var createCount = 0;

      var first = context.ModuleRegistry.GetOrCreateModule(
          "Lifecycle",
          static () => new LifecycleModule(),
          _ => createCount++,
          null
      );
      var second = context.ModuleRegistry.GetOrCreateModule(
          "Lifecycle",
          static () => new LifecycleModule(),
          _ => createCount++,
          null
      );

      Assert.Same(first, second);
      Assert.Equal(1, createCount);
      return true;
    });
  }

  [Fact]
  public void ModuleRegistryRunsLifecycleHooksPerRuntimeContext()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var firstContext = new DotnetRuntimeContext(runtime);
      using var secondContext = new DotnetRuntimeContext(runtime);
      var createCount = 0;

      firstContext.ModuleRegistry.GetOrCreateModule(
          "Lifecycle",
          static () => new LifecycleModule(),
          _ => createCount++,
          null
      );
      secondContext.ModuleRegistry.GetOrCreateModule(
          "Lifecycle",
          static () => new LifecycleModule(),
          _ => createCount++,
          null
      );

      Assert.Equal(2, createCount);
      return true;
    });
  }

  [Fact]
  public void ModuleRegistryRunsDestroyHookBeforeDisposableCleanup()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      var calls = new List<string>();
      var context = new DotnetRuntimeContext(runtime);
      context.ModuleRegistry.GetOrCreateModule(
          "Lifecycle",
          () => new DisposableLifecycleModule(calls),
          null,
          module => module.Destroy()
      );

      context.Dispose();

      Assert.Equal(new[] { "destroy", "dispose" }, calls);
      return true;
    });
  }

  [Fact]
  public void ModuleRegistryAggregatesCleanupFailuresAfterRunningAllCleanup()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      var calls = new List<string>();
      var context = new DotnetRuntimeContext(runtime);
      context.ModuleRegistry.GetOrCreateModule(
          "First",
          () => new FailingLifecycleModule(calls, "first"),
          null,
          module => module.Destroy()
      );
      context.ModuleRegistry.GetOrCreateModule(
          "Second",
          () => new FailingLifecycleModule(calls, "second"),
          null,
          module => module.Destroy()
      );

      var exception = Assert.Throws<AggregateException>(() => context.Dispose());

      Assert.Equal(
          new[]
          {
              "first:destroy",
              "first:dispose",
              "second:destroy",
              "second:dispose",
          },
          calls
      );
      Assert.Equal(4, exception.InnerExceptions.Count);
      Assert.Contains(exception.InnerExceptions, item => item.Message == "first destroy failed");
      Assert.Contains(exception.InnerExceptions, item => item.Message == "first dispose failed");
      Assert.Contains(exception.InnerExceptions, item => item.Message == "second destroy failed");
      Assert.Contains(exception.InnerExceptions, item => item.Message == "second dispose failed");
      return true;
    });
  }

  [Fact]
  public void ContextDisposeReleasesEventStateWhenModuleCleanupFails()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      using var eventsModule = context.ModuleRegistry.DefineNativeModule(modules, "Events");
      context.ModuleRegistry.GetOrCreateModule(
          "Failing",
          () => new FailingLifecycleModule([], "failing"),
          null,
          module => module.Destroy()
      );

      Assert.Throws<AggregateException>(() => context.Dispose());

      using var result = fixture.Evaluate(
          "const events = globalThis._expoDotnet.modules.Events;" +
          "try { events.addListener('onChange', () => {}); 'no error'; } catch (error) { error.message; }",
          "event-emitter-after-failed-module-cleanup.js"
      );

      Assert.Contains("EventEmitterRuntimeState", result.AsString());
      return true;
    });
  }

  [Fact]
  public void ContextOwnedModuleRegistryDoesNotShareInstancesAcrossContexts()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var firstContext = new DotnetRuntimeContext(runtime);
      using var secondContext = new DotnetRuntimeContext(runtime);

      var first = firstContext.ModuleRegistry.GetOrCreateModule(
          "Scoped",
          static () => new LifecycleModule()
      );
      var second = secondContext.ModuleRegistry.GetOrCreateModule(
          "Scoped",
          static () => new LifecycleModule()
      );

      Assert.NotSame(first, second);
      return true;
    });
  }

  [Fact]
  public void ModuleBaseStoresRuntimeContext()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      var module = context.ModuleRegistry.GetOrCreateModule(
          "RuntimeAware",
          () => new RuntimeAwareModule(context)
      );

      Assert.Same(context, module.Context);
      return true;
    });
  }

  [Fact]
  public void CachedModuleRegistryFailsAfterContextTeardown()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      var registry = context.ModuleRegistry;

      context.Dispose();

      Assert.Throws<ObjectDisposedException>(() => registry.GetOrCreateDotnetModulesObject());
      return true;
    });
  }

  private static void RegisterLifecycleModule(
      DotnetRuntimeContext context,
      JavaScriptObject modules,
      out WeakReference moduleReference
  )
  {
    using var module = context.ModuleRegistry.DefineModule(modules, "Lifecycle");
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

  private sealed class DisposableLifecycleModule(List<string> calls) : IDisposable
  {
    public void Destroy()
    {
      calls.Add("destroy");
    }

    public void Dispose()
    {
      calls.Add("dispose");
    }
  }

  private sealed class FailingLifecycleModule(List<string> calls, string name) : IDisposable
  {
    public void Destroy()
    {
      calls.Add($"{name}:destroy");
      throw new InvalidOperationException($"{name} destroy failed");
    }

    public void Dispose()
    {
      calls.Add($"{name}:dispose");
      throw new InvalidOperationException($"{name} dispose failed");
    }
  }

  private sealed class RuntimeAwareModule : Module
  {
    public RuntimeAwareModule(DotnetRuntimeContext context)
        : base(context)
    {
    }

    public DotnetRuntimeContext Context => RuntimeContext;
  }
}
