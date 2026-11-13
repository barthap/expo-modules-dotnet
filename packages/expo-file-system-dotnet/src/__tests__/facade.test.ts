import { beforeEach, describe, expect, it, vi } from "vitest";

const native = vi.hoisted(() => {
  const writes: { method: string; content: unknown; options: unknown }[] = [];
  class FileSystemFile {
    uri: string;
    constructor(uri: string) {
      this.uri = uri.replace(/\/+$/, "");
    }
    validatePath() {}
    write(content: unknown, options?: unknown) {
      writes.push({ method: "write", content, options });
      return Promise.resolve();
    }
    writeSync(content: unknown, options?: unknown) {
      writes.push({ method: "writeSync", content, options });
    }
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
    writes,
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
    native.writes.length = 0;
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

  it("passes strings, buffers, Uint8Arrays, and DataViews to native write unchanged", async () => {
    const { File } = await import("../index");
    const file = new File("file:///docs/a.bin");
    const buffer = new ArrayBuffer(4);
    const bytes = new Uint8Array(buffer, 1, 2);
    const dataView = new DataView(buffer);
    const options = { append: true };

    file.writeSync("text", options);
    file.writeSync(buffer);
    file.writeSync(bytes);
    // A DataView is not in the TypeScript type. It reaches native code, which rejects it.
    file.writeSync(dataView as unknown as Uint8Array);
    await file.write("async", options);

    expect(native.writes.map((call) => call.method)).toEqual([
      "writeSync",
      "writeSync",
      "writeSync",
      "writeSync",
      "write",
    ]);
    expect(native.writes[0].content).toBe("text");
    expect(native.writes[0].options).toBe(options);
    expect(native.writes[1].content).toBe(buffer);
    expect(native.writes[2].content).toBe(bytes);
    expect(native.writes[3].content).toBe(dataView);
    expect(native.writes[4]).toEqual({ method: "write", content: "async", options });
  });

  it("passes other typed arrays as a Uint8Array over the same bytes", async () => {
    const { File } = await import("../index");
    const file = new File("file:///docs/a.bin");
    const buffer = new ArrayBuffer(16);
    const int16 = new Int16Array(buffer, 2, 3);
    const float32 = new Float32Array(buffer, 4, 2);

    file.writeSync(int16 as unknown as Uint8Array);
    await file.write(float32 as unknown as Uint8Array, { encoding: "base64" });

    const [first, second] = native.writes.map((call) => call.content as Uint8Array);
    expect(first).toBeInstanceOf(Uint8Array);
    expect([first.buffer, first.byteOffset, first.byteLength]).toEqual([buffer, 2, 6]);
    expect(second).toBeInstanceOf(Uint8Array);
    expect([second.buffer, second.byteOffset, second.byteLength]).toEqual([buffer, 4, 8]);
    expect(native.writes[1].options).toEqual({ encoding: "base64" });
  });

  it("exports EncodingType with the upstream values", async () => {
    const { EncodingType } = await import("../index");
    expect(EncodingType.UTF8).toBe("utf8");
    expect(EncodingType.Base64).toBe("base64");
  });
});
