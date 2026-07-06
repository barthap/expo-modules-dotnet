using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

public sealed class JavaScriptClassTests
{
  [Fact]
  public void CreateObjectWithPrototypeUsesPrototypeMethods()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var prototype = runtime.CreateObject();
      using var marker = runtime.CreateString("from prototype");
      prototype.SetProperty("marker", marker);

      using var created = runtime.CreateObjectWithPrototype(prototype);
      using var result = created.GetProperty("marker");

      Assert.Equal("from prototype", result.AsString());
      using var global = runtime.Global();
      using var prototypeValue = prototype.AsValue();
      using var createdValue = created.AsValue();
      global.SetProperty("__expoPrototype", prototypeValue);
      global.SetProperty("__expoCreated", createdValue);
      using var prototypeCheck = fixture.Evaluate(
          "Object.getPrototypeOf(globalThis.__expoCreated) === globalThis.__expoPrototype && " +
          "!Object.prototype.hasOwnProperty.call(globalThis.__expoCreated, 'marker')",
          "object-with-prototype-check.js"
      );
      Assert.True(prototypeCheck.AsBool());
      return true;
    });
  }

  [Fact]
  public void EnsureExpoBaseClassesInstallsNativeModuleAsEventEmitterSubclass()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      runtime.EnsureExpoBaseClasses();

      using var result = fixture.Evaluate(
          "const module = new globalThis._expoDotnet.NativeModule();" +
          "module instanceof globalThis._expoDotnet.NativeModule && " +
          "module instanceof globalThis._expoDotnet.EventEmitter && " +
          "typeof module.addListener === 'function' && " +
          "typeof module.removeListener === 'function' && " +
          "typeof module.removeAllListeners === 'function' && " +
          "typeof module.emit === 'function' && " +
          "typeof module.listenerCount === 'function'",
          "expo-base-classes.js"
      );

      Assert.True(result.AsBool());
      return true;
    });
  }

  [Fact]
  public void EnsureExpoBaseClassesReplacesIncompatibleExistingDotnetClasses()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      fixture.Evaluate(
          "globalThis._expoDotnet = { " +
          "EventEmitter: function EventEmitter() {}, " +
          "NativeModule: function NativeModule() {} " +
          "}; true",
          "incompatible-expo-dotnet-classes-setup.js"
      ).Dispose();

      runtime.EnsureExpoBaseClasses();

      using var result = fixture.Evaluate(
          "const module = new globalThis._expoDotnet.NativeModule();" +
          "module instanceof globalThis._expoDotnet.EventEmitter && " +
          "typeof module.addListener === 'function' && " +
          "typeof module.emit === 'function'",
          "incompatible-expo-dotnet-classes-replaced.js"
      );

      Assert.True(result.AsBool());
      return true;
    });
  }

  [Fact]
  public void EnsureExpoBaseClassesDoesNotMutateExpoGlobal()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      fixture.Evaluate(
          "globalThis.expo = { modules: { upstream: true } }; true",
          "expo-global-setup.js"
      ).Dispose();

      runtime.EnsureExpoBaseClasses();

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
}
