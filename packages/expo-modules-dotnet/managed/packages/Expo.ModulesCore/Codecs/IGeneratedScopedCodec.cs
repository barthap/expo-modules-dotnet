using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

/// <summary>Decodes, transfers, and encodes a type whose leaves a conversion scope owns.</summary>
/// <remarks>
/// This helper is for generated bindings, not ordinary module author code. A leaf is a
/// <see cref="JavaScriptValue"/>, an <see cref="ArrayBuffer"/>, or a <see cref="Uint8Array"/>.
/// <c>Decode</c> registers each leaf it creates in the scope, and <c>Transfer</c> registers each
/// leaf of a value the module returns. <c>EncodeBorrowed</c> never consumes or disposes a source
/// leaf.
/// </remarks>
/// <typeparam name="T">The decoded type.</typeparam>
public interface IGeneratedScopedCodec<T>
{
  /// <summary>Decodes a scoped JavaScript value and registers every leaf in <paramref name="scope"/>.</summary>
  static abstract T Decode(
      JavaScriptValueRef value,
      JavaScriptRuntime runtime,
      GeneratedConversionScope scope);

  /// <summary>
  /// Registers every leaf of <paramref name="value"/> in <paramref name="scope"/>. A null value
  /// registers nothing and does not throw. This method does no runtime work.
  /// </summary>
  static abstract void Transfer(T value, GeneratedConversionScope scope);

  /// <summary>Encodes <paramref name="value"/> as a new owned JavaScript value.</summary>
  /// <remarks>The method never consumes or disposes a leaf of <paramref name="value"/>.</remarks>
  static abstract JavaScriptValue EncodeBorrowed(T value, JavaScriptRuntime runtime);
}
