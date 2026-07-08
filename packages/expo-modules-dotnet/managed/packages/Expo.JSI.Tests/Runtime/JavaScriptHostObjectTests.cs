using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

public sealed class JavaScriptHostObjectTests
{
  [Fact]
  public void HostObjectGetterReturnsValuesAndPropertyNames()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var hostObject = runtime.CreateHostObject(new JavaScriptHostObjectDescriptor(
          Get: (callbackRuntime, propertyName, _) =>
              propertyName == "answer"
                  ? callbackRuntime.CreateNumber(42)
                  : callbackRuntime.CreateUndefined(),
          GetPropertyNames: _ => new[] { "answer" }
      ));
      using var global = runtime.Global();
      using var hostValue = hostObject.AsValue();
      global.SetProperty("__hostObject", hostValue);

      using var value = fixture.Evaluate(
          "globalThis.__hostObject.answer + ':' + Object.keys(globalThis.__hostObject).join(',')",
          "host-object-getter.js"
      );

      Assert.Equal("42:answer", value.AsString());
      return true;
    });
  }

  [Fact]
  public void HostObjectGetterExceptionIsCatchableInJavaScript()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var hostObject = runtime.CreateHostObject(new JavaScriptHostObjectDescriptor(
          Get: (_, propertyName, _) => throw new InvalidOperationException($"boom:{propertyName}")
      ));
      using var global = runtime.Global();
      using var hostValue = hostObject.AsValue();
      global.SetProperty("__hostObject", hostValue);

      using var value = fixture.Evaluate(
          "try { globalThis.__hostObject.fail; 'no error'; } catch (error) { error.message; }",
          "host-object-getter-error.js"
      );

      Assert.Contains("boom:fail", value.AsString());
      return true;
    });
  }

  [Fact]
  public void HostObjectWithoutSetterRejectsAssignment()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var hostObject = runtime.CreateHostObject(new JavaScriptHostObjectDescriptor(
          Get: (callbackRuntime, _, _) => callbackRuntime.CreateUndefined()
      ));
      using var global = runtime.Global();
      using var hostValue = hostObject.AsValue();
      global.SetProperty("__hostObject", hostValue);

      using var value = fixture.Evaluate(
          "try { globalThis.__hostObject.name = 1; 'no error'; } catch (error) { error.message; }",
          "host-object-readonly-setter.js"
      );

      Assert.Contains("Cannot set property", value.AsString());
      return true;
    });
  }

  [Fact]
  public void HostObjectSetterReceivesScopedValue()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      double assigned = 0;
      using var hostObject = runtime.CreateHostObject(new JavaScriptHostObjectDescriptor(
          Get: (callbackRuntime, _, _) => callbackRuntime.CreateUndefined(),
          Set: (_, propertyName, value, _) =>
          {
            if (propertyName == "answer")
            {
              assigned = value.AsDouble();
            }
          }
      ));
      using var global = runtime.Global();
      using var hostValue = hostObject.AsValue();
      global.SetProperty("__hostObject", hostValue);

      using var value = fixture.Evaluate(
          "globalThis.__hostObject.answer = 42; 'assigned'",
          "host-object-setter.js"
      );

      Assert.Equal("assigned", value.AsString());
      Assert.Equal(42.0, assigned);
      return true;
    });
  }

  [Fact]
  public void HostObjectPropertyNamesExceptionIsCatchableInJavaScript()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var hostObject = runtime.CreateHostObject(new JavaScriptHostObjectDescriptor(
          Get: (callbackRuntime, _, _) => callbackRuntime.CreateUndefined(),
          GetPropertyNames: _ => throw new InvalidOperationException("names failed")
      ));
      using var global = runtime.Global();
      using var hostValue = hostObject.AsValue();
      global.SetProperty("__hostObject", hostValue);

      using var value = fixture.Evaluate(
          "try { Object.keys(globalThis.__hostObject); 'no error'; } catch (error) { error.message; }",
          "host-object-property-names-error.js"
      );

      Assert.Contains("names failed", value.AsString());
      return true;
    });
  }

  [Fact]
  public void TypedHostObjectComposesJavaScriptObjectAndState()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      var state = new HostObjectState("ready");
      using var hostObject = runtime.CreateHostObject(
          state,
          new JavaScriptHostObjectDescriptor<HostObjectState>(
              Get: (callbackRuntime, propertyName, callbackState) =>
              {
                callbackState.AccessCount++;
                return propertyName == "status"
                    ? callbackRuntime.CreateString(callbackState.Status)
                    : callbackRuntime.CreateUndefined();
              },
              Set: (_, propertyName, value, callbackState) =>
              {
                if (propertyName == "answer")
                {
                  callbackState.Assigned = value.AsDouble();
                }
              },
              GetPropertyNames: callbackState =>
              {
                Assert.Same(state, callbackState);
                return new[] { "status" };
              }
          )
      );

      Assert.Same(state, hostObject.State);
      Assert.Same(hostObject.Object, hostObject.Object);

      using var global = runtime.Global();
      using var hostValue = hostObject.AsValue();
      global.SetProperty("__hostObject", hostValue);

      using var value = fixture.Evaluate(
          "globalThis.__hostObject.answer = 42; " +
          "globalThis.__hostObject.status + ':' + Object.keys(globalThis.__hostObject).join(',')",
          "typed-host-object.js"
      );

      Assert.Equal("ready:status", value.AsString());
      Assert.Equal(1, state.AccessCount);
      Assert.Equal(42.0, state.Assigned);
      return true;
    });
  }

  private sealed class HostObjectState(string status)
  {
    public string Status { get; } = status;

    public int AccessCount { get; set; }

    public double Assigned { get; set; }
  }
}
