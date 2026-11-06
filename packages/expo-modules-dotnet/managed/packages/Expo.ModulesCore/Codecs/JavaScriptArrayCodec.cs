using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

/// <summary>Decodes and encodes a non-null JavaScript array as a read-only list.</summary>
/// <remarks>
/// This codec does not handle nullish input. A nullable list container uses
/// <see cref="NullableReadOnlyListCodec{T, TCodec}"/>. Generated boundary glue calls
/// <c>DecodeToArray</c>, while composed positions such as a list of lists, a record field, or a
/// callback result use the <see cref="IJavaScriptCodec{T}"/> implementation.
/// </remarks>
/// <typeparam name="T">The element type.</typeparam>
/// <typeparam name="TCodec">The codec for each element.</typeparam>
public readonly struct JavaScriptArrayCodec<T, TCodec> : IJavaScriptCodec<IReadOnlyList<T>>
    where TCodec : IJavaScriptCodec<T>
{
  /// <summary>Decodes a scoped JavaScript array into a new array.</summary>
  public static T[] DecodeToArray(JavaScriptValueRef value, JavaScriptRuntime runtime)
  {
    var array = value.AsArray();
    var length = checked((int)array.Length);
    var result = new T[length];

    for (var index = 0; index < length; index++)
    {
      var element = array.GetValue((uint)index);
      result[index] = TCodec.Decode(element, runtime);
    }

    return result;
  }

  /// <summary>
  /// Decodes an owned JavaScript array into a new array. The method disposes the array wrapper and
  /// each element wrapper it creates, and leaves <paramref name="value"/> to the caller.
  /// </summary>
  public static T[] DecodeToArray(JavaScriptValue value, JavaScriptRuntime runtime)
  {
    using var array = value.AsArray();
    var length = checked((int)array.Length);
    var result = new T[length];

    for (var index = 0; index < length; index++)
    {
      using var element = array.GetValue((uint)index);
      result[index] = TCodec.Decode(element, runtime);
    }

    return result;
  }

  /// <summary>Decodes a scoped JavaScript array into a read-only list backed by a new array.</summary>
  public static IReadOnlyList<T> Decode(JavaScriptValueRef value, JavaScriptRuntime runtime) =>
      DecodeToArray(value, runtime);

  /// <summary>Decodes an owned JavaScript array into a read-only list backed by a new array.</summary>
  public static IReadOnlyList<T> Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
      DecodeToArray(value, runtime);

  /// <summary>Encodes a read-only list as a new JavaScript array.</summary>
  public static JavaScriptValue Encode(IReadOnlyList<T> values, JavaScriptRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(values);

    using var array = runtime.CreateArray((uint)values.Count);
    for (var index = 0; index < values.Count; index++)
    {
      using var element = TCodec.Encode(values[index], runtime);
      array.SetValue((uint)index, element);
    }
    return array.AsValue();
  }
}
