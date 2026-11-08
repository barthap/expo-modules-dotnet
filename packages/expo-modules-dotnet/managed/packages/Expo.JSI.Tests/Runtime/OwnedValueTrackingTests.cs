using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

// Native code links every owned non-primitive value handle to its runtime
// state, so runtime teardown can release or abandon the JSI value. These
// tests check that the link set follows the handle's life on every path.
public sealed class OwnedValueTrackingTests
{
  [Fact]
  public void NewRuntimeTracksNoOwnedValues()
  {
    using var fixture = HermesRuntimeFixture.Create();

    Assert.Equal(0u, fixture.Counters.OwnedValuesTracked);
  }

  // A primitive jsi::Value holds no runtime pointer, so it needs no tracking.
  [Fact]
  public void PrimitiveOwnedValuesAreNotTracked()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var values = fixture.Runtime.Execute(runtime => new[]
    {
      runtime.CreateNumber(1),
      runtime.CreateBool(true),
      runtime.CreateNull(),
      runtime.CreateUndefined(),
    });

    Assert.Equal(0u, fixture.Counters.OwnedValuesTracked);
    foreach (var value in values)
    {
      value.Dispose();
    }
  }

  [Fact]
  public void OwnedObjectIsTrackedUntilDisposed()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var retained = fixture.Runtime.Execute(runtime => runtime.CreateObject());

    Assert.Equal(1u, fixture.Counters.OwnedValuesTracked);

    retained.Dispose();

    Assert.Equal(0u, fixture.Counters.OwnedValuesTracked);
  }

  // Off-thread Dispose releases before it returns and queues nothing. The
  // executor is paused, so a queued release could not finish.
  [Fact]
  public async Task OffThreadDisposeReleasesBeforeReturning()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var retained = fixture.Runtime.Execute(runtime => runtime.CreateObject());
    fixture.PauseRuntimeExecutor();
    try
    {
      await Task.Run(retained.Dispose);

      Assert.Equal(0u, fixture.Counters.OwnedValuesTracked);
      Assert.Equal(0u, fixture.Counters.OwnedValuesSwept);
      Assert.Equal(0u, fixture.Counters.OwnedValuesAbandoned);
    }
    finally
    {
      fixture.ResumeRuntimeExecutor();
    }
  }

  [Fact]
  public void ScopedRefsLeaveNoTrackedValues()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.Runtime.Execute(_ =>
    {
      using var source = fixture.Evaluate("({ a: { b: 'x' } })", "scoped-refs.js");
      var nested = source.Ref.AsObject().GetProperty("a").AsObject().GetProperty("b");
      Assert.Equal("x", nested.AsString());
      return true;
    });

    Assert.Equal(0u, fixture.Counters.OwnedValuesTracked);
  }

  // Native code deletes the detached result handle with `delete result.value`,
  // not through release_value. ~ValueHandle must unlink it.
  [Fact]
  public void HostFunctionResultLeavesNoTrackedValues()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.Runtime.Execute(runtime =>
    {
      using var global = runtime.Global();
      using var function = runtime.CreateHostFunction(
          "makeObject",
          0,
          (callbackRuntime, _, _, _) =>
          {
            using var created = callbackRuntime.CreateObject();
            return created.AsValue();
          },
          new object());
      using var functionValue = function.AsValue();
      global.SetProperty("makeObject", functionValue);
      using var result = fixture.Evaluate("typeof globalThis.makeObject()", "host-result.js");
      Assert.Equal("object", result.AsString());
      return true;
    });

    Assert.Equal(0u, fixture.Counters.OwnedValuesTracked);
  }
}
