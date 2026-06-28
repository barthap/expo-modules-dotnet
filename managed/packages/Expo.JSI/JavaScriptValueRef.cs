using Expo.JSI.Interop;

namespace Expo.JSI;

public readonly ref struct JavaScriptValueRef
{
  private readonly JsiContext context;
  private readonly JsiRefScope? scope;
  private readonly ExpoJsiValueHandle valueHandle;

  internal JavaScriptValueRef(JsiContext context, JsiRefScope scope, ExpoJsiValueHandle valueHandle)
  {
    this.context = context;
    this.scope = scope;
    this.valueHandle = valueHandle;
  }

  private ExpoJsiValueRef NativeRef
  {
    get
    {
      ThrowIfInvalid();
      return new ExpoJsiValueRef(scope!.Handle, valueHandle);
    }
  }

  public JavaScriptValueKind Kind
  {
    get
    {
      unsafe
      {
        ExpoJsiError error;
        var kind = context.Api->GetValueRefKind(context.RuntimeHandle, NativeRef, &error);
        context.ThrowIfError(error, "Failed to read JavaScript value ref kind.");
        return (JavaScriptValueKind)kind;
      }
    }
  }

  public bool IsNullish => Kind is JavaScriptValueKind.Undefined or JavaScriptValueKind.Null;

  public bool IsBool => Kind == JavaScriptValueKind.Bool;

  public bool AsBool()
  {
    unsafe
    {
      return context.Api->ReadValueRefBool(context.RuntimeHandle, NativeRef);
    }
  }

  public bool IsDouble => Kind == JavaScriptValueKind.Number;

  public double AsDouble()
  {
    unsafe
    {
      return context.Api->ReadValueRefDouble(context.RuntimeHandle, NativeRef);
    }
  }

  public bool IsString => Kind == JavaScriptValueKind.String;

  public string AsString()
  {
    unsafe
    {
      return context.Api->ReadValueRefString(context.RuntimeHandle, NativeRef);
    }
  }

  public string CoerceToString()
  {
    unsafe
    {
      return context.Api->CoerceValueRefToString(context.RuntimeHandle, NativeRef);
    }
  }

  public bool IsObject => Kind == JavaScriptValueKind.Object;

  public JavaScriptObjectRef AsObject() => new(context, scope!, valueHandle);

  public JavaScriptArrayRef AsArray() => new(context, scope!, valueHandle);

  public JavaScriptValue Retain()
  {
    unsafe
    {
      var result = context.Api->RetainValueRef(context.RuntimeHandle, NativeRef);
      if (result.Ok == 0 || result.Value == 0)
      {
        JsiContext.ThrowNativeError(result.Error, "Failed to retain JavaScript value ref.");
      }
      return JavaScriptValue.FromOwnedHandle(context, result.Value);
    }
  }

  private void ThrowIfInvalid()
  {
    if (scope is null || valueHandle == 0)
    {
      throw new ObjectDisposedException(nameof(JsiRefScope));
    }
    _ = scope.Handle;
  }
}
