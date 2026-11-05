namespace Expo.ModulesCore.Codecs;

/// <summary>Releases the JavaScript leaves that one generated binding invocation decodes.</summary>
/// <remarks>
/// This helper is for generated bindings, not ordinary module author code. A leaf is a
/// <see cref="Expo.JSI.JavaScriptValue"/> or <see cref="ArrayBuffer"/> a scoped codec hands
/// to the module. The scope disposes each registration when it disposes, in registration order.
/// Register accepts only leaves whose Dispose is idempotent, as those two types are, so a leaf
/// registered more than once is disposed once per registration and releases its native handle
/// once. The scope has one owner at a time and takes no lock: generated code hands it from the
/// host frame to the Promise continuation when <c>CreatePromise</c> returns, so two threads never
/// use it together. Registration is by reference, so two distinct leaves that compare equal both
/// release.
/// </remarks>
public sealed class GeneratedConversionScope : IDisposable
{
  private IDisposable? first;
  private List<IDisposable>? rest;
  private bool disposed;

  /// <summary>Appends <paramref name="leaf"/>, which this scope disposes when it disposes.</summary>
  /// <remarks>The leaf's Dispose must be idempotent, because a repeated registration disposes it again.</remarks>
  /// <exception cref="ObjectDisposedException">The scope already disposed. It does not own the leaf.</exception>
  internal void Register(IDisposable leaf)
  {
    ObjectDisposedException.ThrowIf(disposed, this);

    if (first is null)
    {
      first = leaf;
      return;
    }

    if (rest is null)
    {
      rest = [leaf];
      return;
    }

    rest.Add(leaf);
  }

  /// <summary>
  /// Disposes every registration once and keeps releasing after one fails. Later calls do nothing.
  /// </summary>
  /// <exception cref="AggregateException">One or more leaves failed to release.</exception>
  public void Dispose()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;

    List<Exception>? failures = null;
    DisposeAndCapture(first, ref failures);
    if (rest is not null)
    {
      foreach (var leaf in rest)
      {
        DisposeAndCapture(leaf, ref failures);
      }
    }

    first = null;
    rest = null;

    if (failures is not null)
    {
      throw new AggregateException(failures);
    }
  }

  private static void DisposeAndCapture(IDisposable? leaf, ref List<Exception>? failures)
  {
    try
    {
      leaf?.Dispose();
    }
    catch (AggregateException exception)
    {
      (failures ??= []).AddRange(exception.InnerExceptions);
    }
    catch (Exception exception)
    {
      (failures ??= []).Add(exception);
    }
  }
}
