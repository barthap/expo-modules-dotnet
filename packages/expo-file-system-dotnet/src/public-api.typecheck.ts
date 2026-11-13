import type {
  Directory,
  DirectoryCreateOptions,
  File,
  FileCreateOptions,
  FileDigestAlgorithm,
  FileWriteOptions,
  RelocationOptions,
} from "./index";
import { EncodingType } from "./index";
// @ts-expect-error The upstream package exports no CreateOptions.
import type { CreateOptions } from "./index";

type Assert<T extends true> = T;
type Absent<T, K extends PropertyKey> = K extends keyof T ? false : true;
type Present<T, K extends PropertyKey> = K extends keyof T ? true : false;

type FileHasNoType = Assert<Absent<File, "type">>;
type FileHasNoOpen = Assert<Absent<File, "open">>;
type FileHasText = Assert<Present<File, "text">>;
type DirectoryHasList = Assert<Present<Directory, "list">>;
type FileHasWrite = Assert<Present<File, "write" | "writeSync">>;
type FileHasBytes = Assert<Present<File, "bytes" | "bytesSync">>;
type FileHasCopyAndMove = Assert<Present<File, "copy" | "copySync" | "move" | "moveSync">>;
type DirectoryHasCopyAndMove = Assert<Present<Directory, "copy" | "copySync" | "move" | "moveSync">>;
type FileCreateHasNoIdempotent = Assert<Absent<FileCreateOptions, "idempotent">>;
type DirectoryCreateHasIdempotent = Assert<Present<DirectoryCreateOptions, "idempotent">>;
type ExpectedDigestAlgorithms = "MD5" | "SHA-1" | "SHA-256" | "SHA-384" | "SHA-512";
type DigestAlgorithms = Assert<
  [FileDigestAlgorithm] extends [ExpectedDigestAlgorithms]
    ? [ExpectedDigestAlgorithms] extends [FileDigestAlgorithm]
      ? true
      : false
    : false
>;
type WriteEncoding = Assert<EncodingType.Base64 extends FileWriteOptions["encoding"] ? true : false>;
type RelocationOverwrite = Assert<Present<RelocationOptions, "overwrite">>;

export type PublicApiChecks = [
  FileHasNoType,
  FileHasNoOpen,
  FileHasText,
  DirectoryHasList,
  FileHasWrite,
  FileHasBytes,
  FileHasCopyAndMove,
  DirectoryHasCopyAndMove,
  FileCreateHasNoIdempotent,
  DirectoryCreateHasIdempotent,
  DigestAlgorithms,
  WriteEncoding,
  RelocationOverwrite,
];
