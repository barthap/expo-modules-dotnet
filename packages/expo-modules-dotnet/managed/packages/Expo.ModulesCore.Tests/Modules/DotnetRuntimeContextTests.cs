using Expo.JSI;
using Expo.ModulesCore.Codecs;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Modules;

public sealed class DotnetRuntimeContextTests
{
  [Fact]
  public void ContextBackedFunctionFailsAfterTeardown()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      RegisterLifecycleModule(context, modules, out _);

      using var beforeTeardown = fixture.Evaluate(
          "globalThis._expoDotnet.modules.Lifecycle.value()",
          "runtime-context-before-teardown.js"
      );
      Assert.Equal(42.0, beforeTeardown.AsDouble());

      context.Dispose();

      using var afterTeardown = fixture.Evaluate(
          "try { globalThis._expoDotnet.modules.Lifecycle.value(); 'no error'; } catch (e) { e.message; }",
          "runtime-context-after-teardown.js"
      );
      Assert.Equal(JavaScriptValueKind.String, afterTeardown.Kind);
      Assert.Contains("DotnetRuntimeContext", afterTeardown.AsString());

      return true;
    });
  }

  [Fact]
  public void ContextTeardownReleasesModuleInstances()
  {
    using var fixture = HermesRuntimeFixture.Create();
    WeakReference moduleReference = null!;

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      RegisterLifecycleModule(context, modules, out moduleReference);

      Assert.True(moduleReference.IsAlive);
      context.Dispose();
      return true;
    });

    CollectGarbage();
    Assert.False(moduleReference.IsAlive);
  }

  [Fact]
  public void ContextOwnedModuleRegistryReusesModuleInstance()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      var createCount = 0;

      var first = context.ModuleRegistry.GetOrCreateModule(
          "Reusable",
          () =>
          {
            createCount++;
            return new LifecycleModule();
          }
      );
      var second = context.ModuleRegistry.GetOrCreateModule(
          "Reusable",
          () =>
          {
            createCount++;
            return new LifecycleModule();
          }
      );

      Assert.Same(first, second);
      Assert.Equal(1, createCount);
      return true;
    });
  }

  [Fact]
  public void ModuleRegistryRunsCreateHookOnceForNewInstance()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      var createCount = 0;

      var first = context.ModuleRegistry.GetOrCreateModule(
          "Lifecycle",
          static () => new LifecycleModule(),
          _ => createCount++,
          null
      );
      var second = context.ModuleRegistry.GetOrCreateModule(
          "Lifecycle",
          static () => new LifecycleModule(),
          _ => createCount++,
          null
      );

      Assert.Same(first, second);
      Assert.Equal(1, createCount);
      return true;
    });
  }

  [Fact]
  public void ModuleRegistryRunsLifecycleHooksPerRuntimeContext()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var firstContext = new DotnetRuntimeContext(runtime);
      using var secondContext = new DotnetRuntimeContext(runtime);
      var createCount = 0;

      firstContext.ModuleRegistry.GetOrCreateModule(
          "Lifecycle",
          static () => new LifecycleModule(),
          _ => createCount++,
          null
      );
      secondContext.ModuleRegistry.GetOrCreateModule(
          "Lifecycle",
          static () => new LifecycleModule(),
          _ => createCount++,
          null
      );

      Assert.Equal(2, createCount);
      return true;
    });
  }

  [Fact]
  public void ModuleRegistryRunsDestroyHookBeforeDisposableCleanup()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      var calls = new List<string>();
      var context = new DotnetRuntimeContext(runtime);
      context.ModuleRegistry.GetOrCreateModule(
          "Lifecycle",
          () => new DisposableLifecycleModule(calls),
          null,
          module => module.Destroy()
      );

      context.Dispose();

      Assert.Equal(new[] { "destroy", "dispose" }, calls);
      return true;
    });
  }

  [Fact]
  public void ModuleRegistryAggregatesCleanupFailuresAfterRunningAllCleanup()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      var calls = new List<string>();
      var context = new DotnetRuntimeContext(runtime);
      context.ModuleRegistry.GetOrCreateModule(
          "First",
          () => new FailingLifecycleModule(calls, "first"),
          null,
          module => module.Destroy()
      );
      context.ModuleRegistry.GetOrCreateModule(
          "Second",
          () => new FailingLifecycleModule(calls, "second"),
          null,
          module => module.Destroy()
      );

      var exception = Assert.Throws<AggregateException>(() => context.Dispose());

      Assert.Equal(
          new[]
          {
              "first:destroy",
              "first:dispose",
              "second:destroy",
              "second:dispose",
          },
          calls
      );
      Assert.Equal(4, exception.InnerExceptions.Count);
      Assert.Contains(exception.InnerExceptions, item => item.Message == "first destroy failed");
      Assert.Contains(exception.InnerExceptions, item => item.Message == "first dispose failed");
      Assert.Contains(exception.InnerExceptions, item => item.Message == "second destroy failed");
      Assert.Contains(exception.InnerExceptions, item => item.Message == "second dispose failed");
      return true;
    });
  }

  [Fact]
  public void ContextDisposeReleasesEventStateWhenModuleCleanupFails()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      using var eventsModule = context.ModuleRegistry.DefineNativeModule(modules, "Events");
      context.ModuleRegistry.GetOrCreateModule(
          "Failing",
          () => new FailingLifecycleModule([], "failing"),
          null,
          module => module.Destroy()
      );

      Assert.Throws<AggregateException>(() => context.Dispose());

      using var result = fixture.Evaluate(
          "const events = globalThis._expoDotnet.modules.Events;" +
          "try { events.addListener('onChange', () => {}); 'no error'; } catch (error) { error.message; }",
          "event-emitter-after-failed-module-cleanup.js"
      );

      Assert.Contains("EventEmitterRuntimeState", result.AsString());
      return true;
    });
  }

  [Fact]
  public void ContextOwnedModuleRegistryDoesNotShareInstancesAcrossContexts()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var firstContext = new DotnetRuntimeContext(runtime);
      using var secondContext = new DotnetRuntimeContext(runtime);

      var first = firstContext.ModuleRegistry.GetOrCreateModule(
          "Scoped",
          static () => new LifecycleModule()
      );
      var second = secondContext.ModuleRegistry.GetOrCreateModule(
          "Scoped",
          static () => new LifecycleModule()
      );

      Assert.NotSame(first, second);
      return true;
    });
  }

  [Fact]
  public void ModuleBaseStoresRuntimeContext()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      var module = context.ModuleRegistry.GetOrCreateModule(
          "RuntimeAware",
          () => new RuntimeAwareModule(context)
      );

      Assert.Same(context, module.Context);
      return true;
    });
  }

  [Fact]
  public void CachedModuleRegistryFailsAfterContextTeardown()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);
      var registry = context.ModuleRegistry;

      context.Dispose();

      Assert.Throws<ObjectDisposedException>(() => registry.GetOrCreateDotnetModulesObject());
      return true;
    });
  }

  [Fact]
  public void DefaultAppDirectoriesLeaveBothValuesUnconfigured()
  {
    var directories = new AppDirectories();

    // Both parameters default to "the host said nothing". A default that invented a
    // path would reintroduce the user-wide directory collision this model exists to
    // remove, so the parameterless shape must stay empty.
    Assert.Null(directories.CacheDirectory);
    Assert.Null(directories.PersistentFilesDirectory);
    Assert.Null(AppDirectories.Unconfigured.CacheDirectory);
    Assert.Null(AppDirectories.Unconfigured.PersistentFilesDirectory);
  }

  [Fact]
  public void AppDirectoriesRetainSuppliedPathsVerbatim()
  {
    var cache = TestDirectory("cache");
    var persistent = TestDirectory("files");

    var directories = new AppDirectories(cache, persistent);

    // Reference equality proves the record stored exactly what the host handed it.
    // Any rewrite would mean calling a normalizing path helper, and every one of
    // those resolves against process state the host never asked about.
    Assert.Same(cache, directories.CacheDirectory);
    Assert.Same(persistent, directories.PersistentFilesDirectory);
  }

  [Fact]
  public void AppDirectoriesDoNotCanonicalizeFullyQualifiedPaths()
  {
    // A ".." segment survives because collapsing it needs Path.GetFullPath, which
    // the core is not allowed to call. The host owns the exact path; the core only
    // checks that it is usable as one.
    var raw = Path.Combine(TestDirectory("cache"), "..", "cache");

    Assert.Equal(raw, new AppDirectories(raw).CacheDirectory);
    Assert.Equal(raw, new AppDirectories(persistentFilesDirectory: raw).PersistentFilesDirectory);
  }

  [Theory]
  [InlineData(InvalidPathKind.Empty)]
  [InlineData(InvalidPathKind.Whitespace)]
  [InlineData(InvalidPathKind.ContainsNul)]
  [InlineData(InvalidPathKind.Relative)]
  public void AppDirectoriesRejectUnusableCacheDirectory(InvalidPathKind kind)
  {
    var invalid = InvalidPath(kind);

    // The other directory is valid, so only the rejected one can explain the
    // failure. Naming the parameter is what tells a host adapter which of its two
    // values it mis-supplied.
    var exception = Assert.Throws<ArgumentException>(
        () => new AppDirectories(invalid, TestDirectory("files"))
    );

    Assert.Equal("cacheDirectory", exception.ParamName);
  }

  [Theory]
  [InlineData(InvalidPathKind.Empty)]
  [InlineData(InvalidPathKind.Whitespace)]
  [InlineData(InvalidPathKind.ContainsNul)]
  [InlineData(InvalidPathKind.Relative)]
  public void AppDirectoriesRejectUnusablePersistentFilesDirectory(InvalidPathKind kind)
  {
    var invalid = InvalidPath(kind);

    var exception = Assert.Throws<ArgumentException>(
        () => new AppDirectories(TestDirectory("cache"), invalid)
    );

    Assert.Equal("persistentFilesDirectory", exception.ParamName);
  }

  [Fact]
  public void UnconfiguredContextDirectoriesThrowFromBothConstructors()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var implicitlyUnconfigured = new DotnetRuntimeContext(runtime);
      using var explicitlyUnconfigured = new DotnetRuntimeContext(
          runtime,
          AppDirectories.Unconfigured
      );

      // There is no portable fallback: every managed path API is user-wide or
      // process-wide, which is the defect being fixed. A module that asks for a
      // directory the host never supplied must fail loudly instead of writing
      // somewhere a second app on the same machine also writes.
      Assert.Throws<AppDirectoryNotConfiguredException>(
          () => _ = implicitlyUnconfigured.CacheDirectory
      );
      Assert.Throws<AppDirectoryNotConfiguredException>(
          () => _ = implicitlyUnconfigured.PersistentFilesDirectory
      );
      Assert.Throws<AppDirectoryNotConfiguredException>(
          () => _ = explicitlyUnconfigured.CacheDirectory
      );
      Assert.Throws<AppDirectoryNotConfiguredException>(
          () => _ = explicitlyUnconfigured.PersistentFilesDirectory
      );
      return true;
    });
  }

  [Fact]
  public void ContextExposesCacheDirectoryWhilePersistentFilesStaysUnconfigured()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var cache = TestDirectory("cache");

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime, new AppDirectories(cacheDirectory: cache));

      // The two directories are independent. A host adapter that only knows a cache
      // path must not be pushed into fabricating a persistent one to satisfy the
      // model.
      Assert.Equal(cache, context.CacheDirectory);
      Assert.Throws<AppDirectoryNotConfiguredException>(
          () => _ = context.PersistentFilesDirectory
      );
      return true;
    });
  }

  [Fact]
  public void ContextExposesPersistentFilesDirectoryWhileCacheStaysUnconfigured()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var persistent = TestDirectory("files");

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(
          runtime,
          new AppDirectories(persistentFilesDirectory: persistent)
      );

      Assert.Equal(persistent, context.PersistentFilesDirectory);
      Assert.Throws<AppDirectoryNotConfiguredException>(() => _ = context.CacheDirectory);
      return true;
    });
  }

  [Fact]
  public void ContextRejectsNullAppDirectories()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      // "Unconfigured" is a value a caller must state on purpose. Accepting null
      // would give the same meaning a second, undocumented spelling.
      var exception = Assert.Throws<ArgumentNullException>(
          () => new DotnetRuntimeContext(runtime, null!)
      );

      Assert.Equal("directories", exception.ParamName);
      return true;
    });
  }

  [Fact]
  public void DisposedContextReportsDisposalBeforeMissingConfiguration()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      var unconfigured = new DotnetRuntimeContext(runtime);
      var configured = new DotnetRuntimeContext(
          runtime,
          new AppDirectories(TestDirectory("cache"), TestDirectory("files"))
      );

      unconfigured.Dispose();
      configured.Dispose();

      // Lifecycle outranks configuration. A caller holding a stale context must be
      // told the context is gone, not sent off to fix its host's directory setup.
      Assert.Throws<ObjectDisposedException>(() => _ = unconfigured.CacheDirectory);
      Assert.Throws<ObjectDisposedException>(() => _ = unconfigured.PersistentFilesDirectory);
      Assert.Throws<ObjectDisposedException>(() => _ = configured.CacheDirectory);
      Assert.Throws<ObjectDisposedException>(() => _ = configured.PersistentFilesDirectory);
      return true;
    });
  }

  [Fact]
  public void AppDirectoryNotConfiguredExceptionIdentifiesTheMissingAccessor()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var context = new DotnetRuntimeContext(runtime);

      var cacheException = Assert.Throws<AppDirectoryNotConfiguredException>(
          () => _ = context.CacheDirectory
      );
      var persistentException = Assert.Throws<AppDirectoryNotConfiguredException>(
          () => _ = context.PersistentFilesDirectory
      );

      // A host adapter configures the two directories separately, so a shared
      // message would not say which one to supply.
      Assert.Equal(nameof(DotnetRuntimeContext.CacheDirectory), cacheException.DirectoryProperty);
      Assert.Equal(
          nameof(DotnetRuntimeContext.PersistentFilesDirectory),
          persistentException.DirectoryProperty
      );
      Assert.Contains(nameof(DotnetRuntimeContext.CacheDirectory), cacheException.Message);
      Assert.DoesNotContain(
          nameof(DotnetRuntimeContext.PersistentFilesDirectory),
          cacheException.Message
      );
      Assert.Contains(
          nameof(DotnetRuntimeContext.PersistentFilesDirectory),
          persistentException.Message
      );
      return true;
    });
  }

  public enum InvalidPathKind
  {
    Empty,
    Whitespace,
    ContainsNul,
    Relative,
  }

  private static string InvalidPath(InvalidPathKind kind) => kind switch
  {
    InvalidPathKind.Empty => string.Empty,
    InvalidPathKind.Whitespace => "   ",
    // Fully qualified apart from the NUL byte, so only the NUL check can reject it.
    InvalidPathKind.ContainsNul => TestDirectory("ca\0che"),
    // Relative on every supported OS: no leading separator and no drive.
    InvalidPathKind.Relative => Path.Combine("expo-dotnet-tests", "cache"),
    _ => throw new ArgumentOutOfRangeException(nameof(kind)),
  };

  private static string TestDirectory(string leaf) => Path.Combine(
      Path.GetPathRoot(Environment.CurrentDirectory)!,
      "expo-dotnet-tests",
      leaf
  );

  private static void RegisterLifecycleModule(
      DotnetRuntimeContext context,
      JavaScriptObject modules,
      out WeakReference moduleReference
  )
  {
    using var module = context.ModuleRegistry.DefineModule(modules, "Lifecycle");
    var lifecycleModule = new LifecycleModule();
    moduleReference = new WeakReference(lifecycleModule);
    GeneratedFunction.DefineSync(
        context,
        module,
        "value",
        0,
        LifecycleValueHostFunction,
        lifecycleModule
    );
  }

  private static JavaScriptValue LifecycleValueHostFunction(
      JavaScriptRuntime runtime,
      JavaScriptValueRef thisValue,
      JavaScriptArguments arguments,
      object context
  )
  {
    GeneratedFunction.RequireArgumentCount("Lifecycle.value", arguments, 0);
    var module = (LifecycleModule)context;
    return DoubleCodec.Encode(module.Value(), runtime);
  }

  private static void CollectGarbage()
  {
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
  }

  private sealed class LifecycleModule
  {
    public double Value() => 42.0;
  }

  private sealed class DisposableLifecycleModule(List<string> calls) : IDisposable
  {
    public void Destroy()
    {
      calls.Add("destroy");
    }

    public void Dispose()
    {
      calls.Add("dispose");
    }
  }

  private sealed class FailingLifecycleModule(List<string> calls, string name) : IDisposable
  {
    public void Destroy()
    {
      calls.Add($"{name}:destroy");
      throw new InvalidOperationException($"{name} destroy failed");
    }

    public void Dispose()
    {
      calls.Add($"{name}:dispose");
      throw new InvalidOperationException($"{name} dispose failed");
    }
  }

  private sealed class RuntimeAwareModule : Module
  {
    public RuntimeAwareModule(DotnetRuntimeContext context)
        : base(context)
    {
    }

    public DotnetRuntimeContext Context => RuntimeContext;
  }
}
