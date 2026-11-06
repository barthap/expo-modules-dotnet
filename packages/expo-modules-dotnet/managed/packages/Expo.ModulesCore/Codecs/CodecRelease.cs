namespace Expo.ModulesCore.Codecs;

/// <summary>Collects release failures so every leaf is attempted before one exception is thrown.</summary>
/// <remarks>This helper is for codecs and generated bindings, not ordinary module author code.</remarks>
public static class CodecRelease
{
  /// <summary>
  /// Calls <c>TCodec.Release(value)</c> and records a failure in <paramref name="failures"/>. An
  /// <see cref="AggregateException"/> adds its inner exceptions. This method never throws.
  /// </summary>
  public static void Release<T, TCodec>(T value, ref List<Exception>? failures)
      where TCodec : IJavaScriptCodec<T>
  {
    try
    {
      TCodec.Release(value);
    }
    catch (AggregateException aggregate)
    {
      (failures ??= []).AddRange(aggregate.InnerExceptions);
    }
    catch (Exception exception)
    {
      (failures ??= []).Add(exception);
    }
  }

  /// <summary>Throws one <see cref="AggregateException"/> when <paramref name="failures"/> is not null.</summary>
  public static void ThrowIfFailed(List<Exception>? failures)
  {
    if (failures is not null)
    {
      throw new AggregateException(failures);
    }
  }
}
