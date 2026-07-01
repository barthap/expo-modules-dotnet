namespace Expo.JSI;

/// <summary>
/// Describes the JavaScript type represented by a <see cref="JavaScriptValue" /> or
/// <see cref="JavaScriptValueRef" />.
/// </summary>
public enum JavaScriptValueKind
{
  /// <summary>
  /// JavaScript <c>undefined</c>.
  /// </summary>
  Undefined = 0,

  /// <summary>
  /// JavaScript <c>null</c>.
  /// </summary>
  Null = 1,

  /// <summary>
  /// JavaScript boolean.
  /// </summary>
  Bool = 2,

  /// <summary>
  /// JavaScript number.
  /// </summary>
  Number = 3,

  /// <summary>
  /// JavaScript string.
  /// </summary>
  String = 4,

  /// <summary>
  /// JavaScript object.
  /// </summary>
  Object = 5,

  /// <summary>
  /// JavaScript function.
  /// </summary>
  Function = 6,

  /// <summary>
  /// JavaScript ArrayBuffer.
  /// </summary>
  ArrayBuffer = 7,
}
