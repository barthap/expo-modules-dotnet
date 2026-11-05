using System;
using System.Collections.Generic;
using Expo.JSI;
using Expo.ModulesCore.Codecs;
using Expo.ModulesCore.Tests.Fixtures;
using Expo.ModulesCore.Tests.Modules;
using Xunit;

namespace Expo.ModulesCore.Tests.Codecs;

public sealed class SharedObjectBaseCodecTests
{
  private const string NotAnObjectMessage = "Value is not an object.";
  private const string NotAPathBaseMessage = "The shared object receiver is not a 'Expo.ModulesCore.Tests.Modules.PathBase' instance.";

  private static readonly SharedObjectPairingStep[] PairingSteps =
  {
    SharedObjectPairingStep.InstanceObjectCreated,
    SharedObjectPairingStep.NativeStateAttached,
    SharedObjectPairingStep.WeakObjectCreated,
  };

  [Fact]
  public void DecodeReturnsTheOriginalInstanceOfEverySubclass()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      Install<FileEntry>(runtime, context, "FileEntry");
      Install<DirEntry>(runtime, context, "DirEntry");
      var file = new FileEntry("f");
      var dir = new DirEntry("d", file);
      using var fileValue = SharedObjectCodec<FileEntry>.Encode(file, runtime, context);
      using var dirValue = SharedObjectCodec<DirEntry>.Encode(dir, runtime, context);
      var steps = new List<SharedObjectPairingStep>();
      context.SharedObjects.PairingStepForTesting = steps.Add;

      Assert.Same(file, SharedObjectBaseCodec<PathBase>.Decode(fileValue.Ref, runtime, context));
      Assert.Same(file, SharedObjectBaseCodec<PathBase>.Decode(fileValue, runtime, context));
      Assert.Same(dir, SharedObjectBaseCodec<PathBase>.Decode(dirValue.Ref, runtime, context));
      Assert.Same(dir, SharedObjectBaseCodec<PathBase>.Decode(dirValue, runtime, context));
      Assert.Same(file, SharedObjectBaseCodec<PathBase>.Decode(fileValue.Ref, runtime, context));

      Assert.Empty(steps);
      Assert.Equal(2, context.SharedObjects.Count);

      // Control: the hook does record steps when a new instance is paired, so the empty list
      // above is meaningful.
      using var paired = SharedObjectCodec<FileEntry>.Encode(new FileEntry("x"), runtime, context);
      Assert.Equal(PairingSteps, steps);
      return true;
    });
  }

  [Fact]
  public void DecodeRejectsAnInstanceOfAnUnrelatedSharedClass()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      Install<FileEntry>(runtime, context, "FileEntry");
      Install<SnapshotEntry>(runtime, context, "NativeSnapshot");
      using var other = SharedObjectCodec<SnapshotEntry>.Encode(new SnapshotEntry(), runtime, context);
      var steps = new List<SharedObjectPairingStep>();
      context.SharedObjects.PairingStepForTesting = steps.Add;

      var scoped = Assert.Throws<InvalidOperationException>(
          () => SharedObjectBaseCodec<PathBase>.Decode(other.Ref, runtime, context));
      Assert.Equal(
          NotAPathBaseMessage,
          scoped.Message);
      var owned = Assert.Throws<InvalidOperationException>(
          () => SharedObjectBaseCodec<PathBase>.Decode(other, runtime, context));
      Assert.Equal(
          NotAPathBaseMessage,
          owned.Message);

      // Wording control: the exact codec rejects the same value with its own type name.
      var exact = Assert.Throws<InvalidOperationException>(
          () => SharedObjectCodec<FileEntry>.Decode(other.Ref, runtime, context));
      Assert.Equal(
          "The shared object receiver is not a 'Expo.ModulesCore.Tests.Modules.FileEntry' instance.",
          exact.Message);

      Assert.Empty(steps);
      // Control: the hook records steps when a new instance is paired, so the empty list above
      // is meaningful.
      using var paired = SharedObjectCodec<FileEntry>.Encode(new FileEntry("x"), runtime, context);
      Assert.Equal(PairingSteps, steps);
      return true;
    });
  }

  [Fact]
  public void DecodeFailuresMatchTheExactCodec()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      Install<FileEntry>(runtime, context, "FileEntry");
      var contextB = new DotnetRuntimeContext(runtime);
      try
      {
        Install<FileEntry>(runtime, contextB, "FileEntry");

        using var plain = fixture.Evaluate("({})", "shared-object-base-plain.js");
        using var released = SharedObjectCodec<FileEntry>.Encode(new FileEntry("r"), runtime, context);
        using (var releasedObject = released.AsObject())
        {
          context.SharedObjects.ReleaseFromJavaScript(releasedObject);
        }
        using var otherContext = SharedObjectCodec<FileEntry>.Encode(new FileEntry("o"), runtime, contextB);

        AssertDecodeFailuresMatch(
            plain,
            "NativeState entry for SharedObjectNativeState is missing.",
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
      }
      finally
      {
        contextB.Dispose();
      }
      return true;
    });
  }

  [Theory]
  [InlineData("null")]
  [InlineData("undefined")]
  [InlineData("0")]
  [InlineData("false")]
  [InlineData("''")]
  [InlineData("5")]
  [InlineData("'x'")]
  public void DecodeRejectsNonObjectsLikeTheExactCodec(string expression)
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      Install<FileEntry>(runtime, context, "FileEntry");
      using var value = fixture.Evaluate(expression, "shared-object-base-non-object.js");
      var steps = new List<SharedObjectPairingStep>();
      context.SharedObjects.PairingStepForTesting = steps.Add;

      var baseScoped = Assert.Throws<InvalidOperationException>(
          () => SharedObjectBaseCodec<PathBase>.Decode(value.Ref, runtime, context));
      var exactScoped = Assert.Throws<InvalidOperationException>(
          () => SharedObjectCodec<FileEntry>.Decode(value.Ref, runtime, context));
      Assert.Equal(exactScoped.Message, baseScoped.Message);
      Assert.Contains(NotAnObjectMessage, baseScoped.Message);

      var baseOwned = Assert.Throws<InvalidOperationException>(
          () => SharedObjectBaseCodec<PathBase>.Decode(value, runtime, context));
      var exactOwned = Assert.Throws<InvalidOperationException>(
          () => SharedObjectCodec<FileEntry>.Decode(value, runtime, context));
      Assert.Equal(exactOwned.Message, baseOwned.Message);
      Assert.Contains(NotAnObjectMessage, baseOwned.Message);

      Assert.Empty(steps);
      // Control: the hook records steps when a new instance is paired, so the empty list above
      // is meaningful.
      using var paired = SharedObjectCodec<FileEntry>.Encode(new FileEntry("x"), runtime, context);
      Assert.Equal(PairingSteps, steps);
      return true;
    });
  }

  private static void AssertDecodeFailuresMatch(
      JavaScriptValue value,
      string expected,
      JavaScriptRuntime runtime,
      DotnetRuntimeContext context)
  {
    var baseCodec = Assert.Throws<InvalidOperationException>(
        () => SharedObjectBaseCodec<PathBase>.Decode(value.Ref, runtime, context));
    var exact = Assert.Throws<InvalidOperationException>(
        () => SharedObjectCodec<FileEntry>.Decode(value.Ref, runtime, context));
    Assert.Equal(exact.Message, baseCodec.Message);
    Assert.Contains(expected, baseCodec.Message);

    var baseOwned = Assert.Throws<InvalidOperationException>(
        () => SharedObjectBaseCodec<PathBase>.Decode(value, runtime, context));
    var exactOwned = Assert.Throws<InvalidOperationException>(
        () => SharedObjectCodec<FileEntry>.Decode(value, runtime, context));
    Assert.Equal(exactOwned.Message, baseOwned.Message);
    Assert.Contains(expected, baseOwned.Message);
  }

  private static void Install<T>(JavaScriptRuntime runtime, DotnetRuntimeContext context, string name)
      where T : SharedObject
  {
    using var holder = runtime.CreateObject();
    GeneratedSharedObjectClass.Install(context, holder, typeof(T), name, 0, null, null);
  }
}
