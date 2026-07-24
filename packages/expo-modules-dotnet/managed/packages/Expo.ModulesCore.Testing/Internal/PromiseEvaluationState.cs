using Expo.JSI;

namespace Expo.ModulesCore.Testing.Internal;

internal sealed class PromiseEvaluationState
{
  private readonly object gate = new();
  private readonly TaskCompletionSource<bool> signal = new(
      TaskCreationOptions.RunContinuationsAsynchronously
  );
  private State state = State.Waiting;
  private JavaScriptValue? fulfillment;
  private Exception? rejection;

  public TaskCompletionSource<bool> Signal => signal;

  public void TryResolve(JavaScriptValue value)
  {
    ArgumentNullException.ThrowIfNull(value);

    var disposeValue = false;
    var shouldSignal = false;
    lock (gate)
    {
      if (state == State.Waiting)
      {
        fulfillment = value;
        state = State.Settled;
        shouldSignal = true;
      }
      else
      {
        disposeValue = true;
      }
    }

    if (disposeValue)
    {
      value.Dispose();
    }
    if (shouldSignal)
    {
      signal.TrySetResult(true);
    }
  }

  public void TryReject(Exception exception)
  {
    ArgumentNullException.ThrowIfNull(exception);

    var shouldSignal = false;
    lock (gate)
    {
      if (state == State.Waiting)
      {
        rejection = exception;
        state = State.Settled;
        shouldSignal = true;
      }
    }

    if (shouldSignal)
    {
      signal.TrySetResult(true);
    }
  }

  public JavaScriptValue TakeOutcome()
  {
    lock (gate)
    {
      if (state != State.Settled)
      {
        throw new InvalidOperationException("Promise evaluation did not settle.");
      }

      state = State.Transferred;
      if (fulfillment is not null)
      {
        var result = fulfillment;
        fulfillment = null;
        return result;
      }

      throw rejection ?? new InvalidOperationException("Promise evaluation settled without an outcome.");
    }
  }

  public void Abandon()
  {
    JavaScriptValue? value = null;
    lock (gate)
    {
      if (state is State.Waiting or State.Settled)
      {
        state = State.Abandoned;
        value = fulfillment;
        fulfillment = null;
      }
    }

    value?.Dispose();
  }

  public void FailFromHostDisposal()
  {
    JavaScriptValue? value = null;
    var shouldSignal = false;
    lock (gate)
    {
      if (state is State.Waiting or State.Settled)
      {
        value = fulfillment;
        fulfillment = null;
        rejection = new ObjectDisposedException(nameof(ExpoModuleTestHost));
        state = State.Settled;
        shouldSignal = true;
      }
    }

    value?.Dispose();
    if (shouldSignal)
    {
      signal.TrySetResult(true);
    }
  }

  private enum State
  {
    Waiting,
    Settled,
    Transferred,
    Abandoned,
  }
}
