// Ported from expo-file-system@58.0.7 (MIT).
import ExpoFileSystem from "./ExpoFileSystem";
import { File } from "./File";
import { Paths } from "./Paths";

/**
 * Represents a directory on the filesystem.
 *
 * A `Directory` instance can be created for any path, and does not need to exist on the filesystem during creation.
 *
 * The constructor accepts an array of strings that are joined to create the directory URI. The first argument can also be a `Directory` instance (like `Paths.cache`).
 * @example
 * ```ts
 * const directory = new Directory(Paths.cache, "subdirName");
 * ```
 */
export class Directory extends ExpoFileSystem.FileSystemDirectory {
  /**
   * Creates an instance of a directory. It can be created for any path, and does not need to exist on the filesystem during creation.
   *
   * The constructor accepts an array of strings that are joined to create the directory URI. The first argument can also be a `Directory` instance (like `Paths.cache`).
   * @param uris An array of: `file:///` string URIs, `File` instances, and `Directory` instances representing an arbitrary location on the file system.
   * @example
   * ```ts
   * const directory = new Directory(Paths.cache, "subdirName");
   * ```
   */
  constructor(...uris: (string | File | Directory)[]) {
    super(Paths.join(...uris));
    this.validatePath();
  }

  /*
   * Directory containing the file.
   */
  get parentDirectory() {
    return new Directory(Paths.join(this.uri, ".."));
  }

  /**
   * Lists the contents of a directory.
   * Calling this method if the parent directory does not exist will throw an error.
   * @returns An array of `Directory` and `File` instances.
   */
  list(): (Directory | File)[] {
    return super
      .listAsRecords()
      .map(({ isDirectory, uri }) =>
        isDirectory ? new Directory(uri) : new File(uri),
      );
  }

  /**
   * Directory name.
   */
  get name() {
    return Paths.basename(this.uri);
  }

  createFile(name: string, mimeType: string | null): File {
    return new File(super.createFile(name, mimeType).uri);
  }

  createDirectory(name: string): Directory {
    return new Directory(super.createDirectory(name).uri);
  }
}
