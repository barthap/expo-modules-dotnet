using Expo.JSI;
using Expo.ModulesCore;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Generated;

public sealed class GeneratedPropertyTests
{
  [Fact]
  public void DefinesWritableOwnAccessorWithExpectedDescriptorShape()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var module = runtime.CreateObject();
      var state = new ReadyState();
      SetGlobalModule(runtime, module);

      GeneratedProperty.Define(context, module, "ready", GetReady, SetReady, state);

      using var descriptor = fixture.Evaluate(
          "const descriptor = Object.getOwnPropertyDescriptor(module, 'ready');" +
          "[module.ready, descriptor.enumerable, descriptor.configurable, " +
          "descriptor.get.length, descriptor.set.length].join(':')",
          "generated-property-writable-descriptor.js"
      );
      Assert.Equal("false:true:true:0:1", descriptor.AsString());

      using var updated = fixture.Evaluate(
          "module.ready = true; module.ready",
          "generated-property-writable-update.js"
      );
      Assert.True(updated.AsBool());
      Assert.True(state.Ready);
      return true;
    });
  }

  [Fact]
  public void SuccessfulInstallationReleasesEveryTemporaryOwnedWrapper()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var module = runtime.CreateObject();

      fixture.ResetCounters();
      GeneratedProperty.Define(context, module, "ready", GetReady, SetReady, new ReadyState());

      // Define owns global; Object's value and object wrappers; defineProperty's value and function
      // wrappers; property-name string; descriptor; enumerable/configurable values; getter/setter
      // functions and their AsValue wrappers; and defineProperty's result (14). CallWithThis creates
      // a receiver Object clone plus module, property-name, and descriptor argument clones (4).
      // The module wrapper itself predates the reset and is deliberately not included.
      Assert.Equal(18u, fixture.Counters.ReleasedValues);
      return true;
    });
  }

  [Fact]
  public void FailedInstallationDisposesTemporariesButKeepsRegistrationContextOwned()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var module = runtime.CreateObject();
      var state = new ReadyState { Ready = true };
      SetGlobalModule(runtime, module);
      using (
          fixture.Evaluate(
              "Object.defineProperty = function(target, name, descriptor) {" +
              "globalThis.__capturedFailedGetter = descriptor.get; throw new Error('define failed'); };",
              "generated-property-failure-setup.js"
          )
      )
      {
      }

      fixture.ResetCounters();
      var exception = Assert.Throws<InvalidOperationException>(
          () => GeneratedProperty.Define(context, module, "ready", GetReady, SetReady, state)
      );
      Assert.Contains("define failed", exception.Message);

      // This matches the successful inventory except the throwing CallWithThis has no owned result.
      Assert.Equal(17u, fixture.Counters.ReleasedValues);

      using var beforeTeardown = fixture.Evaluate(
          "globalThis.__capturedFailedGetter()",
          "generated-property-failure-before-teardown.js"
      );
      Assert.True(beforeTeardown.AsBool());

      context.Dispose();

      using var afterTeardown = fixture.Evaluate(
          "try { globalThis.__capturedFailedGetter(); 'no error'; } catch (error) { error.message; }",
          "generated-property-failure-after-teardown.js"
      );
      Assert.Contains("DotnetRuntimeContext", afterTeardown.AsString());
      return true;
    });
  }

  [Fact]
  public void ReplacementKeepsCapturedAccessorCallableUntilContextTeardown()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var module = runtime.CreateObject();
      SetGlobalModule(runtime, module);

      GeneratedProperty.Define(
          context,
          module,
          "ready",
          static (callbackRuntime, thisValue, arguments, callbackState) =>
              callbackRuntime.CreateString((string)callbackState),
          null,
          "A"
      );
      using var _ = fixture.Evaluate(
          "globalThis.__capturedA = Object.getOwnPropertyDescriptor(module, 'ready').get;",
          "generated-property-replacement-capture.js"
      );

      GeneratedProperty.Define(
          context,
          module,
          "ready",
          static (callbackRuntime, thisValue, arguments, callbackState) =>
              callbackRuntime.CreateString((string)callbackState),
          null,
          "B"
      );

      using var beforeTeardown = fixture.Evaluate(
          "[module.ready, globalThis.__capturedA.call(module)].join(':')",
          "generated-property-replacement-before-teardown.js"
      );
      Assert.Equal("B:A", beforeTeardown.AsString());

      context.Dispose();
      context.Dispose();

      using var afterTeardown = fixture.Evaluate(
          "try { globalThis.__capturedA.call(module); 'no error'; } catch (error) { error.message; }",
          "generated-property-replacement-after-teardown.js"
      );
      Assert.Contains("DotnetRuntimeContext", afterTeardown.AsString());
      return true;
    });
  }

  [Fact]
  public void ReadOnlyAccessorHasNoSetterAndStrictAssignmentThrowsTypeError()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var module = runtime.CreateObject();
      SetGlobalModule(runtime, module);

      GeneratedProperty.Define(context, module, "ready", GetReady, null, new ReadyState());

      using var result = fixture.Evaluate(
          "const descriptor = Object.getOwnPropertyDescriptor(module, 'ready');" +
          "let assignment; try { (function() { 'use strict'; module.ready = true; })(); " +
          "assignment = 'no error'; } catch (error) { assignment = error instanceof TypeError; } " +
          "[typeof descriptor.set, assignment].join(':')",
          "generated-property-readonly.js"
      );
      Assert.Equal("undefined:true", result.AsString());
      return true;
    });
  }

  private static JavaScriptValue GetReady(
      JavaScriptRuntime runtime,
      JavaScriptValueRef thisValue,
      JavaScriptArguments arguments,
      object context
  )
  {
    Assert.Equal(0u, arguments.Count);
    return runtime.CreateBool(((ReadyState)context).Ready);
  }

  private static void SetGlobalModule(JavaScriptRuntime runtime, JavaScriptObject module)
  {
    using var global = runtime.Global();
    using var moduleValue = module.AsValue();
    global.SetProperty("module", moduleValue);
  }

  private static JavaScriptValue SetReady(
      JavaScriptRuntime runtime,
      JavaScriptValueRef thisValue,
      JavaScriptArguments arguments,
      object context
  )
  {
    Assert.Equal(1u, arguments.Count);
    ((ReadyState)context).Ready = arguments.GetValue(0).AsBool();
    return runtime.CreateUndefined();
  }

  private sealed class ReadyState
  {
    public bool Ready { get; set; }
  }
}
