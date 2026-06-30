using Expo.JSI;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Generated;

public sealed class GeneratedAttributeModuleTests
{
  [Fact]
  public void GeneratedProviderDispatchesExplicitNamedSyncFunction()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(runtime);

      using var result = fixture.Evaluate(
          "globalThis.expo.modules.GeneratedMath.add(20.25, 22.25)",
          "generated-attribute-math-add.js"
      );

      Assert.Equal(JavaScriptValueKind.Number, result.Kind);
      Assert.Equal(42.5, result.AsDouble());
      return true;
    });
  }

  [Fact]
  public void GeneratedProviderDispatchesDefaultNamedSyncFunction()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(runtime);

      using var result = fixture.Evaluate(
          "globalThis.expo.modules.GeneratedMath.AddOneWhen(41.5, true)",
          "generated-attribute-math-default-name.js"
      );

      Assert.Equal(JavaScriptValueKind.Number, result.Kind);
      Assert.Equal(42.5, result.AsDouble());
      return true;
    });
  }

  [Fact]
  public void GeneratedProviderPreservesStrings()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(runtime);

      using var result = fixture.Evaluate(
          "globalThis.expo.modules.GeneratedText.greet('Zoë\\u0000JS')",
          "generated-attribute-text-greet.js"
      );

      Assert.Equal(JavaScriptValueKind.String, result.Kind);
      Assert.Equal("Hello, Zoë\0JS", result.AsString());
      return true;
    });
  }

  [Fact]
  public void GeneratedProviderSupportsReadOnlyListConversions()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(runtime);

      using var result = fixture.Evaluate(
          "const labels = globalThis.expo.modules.GeneratedArray.labels(); " +
          "globalThis.expo.modules.GeneratedArray.sum([1, 2, 3.5]) + ':' + labels.join(',')",
          "generated-attribute-array.js"
      );

      Assert.Equal(JavaScriptValueKind.String, result.Kind);
      Assert.Equal("6.5:one,two", result.AsString());
      return true;
    });
  }
}
