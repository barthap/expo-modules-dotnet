namespace Expo.JSI;

/// <summary>
/// Describes how to settle a managed-created JavaScript promise.
/// </summary>
/// <remarks>
/// The factory callback is invoked later on the JavaScript runtime. It must return a newly owned
/// <see cref="JavaScriptValue" />; the promise scheduler disposes that value after using it.
/// </remarks>
public readonly struct JavaScriptPromiseResult
{
  private readonly Func<JavaScriptRuntime, JavaScriptValue> createValue;
  private readonly IOwnedResultState? ownedState;

  private JavaScriptPromiseResult(
      bool isRejected,
      Func<JavaScriptRuntime, JavaScriptValue> createValue,
      IOwnedResultState? ownedState = null
  )
  {
    IsRejected = isRejected;
    this.createValue = createValue;
    this.ownedState = ownedState;
  }

  internal bool IsRejected { get; }

  /// <summary>
  /// Creates a result that resolves a JavaScript promise.
  /// </summary>
  /// <param name="createValue">
  /// Factory that creates an owned resolution value on the JavaScript runtime.
  /// </param>
  public static JavaScriptPromiseResult Resolve(
      Func<JavaScriptRuntime, JavaScriptValue> createValue
  )
  {
    ArgumentNullException.ThrowIfNull(createValue);
    return new JavaScriptPromiseResult(isRejected: false, createValue);
  }

  /// <summary>
  /// Creates a result that rejects a JavaScript promise.
  /// </summary>
  /// <param name="createReason">
  /// Factory that creates an owned rejection reason on the JavaScript runtime.
  /// </param>
  public static JavaScriptPromiseResult Reject(
      Func<JavaScriptRuntime, JavaScriptValue> createReason
  )
  {
    ArgumentNullException.ThrowIfNull(createReason);
    return new JavaScriptPromiseResult(isRejected: true, createReason);
  }

  /// <summary>
  /// Creates an owned result whose state is disposed if settlement work is abandoned before it
  /// reaches the JavaScript runtime.
  /// </summary>
  public static JavaScriptPromiseResult ResolveOwned<TState>(
      TState state,
      Func<JavaScriptRuntime, TState, JavaScriptValue> createValue,
      Action<TState> abandon
  ) where TState : class
  {
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(createValue);
    ArgumentNullException.ThrowIfNull(abandon);
    var owned = new OwnedResultState<TState>(state, createValue, abandon);
    return new JavaScriptPromiseResult(
        isRejected: false,
        runtime => owned.CreateValue(runtime),
        owned
    );
  }

  internal JavaScriptValue CreateValue(JavaScriptRuntime runtime)
  {
    if (ownedState is not null)
    {
      return ownedState.CreateValue(runtime);
    }
    if (createValue is null)
    {
      throw new InvalidOperationException("Promise result was not initialized.");
    }
    return createValue(runtime);
  }

  internal void Abandon() => ownedState?.Abandon();

  private interface IOwnedResultState
  {
    JavaScriptValue CreateValue(JavaScriptRuntime runtime);
    void Abandon();
  }

  private sealed class OwnedResultState<TState> : IOwnedResultState
      where TState : class
  {
    private TState? state;
    private readonly Func<JavaScriptRuntime, TState, JavaScriptValue> createValue;
    private readonly Action<TState> abandon;

    public OwnedResultState(
        TState state,
        Func<JavaScriptRuntime, TState, JavaScriptValue> createValue,
        Action<TState> abandon)
    {
      this.state = state;
      this.createValue = createValue;
      this.abandon = abandon;
    }

    public JavaScriptValue CreateValue(JavaScriptRuntime runtime)
    {
      var claimed = Interlocked.Exchange(ref state, null);
      if (claimed is null)
      {
        throw new InvalidOperationException("Owned promise result was already claimed or abandoned.");
      }
      return createValue(runtime, claimed);
    }

    public void Abandon()
    {
      var abandoned = Interlocked.Exchange(ref state, null);
      if (abandoned is not null)
      {
        abandon(abandoned);
      }
    }
  }
}
