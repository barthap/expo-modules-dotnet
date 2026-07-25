using Expo.JSI;
using Expo.ModulesCore.Testing.Internal;
using System.Runtime.ExceptionServices;

namespace Expo.ModulesCore.Testing;

public sealed class ExpoModuleTestHost : IDisposable
{
  private static readonly TimeSpan DefaultPromiseTimeout = TimeSpan.FromSeconds(5);

  private readonly object gate = new();
  private readonly HashSet<PromiseEvaluationState> pendingPromiseEvaluations = [];
  private HermesTestRuntime? testRuntime;
  private DotnetRuntimeContext? context;
  private bool disposed;

  private ExpoModuleTestHost(HermesTestRuntime testRuntime, DotnetRuntimeContext context)
  {
    this.testRuntime = testRuntime;
    this.context = context;
  }

  public HermesTestRuntime TestRuntime => GetLiveTestRuntime();

  public JavaScriptRuntime Runtime => GetLiveTestRuntime().Runtime;

  internal int ActivePromiseEvaluationCount
  {
    get
    {
      lock (gate)
      {
        return pendingPromiseEvaluations.Count;
      }
    }
  }

  /// <summary>
  /// Creates a host whose runtime context has no app-scoped directories.
  /// </summary>
  /// <param name="register">Callback that registers modules on the created context.</param>
  public static ExpoModuleTestHost Create(
      Action<DotnetRuntimeContext, JavaScriptObject> register
  ) => Create(AppDirectories.Unconfigured, register);

  /// <summary>
  /// Creates a host whose runtime context carries the given app-scoped directories.
  /// </summary>
  /// <remarks>
  /// The directories are passed through unchanged. This host does not create,
  /// clean, or otherwise manage the lifetime of any directory, so a test that needs
  /// real files on disk owns that fixture itself.
  /// </remarks>
  /// <param name="directories">Directories to expose on the created context.</param>
  /// <param name="register">Callback that registers modules on the created context.</param>
  public static ExpoModuleTestHost Create(
      AppDirectories directories,
      Action<DotnetRuntimeContext, JavaScriptObject> register
  )
  {
    ArgumentNullException.ThrowIfNull(directories);
    ArgumentNullException.ThrowIfNull(register);
    HermesTestRuntime? testRuntime = null;
    try
    {
      testRuntime = HermesTestRuntime.Create();
      var context = testRuntime.Runtime.Execute(runtime =>
      {
        var created = new DotnetRuntimeContext(runtime, directories);
        try
        {
          using var modules = created.ModuleRegistry.GetOrCreateDotnetModulesObject();
          register(created, modules);
          return created;
        }
        catch (Exception registrationException)
        {
          try
          {
            created.Dispose();
          }
          catch (Exception cleanupException)
          {
            throw new AggregateException(registrationException, cleanupException);
          }

          ExceptionDispatchInfo.Capture(registrationException).Throw();
          throw new System.Diagnostics.UnreachableException();
        }
      });
      return new ExpoModuleTestHost(testRuntime, context);
    }
    catch
    {
      testRuntime?.Dispose();
      throw;
    }
  }

  public JavaScriptValue Evaluate(
      string source,
      string sourceUrl = "expo-module-test.js"
  ) => GetLiveTestRuntime().Evaluate(source, sourceUrl);

  public Task<JavaScriptValue> EvaluatePromiseAsync(
      string expression,
      CancellationToken cancellationToken = default
  ) => EvaluatePromiseAsync(expression, DefaultPromiseTimeout, cancellationToken);

  public async Task<JavaScriptValue> EvaluatePromiseAsync(
      string expression,
      TimeSpan timeout,
      CancellationToken cancellationToken = default
  )
  {
    ArgumentNullException.ThrowIfNull(expression);
    if (timeout <= TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    var state = new PromiseEvaluationState();
    HermesTestRuntime activeTestRuntime;
    lock (gate)
    {
      ObjectDisposedException.ThrowIf(disposed, nameof(ExpoModuleTestHost));
      cancellationToken.ThrowIfCancellationRequested();
      activeTestRuntime = testRuntime!;
      pendingPromiseEvaluations.Add(state);
    }

    try
    {
      try
      {
        activeTestRuntime.Runtime.Execute(runtime =>
        {
          using var promise = activeTestRuntime.Evaluate(expression);
          if (!promise.IsPromise)
          {
            throw new InvalidOperationException("The evaluated expression must return a Promise.");
          }

          using var promiseObject = promise.AsObject();
          using var thenValue = promiseObject.GetProperty("then");
          using var then = thenValue.AsFunction();
          using var onFulfilled = runtime.CreateHostFunction(
              "resolvePromise",
              1,
              ResolvePromise,
              state
          );
          using var onRejected = runtime.CreateHostFunction(
              "rejectPromise",
              1,
              RejectPromise,
              state
          );
          using var chainedPromise = then.CallWithThis(promiseObject, onFulfilled, onRejected);
          return true;
        });
      }
      catch
      {
        AbandonOnRuntime(activeTestRuntime, state);
        throw;
      }

      try
      {
        await state.Signal.Task.WaitAsync(timeout, cancellationToken);
      }
      catch (TimeoutException)
      {
        AbandonOnRuntime(activeTestRuntime, state);
        throw;
      }
      catch (OperationCanceledException)
      {
        AbandonOnRuntime(activeTestRuntime, state);
        throw;
      }

      return state.TakeOutcome();
    }
    finally
    {
      lock (gate)
      {
        pendingPromiseEvaluations.Remove(state);
      }
    }
  }

  public void Dispose()
  {
    DotnetRuntimeContext? ownedContext;
    HermesTestRuntime? ownedTestRuntime;
    PromiseEvaluationState[] pendingEvaluations;

    lock (gate)
    {
      if (disposed)
      {
        return;
      }

      disposed = true;
      ownedContext = context;
      context = null;
      ownedTestRuntime = testRuntime;
      testRuntime = null;
      pendingEvaluations = [.. pendingPromiseEvaluations];
      pendingPromiseEvaluations.Clear();
    }

    Exception? contextException = null;
    if (ownedContext is not null && ownedTestRuntime is not null)
    {
      try
      {
        ownedTestRuntime.Runtime.Execute(_ =>
        {
          foreach (var evaluation in pendingEvaluations)
          {
            evaluation.FailFromHostDisposal();
          }
          ownedContext.Dispose();
          return true;
        });
      }
      catch (Exception exception)
      {
        contextException = exception;
      }
    }

    Exception? runtimeException = null;
    try
    {
      ownedTestRuntime?.Dispose();
    }
    catch (Exception exception)
    {
      runtimeException = exception;
    }

    if (contextException is not null && runtimeException is not null)
    {
      throw new AggregateException(contextException, runtimeException);
    }
    if (contextException is not null)
    {
      ExceptionDispatchInfo.Capture(contextException).Throw();
    }
    if (runtimeException is not null)
    {
      ExceptionDispatchInfo.Capture(runtimeException).Throw();
    }
  }

  private HermesTestRuntime GetLiveTestRuntime()
  {
    lock (gate)
    {
      ObjectDisposedException.ThrowIf(disposed, nameof(ExpoModuleTestHost));
      return testRuntime!;
    }
  }

  private static JavaScriptValue ResolvePromise(
      JavaScriptRuntime runtime,
      JavaScriptValueRef _,
      JavaScriptArguments arguments,
      object stateObject
  )
  {
    var state = (PromiseEvaluationState)stateObject;
    var value = arguments.Count == 0
        ? runtime.CreateUndefined()
        : arguments.GetValue(0).Retain();
    state.TryResolve(value);
    return runtime.CreateUndefined();
  }

  private static JavaScriptValue RejectPromise(
      JavaScriptRuntime runtime,
      JavaScriptValueRef _,
      JavaScriptArguments arguments,
      object stateObject
  )
  {
    JavaScriptPromiseRejectedException rejection;
    try
    {
      rejection = arguments.Count == 0
          ? new JavaScriptPromiseRejectedException("undefined", null, null)
          : CreateRejectionException(arguments.GetValue(0));
    }
    catch (Exception exception)
    {
      rejection = new JavaScriptPromiseRejectedException(
          $"Failed to extract JavaScript Promise rejection: {exception.Message}",
          null,
          null
      );
    }
    ((PromiseEvaluationState)stateObject).TryReject(rejection);
    return runtime.CreateUndefined();
  }

  private static JavaScriptPromiseRejectedException CreateRejectionException(
      JavaScriptValueRef rejection
  )
  {
    using var retainedRejection = rejection.Retain();
    if (!retainedRejection.IsError)
    {
      return new JavaScriptPromiseRejectedException(
          rejection.CoerceToString(),
          null,
          null
      );
    }

    using var error = retainedRejection.AsErrorObject();
    return new JavaScriptPromiseRejectedException(
        ExtractErrorField(() => error.Message, "message") ?? string.Empty,
        ExtractErrorField(() => error.Name, "name"),
        ExtractErrorField(() => error.Stack, "stack")
    );
  }

  private static string? ExtractErrorField(Func<string?> extract, string fieldName)
  {
    try
    {
      return extract();
    }
    catch (Exception exception)
    {
      return $"Failed to extract JavaScript Promise rejection: {fieldName}: {exception.Message}";
    }
  }

  private static void AbandonOnRuntime(
      HermesTestRuntime activeTestRuntime,
      PromiseEvaluationState state
  )
  {
    try
    {
      activeTestRuntime.Runtime.Execute(_ =>
      {
        state.Abandon();
        return true;
      });
    }
    catch (ObjectDisposedException)
    {
      // Host disposal has already terminalized this state before releasing the runtime.
    }
  }
}
