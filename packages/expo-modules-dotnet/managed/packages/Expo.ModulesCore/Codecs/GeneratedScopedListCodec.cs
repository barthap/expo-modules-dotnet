using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

/// <summary>Decodes and encodes a non-null JavaScript array as a read-only list of scoped leaves.</summary>
/// <remarks>
/// This helper is for generated bindings, not ordinary module author code. A null element makes
/// <c>EncodeBorrowed</c> throw, and <c>Transfer</c> still registers every other element.
/// </remarks>
/// <typeparam name="T">The element type.</typeparam>
/// <typeparam name="TCodec">The scoped codec for each element.</typeparam>
public readonly struct GeneratedScopedListCodec<T, TCodec> : IGeneratedScopedCodec<IReadOnlyList<T>>
    where TCodec : IGeneratedScopedCodec<T>
{
  /// <summary>Decodes a scoped JavaScript array into a new list and registers each element's leaves.</summary>
  public static IReadOnlyList<T> Decode(
      JavaScriptValueRef value,
      JavaScriptRuntime runtime,
      GeneratedConversionScope scope)
  {
    var array = value.AsArray();
    var length = checked((int)array.Length);
    var result = new T[length];

    for (var index = 0; index < length; index++)
    {
      var element = array.GetValue((uint)index);
      result[index] = TCodec.Decode(element, runtime, scope);
    }

    return result;
  }

  /// <summary>Registers the leaves of every element. A null list registers nothing.</summary>
  public static void Transfer(IReadOnlyList<T> value, GeneratedConversionScope scope)
  {
    if (value is null)
    {
      return;
    }

    for (var index = 0; index < value.Count; index++)
    {
      TCodec.Transfer(value[index], scope);
    }
  }

  /// <summary>Encodes a list as a new JavaScript array without consuming any source leaf.</summary>
  public static JavaScriptValue EncodeBorrowed(IReadOnlyList<T> value, JavaScriptRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(value);

    using var array = runtime.CreateArray((uint)value.Count);
    for (var index = 0; index < value.Count; index++)
    {
      using var element = TCodec.EncodeBorrowed(value[index], runtime);
      array.SetValue((uint)index, element);
    }
    return array.AsValue();
  }
}
