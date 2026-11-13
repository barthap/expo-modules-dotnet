using Expo.JSI;
using Expo.ModulesCore.Codecs;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Modules;

public sealed class GeneratedCallbackModuleTests
{
  [Fact]
  public void GeneratedModuleInvokesCallbackParameter()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "globalThis._expoDotnet.modules.GeneratedCallbacks.callNow('JS', name => `Hello ${name}`)",
          "generated-callback-call-now.js"
      );

      Assert.Equal("Hello JS", result.AsString());
      return true;
    });
  }

  [Fact]
  public void GeneratedModuleInvokesCallbackParameterFromCurrentRuntimeWhenSyncExecutionUnsupported()
  {
    using var fixture = HermesRuntimeFixture.Create();
    DotnetRuntimeContext? context = null;

    fixture.Runtime.Execute(runtime =>
    {
      context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      return true;
    });

    try
    {
      fixture.DisableSyncExecutionForTesting();

      var setupResult = fixture.Evaluate(
          """
          globalThis.__callbackSyncUnsupported =
            globalThis._expoDotnet.modules.GeneratedCallbacks.callNow(
              'JS',
              name => `Hello ${name}`
            );
          true
          """,
          "generated-callback-call-now-sync-unsupported.js"
      );

      fixture.SetSyncExecutionSupportedForTesting(true);
      var result = fixture.Runtime.Execute(_ =>
      {
        setupResult.Dispose();
        using var value = fixture.Evaluate(
            "globalThis.__callbackSyncUnsupported",
            "generated-callback-call-now-sync-unsupported-result.js"
        );
        return value.AsString();
      });

      Assert.Equal("Hello JS", result);
    }
    finally
    {
      fixture.SetSyncExecutionSupportedForTesting(true);
      fixture.Runtime.Execute(_ =>
      {
        context?.Dispose();
        return true;
      });
    }
  }

  [Fact]
  public void GeneratedModuleInvokesZeroArgumentCallbackParameter()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "globalThis._expoDotnet.modules.GeneratedCallbacks.callNoArgs(() => 'No args')",
          "generated-callback-call-no-args.js"
      );

      Assert.Equal("No args", result.AsString());
      return true;
    });
  }

  [Fact]
  public void GeneratedModuleInvokesExplicitValueTupleCallbackParameter()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var result = fixture.Evaluate(
          "globalThis._expoDotnet.modules.GeneratedCallbacks.callExplicitTuple('A', 'B', (first, second) => first + second)",
          "generated-callback-explicit-tuple.js"
      );

      Assert.Equal("AB", result.AsString());
      return true;
    });
  }

  [Fact]
  public async Task GeneratedModuleInvokesRetainedCallbackLater()
  {
    using var fixture = HermesRuntimeFixture.Create();
    DotnetRuntimeContext? context = null;

    fixture.Runtime.Execute(runtime =>
    {
      context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);

      using var _ = fixture.Evaluate(
          """
          globalThis.__callbackResult = null;
          globalThis._expoDotnet.modules.GeneratedCallbacks.store(name => `Stored ${name}`);
          globalThis._expoDotnet.modules.GeneratedCallbacks.callStored('JS').then(
            value => { globalThis.__callbackResult = value; },
            error => { globalThis.__callbackResult = String(error && error.message || error); }
          );
          true;
          """,
          "generated-callback-stored.js"
      );
      return true;
    });

    try
    {
      var result = await WaitForGlobalStringAsync(fixture, "__callbackResult");
      Assert.Equal("Stored JS", result);
    }
    finally
    {
      fixture.Runtime.Execute(_ =>
      {
        context?.Dispose();
        return true;
      });
    }
  }

  [Fact]
  public void RetainedCallbackInvokesImmediately()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var functionValue = fixture.Evaluate("(name) => `Hello ${name}`", "callback-now.js");
      using var function = functionValue.AsFunction();
      using var callback = JavaScriptCallback<ValueTuple<string>, string>.FromFunction(
          context,
          function,
          static (args, jsRuntime, jsContext) => [StringCodec.Encode(args.Item1, jsRuntime, jsContext)],
          static (value, jsRuntime, jsContext) => StringCodec.Decode(value, jsRuntime, jsContext)
      );

      Assert.Equal("Hello JS", callback.Invoke(ValueTuple.Create("JS")));
      return true;
    });
  }

  private static async Task<string?> WaitForGlobalStringAsync(
      HermesRuntimeFixture fixture,
      string globalName)
  {
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
    while (DateTime.UtcNow < deadline)
    {
      fixture.DrainTasks();
      var value = fixture.Runtime.Execute(_ =>
      {
        using var result = fixture.Evaluate($"globalThis.{globalName}", "callback-global-read.js");
        return result.IsNullish ? null : result.AsString();
      });
      if (value is not null)
      {
        return value;
      }

      await Task.Delay(10, TestContext.Current.CancellationToken);
    }

    return null;
  }

  [Fact]
  public async Task RetainedCallbackInvokesLaterOnRuntime()
  {
    using var fixture = HermesRuntimeFixture.Create();
    DotnetRuntimeContext? context = null;
    JavaScriptCallback<ValueTuple<string>, string> callback = fixture.Runtime.Execute(runtime =>
    {
      context = new DotnetRuntimeContext(runtime);
      using var functionValue = fixture.Evaluate("(name) => `Later ${name}`", "callback-later.js");
      using var function = functionValue.AsFunction();
      return JavaScriptCallback<ValueTuple<string>, string>.FromFunction(
          context,
          function,
          static (args, jsRuntime, jsContext) => [StringCodec.Encode(args.Item1, jsRuntime, jsContext)],
          static (value, jsRuntime, jsContext) => StringCodec.Decode(value, jsRuntime, jsContext)
      );
    });

    var task = callback.InvokeAsync(ValueTuple.Create("JS"), TestContext.Current.CancellationToken);
    await WaitForTaskAsync(fixture, task);

    try
    {
      Assert.Equal("Later JS", await task);
    }
    finally
    {
      fixture.Runtime.Execute(_ =>
      {
        context?.Dispose();
        return true;
      });
    }
  }

  [Fact]
  public void RetainedCallbackSupportsTwoArguments()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var functionValue = fixture.Evaluate("(first, second) => first + second", "callback-two.js");
      using var function = functionValue.AsFunction();
      using var callback = JavaScriptCallback<(string, string), string>.FromFunction(
          context,
          function,
          static (args, jsRuntime, jsContext) =>
              [
                StringCodec.Encode(args.Item1, jsRuntime, jsContext),
                StringCodec.Encode(args.Item2, jsRuntime, jsContext),
              ],
          static (value, jsRuntime, jsContext) => StringCodec.Decode(value, jsRuntime, jsContext)
      );

      Assert.Equal("AB", callback.Invoke(("A", "B")));
      return true;
    });
  }

  [Fact]
  public void RuntimeContextDisposeReleasesRetainedCallback()
  {
    using var fixture = HermesRuntimeFixture.Create();
    JavaScriptCallback<ValueTuple<string>, string>? callback = null;
    var context = fixture.Runtime.Execute(runtime =>
    {
      var createdContext = new DotnetRuntimeContext(runtime);
      using var functionValue = fixture.Evaluate("(name) => name", "callback-dispose.js");
      using var function = functionValue.AsFunction();
      callback = JavaScriptCallback<ValueTuple<string>, string>.FromFunction(
          createdContext,
          function,
          static (args, jsRuntime, jsContext) => [StringCodec.Encode(args.Item1, jsRuntime, jsContext)],
          static (value, jsRuntime, jsContext) => StringCodec.Decode(value, jsRuntime, jsContext)
      );
      return createdContext;
    });

    fixture.Runtime.Execute(_ =>
    {
      context.Dispose();
      return true;
    });

    Assert.NotNull(callback);
    Assert.Throws<InvalidOperationException>(() => callback.Invoke(ValueTuple.Create("JS")));
  }

  private const string NullableModulePrelude =
      "globalThis.m = globalThis._expoDotnet.modules.GeneratedNullableCallbacks;";

  [Fact]
  public void NullishCallbackReachesAuthoredCodeAsNull()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var context = CreateRegisteredContext(fixture);
    var baseline = context.RetainedCallbackCount;

    try
    {
      var result = EvaluateString(
          fixture,
          NullableModulePrelude +
          "[m.callOptional(null), m.callOptional(undefined), m.callDefaulted(), " +
          "m.callDefaulted(undefined), m.callDefaulted(null)].join(':')"
      );

      Assert.Equal("-1:-1:null:null:null", result);
      Assert.Equal(baseline, context.RetainedCallbackCount);
      Assert.Null(GeneratedNullableCallbacksModule.LastOptional);
    }
    finally
    {
      DisposeContext(fixture, context);
    }
  }

  [Theory]
  [InlineData("5")]
  [InlineData("'x'")]
  [InlineData("({})")]
  public void NullableCallbackParameterRejectsNonFunctionValues(string value)
  {
    using var fixture = HermesRuntimeFixture.Create();
    var context = CreateRegisteredContext(fixture);
    var baseline = context.RetainedCallbackCount;

    try
    {
      var result = EvaluateString(
          fixture,
          NullableModulePrelude + CatchExpression($"m.callOptional({value})", withMessage: true)
      );

      Assert.StartsWith("Error:", result);
      Assert.Contains("not a function", result);
      Assert.Equal(baseline, context.RetainedCallbackCount);
    }
    finally
    {
      DisposeContext(fixture, context);
    }
  }

  [Fact]
  public void NonNullNullableCallbackStaysRetainedAfterTheMethodReturns()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var context = CreateRegisteredContext(fixture);
    var baseline = context.RetainedCallbackCount;

    try
    {
      Assert.Equal("5", EvaluateString(fixture, NullableModulePrelude + "String(m.callOptional(() => 5))"));
      Assert.Equal(baseline + 1, context.RetainedCallbackCount);

      Assert.Equal("5", EvaluateString(fixture, "String(m.callStoredOptional())"));
      Assert.Equal(baseline + 1, context.RetainedCallbackCount);

      Assert.Equal("Hi JS", EvaluateString(fixture, "m.callDefaulted(name => 'Hi ' + name)"));
      Assert.Equal(baseline + 2, context.RetainedCallbackCount);

      Assert.Equal(
          "9:9",
          EvaluateString(fixture, "globalThis.f = () => 9; [m.callOptional(f), m.callOptional(f)].join(':')")
      );
      Assert.Equal(baseline + 4, context.RetainedCallbackCount);
    }
    finally
    {
      DisposeContext(fixture, context);
    }
  }

  [Fact]
  public async Task NonNullNullableCallbackInvokesAfterAwait()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var context = CreateRegisteredContext(fixture);
    var baseline = context.RetainedCallbackCount;

    try
    {
      EvaluateString(
          fixture,
          NullableModulePrelude +
          """
          m.callOptionalAfterAwait(() => 7).then(
            v => { globalThis.__optionalAfterAwait = String(v); },
            e => { globalThis.__optionalAfterAwait = 'error:' + e.message; }
          );
          'started'
          """
      );

      Assert.Equal("7", await WaitForGlobalStringAsync(fixture, "__optionalAfterAwait"));
      Assert.Equal(baseline + 1, context.RetainedCallbackCount);

      EvaluateString(
          fixture,
          """
          m.callOptionalAfterAwait(null).then(
            v => { globalThis.__optionalAfterAwaitNull = String(v); },
            e => { globalThis.__optionalAfterAwaitNull = 'error:' + e.message; }
          );
          'started'
          """
      );

      Assert.Equal("-1", await WaitForGlobalStringAsync(fixture, "__optionalAfterAwaitNull"));
      Assert.Equal(baseline + 1, context.RetainedCallbackCount);
    }
    finally
    {
      DisposeContext(fixture, context);
    }
  }

  [Fact]
  public void ExplicitDisposeReleasesNullableCallbackOnce()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var context = CreateRegisteredContext(fixture);
    var baseline = context.RetainedCallbackCount;

    try
    {
      EvaluateString(
          fixture,
          NullableModulePrelude +
          "m.callOptional(() => 5); m.disposeStoredOptional(); m.disposeStoredOptional(); 'done'"
      );

      var result = EvaluateString(fixture, CatchExpression("m.callStoredOptional()", withMessage: true));
      Assert.StartsWith("Error:", result);
      Assert.Contains("disposed", result);
      Assert.Equal(baseline + 1, context.RetainedCallbackCount);
    }
    finally
    {
      DisposeContext(fixture, context);
    }

    Assert.Equal(0, context.RetainedCallbackCount);
    Assert.Throws<ObjectDisposedException>(() => GeneratedNullableCallbacksModule.LastOptional!.Invoke());
  }

  [Fact]
  public void ContextTeardownReleasesUndisposedNullableCallbackOnce()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var context = CreateRegisteredContext(fixture);
    var baseline = context.RetainedCallbackCount;

    try
    {
      Assert.Equal("5", EvaluateString(fixture, NullableModulePrelude + "String(m.callOptional(() => 5))"));
      Assert.Equal(baseline + 1, context.RetainedCallbackCount);
    }
    finally
    {
      DisposeContext(fixture, context);
    }

    Assert.Equal(0, context.RetainedCallbackCount);
    Assert.Throws<InvalidOperationException>(() => GeneratedNullableCallbacksModule.LastOptional!.Invoke());
    Assert.Null(Record.Exception(() => GeneratedNullableCallbacksModule.LastOptional!.Dispose()));
  }

  [Fact]
  public void NonNullCallbackParameterStaysStrict()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var context = CreateRegisteredContext(fixture);
    var baseline = context.RetainedCallbackCount;

    try
    {
      var prelude = "globalThis.s = globalThis._expoDotnet.modules.GeneratedCallbacks;";
      Assert.Equal("Error", EvaluateString(fixture, prelude + CatchExpression("s.callNoArgs(null)")));
      Assert.Equal("Error", EvaluateString(fixture, CatchExpression("s.callNoArgs(undefined)")));
      Assert.Equal(baseline, context.RetainedCallbackCount);
    }
    finally
    {
      DisposeContext(fixture, context);
    }
  }

  [Fact]
  public void RequiredNullableCallbackStillRequiresItsArgument()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var context = CreateRegisteredContext(fixture);
    var baseline = context.RetainedCallbackCount;

    try
    {
      Assert.Equal(
          "Error",
          EvaluateString(fixture, NullableModulePrelude + CatchExpression("m.callOptional()"))
      );
      Assert.Equal(baseline, context.RetainedCallbackCount);
    }
    finally
    {
      DisposeContext(fixture, context);
    }
  }

  [Fact]
  public void SharedObjectNullableCallbackParametersDecodeLikeModuleParameters()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var context = CreateRegisteredContext(fixture);
    var baseline = context.RetainedCallbackCount;

    try
    {
      Assert.Equal(
          "-1:-1:null:null:null",
          EvaluateString(
              fixture,
              NullableModulePrelude +
              "globalThis.a = new m.NullableCallbackEntry(null); " +
              "globalThis.u = new m.NullableCallbackEntry(undefined); " +
              "[a.seed, u.seed, a.call(null), a.call(undefined), a.call()].join(':')"
          )
      );
      Assert.Equal(baseline, context.RetainedCallbackCount);

      Assert.Equal(
          "3:Hi entry",
          EvaluateString(
              fixture,
              "globalThis.b = new m.NullableCallbackEntry(() => 3); " +
              "[b.seed, b.call(name => 'Hi ' + name)].join(':')"
          )
      );
      Assert.Equal(baseline + 2, context.RetainedCallbackCount);

      Assert.Equal("Error", EvaluateString(fixture, CatchExpression("new m.NullableCallbackEntry(5)")));
      Assert.Equal(baseline + 2, context.RetainedCallbackCount);
    }
    finally
    {
      DisposeContext(fixture, context);
    }
  }

  // Registering the provider already retains one installation per shared-object class, so the
  // tests count callbacks relative to the count read right after registration.
  private static DotnetRuntimeContext CreateRegisteredContext(HermesRuntimeFixture fixture) =>
      fixture.Runtime.Execute(runtime =>
      {
        var createdContext = new DotnetRuntimeContext(runtime);
        using var modules = createdContext.ModuleRegistry.GetOrCreateDotnetModulesObject();
        ExpoModulesProvider_Expo_ModulesCore_Tests.Register(createdContext, modules);
        return createdContext;
      });

  private static void DisposeContext(HermesRuntimeFixture fixture, DotnetRuntimeContext context) =>
      fixture.Runtime.Execute(_ =>
      {
        context.Dispose();
        return true;
      });

  private static string EvaluateString(HermesRuntimeFixture fixture, string source) =>
      fixture.Runtime.Execute(_ =>
      {
        using var result = fixture.Evaluate(source, "nullable-callback-test.js");
        return result.AsString()!;
      });

  // Evaluates to "Error" when the call throws an Error ("Error:<message>" with withMessage),
  // "no error" when it does not throw, and the typeof the thrown value otherwise.
  private static string CatchExpression(string call, bool withMessage = false)
  {
    var errorResult = withMessage ? "'Error:' + e.message" : "'Error'";
    return $"(() => {{ try {{ {call}; return 'no error'; }} catch (e) {{ return e instanceof Error ? {errorResult} : typeof e; }} }})()";
  }

  private static async Task WaitForTaskAsync<TResult>(
      HermesRuntimeFixture fixture,
      Task<TResult> task)
  {
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
    while (!task.IsCompleted && DateTime.UtcNow < deadline)
    {
      fixture.DrainTasks();
      await Task.Delay(10, TestContext.Current.CancellationToken);
    }
  }
}
