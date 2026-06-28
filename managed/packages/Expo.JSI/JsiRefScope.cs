using Expo.JSI.Interop;

namespace Expo.JSI;

internal sealed unsafe class JsiRefScope : IDisposable
{
  [ThreadStatic]
  private static JsiRefScope? current;

  private readonly JsiContext context;
  private readonly JsiRefScope? previous;
  private ExpoJsiRefScopeHandle handle;

  private JsiRefScope(JsiContext context, ExpoJsiRefScopeHandle handle, JsiRefScope? previous)
  {
    this.context = context;
    this.handle = handle;
    this.previous = previous;
  }

  public ExpoJsiRefScopeHandle Handle
  {
    get
    {
      ThrowIfDisposed();
      return handle;
    }
  }

  public static JsiRefScope Enter(JsiContext context)
  {
    var handle = context.Api->CreateRefScope(context.RuntimeHandle);
    if (handle == 0)
    {
      throw new InvalidOperationException("Failed to create JavaScript ref scope.");
    }

    var scope = new JsiRefScope(context, handle, current);
    current = scope;
    return scope;
  }

  public static JsiRefScope CurrentFor(JsiContext context)
  {
    var scope = current;
    if (scope is null || scope.handle == 0 || scope.context.Api != context.Api
        || scope.context.RuntimeHandle != context.RuntimeHandle)
    {
      throw new InvalidOperationException(
          "Scoped JavaScript refs require active runtime access."
      );
    }
    return scope;
  }

  public void Dispose()
  {
    if (!ReferenceEquals(current, this))
    {
      throw new InvalidOperationException("JavaScript ref scopes must be disposed in stack order.");
    }

    current = previous;
    if (handle != 0)
    {
      context.Api->ReleaseRefScope(context.RuntimeHandle, handle);
      handle = 0;
    }
  }

  private void ThrowIfDisposed()
  {
    if (handle == 0)
    {
      throw new ObjectDisposedException(nameof(JsiRefScope));
    }
  }
}
