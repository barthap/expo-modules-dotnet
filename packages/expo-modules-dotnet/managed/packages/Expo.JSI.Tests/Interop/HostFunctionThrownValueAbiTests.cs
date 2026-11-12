using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Expo.JSI.Interop;
using Expo.JSI.Internal;
using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Interop;

// ABI 24: a host-function callback result with ok = 0 and a non-null value means "throw this
// value". Upstream iOS relays a jsi::JSError built from a value the same way. These tests use raw
// callbacks, so they pin the native meaning without the managed trampoline.
public sealed unsafe class HostFunctionThrownValueAbiTests
{
  [Fact]
  public void FailedResultWithValueThrowsThatValue()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var state = fixture.Runtime.Execute(runtime =>
    {
      var created = RawState.Create(fixture, withReleasableError: false);
      InstallRawHostFunction(runtime, "throwMarker", created);
      using var caught = fixture.Evaluate(
          "try { throwMarker(); 'no error'; } catch (e) { e === globalThis.marker; }",
          "raw-thrown-value.js"
      );
      // JavaScript catches the identical object, not a new Error built from a message.
      Assert.True(caught.AsBool());
      return created;
    });

    Assert.Equal(0, state.ErrorReleases);
    state.Value.Dispose();
    // Native deleted the thrown handle with `delete`, which unlinks it; nothing stays tracked.
    Assert.Equal(0u, fixture.Counters.OwnedValuesTracked);
  }

  [Fact]
  public void FailedResultWithValueReleasesTheErrorOnce()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var state = fixture.Runtime.Execute(runtime =>
    {
      var created = RawState.Create(fixture, withReleasableError: true);
      InstallRawHostFunction(runtime, "throwMarker", created);
      using var caught = fixture.Evaluate(
          "try { throwMarker(); 'no error'; } catch (e) { e === globalThis.marker; }",
          "raw-thrown-value-error.js"
      );
      // The value wins over the message, and the error buffer is still released.
      Assert.True(caught.AsBool());
      return created;
    });

    Assert.Equal(1, state.ErrorReleases);
    state.Value.Dispose();
    Assert.Equal(0u, fixture.Counters.OwnedValuesTracked);
  }

  // HostObject getters keep the old meaning: a value with ok = 0 is deleted and native throws the
  // message. This test passes before and after the change; it guards the scope of the new meaning.
  [Fact]
  public void HostObjectGetterFailureWithValueStillThrowsTheMessage()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var state = fixture.Runtime.Execute(runtime =>
    {
      var created = RawState.Create(fixture, withReleasableError: false);
      InstallRawHostObject(runtime, "rawObject", created);
      using var caught = fixture.Evaluate(
          "try { rawObject.anything; 'no error'; } catch (e) { e === globalThis.marker ? 'value' : e.message; }",
          "raw-host-object-getter.js"
      );
      Assert.Equal("raw getter failed", caught.AsString());
      return created;
    });

    state.Value.Dispose();
    Assert.Equal(0u, fixture.Counters.OwnedValuesTracked);
  }

  private static void InstallRawHostFunction(JavaScriptRuntime runtime, string name, RawState state)
  {
    var jsi = runtime.Context;
    var context = GCHandle.ToIntPtr(GCHandle.Alloc(state));
    var result = jsi.Api->CreateHostFunctionValue(
        jsi.RuntimeHandle,
        Encoding.UTF8.GetBytes(name),
        0,
        &ReturnStateValueAsFailure,
        context,
        &ReleaseStateHandle
    );
    if (!result.IsOk)
    {
      GCHandle.FromIntPtr(context).Free();
      JsiContext.ThrowNativeError(result.Error, "Failed to create the raw host function.");
    }
    using var function = new JavaScriptFunction(jsi, result.Value);
    using var functionValue = function.AsValue();
    using var global = runtime.Global();
    global.SetProperty(name, functionValue);
  }

  private static void InstallRawHostObject(JavaScriptRuntime runtime, string name, RawState state)
  {
    var jsi = runtime.Context;
    var context = GCHandle.ToIntPtr(GCHandle.Alloc(state));
    var result = jsi.Api->CreateHostObjectValue(
        jsi.RuntimeHandle,
        &ReturnStateValueAsGetterFailure,
        null,
        &NoPropertyNames,
        context,
        &ReleaseStateHandle
    );
    if (!result.IsOk)
    {
      GCHandle.FromIntPtr(context).Free();
      JsiContext.ThrowNativeError(result.Error, "Failed to create the raw host object.");
    }
    using var hostObject = new JavaScriptObject(jsi, result.Value);
    using var hostObjectValue = hostObject.AsValue();
    using var global = runtime.Global();
    global.SetProperty(name, hostObjectValue);
  }

  [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
  private static ExpoJsiValueResult ReturnStateValueAsFailure(
      nint context,
      nint runtime,
      nint thisValue,
      nint arguments
  )
  {
    var state = (RawState)GCHandle.FromIntPtr(context).Target!;
    var error = state.WithReleasableError
        ? new ExpoJsiError(100, state.Message, state.MessageLength, context, &CountErrorRelease)
        : default;
    return new ExpoJsiValueResult(0, state.Value.AsValue().Detach(), error);
  }

  [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
  private static ExpoJsiValueResult ReturnStateValueAsGetterFailure(
      nint context,
      nint runtime,
      byte* name,
      int nameLength
  )
  {
    var state = (RawState)GCHandle.FromIntPtr(context).Target!;
    var error = new ExpoJsiError(100, state.Message, state.MessageLength, 0, null);
    return new ExpoJsiValueResult(0, state.Value.AsValue().Detach(), error);
  }

  [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
  private static ExpoJsiPropertyNamesResult NoPropertyNames(nint context, nint runtime) =>
      new(1, null, 0, 0, null, default);

  [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
  private static void CountErrorRelease(nint context)
  {
    ((RawState)GCHandle.FromIntPtr(context).Target!).ErrorReleases++;
  }

  [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
  private static void ReleaseStateHandle(nint context)
  {
    GCHandle.FromIntPtr(context).Free();
  }

  private sealed class RawState : IDisposable
  {
    private RawState(JavaScriptValue value, bool withReleasableError, byte* message, int messageLength)
    {
      Value = value;
      WithReleasableError = withReleasableError;
      Message = message;
      MessageLength = messageLength;
    }

    public JavaScriptValue Value { get; }
    public bool WithReleasableError { get; }
    public byte* Message { get; private set; }
    public int MessageLength { get; }
    public int ErrorReleases { get; set; }

    // Must run inside Runtime.Execute.
    public static RawState Create(HermesRuntimeFixture fixture, bool withReleasableError)
    {
      var value = fixture.Evaluate("globalThis.marker = { tag: 'thrown' }; globalThis.marker", "raw-marker.js");
      var bytes = Encoding.UTF8.GetBytes("raw getter failed");
      var message = (byte*)NativeMemory.Alloc((nuint)bytes.Length);
      bytes.CopyTo(new Span<byte>(message, bytes.Length));
      return new RawState(value, withReleasableError, message, bytes.Length);
    }

    public void Dispose()
    {
      if (Message != null)
      {
        NativeMemory.Free(Message);
        Message = null;
      }
    }
  }
}
