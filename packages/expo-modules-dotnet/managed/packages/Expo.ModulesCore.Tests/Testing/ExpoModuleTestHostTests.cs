using Expo.ModulesCore.Testing;
using Xunit;

namespace Expo.ModulesCore.Tests.Testing;

public sealed partial class ExpoModuleTestHostTests
{
  [Fact]
  public void ExplicitProviderRegistersUnderDotnetModules()
  {
    using var host = ExpoModuleTestHost.Create((context, modules) =>
    {
      using var module = context.ModuleRegistry.DefineModule(modules, "HostTest");
      using var answer = context.Runtime.CreateNumber(42);
      module.SetProperty("answer", answer);
    });

    host.Runtime.Execute(_ =>
    {
      using var result = host.Evaluate(
          "globalThis._expoDotnet.modules.HostTest.answer",
          "module-host-registration.js"
      );
      Assert.Equal(42, result.AsDouble());
      return true;
    });
  }

  [Fact]
  public void RegistrationFailureDisposesCreatedContext()
  {
    DotnetRuntimeContext? captured = null;

    Assert.Throws<InvalidOperationException>(() =>
        ExpoModuleTestHost.Create((context, _) =>
        {
          captured = context;
          throw new InvalidOperationException("registration failed");
        })
    );

    Assert.NotNull(captured);
    Assert.Throws<ObjectDisposedException>(() => _ = captured!.ModuleRegistry);
  }

  [Fact]
  public void DisposeRunsModuleTeardownBeforeReleasingRuntime()
  {
    var callbacks = new List<string>();
    var host = ExpoModuleTestHost.Create((context, _) =>
    {
      context.ModuleRegistry.GetOrCreateModule(
          "Lifecycle",
          () => new LifecycleProbe(callbacks, context.Runtime),
          onCreate: null,
          onDestroy: probe => probe.OnDestroy()
      );
    });

    host.Dispose();
    host.Dispose();

    Assert.Equal(["destroy:runtime-live", "dispose"], callbacks);
  }

  [Fact]
  public void DisposeReleasesRuntimeWhenModuleTeardownThrows()
  {
    var host = ExpoModuleTestHost.Create((context, _) =>
    {
      context.ModuleRegistry.GetOrCreateModule(
          "ThrowingLifecycle",
          static () => new object(),
          onCreate: null,
          onDestroy: static _ =>
              throw new InvalidOperationException("destroy failed")
      );
    });
    var testRuntime = host.TestRuntime;

    var exception = Assert.Throws<AggregateException>(host.Dispose);

    Assert.Contains(
        exception.InnerExceptions,
        error => error.Message == "destroy failed"
    );
    Assert.Throws<ObjectDisposedException>(
        () => testRuntime.Evaluate("true").Dispose()
    );
  }

  private sealed class LifecycleProbe(
      List<string> callbacks,
      Expo.JSI.JavaScriptRuntime runtime
  ) : IDisposable
  {
    public void OnDestroy()
    {
      using var value = runtime.CreateString("runtime-live");
      callbacks.Add($"destroy:{value.AsString()}");
    }

    public void Dispose() => callbacks.Add("dispose");
  }
}
