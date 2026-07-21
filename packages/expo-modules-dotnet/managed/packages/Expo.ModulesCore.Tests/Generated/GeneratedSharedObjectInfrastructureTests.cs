using Expo.JSI;
using Expo.ModulesCore;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Generated;

public sealed class GeneratedSharedObjectInfrastructureTests
{
  [Fact]
  public void HostFunctionConstructorCapabilityIsCharacterized()
  {
    // Hermes accepts managed host functions as constructors, invokes their callbacks, and uses an
    // object returned by the callback as the constructed result.
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      var state = new ConstructorState();
      using var function = runtime.CreateHostFunction(
          "HostFunctionConstructorCapability",
          1,
          CreateCallbackObject,
          state
      );
      using var argument = runtime.CreateString("constructed");
      using var result = function.CallAsConstructor(argument);
      using var resultObject = result.AsObject();
      using var value = resultObject.GetProperty("value");

      Assert.True(state.WasCalled);
      Assert.Equal("constructed", value.AsString());
      return true;
    });
  }

  [Fact]
  public void GeneratedConstructorInvokesManagedCallbackWithNew()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var constructor = GeneratedSharedObjectClass.Define(
          context,
          "GeneratedSharedObject",
          1,
          CreateInstance,
          new ConstructorState()
      );
      SetGlobalConstructor(runtime, constructor);

      using var result = fixture.Evaluate(
          "new globalThis.__generatedSharedObject('created').value",
          "generated-shared-object-constructor-invocation.js"
      );

      Assert.Equal("created", result.AsString());
      return true;
    });
  }

  [Fact]
  public void GeneratedConstructorReturnsCallbackObjectWithExactPrototype()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var constructor = GeneratedSharedObjectClass.Define(
          context,
          "GeneratedSharedObject",
          0,
          CreateInstance,
          new ConstructorState()
      );
      SetGlobalConstructor(runtime, constructor);

      using var result = fixture.Evaluate(
          "const Constructor = globalThis.__generatedSharedObject; " +
          "const value = new Constructor(); " +
          "Object.getPrototypeOf(value) === Constructor.prototype && " +
          "value instanceof Constructor && " +
          "Constructor.prototype.constructor === Constructor",
          "generated-shared-object-constructor-prototype.js"
      );

      Assert.True(result.AsBool());
      return true;
    });
  }

  [Fact]
  public void GeneratedConstructorRejectsCallWithoutNew()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      var state = new ConstructorState();
      using var constructor = GeneratedSharedObjectClass.Define(
          context,
          "GeneratedSharedObject",
          0,
          CreateInstance,
          state
      );
      SetGlobalConstructor(runtime, constructor);

      using var result = fixture.Evaluate(
          "const Constructor = globalThis.__generatedSharedObject; " +
          "const propertyCall = (() => { try { ({ Constructor }).Constructor(); return 'no error'; } catch (error) { return error.message; } })(); " +
          "const explicitReceiverCall = (() => { try { Constructor.call({}); return 'no error'; } catch (error) { return error.message; } })(); " +
          "const reflectApply = (() => { try { Reflect.apply(Constructor, {}, []); return 'no error'; } catch (error) { return error.message; } })(); " +
          "const backReferenceCall = (() => { try { Constructor.prototype.constructor.call({}); return 'no error'; } catch (error) { return error.message; } })(); " +
          "[propertyCall.includes('new'), explicitReceiverCall.includes('new'), reflectApply.includes('new'), backReferenceCall.includes('new')].join(':')",
          "generated-shared-object-constructor-call.js"
      );

      Assert.Equal("true:true:true:true", result.AsString());
      Assert.False(state.WasCalled);
      return true;
    });
  }

  [Fact]
  public void GeneratedConstructorRegistrationIsReleasedWithContext()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      var state = new ConstructorState();
      using var context = new DotnetRuntimeContext(runtime);
      using var constructor = GeneratedSharedObjectClass.Define(
          context,
          "GeneratedSharedObject",
          0,
          CreateInstance,
          state
      );
      SetGlobalConstructor(runtime, constructor);

      context.Dispose();

      using var result = fixture.Evaluate(
          "try { new globalThis.__generatedSharedObject(); 'no error'; } catch (error) { error.message; }",
          "generated-shared-object-constructor-teardown.js"
      );

      Assert.Contains("DotnetRuntimeContext", result.AsString());
      Assert.False(state.WasCalled);
      Assert.Equal(0u, fixture.Counters.LongLivedObjectsRemaining);
      return true;
    });
  }

  private static JavaScriptValue CreateCallbackObject(
      JavaScriptRuntime runtime,
      JavaScriptValueRef thisValue,
      JavaScriptArguments arguments,
      object context
  )
  {
    var state = (ConstructorState)context;
    state.WasCalled = true;
    using var result = runtime.CreateObject();
    using var value = arguments.GetValue(0).Retain();
    result.SetProperty("value", value);
    return result.AsValue();
  }

  private static JavaScriptValue CreateInstance(
      JavaScriptRuntime runtime,
      JavaScriptArrayRef arguments,
      object context
  )
  {
    var state = (ConstructorState)context;
    state.WasCalled = true;
    using var instance = runtime.CreateObject();
    if (arguments.Length > 0)
    {
      using var value = arguments.GetValue(0).Retain();
      instance.SetProperty("value", value);
    }
    return instance.AsValue();
  }

  private static void SetGlobalConstructor(JavaScriptRuntime runtime, JavaScriptFunction constructor)
  {
    using var global = runtime.Global();
    using var constructorValue = constructor.AsValue();
    global.SetProperty("__generatedSharedObject", constructorValue);
  }

  private sealed class ConstructorState
  {
    public bool WasCalled { get; set; }
  }
}
