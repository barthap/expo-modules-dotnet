using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

public sealed class JavaScriptNativeStateTests
{
  [Fact]
  public void SetAndGetNativeStateReturnsTheAttachedManagedState()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var target = runtime.CreateObject();
      var expected = new TestNativeState("alpha");

      target.SetNativeState(expected);
      var actual = target.GetNativeState<TestNativeState>();

      Assert.Same(expected, actual);
      Assert.True(target.TryGetNativeState<TestNativeState>(out var optional));
      Assert.Same(expected, optional);
      return true;
    });
  }

  [Fact]
  public void TryGetNativeStateReturnsFalseWithoutCreatingState()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var target = runtime.CreateObject();

      Assert.False(target.TryGetNativeState<TestNativeState>(out var state));
      Assert.Null(state);
      return true;
    });
  }

  [Fact]
  public void GetNativeStateFailsWhenStateIsMissing()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var target = runtime.CreateObject();

      var error = Assert.Throws<InvalidOperationException>(
          target.GetNativeState<TestNativeState>
      );
      Assert.Contains(nameof(TestNativeState), error.Message);
      return true;
    });
  }

  [Fact]
  public void NativeStateIsHiddenFromJavaScriptProperties()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var target = runtime.CreateObject();
      target.SetNativeState(new TestNativeState("hidden"));
      using var global = runtime.Global();
      using var targetValue = target.AsValue();
      global.SetProperty("__nativeStateTarget", targetValue);

      using var result = fixture.Evaluate(
          "Object.getOwnPropertyNames(globalThis.__nativeStateTarget).join(',')",
          "native-state-hidden.js"
      );

      Assert.Equal(string.Empty, result.AsString());
      return true;
    });
  }

  [Fact]
  public void ReplacingNativeStateDisposesPreviousStateExactlyOnce()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var target = runtime.CreateObject();
      var first = new DisposableNativeState();
      var second = new DisposableNativeState();

      target.SetNativeState(first);
      target.SetNativeState(second);

      Assert.Equal(1, first.DisposeCount);
      Assert.Equal(0, second.DisposeCount);
      Assert.Same(second, target.GetNativeState<DisposableNativeState>());
      return true;
    });
  }

  [Fact]
  public void ClearingNativeStateDisposesStateExactlyOnce()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var target = runtime.CreateObject();
      var state = new DisposableNativeState();

      target.SetNativeState(state);
      target.ClearNativeState<DisposableNativeState>();
      target.ClearNativeState<DisposableNativeState>();

      Assert.Equal(1, state.DisposeCount);
      Assert.False(target.TryGetNativeState<DisposableNativeState>(out _));
      return true;
    });
  }

  [Fact]
  public void DuplicateTypeIdsForDifferentStateTypesFailLoudly()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var first = runtime.CreateObject();
      using var second = runtime.CreateObject();

      first.SetNativeState(new DuplicateOne());
      var error = Assert.Throws<InvalidOperationException>(
          () => second.SetNativeState(new DuplicateTwo())
      );

      Assert.Contains("NativeState type id", error.Message);
      return true;
    });
  }

  [Fact]
  public void ScopedObjectRefCanReadNativeState()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var target = runtime.CreateObject();
      var expected = new TestNativeState("scoped");
      target.SetNativeState(expected);
      using var value = target.AsValue();

      var actual = value.Ref.AsObject().GetNativeState<TestNativeState>();

      Assert.Same(expected, actual);
      return true;
    });
  }

  [Fact]
  public void NativeReleaseCallbackRunsWhenStateIsCleared()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();

    fixture.Runtime.Execute(runtime =>
    {
      using var target = runtime.CreateObject();
      target.SetNativeState(new DisposableNativeState());
      target.ClearNativeState<DisposableNativeState>();
      return true;
    });

    Assert.Equal(1u, fixture.Counters.ReleasedNativeStates);
  }

  [Fact]
  public void HostFunctionCallbackReadsNativeStateFromOriginalRuntimeRegistry()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var target = runtime.CreateObject();
      var expected = new TestNativeState("callback");
      target.SetNativeState(expected);
      using var global = runtime.Global();
      using var targetValue = target.AsValue();
      global.SetProperty("__nativeStateCallbackTarget", targetValue);
      using var callback = runtime.CreateHostFunction(
          "__readNativeState",
          1,
          (callbackRuntime, thisValue, arguments, context) =>
          {
            var actual = arguments.GetValue(0).AsObject().GetNativeState<TestNativeState>();
            return callbackRuntime.CreateBool(ReferenceEquals(expected, actual));
          },
          new object()
      );
      using var callbackValue = callback.AsValue();
      global.SetProperty("__readNativeState", callbackValue);

      using var result = fixture.Evaluate(
          "globalThis.__readNativeState(globalThis.__nativeStateCallbackTarget)",
          "native-state-host-function.js"
      );

      Assert.True(result.AsBool());
      return true;
    });
  }

  private sealed class TestNativeState : IJavaScriptNativeState<TestNativeState>
  {
    public static JavaScriptNativeStateTypeId TypeId { get; } =
      JavaScriptNativeStateTypeId.FromName(nameof(TestNativeState));

    public TestNativeState(string value)
    {
      Value = value;
    }

    public string Value { get; }
  }

  private sealed class DisposableNativeState
      : IJavaScriptNativeState<DisposableNativeState>, IDisposable
  {
    public static JavaScriptNativeStateTypeId TypeId { get; } =
      JavaScriptNativeStateTypeId.FromName(nameof(DisposableNativeState));

    public int DisposeCount { get; private set; }

    public void Dispose() => DisposeCount++;
  }

  private sealed class DuplicateOne : IJavaScriptNativeState<DuplicateOne>
  {
    public static JavaScriptNativeStateTypeId TypeId { get; } =
      JavaScriptNativeStateTypeId.FromName("DuplicateNativeState");
  }

  private sealed class DuplicateTwo : IJavaScriptNativeState<DuplicateTwo>
  {
    public static JavaScriptNativeStateTypeId TypeId { get; } =
      JavaScriptNativeStateTypeId.FromName("DuplicateNativeState");
  }
}
