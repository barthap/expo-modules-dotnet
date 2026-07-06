using Expo.JSI;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Generated;

public sealed class ModuleRegistryTests
{
  [Fact]
  public void DotnetModulesObjectIsCreatedWithoutCreatingExpoGlobal()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var modules = ModuleRegistry.GetOrCreateDotnetModulesObject(runtime);

      using var hasExpo = fixture.Evaluate(
          "typeof globalThis.expo",
          "dotnet-modules-no-expo.js"
      );
      using var hasDotnetModules = fixture.Evaluate(
          "typeof globalThis._expoDotnet === 'object' && typeof globalThis._expoDotnet.modules === 'object'",
          "dotnet-modules-created.js"
      );

      Assert.Equal(JavaScriptValueKind.String, hasExpo.Kind);
      Assert.Equal("undefined", hasExpo.AsString());
      Assert.Equal(JavaScriptValueKind.Bool, hasDotnetModules.Kind);
      Assert.True(hasDotnetModules.AsBool());
      return true;
    });
  }

  [Fact]
  public void DotnetModulesObjectReusesExistingObject()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      fixture.Evaluate(
          "globalThis._expoDotnet = { modules: { existing: 42 } }; true",
          "dotnet-modules-existing-setup.js"
      ).Dispose();

      using var modules = ModuleRegistry.GetOrCreateDotnetModulesObject(runtime);
      using var existing = fixture.Evaluate(
          "globalThis._expoDotnet.modules.existing",
          "dotnet-modules-existing-value.js"
      );

      Assert.Equal(JavaScriptValueKind.Number, existing.Kind);
      Assert.Equal(42.0, existing.AsDouble());
      return true;
    });
  }

  [Fact]
  public void DefineNativeModuleCreatesNativeModuleInstance()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      using var module = context.ModuleRegistry.DefineNativeModule(modules, "Events");

      using var result = fixture.Evaluate(
          "const module = globalThis._expoDotnet.modules.Events;" +
          "module instanceof globalThis._expoDotnet.NativeModule && " +
          "module instanceof globalThis._expoDotnet.EventEmitter && " +
          "typeof globalThis.expo === 'undefined' && " +
          "typeof module.addListener === 'function' && " +
          "typeof module.emit === 'function'",
          "native-module-registry.js"
      );

      Assert.True(result.AsBool());
      return true;
    });
  }

  [Fact]
  public void RuntimeContextInstallsNativeModuleAsEventEmitterSubclass()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);

      using var result = fixture.Evaluate(
          "const module = new globalThis._expoDotnet.NativeModule();" +
          "module instanceof globalThis._expoDotnet.NativeModule && " +
          "module instanceof globalThis._expoDotnet.EventEmitter && " +
          "typeof module.addListener === 'function' && " +
          "typeof module.removeListener === 'function' && " +
          "typeof module.removeAllListeners === 'function' && " +
          "typeof module.emit === 'function' && " +
          "typeof module.listenerCount === 'function'",
          "modules-core-base-classes.js"
      );

      Assert.True(result.AsBool());
      return true;
    });
  }

  [Fact]
  public void RuntimeContextReplacesIncompatibleExistingDotnetClasses()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      fixture.Evaluate(
          "globalThis._expoDotnet = { " +
          "EventEmitter: function EventEmitter() {}, " +
          "NativeModule: function NativeModule() {} " +
          "}; true",
          "incompatible-dotnet-classes-setup.js"
      ).Dispose();

      using var context = new DotnetRuntimeContext(runtime);

      using var result = fixture.Evaluate(
          "const module = new globalThis._expoDotnet.NativeModule();" +
          "module instanceof globalThis._expoDotnet.EventEmitter && " +
          "typeof module.addListener === 'function' && " +
          "typeof module.emit === 'function'",
          "incompatible-dotnet-classes-replaced.js"
      );

      Assert.True(result.AsBool());
      return true;
    });
  }

  [Fact]
  public void RuntimeContextDoesNotMutateExpoGlobal()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      fixture.Evaluate(
          "globalThis.expo = { modules: { upstream: true } }; true",
          "expo-global-setup.js"
      ).Dispose();

      using var context = new DotnetRuntimeContext(runtime);

      using var result = fixture.Evaluate(
          "globalThis.expo.modules.upstream === true && " +
          "typeof globalThis.expo.NativeModule === 'undefined' && " +
          "typeof globalThis.expo.EventEmitter === 'undefined' && " +
          "typeof globalThis._expoDotnet.NativeModule === 'function' && " +
          "typeof globalThis._expoDotnet.EventEmitter === 'function'",
          "expo-global-not-mutated.js"
      );

      Assert.True(result.AsBool());
      return true;
    });
  }

  [Fact]
  public void DefineNativeModuleReusesExistingObject()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      fixture.Evaluate(
          "globalThis._expoDotnet.modules.Events = { existing: 42 }; true",
          "native-module-existing-setup.js"
      ).Dispose();

      using var module = context.ModuleRegistry.DefineNativeModule(modules, "Events");
      using var result = fixture.Evaluate(
          "globalThis._expoDotnet.modules.Events.existing",
          "native-module-existing-value.js"
      );

      Assert.Equal(42.0, result.AsDouble());
      return true;
    });
  }

  [Fact]
  public void EventEmitterSupportsFrozenListeners()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      using var module = context.ModuleRegistry.DefineNativeModule(modules, "Events");

      using var result = fixture.Evaluate(
          "const events = globalThis._expoDotnet.modules.Events;" +
          "const listener = Object.freeze(function () {});" +
          "events.addListener('onChange', listener);" +
          "const before = events.listenerCount('onChange');" +
          "events.removeListener('onChange', listener);" +
          "before + ':' + events.listenerCount('onChange')",
          "frozen-listener.js"
      );

      Assert.Equal("1:0", result.AsString());
      return true;
    });
  }

  [Fact]
  public void EventEmitterContinuesAfterListenerThrows()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      using var module = context.ModuleRegistry.DefineNativeModule(modules, "Events");

      using var result = fixture.Evaluate(
          "const events = globalThis._expoDotnet.modules.Events;" +
          "let seen = '';" +
          "events.addListener('onChange', () => { throw new Error('boom'); });" +
          "events.addListener('onChange', () => { seen = 'second'; });" +
          "events.emit('onChange');" +
          "seen",
          "listener-throw-continues.js"
      );

      Assert.Equal("second", result.AsString());
      return true;
    });
  }

  [Fact]
  public void EventEmitterMethodsFailAfterRuntimeContextDisposal()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      using var module = context.ModuleRegistry.DefineNativeModule(modules, "Events");

      context.Dispose();

      using var result = fixture.Evaluate(
          "const events = globalThis._expoDotnet.modules.Events;" +
          "try { events.addListener('onChange', () => {}); 'no error'; } catch (error) { error.message; }",
          "event-emitter-after-dispose.js"
      );

      Assert.Contains("EventEmitterRuntimeState", result.AsString());
      return true;
    });
  }

  [Fact]
  public void RuntimeContextRefreshesDisposedEventEmitterClasses()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using (var firstContext = new DotnetRuntimeContext(runtime))
      {
        using var firstModules = firstContext.ModuleRegistry.GetOrCreateDotnetModulesObject();
        using var firstModule = firstContext.ModuleRegistry.DefineNativeModule(firstModules, "FirstEvents");
      }

      using var secondContext = new DotnetRuntimeContext(runtime);
      using var secondModules = secondContext.ModuleRegistry.GetOrCreateDotnetModulesObject();
      using var secondModule = secondContext.ModuleRegistry.DefineNativeModule(secondModules, "SecondEvents");

      using var result = fixture.Evaluate(
          "const events = globalThis._expoDotnet.modules.SecondEvents;" +
          "events.addListener('onChange', () => {});" +
          "events.listenerCount('onChange')",
          "fresh-context-event-emitter.js"
      );

      Assert.Equal(1.0, result.AsDouble());
      return true;
    });
  }
}
