namespace Expo.JSI;

public readonly record struct JavaScriptNativeStateTypeId
{
  private JavaScriptNativeStateTypeId(ulong value)
  {
    if (value == 0)
    {
      throw new ArgumentOutOfRangeException(nameof(value), "NativeState type id cannot be zero.");
    }
    Value = value;
  }

  internal ulong Value { get; }

  public static JavaScriptNativeStateTypeId FromName(string name)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    const ulong offset = 14695981039346656037UL;
    const ulong prime = 1099511628211UL;
    var hash = offset;
    foreach (var ch in name)
    {
      hash ^= ch;
      hash *= prime;
    }
    return new JavaScriptNativeStateTypeId(hash == 0 ? 1 : hash);
  }

  public override string ToString() => Value.ToString("x16");
}
