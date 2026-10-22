// Ported from expo-file-system@58.0.7 (MIT).
import { Directory } from "./Directory";
import ExpoFileSystem from "./ExpoFileSystem";
import { Paths } from "./Paths";

/**
 * Represents a file on the filesystem.
 *
 * A `File` instance can be created for any path, and does not need to exist on the filesystem during creation.
 *
 * The constructor accepts an array of strings that are joined to create the file URI. The first argument can also be a `Directory` instance (like `Paths.cache`) or a `File` instance (which creates a new reference to the same file).
 * @example
 * ```ts
 * const file = new File(Paths.cache, "subdirName", "file.txt");
 * ```
 */
export class File extends ExpoFileSystem.FileSystemFile {
  /**
   * Creates an instance of a file. It can be created for any path, and does not need to exist on the filesystem during creation.
   *
   * The constructor accepts an array of strings that are joined to create the file URI. The first argument can also be a `Directory` instance (like `Paths.cache`) or a `File` instance (which creates a new reference to the same file).
   * @param uris An array of: `file:///` string URIs, `File` instances, and `Directory` instances representing an arbitrary location on the file system.
   * @example
   * ```ts
   * const file = new File(Paths.cache, "subdirName", "file.txt");
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
    return new Directory(Paths.dirname(this.uri));
  }

  /**
   * File extension.
   * @example '.png'
   */
  get extension() {
    return Paths.extname(this.uri);
  }

  /**
   * File name. Includes the extension.
   */
  get name() {
    return Paths.basename(this.uri);
  }
}
