using System;
using System.Collections.Generic;
using Expo.JSI;
using Expo.ModulesCore.Codecs;
using Expo.ModulesCore.Tests.Fixtures;
using Expo.ModulesCore.Tests.Modules;
using Xunit;

namespace Expo.ModulesCore.Tests.Codecs;

public sealed class NullableSharedObjectCodecTests
{
  private const string NotAnObjectMessage = "Value is not an object.";

  [Theory]
  [InlineData("null")]
  [InlineData("undefined")]
  public void DecodeReturnsNullForNullishInputWithoutTouchingTheRegistry(string expression)
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var value = fixture.Evaluate(expression, "nullable-shared-object-decode-nullish.js");
      var steps = new List<SharedObjectPairingStep>();
      context.SharedObjects.PairingStepForTesting = steps.Add;

      Assert.Null(NullableSharedObjectCodec<SnapshotEntry>.Decode(value.Ref, runtime, context));
      Assert.Null(NullableSharedObjectCodec<SnapshotEntry>.Decode(value, runtime, context));

      // Control: the exact codec reaches AsObject on the same input and throws, so the nullable
      // codec must have returned before it.
      var exact = Assert.Throws<InvalidOperationException>(
          () => SharedObjectCodec<SnapshotEntry>.Decode(value.Ref, runtime, context));
      Assert.Contains(NotAnObjectMessage, exact.Message);

      Assert.Empty(steps);
      Assert.Equal(0, context.SharedObjects.Count);

      // Control: the hook does record steps once a class is installed and a non-null value is
      // encoded, so the empty list above is meaningful.
      Install<SnapshotEntry>(runtime, context, "NativeSnapshot");
      using var paired = NullableSharedObjectCodec<SnapshotEntry>.Encode(new SnapshotEntry(), runtime, context);
      Assert.NotEmpty(steps);
      return true;
    });
  }

  [Fact]
  public void EncodeNullReturnsJavaScriptNullWithoutClassRegistration()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      var steps = new List<SharedObjectPairingStep>();
      context.SharedObjects.PairingStepForTesting = steps.Add;

      using var encoded = NullableSharedObjectCodec<SnapshotEntry>.Encode(null, runtime, context);
      Assert.Equal(JavaScriptValueKind.Null, encoded.Kind);
      Assert.Empty(steps);
      Assert.Equal(0, context.SharedObjects.Count);

      // Control: no class is installed, so the exact codec fails at the registration lookup.
      // That proves the null branch returned before GetRegistration.
      var exact = Assert.Throws<InvalidOperationException>(
          () => SharedObjectCodec<SnapshotEntry>.Encode(new SnapshotEntry(), runtime, context));
      Assert.Contains("is not installed for this runtime context", exact.Message);

      // Control: the hook does record steps once a class is installed and a non-null value is
      // encoded, so the empty list above is meaningful.
      Install<SnapshotEntry>(runtime, context, "NativeSnapshot");
      using var paired = NullableSharedObjectCodec<SnapshotEntry>.Encode(new SnapshotEntry(), runtime, context);
      Assert.NotEmpty(steps);
      return true;
    });
  }

  [Theory]
  [InlineData("0")]
  [InlineData("false")]
  [InlineData("''")]
  [InlineData("5")]
  [InlineData("'x'")]
  public void DecodeRejectsNonNullishPrimitivesLikeTheExactCodec(string expression)
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      Install<SnapshotEntry>(runtime, context, "NativeSnapshot");
      using var value = fixture.Evaluate(expression, "nullable-shared-object-decode-primitive.js");
      var steps = new List<SharedObjectPairingStep>();
      context.SharedObjects.PairingStepForTesting = steps.Add;

      // Only null and undefined map to C# null. Falsy primitives such as 0, false and '' must
      // still fail like the exact codec.
      var nullableRef = Assert.Throws<InvalidOperationException>(
          () => NullableSharedObjectCodec<SnapshotEntry>.Decode(value.Ref, runtime, context));
      var nullableOwned = Assert.Throws<InvalidOperationException>(
          () => NullableSharedObjectCodec<SnapshotEntry>.Decode(value, runtime, context));
      var exact = Assert.Throws<InvalidOperationException>(
          () => SharedObjectCodec<SnapshotEntry>.Decode(value.Ref, runtime, context));

      Assert.Contains(NotAnObjectMessage, nullableRef.Message);
      Assert.Contains(NotAnObjectMessage, nullableOwned.Message);
      Assert.Equal(exact.Message, nullableRef.Message);
      Assert.Empty(steps);
      return true;
    });
  }

  [Fact]
  public void NonNullValueRoundTripsThroughTheExactCodec()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      Install<SnapshotEntry>(runtime, context, "NativeSnapshot");
      var instance = new SnapshotEntry();
      var steps = new List<SharedObjectPairingStep>();
      context.SharedObjects.PairingStepForTesting = steps.Add;

      using var first = NullableSharedObjectCodec<SnapshotEntry>.Encode(instance, runtime, context);
      Assert.Equal(
          new[]
          {
            SharedObjectPairingStep.InstanceObjectCreated,
            SharedObjectPairingStep.NativeStateAttached,
            SharedObjectPairingStep.WeakObjectCreated,
          },
          steps);

      steps.Clear();
      using var second = NullableSharedObjectCodec<SnapshotEntry>.Encode(instance, runtime, context);
      Assert.Equal(new[] { SharedObjectPairingStep.ExistingWeakObjectLocked }, steps);
      Assert.True(runtime.StrictEquals(first, second));

      Assert.Same(instance, NullableSharedObjectCodec<SnapshotEntry>.Decode(first.Ref, runtime, context));
      Assert.Same(instance, NullableSharedObjectCodec<SnapshotEntry>.Decode(second, runtime, context));
      Assert.Same(instance, SharedObjectCodec<SnapshotEntry>.Decode(first.Ref, runtime, context));
      Assert.Equal(1, context.SharedObjects.Count);
      return true;
    });
  }

  [Fact]
  public void NonNullFailuresMatchTheExactCodec()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      Install<SnapshotEntry>(runtime, context, "NativeSnapshot");
      Install<SiblingEntry>(runtime, context, "Sibling");
      var contextB = new DotnetRuntimeContext(runtime);
      try
      {
        Install<SnapshotEntry>(runtime, contextB, "NativeSnapshot");

        using var plain = fixture.Evaluate("({})", "nullable-shared-object-plain.js");
        using var otherClass = SharedObjectCodec<SiblingEntry>.Encode(new SiblingEntry(), runtime, context);
        var releasedInstance = new SnapshotEntry();
        using var released = SharedObjectCodec<SnapshotEntry>.Encode(releasedInstance, runtime, context);
        using (var releasedObject = released.AsObject())
        {
          context.SharedObjects.ReleaseFromJavaScript(releasedObject);
        }
        using var otherContext = SharedObjectCodec<SnapshotEntry>.Encode(new SnapshotEntry(), runtime, contextB);

        AssertDecodeFailuresMatch(
            plain,
            "NativeState entry for SharedObjectNativeState is missing.",
            runtime,
            context);
        AssertDecodeFailuresMatch(
            otherClass,
            "The shared object receiver is not a 'Expo.ModulesCore.Tests.Modules.SnapshotEntry' instance.",
            runtime,
            context);
        AssertDecodeFailuresMatch(
            released,
            "The JavaScript object is not an active shared object.",
            runtime,
            context);
        AssertDecodeFailuresMatch(
            otherContext,
            "The JavaScript object is not an active shared object.",
            runtime,
            context);

        var nullableEncode = Assert.Throws<InvalidOperationException>(
            () => NullableSharedObjectCodec<SnapshotEntry>.Encode(releasedInstance, runtime, context));
        var exactEncode = Assert.Throws<InvalidOperationException>(
            () => SharedObjectCodec<SnapshotEntry>.Encode(releasedInstance, runtime, context));
        Assert.Equal(exactEncode.Message, nullableEncode.Message);
        Assert.Contains("already been released", nullableEncode.Message);
      }
      finally
      {
        contextB.Dispose();
      }
      return true;
    });
  }

  private static void AssertDecodeFailuresMatch(
      JavaScriptValue value,
      string expected,
      JavaScriptRuntime runtime,
      DotnetRuntimeContext context)
  {
    var nullable = Assert.Throws<InvalidOperationException>(
        () => NullableSharedObjectCodec<SnapshotEntry>.Decode(value.Ref, runtime, context));
    var exact = Assert.Throws<InvalidOperationException>(
        () => SharedObjectCodec<SnapshotEntry>.Decode(value.Ref, runtime, context));
    Assert.Equal(exact.Message, nullable.Message);
    Assert.Contains(expected, nullable.Message);

    var nullableOwned = Assert.Throws<InvalidOperationException>(
        () => NullableSharedObjectCodec<SnapshotEntry>.Decode(value, runtime, context));
    var exactOwned = Assert.Throws<InvalidOperationException>(
        () => SharedObjectCodec<SnapshotEntry>.Decode(value, runtime, context));
    Assert.Equal(exactOwned.Message, nullableOwned.Message);
    Assert.Contains(expected, nullableOwned.Message);
  }

  private static void Install<T>(JavaScriptRuntime runtime, DotnetRuntimeContext context, string name)
      where T : SharedObject
  {
    using var holder = runtime.CreateObject();
    GeneratedSharedObjectClass.Install(context, holder, typeof(T), name, 0, null, null);
  }
}
