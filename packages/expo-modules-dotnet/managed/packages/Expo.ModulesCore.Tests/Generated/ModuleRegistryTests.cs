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
}
