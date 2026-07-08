namespace Expo.JSI;

public sealed class JavaScriptHostObject<TState> : IJavaScriptValueRepresentable, IDisposable
    where TState : class
{
  internal JavaScriptHostObject(TState state, JavaScriptObject obj)
  {
    State = state;
    Object = obj;
  }

  public TState State { get; }

  public JavaScriptObject Object { get; }

  public JavaScriptValue AsValue() => Object.AsValue();

  public void Dispose() => Object.Dispose();
}

public sealed record JavaScriptHostObjectDescriptor(
    JavaScriptHostObjectGetter Get,
    JavaScriptHostObjectSetter? Set = null,
    JavaScriptHostObjectPropertyNamesGetter? GetPropertyNames = null,
    object? State = null);

public sealed record JavaScriptHostObjectDescriptor<TState>(
    JavaScriptHostObjectGetter<TState> Get,
    JavaScriptHostObjectSetter<TState>? Set = null,
    JavaScriptHostObjectPropertyNamesGetter<TState>? GetPropertyNames = null)
    where TState : class;

public delegate JavaScriptValue JavaScriptHostObjectGetter(
    JavaScriptRuntime runtime,
    string propertyName,
    object? state);

public delegate void JavaScriptHostObjectSetter(
    JavaScriptRuntime runtime,
    string propertyName,
    JavaScriptValueRef value,
    object? state);

public delegate IReadOnlyList<string> JavaScriptHostObjectPropertyNamesGetter(object? state);

public delegate JavaScriptValue JavaScriptHostObjectGetter<TState>(
    JavaScriptRuntime runtime,
    string propertyName,
    TState state)
    where TState : class;

public delegate void JavaScriptHostObjectSetter<TState>(
    JavaScriptRuntime runtime,
    string propertyName,
    JavaScriptValueRef value,
    TState state)
    where TState : class;

public delegate IReadOnlyList<string> JavaScriptHostObjectPropertyNamesGetter<TState>(TState state)
    where TState : class;
