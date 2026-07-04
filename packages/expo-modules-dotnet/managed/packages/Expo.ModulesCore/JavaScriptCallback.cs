using Expo.JSI;

namespace Expo.ModulesCore;

internal interface IRuntimeContextRetainedCallback
{
  void DisposeFromRuntimeContext();
}

public sealed class JavaScriptCallback<TResult> : IDisposable, IRuntimeContextRetainedCallback
{
  private readonly DotnetRuntimeContext context;
  private readonly JavaScriptFunction function;
  private readonly Func<JavaScriptValue, JavaScriptRuntime, TResult> decodeResult;
  private bool disposed;
  private bool runtimeContextDisposed;

  private JavaScriptCallback(
      DotnetRuntimeContext context,
      JavaScriptFunction function,
      Func<JavaScriptValue, JavaScriptRuntime, TResult> decodeResult)
  {
    this.context = context;
    this.function = function;
    this.decodeResult = decodeResult;
  }

  public static JavaScriptCallback<TResult> FromFunction(
      DotnetRuntimeContext context,
      JavaScriptFunction function,
      Func<JavaScriptValue, JavaScriptRuntime, TResult> decodeResult)
  {
    ArgumentNullException.ThrowIfNull(function);
    using var functionValue = function.AsValue();
    return FromOwnedFunction(context, functionValue.AsFunction(), decodeResult);
  }

  internal static JavaScriptCallback<TResult> FromOwnedFunction(
      DotnetRuntimeContext context,
      JavaScriptFunction function,
      Func<JavaScriptValue, JavaScriptRuntime, TResult> decodeResult)
  {
    ArgumentNullException.ThrowIfNull(context);
    ArgumentNullException.ThrowIfNull(function);
    ArgumentNullException.ThrowIfNull(decodeResult);
    return context.RegisterRetainedCallback(new JavaScriptCallback<TResult>(
        context,
        function,
        decodeResult
    ));
  }

  public TResult Invoke()
  {
    ThrowIfDisposed();
    if (!context.Runtime.CanExecuteSync)
    {
      throw new NotSupportedException(
          "Synchronous JavaScript callback invocation is not supported by this host."
      );
    }

    return context.Runtime.Execute(runtime =>
    {
      ThrowIfDisposed();
      using var result = function.Call();
      return decodeResult(result, runtime);
    });
  }

  public Task<TResult> InvokeAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    return context.Runtime.ExecuteAsync(
        runtime =>
        {
          ThrowIfDisposed();
          using var result = function.Call();
          return decodeResult(result, runtime);
        },
        cancellationToken: cancellationToken
    );
  }

  public void Dispose()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    function.Dispose();
  }

  void IRuntimeContextRetainedCallback.DisposeFromRuntimeContext()
  {
    if (disposed)
    {
      return;
    }

    runtimeContextDisposed = true;
    disposed = true;
    function.Dispose();
  }

  private void ThrowIfDisposed()
  {
    if (runtimeContextDisposed)
    {
      throw new InvalidOperationException("The JavaScript runtime context has been disposed.");
    }

    ObjectDisposedException.ThrowIf(disposed, this);
  }
}

public sealed class JavaScriptCallback<T1, TResult> : IDisposable, IRuntimeContextRetainedCallback
{
  private readonly DotnetRuntimeContext context;
  private readonly JavaScriptFunction function;
  private readonly Func<T1, JavaScriptRuntime, JavaScriptValue> encodeArg1;
  private readonly Func<JavaScriptValue, JavaScriptRuntime, TResult> decodeResult;
  private bool disposed;
  private bool runtimeContextDisposed;

  private JavaScriptCallback(
      DotnetRuntimeContext context,
      JavaScriptFunction function,
      Func<T1, JavaScriptRuntime, JavaScriptValue> encodeArg1,
      Func<JavaScriptValue, JavaScriptRuntime, TResult> decodeResult)
  {
    this.context = context;
    this.function = function;
    this.encodeArg1 = encodeArg1;
    this.decodeResult = decodeResult;
  }

  public static JavaScriptCallback<T1, TResult> FromFunction(
      DotnetRuntimeContext context,
      JavaScriptFunction function,
      Func<T1, JavaScriptRuntime, JavaScriptValue> encodeArg1,
      Func<JavaScriptValue, JavaScriptRuntime, TResult> decodeResult)
  {
    ArgumentNullException.ThrowIfNull(function);
    using var functionValue = function.AsValue();
    return FromOwnedFunction(context, functionValue.AsFunction(), encodeArg1, decodeResult);
  }

  internal static JavaScriptCallback<T1, TResult> FromOwnedFunction(
      DotnetRuntimeContext context,
      JavaScriptFunction function,
      Func<T1, JavaScriptRuntime, JavaScriptValue> encodeArg1,
      Func<JavaScriptValue, JavaScriptRuntime, TResult> decodeResult)
  {
    ArgumentNullException.ThrowIfNull(context);
    ArgumentNullException.ThrowIfNull(function);
    ArgumentNullException.ThrowIfNull(encodeArg1);
    ArgumentNullException.ThrowIfNull(decodeResult);
    return context.RegisterRetainedCallback(new JavaScriptCallback<T1, TResult>(
        context,
        function,
        encodeArg1,
        decodeResult
    ));
  }

  public TResult Invoke(T1 arg1)
  {
    ThrowIfDisposed();
    if (!context.Runtime.CanExecuteSync)
    {
      throw new NotSupportedException(
          "Synchronous JavaScript callback invocation is not supported by this host."
      );
    }

    return context.Runtime.Execute(runtime => InvokeCore(arg1, runtime));
  }

  public Task<TResult> InvokeAsync(T1 arg1, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    return context.Runtime.ExecuteAsync(
        runtime => InvokeCore(arg1, runtime),
        cancellationToken: cancellationToken
    );
  }

  public void Dispose()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    function.Dispose();
  }

  void IRuntimeContextRetainedCallback.DisposeFromRuntimeContext()
  {
    if (disposed)
    {
      return;
    }

    runtimeContextDisposed = true;
    disposed = true;
    function.Dispose();
  }

  private TResult InvokeCore(T1 arg1, JavaScriptRuntime runtime)
  {
    ThrowIfDisposed();
    using var jsArg1 = encodeArg1(arg1, runtime);
    using var result = function.Call(jsArg1);
    return decodeResult(result, runtime);
  }

  private void ThrowIfDisposed()
  {
    if (runtimeContextDisposed)
    {
      throw new InvalidOperationException("The JavaScript runtime context has been disposed.");
    }

    ObjectDisposedException.ThrowIf(disposed, this);
  }
}

public sealed class JavaScriptCallback<T1, T2, TResult> : IDisposable, IRuntimeContextRetainedCallback
{
  private readonly DotnetRuntimeContext context;
  private readonly JavaScriptFunction function;
  private readonly Func<T1, JavaScriptRuntime, JavaScriptValue> encodeArg1;
  private readonly Func<T2, JavaScriptRuntime, JavaScriptValue> encodeArg2;
  private readonly Func<JavaScriptValue, JavaScriptRuntime, TResult> decodeResult;
  private bool disposed;
  private bool runtimeContextDisposed;

  private JavaScriptCallback(
      DotnetRuntimeContext context,
      JavaScriptFunction function,
      Func<T1, JavaScriptRuntime, JavaScriptValue> encodeArg1,
      Func<T2, JavaScriptRuntime, JavaScriptValue> encodeArg2,
      Func<JavaScriptValue, JavaScriptRuntime, TResult> decodeResult)
  {
    this.context = context;
    this.function = function;
    this.encodeArg1 = encodeArg1;
    this.encodeArg2 = encodeArg2;
    this.decodeResult = decodeResult;
  }

  public static JavaScriptCallback<T1, T2, TResult> FromFunction(
      DotnetRuntimeContext context,
      JavaScriptFunction function,
      Func<T1, JavaScriptRuntime, JavaScriptValue> encodeArg1,
      Func<T2, JavaScriptRuntime, JavaScriptValue> encodeArg2,
      Func<JavaScriptValue, JavaScriptRuntime, TResult> decodeResult)
  {
    ArgumentNullException.ThrowIfNull(function);
    using var functionValue = function.AsValue();
    return FromOwnedFunction(context, functionValue.AsFunction(), encodeArg1, encodeArg2, decodeResult);
  }

  internal static JavaScriptCallback<T1, T2, TResult> FromOwnedFunction(
      DotnetRuntimeContext context,
      JavaScriptFunction function,
      Func<T1, JavaScriptRuntime, JavaScriptValue> encodeArg1,
      Func<T2, JavaScriptRuntime, JavaScriptValue> encodeArg2,
      Func<JavaScriptValue, JavaScriptRuntime, TResult> decodeResult)
  {
    ArgumentNullException.ThrowIfNull(context);
    ArgumentNullException.ThrowIfNull(function);
    ArgumentNullException.ThrowIfNull(encodeArg1);
    ArgumentNullException.ThrowIfNull(encodeArg2);
    ArgumentNullException.ThrowIfNull(decodeResult);
    return context.RegisterRetainedCallback(new JavaScriptCallback<T1, T2, TResult>(
        context,
        function,
        encodeArg1,
        encodeArg2,
        decodeResult
    ));
  }

  public TResult Invoke(T1 arg1, T2 arg2)
  {
    ThrowIfDisposed();
    if (!context.Runtime.CanExecuteSync)
    {
      throw new NotSupportedException(
          "Synchronous JavaScript callback invocation is not supported by this host."
      );
    }

    return context.Runtime.Execute(runtime => InvokeCore(arg1, arg2, runtime));
  }

  public Task<TResult> InvokeAsync(
      T1 arg1,
      T2 arg2,
      CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    return context.Runtime.ExecuteAsync(
        runtime => InvokeCore(arg1, arg2, runtime),
        cancellationToken: cancellationToken
    );
  }

  public void Dispose()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    function.Dispose();
  }

  void IRuntimeContextRetainedCallback.DisposeFromRuntimeContext()
  {
    if (disposed)
    {
      return;
    }

    runtimeContextDisposed = true;
    disposed = true;
    function.Dispose();
  }

  private TResult InvokeCore(T1 arg1, T2 arg2, JavaScriptRuntime runtime)
  {
    ThrowIfDisposed();
    using var jsArg1 = encodeArg1(arg1, runtime);
    using var jsArg2 = encodeArg2(arg2, runtime);
    using var result = function.Call(jsArg1, jsArg2);
    return decodeResult(result, runtime);
  }

  private void ThrowIfDisposed()
  {
    if (runtimeContextDisposed)
    {
      throw new InvalidOperationException("The JavaScript runtime context has been disposed.");
    }

    ObjectDisposedException.ThrowIf(disposed, this);
  }
}
