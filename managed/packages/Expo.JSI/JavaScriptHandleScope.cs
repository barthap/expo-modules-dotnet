using Expo.JSI.Interop;

namespace Expo.JSI;

internal sealed unsafe class JavaScriptHandleScope : IDisposable
{
  [ThreadStatic]
  private static JavaScriptHandleScope? current;

  private readonly JsiContext context;
  private readonly JavaScriptHandleScope? previous;
  private List<ExpoJsiValueHandle>? values;
  private List<ExpoJsiObjectHandle>? objects;
  private List<ExpoJsiArrayHandle>? arrays;
  private bool disposed;

  private JavaScriptHandleScope(JsiContext context, JavaScriptHandleScope? previous)
  {
    this.context = context;
    this.previous = previous;
  }

  public static JavaScriptHandleScope Enter(JsiContext context)
  {
    var scope = new JavaScriptHandleScope(context, current);
    current = scope;
    return scope;
  }

  public static JavaScriptHandleScope CurrentFor(JsiContext context)
  {
    var scope = current;
    if (scope is null || scope.disposed || scope.context.Api != context.Api
        || scope.context.RuntimeHandle != context.RuntimeHandle)
    {
      throw new InvalidOperationException(
          "Scoped JavaScript refs require active runtime access."
      );
    }
    return scope;
  }

  public ExpoJsiValueHandle TrackValue(ExpoJsiValueHandle handle)
  {
    ThrowIfDisposed();
    if (handle != 0)
    {
      values ??= [];
      values.Add(handle);
    }
    return handle;
  }

  public ExpoJsiObjectHandle TrackObject(ExpoJsiObjectHandle handle)
  {
    ThrowIfDisposed();
    if (handle != 0)
    {
      objects ??= [];
      objects.Add(handle);
    }
    return handle;
  }

  public ExpoJsiArrayHandle TrackArray(ExpoJsiArrayHandle handle)
  {
    ThrowIfDisposed();
    if (handle != 0)
    {
      arrays ??= [];
      arrays.Add(handle);
    }
    return handle;
  }

  public void Dispose()
  {
    if (!ReferenceEquals(current, this))
    {
      throw new InvalidOperationException("JavaScript handle scopes must be disposed in stack order.");
    }

    current = previous;
    if (disposed)
    {
      return;
    }

    disposed = true;
    if (arrays is not null)
    {
      for (var index = arrays.Count - 1; index >= 0; index--)
      {
        context.Api->ReleaseArrayHandle(context.RuntimeHandle, arrays[index]);
      }
    }
    if (objects is not null)
    {
      for (var index = objects.Count - 1; index >= 0; index--)
      {
        context.Api->ReleaseObjectHandle(context.RuntimeHandle, objects[index]);
      }
    }
    if (values is not null)
    {
      for (var index = values.Count - 1; index >= 0; index--)
      {
        context.Api->ReleaseValueHandle(context.RuntimeHandle, values[index]);
      }
    }
  }

  private void ThrowIfDisposed()
  {
    if (disposed)
    {
      throw new ObjectDisposedException(nameof(JavaScriptHandleScope));
    }
  }
}
