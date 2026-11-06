using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public readonly struct JavaScriptValueCodec : IJavaScriptCodec<JavaScriptValue>, IGeneratedScopedCodec<JavaScriptValue>
{
  public static JavaScriptValue Decode(JavaScriptValueRef value, JavaScriptRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    return value.Retain();
  }

  public static JavaScriptValue Decode(JavaScriptValue value, JavaScriptRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(value);
    ArgumentNullException.ThrowIfNull(runtime);
    return value.Retain();
  }

  public static JavaScriptValue Encode(JavaScriptValue value, JavaScriptRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(value);
    ArgumentNullException.ThrowIfNull(runtime);
    return value;
  }

  /// <summary>Retains a scoped value and registers the retained wrapper in <paramref name="scope"/>.</summary>
  /// <remarks>This helper is for generated bindings, not ordinary module author code.</remarks>
  public static JavaScriptValue Decode(
      JavaScriptValueRef value,
      JavaScriptRuntime runtime,
      GeneratedConversionScope scope)
  {
    var result = value.Retain();
    try
    {
      scope.Register(result);
    }
    catch
    {
      result.Dispose();
      throw;
    }

    return result;
  }

  /// <summary>Registers <paramref name="value"/> in <paramref name="scope"/>. A null value registers nothing.</summary>
  /// <remarks>This helper is for generated bindings, not ordinary module author code.</remarks>
  public static void Transfer(JavaScriptValue value, GeneratedConversionScope scope)
  {
    if (value is not null)
    {
      scope.Register(value);
    }
  }

  /// <summary>Returns a new owned wrapper and leaves <paramref name="value"/> usable.</summary>
  /// <remarks>This helper is for generated bindings, not ordinary module author code.</remarks>
  public static JavaScriptValue EncodeBorrowed(JavaScriptValue value, JavaScriptRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(value);
    ArgumentNullException.ThrowIfNull(runtime);
    return value.Retain();
  }
}
