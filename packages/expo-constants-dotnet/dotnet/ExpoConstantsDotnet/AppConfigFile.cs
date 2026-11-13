using System.Text;
using System.Text.Json;

namespace ExpoConstantsDotnet;

/// <summary>
/// Reads the public app config that the autolinking CLI stages as app.config
/// next to the managed artifacts.
/// </summary>
// @ref docs/specs/modules/expo-constants.md "Expo Constants Dotnet Exposes Typed Host Values"
internal static class AppConfigFile
{
  internal const string FileName = "app.config";

  private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

  /// <summary>
  /// Returns the file content as a JSON string, or null when the host supplies no
  /// resources directory or the directory has no app.config.
  /// </summary>
  /// <exception cref="InvalidOperationException">
  /// The file exists but cannot be read, is not valid UTF-8, starts with a byte-order
  /// mark, or is not a JSON object. Upstream expo-constants parses the string while
  /// it is imported, so a bad file must fail here with the file named, not later.
  /// </exception>
  internal static string? Read(string? resourcesDirectory)
  {
    if (resourcesDirectory is null)
    {
      return null;
    }

    var path = Path.Combine(resourcesDirectory, FileName);
    byte[] bytes;
    try
    {
      bytes = File.ReadAllBytes(path);
    }
    catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
    {
      return null;
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
    {
      throw Invalid(path, $"cannot be read: {error.Message}");
    }

    // JsonDocument would accept a leading byte-order mark that JSON.parse rejects.
    if (bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))
    {
      throw Invalid(path, "starts with a byte-order mark");
    }

    string content;
    try
    {
      content = StrictUtf8.GetString(bytes);
    }
    catch (DecoderFallbackException)
    {
      throw Invalid(path, "is not valid UTF-8");
    }

    try
    {
      using var document = JsonDocument.Parse(content);
      if (document.RootElement.ValueKind != JsonValueKind.Object)
      {
        throw Invalid(path, "is not a JSON object");
      }
    }
    catch (JsonException)
    {
      throw Invalid(path, "is not a JSON object");
    }

    return content;
  }

  private static InvalidOperationException Invalid(string path, string reason)
      => new($"The app config file '{path}' {reason}. Rebuild the app so the autolinking CLI writes it again.");
}
