import { DotnetSharedObject, requireDotnetModule } from "expo-modules-dotnet";

export type CreateOptions = {
  intermediates?: boolean;
  overwrite?: boolean;
  idempotent?: boolean;
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
  create(options?: CreateOptions): void;
  delete(): void;
  rename(newName: string): void;
  info(options?: InfoOptions): FileInfo;
  textSync(): string;
  text(): Promise<string>;
  base64Sync(): string;
  base64(): Promise<string>;
  digest(
    algorithm: "MD5" | "SHA-1" | "SHA-256" | "SHA-384" | "SHA-512",
  ): Promise<string>;
}

export declare class NativeDirectory extends DotnetSharedObject {
  constructor(uri: string);
  readonly uri: string;
  readonly exists: boolean;
  readonly size: number | null;
  validatePath(): void;
  create(options?: CreateOptions): void;
  delete(): void;
  rename(newName: string): void;
  info(): DirectoryInfo;
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
