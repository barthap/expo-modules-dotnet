using Expo.JSI;

namespace Expo.ModulesCore;

internal interface IRuntimeContextRetainedCallback
{
  void DisposeFromRuntimeContext();
}

public sealed class JavaScriptCallback<TResult> : IDisposable
{
  private readonly JavaScriptCallback<ValueTuple, TResult> inner;

  private JavaScriptCallback(JavaScriptCallback<ValueTuple, TResult> inner)
  {
    this.inner = inner;
  }

  public static JavaScriptCallback<TResult> FromFunction(
      DotnetRuntimeContext context,
      JavaScriptFunction function,
      Func<JavaScriptValue, JavaScriptRuntime, TResult> decodeResult)
  {
    return new(JavaScriptCallback<ValueTuple, TResult>.FromFunction(
        context,
        function,
        static (_, _) => [],
        decodeResult
    ));
  }

  internal static JavaScriptCallback<TResult> FromOwnedFunction(
      DotnetRuntimeContext context,
      JavaScriptFunction function,
      Func<JavaScriptValue, JavaScriptRuntime, TResult> decodeResult)
  {
    return new(JavaScriptCallback<ValueTuple, TResult>.FromOwnedFunction(
        context,
        function,
        static (_, _) => [],
        decodeResult
    ));
  }

  public TResult Invoke() => inner.Invoke(default);

  public Task<TResult> InvokeAsync(CancellationToken cancellationToken = default) =>
      inner.InvokeAsync(default, cancellationToken);

  public void Dispose() => inner.Dispose();
}

public sealed class JavaScriptCallback<TArgs, TResult> : IDisposable, IRuntimeContextRetainedCallback
    where TArgs : struct
{
  private readonly DotnetRuntimeContext context;
  private readonly JavaScriptFunction function;
  private readonly Func<TArgs, JavaScriptRuntime, JavaScriptValue[]> encodeArgs;
  private readonly Func<JavaScriptValue, JavaScriptRuntime, TResult> decodeResult;
  private bool disposed;
  private bool runtimeContextDisposed;

  private JavaScriptCallback(
      DotnetRuntimeContext context,
      JavaScriptFunction function,
      Func<TArgs, JavaScriptRuntime, JavaScriptValue[]> encodeArgs,
      Func<JavaScriptValue, JavaScriptRuntime, TResult> decodeResult)
  {
    this.context = context;
    this.function = function;
    this.encodeArgs = encodeArgs;
    this.decodeResult = decodeResult;
  }

  public static JavaScriptCallback<TArgs, TResult> FromFunction(
      DotnetRuntimeContext context,
      JavaScriptFunction function,
      Func<TArgs, JavaScriptRuntime, JavaScriptValue[]> encodeArgs,
      Func<JavaScriptValue, JavaScriptRuntime, TResult> decodeResult)
  {
    ArgumentNullException.ThrowIfNull(function);
    using var functionValue = function.AsValue();
    return FromOwnedFunction(context, functionValue.AsFunction(), encodeArgs, decodeResult);
  }

  internal static JavaScriptCallback<TArgs, TResult> FromOwnedFunction(
      DotnetRuntimeContext context,
      JavaScriptFunction function,
      Func<TArgs, JavaScriptRuntime, JavaScriptValue[]> encodeArgs,
      Func<JavaScriptValue, JavaScriptRuntime, TResult> decodeResult)
  {
    ArgumentNullException.ThrowIfNull(context);
    ArgumentNullException.ThrowIfNull(function);
    ArgumentNullException.ThrowIfNull(encodeArgs);
    ArgumentNullException.ThrowIfNull(decodeResult);
    return context.RegisterRetainedCallback(new JavaScriptCallback<TArgs, TResult>(
        context,
        function,
        encodeArgs,
        decodeResult
    ));
  }

  public TResult Invoke(TArgs args)
  {
    ThrowIfDisposed();
    if (context.Runtime.HasExclusiveRuntimeAccess)
    {
      // Synchronous callback parameters are commonly invoked while a generated module host function
      // is still on the JS stack. In that case the retained JS function can be called directly:
      // scheduling a sync hop back onto the same runtime is unnecessary and can deadlock on React
      // Native's RuntimeScheduler-backed CallInvoker.
      return InvokeCore(args, context.Runtime);
    }

    if (!context.Runtime.CanExecuteSync)
    {
      throw new NotSupportedException(
          "Synchronous JavaScript callback invocation is not supported by this host."
      );
    }

    return context.Runtime.Execute(runtime => InvokeCore(args, runtime));
  }

  public Task<TResult> InvokeAsync(TArgs args, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    return context.Runtime.ExecuteAsync(
        runtime => InvokeCore(args, runtime),
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

  private TResult InvokeCore(TArgs args, JavaScriptRuntime runtime)
  {
    ThrowIfDisposed();
    var encodedArgs = encodeArgs(args, runtime);
    try
    {
      using var result = function.Call(encodedArgs);
      return decodeResult(result, runtime);
    }
    finally
    {
      foreach (var encodedArg in encodedArgs)
      {
        encodedArg.Dispose();
      }
    }
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
