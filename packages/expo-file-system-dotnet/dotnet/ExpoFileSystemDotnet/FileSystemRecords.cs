namespace ExpoFileSystemDotnet;

public sealed record CreateOptions(bool Intermediates = false, bool Overwrite = false, bool Idempotent = false);

public sealed record InfoOptions(bool Md5 = false);

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
