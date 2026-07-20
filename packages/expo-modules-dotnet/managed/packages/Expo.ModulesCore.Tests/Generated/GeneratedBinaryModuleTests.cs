using Expo.JSI;
using Expo.ModulesCore;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Generated;

public sealed class GeneratedBinaryModuleTests
{
  [Fact]
  public void GeneratedBinaryModuleRoundTripsNativeBuffersAndCopiesByteArrays()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      using var result = fixture.Evaluate(
          "const b = globalThis._expoDotnet.modules.Binary.allocate(3); " +
          "new Uint8Array(b).set([1, 2, 3]); " +
          "const copied = globalThis._expoDotnet.modules.Binary.echoBytes(b); " +
          "new Uint8Array(b)[0] = 9; " +
          "[b.byteLength, new Uint8Array(globalThis._expoDotnet.modules.Binary.echo(b))[1], " +
          "new Uint8Array(copied)[0]]",
          "binary-round-trip.js"
      );
      using var values = result.AsArray();
      using var length = values.GetValue(0);
      using var middle = values.GetValue(1);
      using var copiedFirst = values.GetValue(2);
      Assert.Equal(3, length.AsDouble());
      Assert.Equal(2, middle.AsDouble());
      Assert.Equal(1, copiedFirst.AsDouble());
      return true;
    });
  }

  [Fact]
  public void GeneratedSpanMethodsBorrowInputsAndCopyReturns()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      using var result = fixture.Evaluate(
          "const b = new ArrayBuffer(3); " +
          "new Uint8Array(b).set([1, 2, 3]); " +
          "globalThis._expoDotnet.modules.Binary.fill(b); " +
          "const transformed = globalThis._expoDotnet.modules.Binary.transform(b); " +
          "const first = globalThis._expoDotnet.modules.Binary.returnView(); " +
          "const second = globalThis._expoDotnet.modules.Binary.returnView(); " +
          "[globalThis._expoDotnet.modules.Binary.sum(b), first === second, " +
          "new Uint8Array(first)[0], new Uint8Array(second)[0], " +
          "new Uint8Array(transformed)[0]]",
          "binary-span.js"
      );
      using var values = result.AsArray();
      using var sum = values.GetValue(0);
      using var isSameObject = values.GetValue(1);
      using var firstByte = values.GetValue(2);
      using var secondByte = values.GetValue(3);
      using var transformedFirstByte = values.GetValue(4);
      Assert.Equal(27, sum.AsDouble());
      Assert.False(isSameObject.AsBool());
      Assert.Equal(4, firstByte.AsDouble());
      Assert.Equal(4, secondByte.AsDouble());
      Assert.Equal(9, transformedFirstByte.AsDouble());
      return true;
    });
  }

  [Fact]
  public void GeneratedAsyncArrayBufferPreservesOwnedResultIdentity()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      using var setup = fixture.Evaluate(
          "const input = new ArrayBuffer(2); " +
          "new Uint8Array(input).set([8, 9]); " +
          "globalThis.asyncInput = input; " +
          "globalThis.asyncSame = false; " +
          "globalThis.asyncFirst = -1; " +
          "globalThis._expoDotnet.modules.Binary.echoAsync(input).then(value => { " +
          "globalThis.asyncSame = value === globalThis.asyncInput; " +
          "globalThis.asyncFirst = new Uint8Array(value)[0]; " +
          "});",
          "binary-async.js"
      );
      return true;
    });

    fixture.WaitUntilIdle();
    fixture.Runtime.Execute(_ =>
    {
      using var same = fixture.Evaluate("globalThis.asyncSame", "binary-async-same.js");
      using var first = fixture.Evaluate("globalThis.asyncFirst", "binary-async-first.js");
      Assert.True(same.AsBool());
      Assert.Equal(8, first.AsDouble());
      return true;
    });
  }
}
