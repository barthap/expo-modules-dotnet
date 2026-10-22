import type { Directory, File } from "./index";

type Assert<T extends true> = T;
type Absent<T, K extends PropertyKey> = K extends keyof T ? false : true;

type FileHasNoWrite = Assert<Absent<File, "write">>;
type FileHasNoBytes = Assert<Absent<File, "bytes">>;
type FileHasNoCopy = Assert<Absent<File, "copy">>;
type FileHasNoMove = Assert<Absent<File, "move">>;
type FileHasNoType = Assert<Absent<File, "type">>;
type DirectoryHasNoCopy = Assert<Absent<Directory, "copy">>;
type FileHasText = Assert<"text" extends keyof File ? true : false>;
type DirectoryHasList = Assert<"list" extends keyof Directory ? true : false>;

export type PublicApiChecks = [
  FileHasNoWrite,
  FileHasNoBytes,
  FileHasNoCopy,
  FileHasNoMove,
  FileHasNoType,
  DirectoryHasNoCopy,
  FileHasText,
  DirectoryHasList,
];
