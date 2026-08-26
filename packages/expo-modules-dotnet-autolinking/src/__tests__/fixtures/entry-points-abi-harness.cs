// Compiled ABI harness for the generated EntryPoints host.
//
// The generator emits a second-language mirror of expo_dotnet_app_directories,
// so matching the emitted source text is not enough: the layout and the decoding
// rules have to be executed. The autolinking Vitest suite generates a host
// project against the real Expo.JSI and Expo.ModulesCore projects, adds this file
// as a compile item, and runs it. A nonzero exit code fails the test.
//
// This is a partial declaration of the generated class so it can reach the
// private decoder and the private native mirror without the generator widening
// their visibility in shipped apps.

using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Expo.ModulesCore.Generated;

public static partial class EntryPoints
{
  private const string ApiParameterMarker = "(Parameter 'api')";

  private static int failures;

  public static int Main()
  {
    AssertNativeMirrorLayout();
    AssertExportedNames();
    AssertValidDecoding();
    AssertRejectedInputsProduceStructuredErrors();

    if (failures > 0)
    {
      Console.Error.WriteLine($"entry-points ABI harness: {failures} check(s) failed.");
      return 1;
    }

    Console.WriteLine("entry-points ABI harness: all checks passed.");
    return 0;
  }

  // The native header static_asserts its own layout for both pointer widths.
  // These assertions are the other half of that pair: if the managed mirror ever
  // drifts, the create call would read the wrong bytes at runtime instead of
  // failing to compile.
  private static unsafe void AssertNativeMirrorLayout()
  {
    var pointerSize = sizeof(void*);
    Check(pointerSize == 4 || pointerSize == 8, $"unexpected pointer size {pointerSize}");

    NativeAppDirectories value = default;
    var origin = (byte*)&value;

    CheckEqual(0, OffsetOf(origin, &value.Size), "Size offset");
    CheckEqual(4, OffsetOf(origin, &value.Version), "Version offset");
    CheckEqual(8, OffsetOf(origin, &value.CacheDirectory), "CacheDirectory offset");
    CheckEqual(
        8 + pointerSize,
        OffsetOf(origin, &value.CacheDirectoryLength),
        "CacheDirectoryLength offset"
    );
    CheckEqual(
        pointerSize == 8 ? 24 : 16,
        OffsetOf(origin, &value.PersistentFilesDirectory),
        "PersistentFilesDirectory offset"
    );
    CheckEqual(
        pointerSize == 8 ? 32 : 20,
        OffsetOf(origin, &value.PersistentFilesDirectoryLength),
        "PersistentFilesDirectoryLength offset"
    );
    CheckEqual(
        pointerSize == 8 ? 40 : 24,
        sizeof(NativeAppDirectories),
        "sizeof(NativeAppDirectories)"
    );
  }

  // The v2 rename is what makes a stale adapter and host pair fail resolution
  // instead of calling through the wrong function-pointer signature. The NativeAOT
  // symbol is checked through the attribute; the HostFXR method is checked by
  // name, because that is the string both loaders hand to hostfxr.
  private static void AssertExportedNames()
  {
    var type = typeof(EntryPoints);
    var create = type.GetMethod(
        "CreateRuntimeContextResultV2",
        BindingFlags.Public | BindingFlags.Static
    );
    Check(create is not null, "CreateRuntimeContextResultV2 is missing");
    Check(
        type.GetMethod("CreateRuntimeContextResult", BindingFlags.Public | BindingFlags.Static)
            is null,
        "the old CreateRuntimeContextResult method is still present"
    );

    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
    {
      var entryPoint = method.GetCustomAttribute<UnmanagedCallersOnlyAttribute>()?.EntryPoint;
      Check(
          entryPoint != "expo_dotnet_create_runtime_context_result",
          "the old NativeAOT create symbol is still exported"
      );
    }

    if (create is null)
    {
      return;
    }

    CheckEqual(4, create.GetParameters().Length, "CreateRuntimeContextResultV2 parameter count");
    var export = create.GetCustomAttribute<UnmanagedCallersOnlyAttribute>();
    Check(export is not null, "CreateRuntimeContextResultV2 is not an unmanaged export");
    CheckEqual<string?>(
        "expo_dotnet_create_runtime_context_result_v2",
        export?.EntryPoint,
        "NativeAOT export name"
    );
  }

  private static unsafe void AssertValidDecoding()
  {
    var root = Path.GetPathRoot(Environment.CurrentDirectory)!;
    var cache = Path.Combine(root, "expo-dotnet-abi-harness", "cache");
    var persistent = Path.Combine(root, "expo-dotnet-abi-harness", "files");
    // A non-ASCII path proves the decoder measures bytes, not characters.
    var unicode = Path.Combine(root, "expo-dotnet-abi-harness", "cachę-π");

    var unconfigured = DecodeAppDirectories(0);
    Check(unconfigured.CacheDirectory is null, "null struct pointer configured a cache directory");
    Check(
        unconfigured.PersistentFilesDirectory is null,
        "null struct pointer configured a persistent files directory"
    );

    WithDirectories(cache, persistent, (nint pointer) =>
    {
      var decoded = DecodeAppDirectories(pointer);
      CheckEqual(cache, decoded.CacheDirectory, "both configured: cache directory");
      CheckEqual(
          persistent,
          decoded.PersistentFilesDirectory,
          "both configured: persistent files directory"
      );
    });

    // Each directory is independent: an adapter that only knows a cache path must
    // not be forced to fabricate the other one.
    WithDirectories(cache, null, (nint pointer) =>
    {
      var decoded = DecodeAppDirectories(pointer);
      CheckEqual(cache, decoded.CacheDirectory, "cache only: cache directory");
      Check(
          decoded.PersistentFilesDirectory is null,
          "cache only: persistent files directory was configured"
      );
    });

    WithDirectories(null, persistent, (nint pointer) =>
    {
      var decoded = DecodeAppDirectories(pointer);
      Check(decoded.CacheDirectory is null, "persistent only: cache directory was configured");
      CheckEqual(
          persistent,
          decoded.PersistentFilesDirectory,
          "persistent only: persistent files directory"
      );
    });

    WithDirectories(unicode, null, (nint pointer) =>
    {
      var decoded = DecodeAppDirectories(pointer);
      CheckEqual(unicode, decoded.CacheDirectory, "non-ASCII cache directory round trip");
    });

    // Size is a lower bound, not an equality check, so a host built against a
    // later header that only appended fields still decodes.
    WithDirectories(cache, persistent, (nint pointer) =>
    {
      ((NativeAppDirectories*)pointer)->Size = (uint)sizeof(NativeAppDirectories) + 8u;
      var decoded = DecodeAppDirectories(pointer);
      CheckEqual(cache, decoded.CacheDirectory, "oversized struct: cache directory");
    });
  }

  // Every rejected input goes through the real unmanaged entry point, so the
  // structured error result and its release callback are exercised too.
  private static unsafe void AssertRejectedInputsProduceStructuredErrors()
  {
    var root = Path.GetPathRoot(Environment.CurrentDirectory)!;
    var cache = Path.Combine(root, "expo-dotnet-abi-harness", "cache");

    WithDirectories(cache, null, (nint pointer) =>
    {
      ((NativeAppDirectories*)pointer)->Size = (uint)sizeof(NativeAppDirectories) - 1u;
      CheckCreateFails(pointer, "too small", "undersized struct");
    });

    WithDirectories(cache, null, (nint pointer) =>
    {
      ((NativeAppDirectories*)pointer)->Version = ExpectedHostAbiVersion + 1u;
      CheckCreateFails(pointer, "ABI version mismatch", "wrong host ABI version");
    });

    WithDirectories(cache, null, (nint pointer) =>
    {
      ((NativeAppDirectories*)pointer)->CacheDirectoryLength = -1;
      CheckCreateFails(pointer, "negative byte length", "negative cache length");
    });

    WithDirectories(cache, null, (nint pointer) =>
    {
      var native = (NativeAppDirectories*)pointer;
      native->PersistentFilesDirectory = null;
      native->PersistentFilesDirectoryLength = 4;
      CheckCreateFails(
          pointer,
          "has a byte length but no data",
          "null pointer with a nonzero length"
      );
    });

    // A non-null pointer with zero length is a supplied empty string, not
    // "unconfigured", so it has to fail path validation instead of decoding to
    // null.
    WithDirectories(cache, string.Empty, (nint pointer) =>
    {
      var native = (NativeAppDirectories*)pointer;
      Check(
          native->PersistentFilesDirectory != null,
          "empty supplied path: the fixture did not pin a non-null pointer"
      );
      CheckEqual(0, native->PersistentFilesDirectoryLength, "empty supplied path: byte length");
      CheckCreateFails(pointer, "persistentFilesDirectory", "empty supplied path");
    });

    // Fully decodable but not app-scoped, so the managed model rejects it.
    WithDirectories("relative/cache", null, (nint pointer) =>
    {
      CheckCreateFails(pointer, "fully qualified", "relative cache directory");
    });

    // Invalid UTF-8 must surface as a decoding failure rather than replacement
    // characters. The framework owns the message text, so only its presence is
    // asserted.
    var invalidUtf8 = new byte[] { 0xFF, 0xFE };
    fixed (byte* invalid = invalidUtf8)
    {
      var native = new NativeAppDirectories
      {
        Size = (uint)sizeof(NativeAppDirectories),
        Version = ExpectedHostAbiVersion,
        CacheDirectory = invalid,
        CacheDirectoryLength = invalidUtf8.Length,
      };
      CheckCreateFails((nint)(&native), string.Empty, "invalid UTF-8 cache directory");
    }
  }

  private static unsafe void CheckCreateFails(
      nint directories,
      string expectedMessagePart,
      string label
  )
  {
    var create =
        (delegate* unmanaged[Cdecl]<nint, nint, nint, RuntimeContextResult*, void>)
            &CreateRuntimeContextResultV2;

    RuntimeContextResult result = default;
    // Zero API and runtime handles would fail too, but the directories decode
    // first, so the reported failure is the one under test.
    create(0, 0, directories, &result);

    try
    {
      CheckEqual(0, result.Ok, $"{label}: Ok");
      CheckEqual((nint)0, result.RuntimeContext, $"{label}: RuntimeContext");
      Check(result.Error.Message != null, $"{label}: no error message");
      Check(result.Error.MessageLength > 0, $"{label}: empty error message");
      Check(result.Error.Release != null, $"{label}: no error release callback");

      if (result.Error.Message == null || result.Error.MessageLength <= 0)
      {
        return;
      }

      var message = Encoding.UTF8.GetString(
          new ReadOnlySpan<byte>(result.Error.Message, result.Error.MessageLength)
      );
      Check(
          !message.Contains(ApiParameterMarker, StringComparison.Ordinal),
          $"{label}: the runtime was built before the directories were decoded ({message})"
      );
      Check(
          expectedMessagePart.Length == 0
              || message.Contains(expectedMessagePart, StringComparison.Ordinal),
          $"{label}: expected '{expectedMessagePart}' in '{message}'"
      );
    }
    finally
    {
      if (result.Error.Release != null)
      {
        result.Error.Release(result.Error.ReleaseContext);
      }
    }
  }

  // Pins UTF-8 bytes for both directories and hands over a populated struct. The
  // ABI borrows the strings for the duration of the call, so they stay alive only
  // inside the callback. Each buffer carries one extra byte that the reported
  // length excludes, so an empty string still yields a non-null pointer and a
  // decoder that scanned for a terminator instead of honoring the length would be
  // caught.
  private static unsafe void WithDirectories(
      string? cacheDirectory,
      string? persistentFilesDirectory,
      DirectoriesCallback body
  )
  {
    var cacheLength = cacheDirectory is null ? 0 : Encoding.UTF8.GetByteCount(cacheDirectory);
    var persistentLength =
        persistentFilesDirectory is null
            ? 0
            : Encoding.UTF8.GetByteCount(persistentFilesDirectory);

    fixed (byte* cache = BorrowedBytes(cacheDirectory))
    fixed (byte* persistent = BorrowedBytes(persistentFilesDirectory))
    {
      var native = new NativeAppDirectories
      {
        Size = (uint)sizeof(NativeAppDirectories),
        Version = ExpectedHostAbiVersion,
        CacheDirectory = cache,
        CacheDirectoryLength = cacheLength,
        PersistentFilesDirectory = persistent,
        PersistentFilesDirectoryLength = persistentLength,
      };
      body((nint)(&native));
    }
  }

  private static byte[]? BorrowedBytes(string? value)
  {
    if (value is null)
    {
      return null;
    }

    var encoded = Encoding.UTF8.GetBytes(value);
    var buffer = new byte[encoded.Length + 1];
    encoded.CopyTo(buffer, 0);
    buffer[encoded.Length] = (byte)'!';
    return buffer;
  }

  private delegate void DirectoriesCallback(nint directories);

  private static unsafe int OffsetOf(byte* origin, void* field) => (int)((byte*)field - origin);

  private static void Check(bool condition, string message)
  {
    if (!condition)
    {
      failures++;
      Console.Error.WriteLine($"FAIL {message}");
    }
  }

  private static void CheckEqual<T>(T expected, T actual, string label)
  {
    Check(
        EqualityComparer<T>.Default.Equals(expected, actual),
        $"{label}: expected '{expected}', got '{actual}'"
    );
  }
}
