import { describe, expect, it } from "vitest";

import { PathUtilities } from "../pathUtilities";

describe("PathUtilities", () => {
  it("joins onto a file URI and encodes later segments", () => {
    expect(PathUtilities.join("file:///docs/", "a b", "c#.txt")).toBe(
      "file:///docs/a%20b/c%23.txt",
    );
  });

  it("joins plain POSIX paths", () => {
    expect(PathUtilities.join("/docs", "..", "x.txt")).toBe("/x.txt");
  });

  it("converts a single Windows drive path to a file URI", () => {
    expect(PathUtilities.join("C:\\x\\a.txt")).toBe("file:///C:/x/a.txt");
  });

  it("joins onto a Windows drive path", () => {
    expect(PathUtilities.join("C:\\docs", "a.txt")).toBe(
      "file:///C:/docs/a.txt",
    );
  });

  it("keeps a hash in a Windows drive path as part of the name", () => {
    expect(PathUtilities.join("C:\\a\\x#1.txt")).toBe(
      "file:///C:/a/x%231.txt",
    );
  });

  it("resolves dot segments after a Windows drive path", () => {
    expect(PathUtilities.join("C:\\docs\\", "..", "a.txt")).toBe(
      "file:///C:/a.txt",
    );
  });

  it("joins onto a Windows UNC path", () => {
    expect(PathUtilities.join("\\\\server\\share\\d", "a b.txt")).toBe(
      "file://server/share/d/a%20b.txt",
    );
  });

  it("exposes basename, extname, and dirname", () => {
    expect(PathUtilities.basename("file:///docs/a.txt")).toBe("a.txt");
    expect(PathUtilities.extname("file:///docs/a.txt")).toBe(".txt");
    expect(PathUtilities.dirname("file:///docs/a.txt")).toBe("file:///docs");
  });
});
