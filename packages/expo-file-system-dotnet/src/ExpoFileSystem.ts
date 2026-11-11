import { DotnetSharedObject, requireDotnetModule } from "expo-modules-dotnet";

export type FileCreateOptions = {
  /**
   * Whether to create intermediate directories if they do not exist.
   * @default false
   */
  intermediates?: boolean;
  /**
   * Whether to overwrite the file if it exists.
   * @default false
   */
  overwrite?: boolean;
};

export type DirectoryCreateOptions = {
  /**
   * Whether to create intermediate directories if they do not exist.
   * @default false
   */
  intermediates?: boolean;
  /**
   * Whether to overwrite the directory if it exists.
   * @default false
   */
  overwrite?: boolean;
  /**
   * This flag controls whether the `create` operation is idempotent
   * (safe to call multiple times without error).
   *
   * If `true`, creating a directory that already exists will succeed silently.
   * If `false`, an error will be thrown when the target directory already exists.
   *
   * @default false
   */
  idempotent?: boolean;
};

export type FileDigestAlgorithm = "MD5" | "SHA-1" | "SHA-256" | "SHA-384" | "SHA-512";

/**
 * Options for moving or copying files and directories.
 */
export type RelocationOptions = {
  /**
   * Whether to overwrite the destination if it exists.
   * @default false
   */
  overwrite?: boolean;
};

export enum EncodingType {
  /**
   * Standard encoding format.
   */
  UTF8 = "utf8",
  /**
   * Binary, radix-64 representation.
   */
  Base64 = "base64",
}

export type FileWriteOptions = {
  /**
   * The encoding format to use when writing the file.
   * @default EncodingType.UTF8
   */
  encoding?: EncodingType | "utf8" | "base64";
  /**
   * Whether to append the contents to the end of the file or overwrite the existing file.
   * @default false
   */
  append?: boolean;
};

export type InfoOptions = { md5?: boolean };

export type FileInfo = {
  exists: boolean;
  uri?: string;
  md5?: string | null;
  size?: number | null;
  modificationTime?: number | null;
  creationTime?: number | null;
};

export type DirectoryInfo = {
  exists: boolean;
  uri?: string;
  files?: string[] | null;
  size?: number | null;
  modificationTime?: number | null;
  creationTime?: number | null;
};

export type PathInfo = { exists: boolean; isDirectory: boolean | null };

export declare class NativeFile extends DotnetSharedObject {
  constructor(uri: string);
  readonly uri: string;
  readonly exists: boolean;
  readonly size: number;
  readonly md5: string | null;
  readonly modificationTime: number | null;
  readonly lastModified: number | null;
  readonly creationTime: number | null;
  validatePath(): void;
  create(options?: FileCreateOptions): void;
  delete(): void;
  rename(newName: string): void;
  info(options?: InfoOptions): FileInfo;
  textSync(): string;
  text(): Promise<string>;
  base64Sync(): string;
  base64(): Promise<string>;
  bytesSync(): Uint8Array;
  bytes(): Promise<Uint8Array<ArrayBuffer>>;
  writeSync(content: string | Uint8Array | ArrayBuffer, options?: FileWriteOptions): void;
  write(content: string | Uint8Array | ArrayBuffer, options?: FileWriteOptions): Promise<void>;
  digest(algorithm: FileDigestAlgorithm): Promise<string>;
  copySync(destination: NativeFile | NativeDirectory, options?: RelocationOptions): void;
  copy(destination: NativeFile | NativeDirectory, options?: RelocationOptions): Promise<void>;
  moveSync(destination: NativeFile | NativeDirectory, options?: RelocationOptions): void;
  move(destination: NativeFile | NativeDirectory, options?: RelocationOptions): Promise<void>;
}

export declare class NativeDirectory extends DotnetSharedObject {
  constructor(uri: string);
  readonly uri: string;
  readonly exists: boolean;
  readonly size: number | null;
  validatePath(): void;
  create(options?: DirectoryCreateOptions): void;
  delete(): void;
  rename(newName: string): void;
  info(): DirectoryInfo;
  copySync(destination: NativeFile | NativeDirectory, options?: RelocationOptions): void;
  copy(destination: NativeFile | NativeDirectory, options?: RelocationOptions): Promise<void>;
  moveSync(destination: NativeFile | NativeDirectory, options?: RelocationOptions): void;
  move(destination: NativeFile | NativeDirectory, options?: RelocationOptions): Promise<void>;
  listAsRecords(): { isDirectory: boolean; uri: string }[];
  createFile(name: string, content: string | null): NativeFile;
  createDirectory(name: string): NativeDirectory;
}

type FileSystemNativeModule = {
  FileSystemFile: typeof NativeFile;
  FileSystemDirectory: typeof NativeDirectory;
  readonly documentDirectory: string;
  readonly cacheDirectory: string;
  readonly bundleDirectory: string;
  readonly totalDiskSpace: number | null;
  readonly availableDiskSpace: number | null;
  info(uri: string): PathInfo;
};

export default requireDotnetModule<FileSystemNativeModule>("FileSystem");
