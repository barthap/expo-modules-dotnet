using System;
using System.Threading;
using System.Threading.Tasks;
using Expo.JSI;
using Expo.ModulesCore;
using Expo.ModulesCore.Codecs;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Modules;

public sealed class GeneratedAsyncModuleTests
{
  [Fact]
  public async Task GeneratedAsyncTaskFunctionReturnsPromiseAndResolvesUndefined()
  {
    var outcome = await EvaluateAsyncFunctionOutcomeAsync(
        "globalThis._expoDotnet.modules.Async.complete()"
    );

    Assert.False(outcome.SyncThrow);
    Assert.True(outcome.IsPromise);
    Assert.Equal("fulfilled", outcome.Status);
    Assert.Equal("undefined", outcome.ValueKind);
  }

  [Fact]
  public async Task GeneratedAsyncTaskOfIntFunctionResolvesEncodedValue()
  {
    var outcome = await EvaluateAsyncFunctionOutcomeAsync(
        "globalThis._expoDotnet.modules.Async.getValue()"
    );

    Assert.False(outcome.SyncThrow);
    Assert.True(outcome.IsPromise);
    Assert.Equal("fulfilled", outcome.Status);
    Assert.Equal("number", outcome.ValueKind);
    Assert.Equal(42, outcome.Value);
  }

  [Fact]
  public async Task GeneratedAsyncArgumentCountFailureRejectsPromise()
  {
    var outcome = await EvaluateAsyncFunctionOutcomeAsync(
        "globalThis._expoDotnet.modules.Async.requiresInt()"
    );

    AssertPromiseRejected(outcome);
    Assert.Contains("expects 1 arguments", outcome.ErrorMessage);
  }

  [Fact]
  public async Task GeneratedAsyncCodecFailureRejectsPromise()
  {
    var outcome = await EvaluateAsyncFunctionOutcomeAsync(
        "globalThis._expoDotnet.modules.Async.requiresInt('not a number')"
    );

    AssertPromiseRejected(outcome);
    Assert.Contains("number", outcome.ErrorMessage, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task GeneratedAsyncAuthoredMethodThrowingBeforeTaskRejectsPromise()
  {
    var outcome = await EvaluateAsyncFunctionOutcomeAsync(
        "globalThis._expoDotnet.modules.Async.throwBeforeTask()"
    );

    AssertPromiseRejected(outcome);
    Assert.Equal("pre-task failure", outcome.ErrorMessage);
  }

  [Fact]
  public async Task GeneratedAsyncFaultedTaskRejectsPromise()
  {
    var outcome = await EvaluateAsyncFunctionOutcomeAsync(
        "globalThis._expoDotnet.modules.Async.faulted()"
    );

    AssertPromiseRejected(outcome);
    Assert.Equal("faulted task", outcome.ErrorMessage);
  }

  [Fact]
  public async Task GeneratedAsyncCanceledTaskRejectsPromise()
  {
    var outcome = await EvaluateAsyncFunctionOutcomeAsync(
        "globalThis._expoDotnet.modules.Async.canceled()"
    );

    AssertPromiseRejected(outcome);
    Assert.False(string.IsNullOrWhiteSpace(outcome.ErrorMessage));
  }

  private static async Task<PromiseOutcome> EvaluateAsyncFunctionOutcomeAsync(string expression)
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      GeneratedAsyncModuleProvider.Register(context, modules);

      using var setup = fixture.Evaluate(
          $$"""
          globalThis.__generatedAsyncOutcome = {
            SyncThrow: false,
            IsPromise: false,
            Status: 'pending',
            ValueKind: null,
            Value: null,
            ErrorName: null,
            ErrorMessage: null
          };

          try {
            const promise = {{expression}};
            globalThis.__generatedAsyncOutcome.IsPromise = promise instanceof Promise;
            promise.then(
              value => {
                globalThis.__generatedAsyncOutcome.Status = 'fulfilled';
                globalThis.__generatedAsyncOutcome.ValueKind =
                  value === undefined ? 'undefined' : typeof value;
                globalThis.__generatedAsyncOutcome.Value = value;
              },
              error => {
                globalThis.__generatedAsyncOutcome.Status = 'rejected';
                globalThis.__generatedAsyncOutcome.ErrorName = error && error.name;
                globalThis.__generatedAsyncOutcome.ErrorMessage =
                  error && error.message ? error.message : String(error);
              }
            );
          } catch (error) {
            globalThis.__generatedAsyncOutcome.SyncThrow = true;
            globalThis.__generatedAsyncOutcome.Status = 'sync-thrown';
            globalThis.__generatedAsyncOutcome.ErrorName = error && error.name;
            globalThis.__generatedAsyncOutcome.ErrorMessage =
              error && error.message ? error.message : String(error);
          }

          true;
          """,
          "generated-async-module-outcome.js"
      );
      return true;
    });

    await WaitForPromiseOutcomeAsync(fixture);

    return fixture.Runtime.Execute(_ =>
    {
      using var outcomeValue = fixture.Evaluate(
          "globalThis.__generatedAsyncOutcome",
          "generated-async-module-outcome-read.js"
      );
      using var outcome = outcomeValue.AsObject();
      return ReadPromiseOutcome(outcome);
    });
  }

  private static PromiseOutcome ReadPromiseOutcome(JavaScriptObject outcome)
  {
    using var syncThrow = outcome.GetProperty("SyncThrow");
    using var isPromise = outcome.GetProperty("IsPromise");
    using var status = outcome.GetProperty("Status");
    using var valueKind = outcome.GetProperty("ValueKind");
    using var value = outcome.GetProperty("Value");
    using var errorName = outcome.GetProperty("ErrorName");
    using var errorMessage = outcome.GetProperty("ErrorMessage");

    return new PromiseOutcome(
        syncThrow.AsBool(),
        isPromise.AsBool(),
        status.AsString(),
        IsNullish(valueKind) ? null : valueKind.AsString(),
        IsNullish(value) ? null : checked((int)value.AsDouble()),
        IsNullish(errorName) ? null : errorName.AsString(),
        IsNullish(errorMessage) ? null : errorMessage.AsString()
    );
  }

  private static bool IsNullish(JavaScriptValue value) => value.IsNullish;

  private static async Task WaitForPromiseOutcomeAsync(HermesRuntimeFixture fixture)
  {
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
    while (DateTime.UtcNow < deadline)
    {
      fixture.DrainTasks();
      var settled = fixture.Runtime.Execute(_ =>
      {
        using var value = fixture.Evaluate(
            "globalThis.__generatedAsyncOutcome.Status !== 'pending'",
            "generated-async-module-outcome-settled.js"
        );
        return value.AsBool();
      });

      if (settled)
      {
        return;
      }

      await Task.Delay(10, TestContext.Current.CancellationToken);
    }

    Assert.Fail("Timed out waiting for async module Promise outcome.");
  }

  private static void AssertPromiseRejected(PromiseOutcome outcome)
  {
    Assert.False(outcome.SyncThrow);
    Assert.True(outcome.IsPromise);
    Assert.Equal("rejected", outcome.Status);
    Assert.Equal("Error", outcome.ErrorName);
  }

  private sealed record PromiseOutcome(
      bool SyncThrow,
      bool IsPromise,
      string Status,
      string? ValueKind,
      int? Value,
      string? ErrorName,
      string? ErrorMessage
  );

  private sealed class AsyncModule
  {
    public async Task CompleteAsync()
    {
      await Task.Yield();
    }

    public async Task<int> GetValueAsync()
    {
      await Task.Yield();
      return 42;
    }

    public async Task<int> RequiresIntAsync(int value)
    {
      await Task.Yield();
      return value;
    }

    public Task ThrowBeforeTaskAsync() =>
        throw new InvalidOperationException("pre-task failure");

    public Task FaultedAsync() =>
        Task.FromException(new InvalidOperationException("faulted task"));

    public Task CanceledAsync() =>
        Task.FromCanceled(new CancellationToken(canceled: true));
  }

  private static class GeneratedAsyncModuleProvider
  {
    public static void Register(DotnetRuntimeContext context, JavaScriptObject modules)
    {
      using var asyncModule = context.ModuleRegistry.DefineModule(modules, "Async");
      var module = context.ModuleRegistry.GetOrCreateModule("Async", static () => new AsyncModule());

      GeneratedFunction.DefineAsync(
          context,
          asyncModule,
          "complete",
          0,
          CompleteHostFunction,
          module
      );
      GeneratedFunction.DefineAsync(
          context,
          asyncModule,
          "getValue",
          0,
          GetValueHostFunction,
          module
      );
      GeneratedFunction.DefineAsync(
          context,
          asyncModule,
          "requiresInt",
          1,
          RequiresIntHostFunction,
          module
      );
      GeneratedFunction.DefineAsync(
          context,
          asyncModule,
          "throwBeforeTask",
          0,
          ThrowBeforeTaskHostFunction,
          module
      );
      GeneratedFunction.DefineAsync(
          context,
          asyncModule,
          "faulted",
          0,
          FaultedHostFunction,
          module
      );
      GeneratedFunction.DefineAsync(
          context,
          asyncModule,
          "canceled",
          0,
          CanceledHostFunction,
          module
      );
    }

    private static JavaScriptValue CompleteHostFunction(
        JavaScriptRuntime jsRuntime,
        JavaScriptValueRef thisValue,
        JavaScriptArguments arguments,
        object context)
    {
      try
      {
        GeneratedFunction.RequireArgumentCount("Async.complete", arguments, 0);
        var module = (AsyncModule)context;
        var __expoTask = module.CompleteAsync();
        using var __expoPromiseValue = jsRuntime.CreatePromise(
            async _ =>
            {
              await __expoTask.ConfigureAwait(false);
              return JavaScriptPromiseResult.Resolve(static runtime => runtime.CreateUndefined());
            }
        );
        return __expoPromiseValue.AsValue();
      }
      catch (Exception exception)
      {
        return GeneratedFunction.CreateRejectedPromise(jsRuntime, exception);
      }
    }

    private static JavaScriptValue GetValueHostFunction(
        JavaScriptRuntime jsRuntime,
        JavaScriptValueRef thisValue,
        JavaScriptArguments arguments,
        object context)
    {
      try
      {
        GeneratedFunction.RequireArgumentCount("Async.getValue", arguments, 0);
        var module = (AsyncModule)context;
        var __expoTask = module.GetValueAsync();
        using var __expoPromiseValue = jsRuntime.CreatePromise(
            async _ =>
            {
              var __expoResult = await __expoTask.ConfigureAwait(false);
              return JavaScriptPromiseResult.Resolve(
                  runtime => NumberCodec<int>.Encode(__expoResult, runtime)
              );
            }
        );
        return __expoPromiseValue.AsValue();
      }
      catch (Exception exception)
      {
        return GeneratedFunction.CreateRejectedPromise(jsRuntime, exception);
      }
    }

    private static JavaScriptValue RequiresIntHostFunction(
        JavaScriptRuntime jsRuntime,
        JavaScriptValueRef thisValue,
        JavaScriptArguments arguments,
        object context)
    {
      try
      {
        GeneratedFunction.RequireArgumentCount("Async.requiresInt", arguments, 1);
        var module = (AsyncModule)context;
        var __expoArg0 = NumberCodec<int>.Decode(arguments.GetValue(0), jsRuntime);
        var __expoTask = module.RequiresIntAsync(__expoArg0);
        using var __expoPromiseValue = jsRuntime.CreatePromise(
            async _ =>
            {
              var __expoResult = await __expoTask.ConfigureAwait(false);
              return JavaScriptPromiseResult.Resolve(
                  runtime => NumberCodec<int>.Encode(__expoResult, runtime)
              );
            }
        );
        return __expoPromiseValue.AsValue();
      }
      catch (Exception exception)
      {
        return GeneratedFunction.CreateRejectedPromise(jsRuntime, exception);
      }
    }

    private static JavaScriptValue ThrowBeforeTaskHostFunction(
        JavaScriptRuntime jsRuntime,
        JavaScriptValueRef thisValue,
        JavaScriptArguments arguments,
        object context)
    {
      try
      {
        GeneratedFunction.RequireArgumentCount("Async.throwBeforeTask", arguments, 0);
        var module = (AsyncModule)context;
        var __expoTask = module.ThrowBeforeTaskAsync();
        using var __expoPromiseValue = jsRuntime.CreatePromise(
            async _ =>
            {
              await __expoTask.ConfigureAwait(false);
              return JavaScriptPromiseResult.Resolve(static runtime => runtime.CreateUndefined());
            }
        );
        return __expoPromiseValue.AsValue();
      }
      catch (Exception exception)
      {
        return GeneratedFunction.CreateRejectedPromise(jsRuntime, exception);
      }
    }

    private static JavaScriptValue FaultedHostFunction(
        JavaScriptRuntime jsRuntime,
        JavaScriptValueRef thisValue,
        JavaScriptArguments arguments,
        object context)
    {
      try
      {
        GeneratedFunction.RequireArgumentCount("Async.faulted", arguments, 0);
        var module = (AsyncModule)context;
        var __expoTask = module.FaultedAsync();
        using var __expoPromiseValue = jsRuntime.CreatePromise(
            async _ =>
            {
              await __expoTask.ConfigureAwait(false);
              return JavaScriptPromiseResult.Resolve(static runtime => runtime.CreateUndefined());
            }
        );
        return __expoPromiseValue.AsValue();
      }
      catch (Exception exception)
      {
        return GeneratedFunction.CreateRejectedPromise(jsRuntime, exception);
      }
    }

    private static JavaScriptValue CanceledHostFunction(
        JavaScriptRuntime jsRuntime,
        JavaScriptValueRef thisValue,
        JavaScriptArguments arguments,
        object context)
    {
      try
      {
        GeneratedFunction.RequireArgumentCount("Async.canceled", arguments, 0);
        var module = (AsyncModule)context;
        var __expoTask = module.CanceledAsync();
        using var __expoPromiseValue = jsRuntime.CreatePromise(
            async _ =>
            {
              await __expoTask.ConfigureAwait(false);
              return JavaScriptPromiseResult.Resolve(static runtime => runtime.CreateUndefined());
            }
        );
        return __expoPromiseValue.AsValue();
      }
      catch (Exception exception)
      {
        return GeneratedFunction.CreateRejectedPromise(jsRuntime, exception);
      }
    }
  }
}
