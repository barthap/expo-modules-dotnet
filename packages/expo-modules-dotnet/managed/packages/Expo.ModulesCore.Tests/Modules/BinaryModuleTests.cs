using Expo.JSI;
using Expo.ModulesCore;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Modules;

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
  public void GeneratedMemoryByteCodecsCopyInputsAndReturnedSlices()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      using var result = fixture.Evaluate(
          "const mutableInput = new ArrayBuffer(4); " +
          "new Uint8Array(mutableInput).set([1, 2, 3, 4]); " +
          "const readOnlyInput = new ArrayBuffer(4); " +
          "new Uint8Array(readOnlyInput).set([5, 6, 7, 8]); " +
          "const mutableOutput = globalThis._expoDotnet.modules.Binary.mutableSlice(mutableInput); " +
          "const mutableInputSecondAfterCall = new Uint8Array(mutableInput)[1]; " +
          "const readOnlyOutput = globalThis._expoDotnet.modules.Binary.readOnlySlice(readOnlyInput); " +
          "const combinedOutput = globalThis._expoDotnet.modules.Binary.combineMemory(mutableInput, readOnlyInput); " +
          "const asyncInput = new ArrayBuffer(3); " +
          "new Uint8Array(asyncInput).set([9, 10, 11]); " +
          "globalThis.memoryAsyncInput = asyncInput; " +
          "globalThis._expoDotnet.modules.Binary.readOnlySliceAsync(asyncInput).then(value => { " +
          "globalThis.memoryAsyncOutput = value; " +
          "}); " +
          "new Uint8Array(mutableInput).fill(0); " +
          "new Uint8Array(readOnlyInput).fill(0); " +
          "new Uint8Array(asyncInput).fill(0); " +
          "[mutableOutput instanceof ArrayBuffer, readOnlyOutput instanceof ArrayBuffer, combinedOutput instanceof ArrayBuffer, " +
          "mutableOutput === mutableInput, readOnlyOutput === readOnlyInput, combinedOutput === mutableInput, " +
          "mutableInputSecondAfterCall, mutableOutput.byteLength, new Uint8Array(mutableOutput)[0], " +
          "new Uint8Array(mutableOutput)[1], readOnlyOutput.byteLength, new Uint8Array(readOnlyOutput)[0], " +
          "new Uint8Array(readOnlyOutput)[1], combinedOutput.byteLength, new Uint8Array(combinedOutput)[0], " +
          "new Uint8Array(combinedOutput)[1], new Uint8Array(combinedOutput)[2], new Uint8Array(combinedOutput)[3]]",
          "binary-memory.js"
      );
      using var values = result.AsArray();
      using var mutableIsArrayBuffer = values.GetValue(0);
      using var readOnlyIsArrayBuffer = values.GetValue(1);
      using var combinedIsArrayBuffer = values.GetValue(2);
      using var mutableSame = values.GetValue(3);
      using var readOnlySame = values.GetValue(4);
      using var combinedSame = values.GetValue(5);
      using var mutableInputSecondAfterCall = values.GetValue(6);
      using var mutableLength = values.GetValue(7);
      using var mutableFirst = values.GetValue(8);
      using var mutableSecond = values.GetValue(9);
      using var readOnlyLength = values.GetValue(10);
      using var readOnlyFirst = values.GetValue(11);
      using var readOnlySecond = values.GetValue(12);
      using var combinedLength = values.GetValue(13);
      using var combinedFirst = values.GetValue(14);
      using var combinedSecond = values.GetValue(15);
      using var combinedThird = values.GetValue(16);
      using var combinedFourth = values.GetValue(17);
      Assert.True(mutableIsArrayBuffer.AsBool());
      Assert.True(readOnlyIsArrayBuffer.AsBool());
      Assert.True(combinedIsArrayBuffer.AsBool());
      Assert.False(mutableSame.AsBool());
      Assert.False(readOnlySame.AsBool());
      Assert.False(combinedSame.AsBool());
      Assert.Equal(2, mutableInputSecondAfterCall.AsDouble());
      Assert.Equal(2, mutableLength.AsDouble());
      Assert.Equal(42, mutableFirst.AsDouble());
      Assert.Equal(3, mutableSecond.AsDouble());
      Assert.Equal(2, readOnlyLength.AsDouble());
      Assert.Equal(6, readOnlyFirst.AsDouble());
      Assert.Equal(7, readOnlySecond.AsDouble());
      Assert.Equal(4, combinedLength.AsDouble());
      Assert.Equal(2, combinedFirst.AsDouble());
      Assert.Equal(3, combinedSecond.AsDouble());
      Assert.Equal(6, combinedThird.AsDouble());
      Assert.Equal(7, combinedFourth.AsDouble());
      return true;
    });

    fixture.WaitUntilIdle();
    fixture.Runtime.Execute(_ =>
    {
      using var result = fixture.Evaluate(
          "[globalThis.memoryAsyncOutput instanceof ArrayBuffer, " +
          "globalThis.memoryAsyncOutput === globalThis.memoryAsyncInput, globalThis.memoryAsyncOutput.byteLength, " +
          "new Uint8Array(globalThis.memoryAsyncOutput)[0], new Uint8Array(globalThis.memoryAsyncOutput)[1]]",
          "binary-memory-async.js"
      );
      using var values = result.AsArray();
      using var isArrayBuffer = values.GetValue(0);
      using var isSame = values.GetValue(1);
      using var length = values.GetValue(2);
      using var first = values.GetValue(3);
      using var second = values.GetValue(4);
      Assert.True(isArrayBuffer.AsBool());
      Assert.False(isSame.AsBool());
      Assert.Equal(2, length.AsDouble());
      Assert.Equal(10, first.AsDouble());
      Assert.Equal(11, second.AsDouble());
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
