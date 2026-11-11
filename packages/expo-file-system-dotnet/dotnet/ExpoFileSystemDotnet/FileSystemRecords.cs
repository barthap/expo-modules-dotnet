using System.Runtime.Serialization;
using Expo.ModulesCore;

namespace ExpoFileSystemDotnet;

public sealed record CreateOptions(bool Intermediates = false, bool Overwrite = false, bool Idempotent = false);

public sealed record InfoOptions(bool Md5 = false);

public enum EncodingType
{
  [EnumMember(Value = "utf8")]
  UTF8,

  [EnumMember(Value = "base64")]
  Base64,
}

public sealed record WriteOptions(EncodingType Encoding = EncodingType.UTF8, bool Append = false);

// The payloads cross as three different JavaScript kinds (string, ArrayBuffer, object), so the
// case order decides nothing.
[Union]
public abstract record WriteContent
{
  public sealed record Text(string Value) : WriteContent;

  public sealed record Buffer(ArrayBuffer Value) : WriteContent;

  public sealed record View(Uint8Array Value) : WriteContent;
}

public sealed record PathInfo(bool Exists, bool? IsDirectory);

public sealed record FileSystemFileInfo(
    bool Exists,
    string Uri,
    string? Md5,
    long? Size,
    long? ModificationTime,
    long? CreationTime
);

public sealed record FileSystemDirectoryInfo(
    bool Exists,
    string Uri,
    IReadOnlyList<string>? Files,
    long? Size,
    long? ModificationTime,
    long? CreationTime
);

public sealed record DirectoryEntryRecord(bool IsDirectory, string Uri);
