using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public readonly struct Uint8ArrayCodec : IJavaScriptCodec<Uint8Array>
{
  public static Uint8Array Decode(JavaScriptValueRef value, JavaScriptRuntime runtime)
  {
    if (!value.IsObject)
    {
      throw NotUint8Array();
    }

    if (!IsUint8ArrayPrototypeOf(value, runtime))
    {
      throw NotUint8Array();
    }

    var obj = value.AsObject();
    var bufferRef = obj.GetProperty("buffer");
    if (!bufferRef.IsArrayBuffer)
    {
      throw NotUint8Array();
    }

    var byteOffset = ReadIndex(obj.GetProperty("byteOffset"));
    var byteLength = ReadIndex(obj.GetProperty("byteLength"));

    var buffer = ArrayBufferCodec.Decode(bufferRef, runtime);
    try
    {
      return new Uint8Array(buffer, byteOffset, byteLength);
    }
    catch
    {
      buffer.Dispose();
      throw;
    }
  }

  public static Uint8Array Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
      Decode(value.Ref, runtime);

  public static JavaScriptValue Encode(Uint8Array value, JavaScriptRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(value);
    using var bufferValue = value.Buffer.Encode(runtime);
    using var offset = runtime.CreateNumber(value.ByteOffset);
    using var length = runtime.CreateNumber(value.Length);
    using var global = runtime.Global();
    using var constructorValue = global.GetProperty("Uint8Array");
    using var function = constructorValue.AsFunction();
    return function.CallAsConstructor(bufferValue, offset, length);
  }

  /// <summary>Disposes <paramref name="value"/>. A null view does nothing.</summary>
  public static void Release(Uint8Array value) => value?.Dispose();

  public static JavaScriptValue EncodeCopy(ReadOnlySpan<byte> bytes, JavaScriptRuntime runtime)
  {
    using var view = Uint8Array.CopyFrom(bytes);
    return Encode(view, runtime);
  }

  private static bool IsUint8ArrayPrototypeOf(JavaScriptValueRef value, JavaScriptRuntime runtime)
  {
    using var global = runtime.Global();
    using var constructorValue = global.GetProperty("Uint8Array");
    using var constructorObject = constructorValue.AsObject();
    using var prototypeValue = constructorObject.GetProperty("prototype");
    using var prototypeObject = prototypeValue.AsObject();
    using var isPrototypeOfValue = prototypeObject.GetProperty("isPrototypeOf");
    using var isPrototypeOf = isPrototypeOfValue.AsFunction();
    using var candidate = value.Retain();
    using var verdict = isPrototypeOf.CallWithThis(prototypeObject, candidate);
    return verdict.IsBool && verdict.AsBool();
  }

  private static int ReadIndex(JavaScriptValueRef value)
  {
    if (!value.IsDouble)
    {
      throw NotUint8Array();
    }

    var number = value.AsDouble();
    if (!(number >= 0 && number <= int.MaxValue && number == Math.Truncate(number)))
    {
      throw NotUint8Array();
    }

    return (int)number;
  }

  private static InvalidOperationException NotUint8Array() =>
      new("Value is not a Uint8Array.");
}
