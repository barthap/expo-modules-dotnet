using Expo.JSI;
using System.Runtime.ExceptionServices;

namespace Expo.ModulesCore.Testing;

public sealed class ExpoModuleTestHost : IDisposable
{
  private readonly object gate = new();
  private readonly HashSet<object> pendingPromiseEvaluations = [];
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

  public static ExpoModuleTestHost Create(
      Action<DotnetRuntimeContext, JavaScriptObject> register
  )
  {
    ArgumentNullException.ThrowIfNull(register);
    HermesTestRuntime? testRuntime = null;
    try
    {
      testRuntime = HermesTestRuntime.Create();
      var context = testRuntime.Runtime.Execute(runtime =>
      {
        var created = new DotnetRuntimeContext(runtime);
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

  public void Dispose()
  {
    DotnetRuntimeContext? ownedContext;
    HermesTestRuntime? ownedTestRuntime;

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
    }

    Exception? contextException = null;
    if (ownedContext is not null && ownedTestRuntime is not null)
    {
      try
      {
        ownedTestRuntime.Runtime.Execute(_ =>
        {
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
}
