import { beforeEach, describe, expect, it, vi } from "vitest";

const native = vi.hoisted(() => {
  class FileSystemFile {
    uri: string;
    constructor(uri: string) {
      this.uri = uri.replace(/\/+$/, "");
    }
    validatePath() {}
  }
  class FileSystemDirectory {
    uri: string;
    constructor(uri: string) {
      this.uri = uri.endsWith("/") ? uri : `${uri}/`;
    }
    validatePath() {}
    listAsRecords() {
      return [
        { isDirectory: false, uri: `${this.uri}a.txt` },
        { isDirectory: true, uri: `${this.uri}sub/` },
      ];
    }
    createFile(name: string) {
      return new FileSystemFile(`${this.uri}${name}`);
    }
    createDirectory(name: string) {
      return new FileSystemDirectory(`${this.uri}${name}`);
    }
  }
  return {
    module: {
      FileSystemFile,
      FileSystemDirectory,
      documentDirectory: "file:///docs/",
      cacheDirectory: "file:///cache/",
      bundleDirectory: "/app/X.app",
      totalDiskSpace: 100,
      availableDiskSpace: 40,
      info: vi.fn(() => ({ exists: true, isDirectory: false })),
    },
  };
});

vi.mock("expo-modules-dotnet", () => ({
  DotnetSharedObject: class {},
  requireDotnetModule: vi.fn(() => native.module),
}));

describe("facade", () => {
  beforeEach(() => {
    native.module.info.mockClear();
  });

  it("builds files from directories and segments", async () => {
    const { Directory, File, Paths } = await import("../index");
    const file = new File(Paths.document, "notes", "a b.txt");
    expect(file).toBeInstanceOf(File);
    expect(file).toBeInstanceOf(native.module.FileSystemFile);
    expect(file.uri).toBe("file:///docs/notes/a%20b.txt");
    expect(file.name).toBe("a b.txt");
    expect(file.extension).toBe(".txt");
    expect(file.parentDirectory).toBeInstanceOf(Directory);
  });

  it("joins Windows native path segments into a directory URI", async () => {
    const { Directory } = await import("../index");
    expect(new Directory("C:\\data", "cache").uri).toMatch(/\/data\/cache\/$/);
  });

  it("maps listed records to facade instances", async () => {
    const { Directory, File } = await import("../index");
    const entries = new Directory("file:///docs/").list();
    expect(entries[0]).toBeInstanceOf(File);
    expect(entries[1]).toBeInstanceOf(Directory);
  });

  it("wraps created children", async () => {
    const { Directory, File } = await import("../index");
    const directory = new Directory("file:///docs/");
    expect(directory.createFile("a.txt", null)).toBeInstanceOf(File);
    expect(directory.createDirectory("sub")).toBeInstanceOf(Directory);
  });

  it("exposes Paths roots and joins info segments", async () => {
    const { Directory, Paths } = await import("../index");
    expect(Paths.cache).toBeInstanceOf(Directory);
    expect(Paths.bundle.uri).toBe("/app/X.app/");
    expect(Paths.totalDiskSpace).toBe(100);
    expect(Paths.availableDiskSpace).toBe(40);
    Paths.info("file:///docs", "a.txt");
    expect(native.module.info).toHaveBeenCalledWith("file:///docs/a.txt");
  });
});
