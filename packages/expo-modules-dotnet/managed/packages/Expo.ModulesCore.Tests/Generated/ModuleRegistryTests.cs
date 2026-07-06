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
}
