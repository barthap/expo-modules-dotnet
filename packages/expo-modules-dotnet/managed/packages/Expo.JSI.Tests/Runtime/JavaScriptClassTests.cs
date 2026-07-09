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
  public void CreateClassCreatesConstructableFunctionWithPrototype()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var constructor = runtime.CreateClass("BridgeClass");
      using var global = runtime.Global();
      using var constructorValue = constructor.AsValue();
      global.SetProperty("__BridgeClass", constructorValue);

      using var result = fixture.Evaluate(
          "const instance = new globalThis.__BridgeClass();" +
          "instance instanceof globalThis.__BridgeClass && " +
          "Object.getPrototypeOf(instance) === globalThis.__BridgeClass.prototype",
          "create-class-check.js"
      );

      Assert.True(result.AsBool());
      return true;
    });
  }

  [Fact]
  public void CreateClassWithSuperclassLinksPrototypeChain()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var baseClass = runtime.CreateClass("BridgeBase");
      using var subclass = runtime.CreateClass("BridgeSubclass", baseClass);
      using var global = runtime.Global();
      using var baseValue = baseClass.AsValue();
      using var subclassValue = subclass.AsValue();
      global.SetProperty("__BridgeBase", baseValue);
      global.SetProperty("__BridgeSubclass", subclassValue);

      using var result = fixture.Evaluate(
          "const instance = new globalThis.__BridgeSubclass();" +
          "instance instanceof globalThis.__BridgeSubclass && " +
          "instance instanceof globalThis.__BridgeBase && " +
          "Object.getPrototypeOf(globalThis.__BridgeSubclass) === globalThis.__BridgeBase && " +
          "Object.getPrototypeOf(globalThis.__BridgeSubclass.prototype) === globalThis.__BridgeBase.prototype",
          "create-subclass-check.js"
      );

      Assert.True(result.AsBool());
      return true;
    });
  }

  [Fact]
  public void CreateClassWithSuperclassDoesNotRequireProtoAccessor()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      fixture.Evaluate(
          "delete Object.prototype.__proto__; true",
          "delete-proto-accessor.js"
      ).Dispose();

      using var baseClass = runtime.CreateClass("BridgeBaseWithoutProto");
      using var subclass = runtime.CreateClass("BridgeSubclassWithoutProto", baseClass);
      using var global = runtime.Global();
      using var baseValue = baseClass.AsValue();
      using var subclassValue = subclass.AsValue();
      global.SetProperty("__BridgeBaseWithoutProto", baseValue);
      global.SetProperty("__BridgeSubclassWithoutProto", subclassValue);

      using var result = fixture.Evaluate(
          "const instance = new globalThis.__BridgeSubclassWithoutProto();" +
          "instance instanceof globalThis.__BridgeSubclassWithoutProto && " +
          "instance instanceof globalThis.__BridgeBaseWithoutProto && " +
          "Object.getPrototypeOf(globalThis.__BridgeSubclassWithoutProto) === globalThis.__BridgeBaseWithoutProto && " +
          "Object.getPrototypeOf(globalThis.__BridgeSubclassWithoutProto.prototype) === globalThis.__BridgeBaseWithoutProto.prototype",
          "create-subclass-without-proto-accessor-check.js"
      );

      Assert.True(result.AsBool());
      return true;
    });
  }
}
