using System.Threading.Tasks;
using Expo.JSI;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Modules;

public sealed class NestedCollectionModuleTests
{
  [Theory]
  [InlineData("JSON.stringify(modules.GeneratedNestedCollections.grid([[1, 2], [], [3]]))", "[[1,2],[],[3]]")]
  [InlineData(
      "JSON.stringify(modules.GeneratedNestedCollections.groups({ a: ['x'], b: [] }))",
      "{\"a\":[\"x\"],\"b\":[]}")]
  [InlineData(
      "JSON.stringify(modules.GeneratedNestedCollections.holder({ values: [1, 2], labels: { k: 'v' } }))",
      "{\"values\":[1,2],\"labels\":{\"k\":\"v\"}}")]
  [InlineData("String(modules.GeneratedNestedCollections.countAll(() => [1, 2, 3]))", "6")]
  public void GeneratedModuleRoundTripsNonNullCollectionsInComposedPositions(
      string expression,
      string expected)
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          $"const modules = globalThis._expoDotnet.modules; {expression}",
          "nested-collections.js"
      );

      Assert.Equal(expected, result.AsString());
      return true;
    });
  }

  [Fact]
  public async Task EventWithListPayloadReachesJavaScriptListener()
  {
    using var fixture = HermesRuntimeFixture.Create();
    DotnetRuntimeContext? context = null;

    try
    {
      fixture.Runtime.Execute(runtime =>
      {
        context = new DotnetRuntimeContext(runtime);
        using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
        ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

        using var setup = fixture.Evaluate(
            """
            const events = globalThis._expoDotnet.modules.GeneratedNestedCollections;
            events.addListener('changed', value => { globalThis.__changed = JSON.stringify(value); });
            events.raise([4, 5]);
            true
            """,
            "nested-collections-event.js"
        );
        return true;
      });

      await Task.Yield();
      fixture.WaitUntilIdle();

      var seen = fixture.Runtime.Execute(_ =>
      {
        using var result = fixture.Evaluate("globalThis.__changed", "nested-collections-event-result.js");
        return result.AsString();
      });

      Assert.Equal("[4,5]", seen);
    }
    finally
    {
      fixture.Runtime.Execute(_ =>
      {
        context?.Dispose();
        return true;
      });
    }
  }
}
