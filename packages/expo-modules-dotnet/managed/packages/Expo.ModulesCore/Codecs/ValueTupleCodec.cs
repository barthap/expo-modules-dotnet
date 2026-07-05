using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public interface IJavaScriptArgsCodec<TArgs>
    where TArgs : struct
{
  static abstract JavaScriptValue[] Encode(TArgs args, JavaScriptRuntime runtime);
}

internal static class JavaScriptArgsCodec
{
  public static void DisposeEncoded(JavaScriptValue[] values)
  {
    foreach (var value in values)
    {
      value?.Dispose();
    }
  }
}

public readonly struct ValueTupleCodec : IJavaScriptArgsCodec<ValueTuple>
{
  public static JavaScriptValue[] Encode(ValueTuple args, JavaScriptRuntime runtime) => [];
}

public readonly struct ValueTupleCodec<T1, T1Codec> : IJavaScriptArgsCodec<ValueTuple<T1>>
    where T1Codec : IJavaScriptCodec<T1>
{
  public static JavaScriptValue[] Encode(ValueTuple<T1> args, JavaScriptRuntime runtime) =>
      [T1Codec.Encode(args.Item1, runtime)];
}

public readonly struct ValueTupleCodec<T1, T1Codec, T2, T2Codec>
    : IJavaScriptArgsCodec<(T1, T2)>
    where T1Codec : IJavaScriptCodec<T1>
    where T2Codec : IJavaScriptCodec<T2>
{
  public static JavaScriptValue[] Encode((T1, T2) args, JavaScriptRuntime runtime)
  {
    var values = new JavaScriptValue[2];
    try
    {
      values[0] = T1Codec.Encode(args.Item1, runtime);
      values[1] = T2Codec.Encode(args.Item2, runtime);
      return values;
    }
    catch
    {
      JavaScriptArgsCodec.DisposeEncoded(values);
      throw;
    }
  }
}

public readonly struct ValueTupleCodec<T1, T1Codec, T2, T2Codec, T3, T3Codec>
    : IJavaScriptArgsCodec<(T1, T2, T3)>
    where T1Codec : IJavaScriptCodec<T1>
    where T2Codec : IJavaScriptCodec<T2>
    where T3Codec : IJavaScriptCodec<T3>
{
  public static JavaScriptValue[] Encode((T1, T2, T3) args, JavaScriptRuntime runtime)
  {
    var values = new JavaScriptValue[3];
    try
    {
      values[0] = T1Codec.Encode(args.Item1, runtime);
      values[1] = T2Codec.Encode(args.Item2, runtime);
      values[2] = T3Codec.Encode(args.Item3, runtime);
      return values;
    }
    catch
    {
      JavaScriptArgsCodec.DisposeEncoded(values);
      throw;
    }
  }
}

public readonly struct ValueTupleCodec<T1, T1Codec, T2, T2Codec, T3, T3Codec, T4, T4Codec>
    : IJavaScriptArgsCodec<(T1, T2, T3, T4)>
    where T1Codec : IJavaScriptCodec<T1>
    where T2Codec : IJavaScriptCodec<T2>
    where T3Codec : IJavaScriptCodec<T3>
    where T4Codec : IJavaScriptCodec<T4>
{
  public static JavaScriptValue[] Encode((T1, T2, T3, T4) args, JavaScriptRuntime runtime)
  {
    var values = new JavaScriptValue[4];
    try
    {
      values[0] = T1Codec.Encode(args.Item1, runtime);
      values[1] = T2Codec.Encode(args.Item2, runtime);
      values[2] = T3Codec.Encode(args.Item3, runtime);
      values[3] = T4Codec.Encode(args.Item4, runtime);
      return values;
    }
    catch
    {
      JavaScriptArgsCodec.DisposeEncoded(values);
      throw;
    }
  }
}

public readonly struct ValueTupleCodec<T1, T1Codec, T2, T2Codec, T3, T3Codec, T4, T4Codec, T5, T5Codec>
    : IJavaScriptArgsCodec<(T1, T2, T3, T4, T5)>
    where T1Codec : IJavaScriptCodec<T1>
    where T2Codec : IJavaScriptCodec<T2>
    where T3Codec : IJavaScriptCodec<T3>
    where T4Codec : IJavaScriptCodec<T4>
    where T5Codec : IJavaScriptCodec<T5>
{
  public static JavaScriptValue[] Encode((T1, T2, T3, T4, T5) args, JavaScriptRuntime runtime)
  {
    var values = new JavaScriptValue[5];
    try
    {
      values[0] = T1Codec.Encode(args.Item1, runtime);
      values[1] = T2Codec.Encode(args.Item2, runtime);
      values[2] = T3Codec.Encode(args.Item3, runtime);
      values[3] = T4Codec.Encode(args.Item4, runtime);
      values[4] = T5Codec.Encode(args.Item5, runtime);
      return values;
    }
    catch
    {
      JavaScriptArgsCodec.DisposeEncoded(values);
      throw;
    }
  }
}

public readonly struct ValueTupleCodec<T1, T1Codec, T2, T2Codec, T3, T3Codec, T4, T4Codec, T5, T5Codec, T6, T6Codec>
    : IJavaScriptArgsCodec<(T1, T2, T3, T4, T5, T6)>
    where T1Codec : IJavaScriptCodec<T1>
    where T2Codec : IJavaScriptCodec<T2>
    where T3Codec : IJavaScriptCodec<T3>
    where T4Codec : IJavaScriptCodec<T4>
    where T5Codec : IJavaScriptCodec<T5>
    where T6Codec : IJavaScriptCodec<T6>
{
  public static JavaScriptValue[] Encode((T1, T2, T3, T4, T5, T6) args, JavaScriptRuntime runtime)
  {
    var values = new JavaScriptValue[6];
    try
    {
      values[0] = T1Codec.Encode(args.Item1, runtime);
      values[1] = T2Codec.Encode(args.Item2, runtime);
      values[2] = T3Codec.Encode(args.Item3, runtime);
      values[3] = T4Codec.Encode(args.Item4, runtime);
      values[4] = T5Codec.Encode(args.Item5, runtime);
      values[5] = T6Codec.Encode(args.Item6, runtime);
      return values;
    }
    catch
    {
      JavaScriptArgsCodec.DisposeEncoded(values);
      throw;
    }
  }
}

public readonly struct ValueTupleCodec<T1, T1Codec, T2, T2Codec, T3, T3Codec, T4, T4Codec, T5, T5Codec, T6, T6Codec, T7, T7Codec>
    : IJavaScriptArgsCodec<(T1, T2, T3, T4, T5, T6, T7)>
    where T1Codec : IJavaScriptCodec<T1>
    where T2Codec : IJavaScriptCodec<T2>
    where T3Codec : IJavaScriptCodec<T3>
    where T4Codec : IJavaScriptCodec<T4>
    where T5Codec : IJavaScriptCodec<T5>
    where T6Codec : IJavaScriptCodec<T6>
    where T7Codec : IJavaScriptCodec<T7>
{
  public static JavaScriptValue[] Encode((T1, T2, T3, T4, T5, T6, T7) args, JavaScriptRuntime runtime)
  {
    var values = new JavaScriptValue[7];
    try
    {
      values[0] = T1Codec.Encode(args.Item1, runtime);
      values[1] = T2Codec.Encode(args.Item2, runtime);
      values[2] = T3Codec.Encode(args.Item3, runtime);
      values[3] = T4Codec.Encode(args.Item4, runtime);
      values[4] = T5Codec.Encode(args.Item5, runtime);
      values[5] = T6Codec.Encode(args.Item6, runtime);
      values[6] = T7Codec.Encode(args.Item7, runtime);
      return values;
    }
    catch
    {
      JavaScriptArgsCodec.DisposeEncoded(values);
      throw;
    }
  }
}

public readonly struct ValueTupleCodec<T1, T1Codec, T2, T2Codec, T3, T3Codec, T4, T4Codec, T5, T5Codec, T6, T6Codec, T7, T7Codec, T8, T8Codec>
    : IJavaScriptArgsCodec<(T1, T2, T3, T4, T5, T6, T7, T8)>
    where T1Codec : IJavaScriptCodec<T1>
    where T2Codec : IJavaScriptCodec<T2>
    where T3Codec : IJavaScriptCodec<T3>
    where T4Codec : IJavaScriptCodec<T4>
    where T5Codec : IJavaScriptCodec<T5>
    where T6Codec : IJavaScriptCodec<T6>
    where T7Codec : IJavaScriptCodec<T7>
    where T8Codec : IJavaScriptCodec<T8>
{
  public static JavaScriptValue[] Encode((T1, T2, T3, T4, T5, T6, T7, T8) args, JavaScriptRuntime runtime)
  {
    var values = new JavaScriptValue[8];
    try
    {
      values[0] = T1Codec.Encode(args.Item1, runtime);
      values[1] = T2Codec.Encode(args.Item2, runtime);
      values[2] = T3Codec.Encode(args.Item3, runtime);
      values[3] = T4Codec.Encode(args.Item4, runtime);
      values[4] = T5Codec.Encode(args.Item5, runtime);
      values[5] = T6Codec.Encode(args.Item6, runtime);
      values[6] = T7Codec.Encode(args.Item7, runtime);
      values[7] = T8Codec.Encode(args.Item8, runtime);
      return values;
    }
    catch
    {
      JavaScriptArgsCodec.DisposeEncoded(values);
      throw;
    }
  }
}
