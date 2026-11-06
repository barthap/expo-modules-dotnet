namespace Expo.ModulesCore;

/// <summary>Carries exactly one value of one of two types.</summary>
/// <remarks>
/// Create a value with <see cref="FromFirst"/> or <see cref="FromSecond"/>. <c>default</c> is the
/// second member with a default value, so <see cref="IsFirst"/> is <c>false</c>.
/// </remarks>
/// <typeparam name="TFirst">The type of the first member.</typeparam>
/// <typeparam name="TSecond">The type of the second member.</typeparam>
public readonly struct Either<TFirst, TSecond>
{
  private readonly TFirst first;
  private readonly TSecond second;

  private Either(TFirst first, TSecond second, bool isFirst)
  {
    this.first = first;
    this.second = second;
    IsFirst = isFirst;
  }

  /// <summary>Gets a value that is <c>true</c> when the union holds its first member.</summary>
  public bool IsFirst { get; }

  /// <summary>Gets the first member.</summary>
  /// <exception cref="InvalidOperationException">The union holds its second member.</exception>
  public TFirst First =>
      IsFirst ? first : throw new InvalidOperationException("The union holds its second member.");

  /// <summary>Gets the second member.</summary>
  /// <exception cref="InvalidOperationException">The union holds its first member.</exception>
  public TSecond Second =>
      !IsFirst ? second : throw new InvalidOperationException("The union holds its first member.");

  /// <summary>Creates a union that holds <paramref name="value"/> as its first member.</summary>
  /// <param name="value">The first member.</param>
  public static Either<TFirst, TSecond> FromFirst(TFirst value) => new(value, default!, true);

  /// <summary>Creates a union that holds <paramref name="value"/> as its second member.</summary>
  /// <param name="value">The second member.</param>
  public static Either<TFirst, TSecond> FromSecond(TSecond value) => new(default!, value, false);

  /// <summary>Calls the function that matches the member the union holds.</summary>
  /// <typeparam name="TResult">The result type.</typeparam>
  /// <param name="first">The function to call when the union holds its first member.</param>
  /// <param name="second">The function to call when the union holds its second member.</param>
  /// <returns>The result of the function that was called.</returns>
  public TResult Match<TResult>(Func<TFirst, TResult> first, Func<TSecond, TResult> second)
  {
    ArgumentNullException.ThrowIfNull(first);
    ArgumentNullException.ThrowIfNull(second);
    return IsFirst ? first(this.first) : second(this.second);
  }
}
