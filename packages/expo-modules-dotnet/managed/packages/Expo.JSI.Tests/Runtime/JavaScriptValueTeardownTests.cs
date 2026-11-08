using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

// An author may keep an owned wrapper, such as a Retain() copy, past the call
// that produced it. Runtime teardown must release or abandon its JSI value, so
// a later Dispose never runs JSI code against a destroyed runtime.
public sealed class JavaScriptValueTeardownTests
{
  public static TheoryData<string> OwnedWrapperKinds =>
    new() { "value", "object", "function", "array", "string" };

  private static IDisposable CreateRetained(HermesRuntimeFixture fixture, string kind) =>
    fixture.Runtime.Execute<IDisposable>(runtime => kind switch
    {
      "value" => fixture.Evaluate("({ payload: 'x'.repeat(64) })", "retained-teardown.js"),
      "object" => runtime.CreateObject(),
      "function" => runtime.CreateHostFunction(
          "retained", 0, (callbackRuntime, _, _, _) => callbackRuntime.CreateUndefined(), new object()),
      "array" => runtime.CreateArray(1),
      "string" => runtime.CreateString("retained"),
      _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    });

  [Theory]
  [MemberData(nameof(OwnedWrapperKinds))]
  public void OwnedWrapperIsSweptByProductionOrderTeardown(string kind)
  {
    using var fixture = HermesRuntimeFixture.Create();
    var retained = CreateRetained(fixture, kind);

    // Production order: prepare -> invalidate connector (destroys Hermes)
    // -> release runtime handle.
    fixture.PrepareRuntimeForInvalidation();
    fixture.InvalidateRuntime();
    fixture.ReleaseBridgeRuntimeHandle();

    Assert.Equal(1u, fixture.Counters.OwnedValuesSwept);
    Assert.Equal(0u, fixture.Counters.OwnedValuesAbandoned);
    Assert.Equal(0u, fixture.Counters.OwnedValuesTracked);
    retained.Dispose();
  }

  [Fact]
  public void ControlOwnedValueDisposedBeforeTeardownIsNeitherSweptNorAbandoned()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var retained = CreateRetained(fixture, "value");
    retained.Dispose();
    Assert.Equal(0u, fixture.Counters.OwnedValuesTracked);

    fixture.PrepareRuntimeForInvalidation();
    fixture.InvalidateRuntime();
    fixture.ReleaseBridgeRuntimeHandle();

    Assert.Equal(0u, fixture.Counters.OwnedValuesSwept);
    Assert.Equal(0u, fixture.Counters.OwnedValuesAbandoned);
  }

  // Without preparation, JSI is gone before native code can sweep. Dispose
  // must leak the JSI value instead of destroying it.
  [Fact]
  public void LateInvalidationAbandonsOwnedValueOnDispose()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var retained = CreateRetained(fixture, "object");

    fixture.InvalidateRuntime();
    retained.Dispose();

    Assert.Equal(0u, fixture.Counters.OwnedValuesSwept);
    Assert.Equal(1u, fixture.Counters.OwnedValuesAbandoned);
    Assert.Equal(0u, fixture.Counters.OwnedValuesTracked);
  }

  [Fact]
  public void RuntimeHandleReleaseAbandonsUndisposedOwnedValue()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var retained = CreateRetained(fixture, "object");

    fixture.ReleaseBridgeRuntimeHandle();

    Assert.Equal(0u, fixture.Counters.OwnedValuesSwept);
    Assert.Equal(1u, fixture.Counters.OwnedValuesAbandoned);
    Assert.Equal(0u, fixture.Counters.OwnedValuesTracked);
    retained.Dispose();
  }

  [Fact]
  public void PropertyReadAfterTeardownFailsWithError()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var retained = (JavaScriptObject)CreateRetained(fixture, "object");
    fixture.PrepareRuntimeForInvalidation();
    fixture.InvalidateRuntime();

    var error = Assert.Throws<InvalidOperationException>(() => retained.GetProperty("payload"));
    Assert.Contains("Runtime handle is invalid", error.Message);
    retained.Dispose();
  }

  [Fact]
  public void NumberReadAfterTeardownFailsWithError()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var retained = (JavaScriptValue)CreateRetained(fixture, "value");
    fixture.PrepareRuntimeForInvalidation();
    fixture.InvalidateRuntime();

    Assert.Throws<InvalidOperationException>(() => retained.AsDouble());
    retained.Dispose();
  }

  // A release must not overlap a concurrent preparation sweep. The runtime is
  // valid while it is closing, so nothing may be abandoned.
  [Fact]
  public async Task OffThreadDisposeDuringPreparationNeverOverlapsSweep()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var values = fixture.Runtime.Execute(runtime =>
        Enumerable.Range(0, 64).Select(_ => runtime.CreateObject()).ToArray());

    var disposer = Task.Run(() =>
    {
      foreach (var value in values)
      {
        value.Dispose();
      }
    });
    fixture.PrepareRuntimeForInvalidation();
    await disposer;
    fixture.InvalidateRuntime();

    Assert.Equal(0u, fixture.Counters.OwnedValuesTracked);
    Assert.Equal(0u, fixture.Counters.OwnedValuesAbandoned);
    Assert.InRange(fixture.Counters.OwnedValuesSwept, 0u, 64u);
  }
}
