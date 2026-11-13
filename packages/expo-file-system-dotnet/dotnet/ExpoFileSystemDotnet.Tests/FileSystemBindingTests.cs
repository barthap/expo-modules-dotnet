using System.Text.Json;
using Expo.ModulesCore;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Testing;
using Xunit;

namespace ExpoFileSystemDotnet.Tests;

public sealed class FileSystemBindingTests
{
  private const string Mismatch =
      "ERR_UNION_CASE_MISMATCH|'WriteContent' expected string or ArrayBuffer or Uint8Array, but received ";

  private static string Eval(ExpoModuleTestHost host, string code) =>
      host.Runtime.Execute(_ =>
      {
        using var value = host.Evaluate(code, "file-system-binding.js");
        return value.AsString();
      });

  // Evaluates code that returns JSON.stringify of a string array.
  private static string[] EvalList(ExpoModuleTestHost host, string code) =>
      JsonSerializer.Deserialize<string[]>(Eval(host, code))!;

  // Evaluates code that returns a Promise of JSON.stringify of a string array.
  private static async Task<string[]> EvalListAsync(ExpoModuleTestHost host, string code)
  {
    var value = await host.EvaluatePromiseAsync(code, TestContext.Current.CancellationToken);
    var json = host.Runtime.Execute(_ =>
    {
      using (value)
      {
        return value.AsString();
      }
    });
    return JsonSerializer.Deserialize<string[]>(json)!;
  }

  private static string DirUri(string path) =>
      FileSystemLocation.Parse(path, isDirectory: true, PathFlavors.Current).Uri;

  // JavaScript helpers: `m` is the module, `dir` the directory URI, `err(fn)` gives
  // `code|message` of what `fn` throws (`(none)` when the error has no code), `rejection(p)` the
  // same for a Promise, and `hex(bytes)` the bytes as lowercase hex.
  private static string Prelude(string dir) =>
      $$"""
      const m = _expoDotnet.modules.FileSystem;
      const dir = '{{dir}}';
      const describe = (e) => ('code' in e ? e.code : '(none)') + '|' + e.message;
      const err = (fn) => { try { fn(); return 'no error'; } catch (e) { return describe(e); } };
      const rejection = (p) => p.then(() => 'no error', describe);
      const hex = (bytes) => Array.prototype.map
        .call(bytes, (b) => (b < 16 ? '0' : '') + b.toString(16))
        .join('');
      """;

  [Fact]
  public void JavaScriptSubclassKeepsItsPrototype()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    File.WriteAllText(Path.Combine(temp.Path, "a.txt"), "x");
    using var host = ExpoModuleTestHost.Create(ExpoModulesProvider_ExpoFileSystemDotnet.Register);

    // The testhost's Hermes does not compile ES6 `class` syntax, and Metro/Babel
    // emits this transpiled form (the `_callSuper` pattern) for apps.
    var result = Eval(
        host,
        $$"""
        (() => {
          const m = _expoDotnet.modules.FileSystem;
          const Base = m.FileSystemFile;
          function File(uri) {
            const self = Reflect.construct(Base, [uri], File);
            self.validatePath();
            return self;
          }
          Object.setPrototypeOf(File, Base);
          File.prototype = Object.create(Base.prototype, {
            constructor: { value: File, writable: true, configurable: true },
          });
          Object.defineProperty(File.prototype, 'name', {
            get() { return this.uri.split('/').pop(); },
            configurable: true,
          });
          const f = new File('{{DirUri(temp.Path)}}a.txt');
          return [
            Object.getPrototypeOf(f) === File.prototype,
            f instanceof File,
            f instanceof m.FileSystemFile,
            f.name,
            f.exists,
            f.textSync(),
          ].join(',');
        })()
        """
    );

    Assert.Equal("true,true,true,a.txt,true,x", result);
  }

  [Fact]
  public void LaterStepMembersAreAbsent()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    using var host = ExpoModuleTestHost.Create(ExpoModulesProvider_ExpoFileSystemDotnet.Register);

    var result = Eval(
        host,
        $$"""
        (() => {
          const m = _expoDotnet.modules.FileSystem;
          const f = new m.FileSystemFile('{{DirUri(temp.Path)}}a.txt');
          const d = new m.FileSystemDirectory('{{DirUri(temp.Path)}}');
          const absent = ['open', 'type'];
          return absent.filter((name) => name in f || name in d).join(',');
        })()
        """
    );

    Assert.Equal(string.Empty, result);
  }

  [Fact]
  public async Task AsyncFailureRejectsWithCatchableError()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    File.WriteAllText(Path.Combine(temp.Path, "a.txt"), "x");
    using var host = ExpoModuleTestHost.Create(ExpoModulesProvider_ExpoFileSystemDotnet.Register);

    var error = await Assert.ThrowsAsync<JavaScriptPromiseRejectedException>(
        () => host.EvaluatePromiseAsync(
            $"new _expoDotnet.modules.FileSystem.FileSystemFile('{DirUri(temp.Path)}a.txt').digest('SHA-3')",
            TestContext.Current.CancellationToken
        )
    );

    Assert.Contains("Unsupported digest algorithm: SHA-3", error.Message);
    Assert.Equal("ERR_UNSUPPORTED_DIGEST_ALGORITHM", error.JavaScriptCode);
  }

  [Fact]
  public void SyncFailureAndInvalidLocationThrowCatchableErrors()
  {
    using var platform = PlatformScope.Supported();
    using var host = ExpoModuleTestHost.Create(ExpoModulesProvider_ExpoFileSystemDotnet.Register);

    var result = Eval(
        host,
        """
        (() => {
          const m = _expoDotnet.modules.FileSystem;
          const messages = [];
          try { new m.FileSystemFile('relative/a.txt'); } catch (e) { messages.push(e.message); }
          try { new m.FileSystemFile('http://example.com/a'); } catch (e) { messages.push(e.message); }
          return messages.map((message) => message.includes('Invalid file system location')).join(',');
        })()
        """
    );

    Assert.Equal("true,true", result);
  }

  [Fact]
  public void CreateFileReturnsNativeInstanceAndOptionsDecode()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    using var host = ExpoModuleTestHost.Create(ExpoModulesProvider_ExpoFileSystemDotnet.Register);

    var result = Eval(
        host,
        $$"""
        (() => {
          const m = _expoDotnet.modules.FileSystem;
          const d = new m.FileSystemDirectory('{{DirUri(temp.Path)}}nested/deep');
          d.create({ intermediates: true });
          d.create({ idempotent: true });
          const f = d.createFile('a.txt', null);
          const info = f.info({ md5: true });
          const records = d.listAsRecords();
          return [
            f instanceof m.FileSystemFile,
            f.uri.endsWith('/nested/deep/a.txt'),
            info.exists,
            info.size,
            typeof info.md5,
            records.length,
            records[0].isDirectory,
          ].join(',');
        })()
        """
    );

    Assert.Equal("true,true,true,0,string,1,false", result);
  }

  // Behavior this item must keep: the messages of existing errors, uncoded I/O and host
  // failures, and the defaults of omitted, undefined, and null options.
  [Fact]
  public async Task ExistingMessagesAndUncodedFailuresAreKept()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    File.WriteAllText(Path.Combine(temp.Path, "a.txt"), "x");
    Directory.CreateDirectory(Path.Combine(temp.Path, "folder"));
    using var host = ExpoModuleTestHost.Create(ExpoModulesProvider_ExpoFileSystemDotnet.Register);
    var dir = DirUri(temp.Path);
    var root = DirUri(Path.GetPathRoot(temp.Path)!);

    var sync = Eval(
        host,
        $$"""
        (() => {
          const m = _expoDotnet.modules.FileSystem;
          const dir = '{{dir}}';
          const message = (fn) => { try { fn(); return 'no error'; } catch (e) { return e.message; } };
          const coded = (fn) => { try { fn(); return 'no error'; } catch (e) { return 'code' in e; } };
          return [
            message(() => new m.FileSystemFile(dir + 'a.txt').create()) === "File '" + dir + "a.txt' already exists",
            message(() => new m.FileSystemFile(dir + 'folder').textSync()),
            message(() => new m.FileSystemDirectory(dir + 'a.txt').info()),
            message(() => new m.FileSystemDirectory(dir + 'missing').delete()),
            message(() => new m.FileSystemFile(dir + 'a.txt').rename('../b.txt')),
            message(() => new m.FileSystemFile('relative/a.txt')),
            message(() => new m.FileSystemFile('{{root}}').validatePath()),
            coded(() => new m.FileSystemFile(dir + 'missing.txt').textSync()),
            coded(() => m.cacheDirectory),
          ].join('\n');
        })()
        """
    );
    var rejected = await Assert.ThrowsAsync<JavaScriptPromiseRejectedException>(
        () => host.EvaluatePromiseAsync(
            $"new _expoDotnet.modules.FileSystem.FileSystemFile('{dir}a.txt').digest('SHA-3')",
            TestContext.Current.CancellationToken));

    Assert.Equal(
        "true\n"
        + "A folder with the same name already exists in the file location\n"
        + "A file with the same name already exists in the directory location\n"
        + "Unable to delete file or directory: path does not exist\n"
        + "Invalid file name: '../b.txt'\n"
        + "Invalid file system location: 'relative/a.txt'\n"
        + "tried to create a file with a directory path\n"
        + "false\n"
        + "false",
        sync);
    Assert.Equal("Unsupported digest algorithm: SHA-3", rejected.Message);
  }

  [Fact]
  public void OmittedUndefinedAndNullOptionsTakeEveryDefault()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    File.WriteAllText(Path.Combine(temp.Path, "a.txt"), "keep");
    Directory.CreateDirectory(Path.Combine(temp.Path, "d"));
    using var host = ExpoModuleTestHost.Create(ExpoModulesProvider_ExpoFileSystemDotnet.Register);

    var result = Eval(
        host,
        $$"""
        (() => {
          const m = _expoDotnet.modules.FileSystem;
          const dir = '{{DirUri(temp.Path)}}';
          const fails = (fn) => { try { fn(); return false; } catch (e) { return true; } };
          const a = new m.FileSystemFile(dir + 'a.txt');
          const d = new m.FileSystemDirectory(dir + 'd');
          const created = [];
          ['b', 'c', 'e', 'f', 'g'].forEach((name, index) => {
            const f = new m.FileSystemFile(dir + name + '.txt');
            const options = [undefined, undefined, null, {}, { intermediates: undefined, overwrite: undefined }][index];
            if (index === 0) { f.create(); } else { f.create(options); }
            created.push(f.exists);
          });
          return [
            created.join(','),
            fails(() => a.create()),
            fails(() => a.create(null)),
            fails(() => a.create({})),
            fails(() => d.create(null)),
            fails(() => d.create({ idempotent: undefined })),
            a.info().md5 === null,
            a.info(undefined).md5 === null,
            a.info(null).md5 === null,
            a.info({}).md5 === null,
            typeof a.info({ md5: true }).md5,
          ].join('|');
        })()
        """
    );

    Assert.Equal("true,true,true,true,true|true|true|true|true|true|true|true|true|true|string", result);
    Assert.Equal("keep", File.ReadAllText(Path.Combine(temp.Path, "a.txt")));
  }

  [Fact]
  public async Task ErrorsCarryUpstreamCodes()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    File.WriteAllText(Path.Combine(temp.Path, "a.txt"), "x");
    Directory.CreateDirectory(Path.Combine(temp.Path, "folder"));
    using var host = ExpoModuleTestHost.Create(ExpoModulesProvider_ExpoFileSystemDotnet.Register);
    var dir = DirUri(temp.Path);

    var sync = EvalList(
        host,
        $$"""
        (() => {
          {{Prelude(dir)}}
          return JSON.stringify([
            err(() => new m.FileSystemFile(dir + 'a.txt').create()),
            err(() => new m.FileSystemFile(dir + 'folder').textSync()),
            err(() => new m.FileSystemFile(dir + 'x/y/a.txt').create()),
          ]);
        })()
        """
    );
    var rejected = await EvalListAsync(
        host,
        $$"""
        (() => {
          {{Prelude(dir)}}
          return rejection(new m.FileSystemFile(dir + 'folder').text())
            .then((text) => JSON.stringify([text]));
        })()
        """
    );

    Assert.Equal($"ERR_FILE_ALREADY_EXISTS|File '{dir}a.txt' already exists", sync[0]);
    Assert.Equal("ERR_INVALID_TYPE_FILE|A folder with the same name already exists in the file location", sync[1]);
    Assert.Equal(sync[1], rejected[0]);
    // FileSystemFile.Create wraps the .NET failure and keeps no cause, so the reason shows once.
    Assert.StartsWith("ERR_UNABLE_TO_CREATE|Unable to create file or directory: ", sync[2]);
    Assert.Contains(Path.Combine(temp.Path, "x", "y", "a.txt"), sync[2]);
    Assert.DoesNotContain("→ Caused by:", sync[2]);
  }

  [Fact]
  public void ExplicitNullInAnExistingOptionRecordFails()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    File.WriteAllText(Path.Combine(temp.Path, "a.txt"), "keep");
    Directory.CreateDirectory(Path.Combine(temp.Path, "d"));
    File.WriteAllText(Path.Combine(temp.Path, "d", "child.txt"), "c");
    using var host = ExpoModuleTestHost.Create(ExpoModulesProvider_ExpoFileSystemDotnet.Register);

    var result = EvalList(
        host,
        $$"""
        (() => {
          {{Prelude(DirUri(temp.Path))}}
          const missing = new m.FileSystemFile(dir + 'n.txt');
          const d = new m.FileSystemDirectory(dir + 'd');
          const a = new m.FileSystemFile(dir + 'a.txt');
          const results = [
            err(() => missing.create({ overwrite: null })),
            err(() => d.create({ idempotent: null })),
            err(() => a.info({ md5: null })),
            String(missing.exists),
          ];
          results.push(err(() => missing.create(null)), String(missing.exists), String(a.info(null).md5));
          return JSON.stringify(results);
        })()
        """
    );

    Assert.All(result[..3], error => Assert.Matches(@"^\(none\)\|.*Value is not a boolean\.$", error));
    Assert.Equal(new[] { "false", "no error", "true", "null" }, result[3..]);
    Assert.Equal("keep", File.ReadAllText(Path.Combine(temp.Path, "a.txt")));
    Assert.Equal("c", File.ReadAllText(Path.Combine(temp.Path, "d", "child.txt")));
  }

  [Fact]
  public async Task BytesReturnsAViewOverTheWholeFile()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    File.WriteAllBytes(Path.Combine(temp.Path, "all.bin"), Enumerable.Range(0, 256).Select(i => (byte)i).ToArray());
    File.WriteAllBytes(Path.Combine(temp.Path, "empty.bin"), []);
    Directory.CreateDirectory(Path.Combine(temp.Path, "folder"));
    using var host = ExpoModuleTestHost.Create(ExpoModulesProvider_ExpoFileSystemDotnet.Register);

    var result = await EvalListAsync(
        host,
        $$"""
        (() => {
          {{Prelude(DirUri(temp.Path))}}
          const shape = (b) => [b instanceof Uint8Array, b.byteOffset, b.length, b.buffer.byteLength, b[0], b[255]].join(':');
          const f = new m.FileSystemFile(dir + 'all.bin');
          const empty = new m.FileSystemFile(dir + 'empty.bin').bytesSync();
          const results = [
            shape(f.bytesSync()),
            empty.length + ':' + empty.buffer.byteLength,
            err(() => new m.FileSystemFile(dir + 'missing.bin').bytesSync()),
            err(() => new m.FileSystemFile(dir + 'folder').bytesSync()),
          ];
          return f.bytes().then((b) => JSON.stringify(results.concat([shape(b)])));
        })()
        """
    );

    Assert.Equal(
        new[]
        {
          "true:0:256:256:0:255",
          "0:0",
          $"(none)|Could not find file '{Path.Combine(temp.Path, "missing.bin")}'.",
          "ERR_INVALID_TYPE_FILE|A folder with the same name already exists in the file location",
          "true:0:256:256:0:255",
        },
        result);
  }

  [Fact]
  public void WriteSyncTakesEachContentKind()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    using var host = ExpoModuleTestHost.Create(ExpoModulesProvider_ExpoFileSystemDotnet.Register);

    var result = EvalList(
        host,
        $$"""
        (() => {
          {{Prelude(DirUri(temp.Path))}}
          const f = new m.FileSystemFile(dir + 'a.bin');
          const g = new m.FileSystemFile(dir + 'b.bin');
          const out = [];
          f.writeSync('zażółć', {});
          out.push(hex(f.bytesSync()));
          f.writeSync(new Uint8Array([1, 2, 3]).buffer, { encoding: undefined, append: undefined });
          out.push(hex(f.bytesSync()));
          const buf = new Uint8Array([0, 1, 2, 3, 4, 5, 6, 7]).buffer;
          f.writeSync(new Uint8Array(buf, 2, 3));
          out.push(hex(f.bytesSync()));
          // bytesSync returns a native-backed view; its buffer is native-backed too.
          g.writeSync(f.bytesSync());
          out.push(hex(g.bytesSync()));
          g.writeSync(f.bytesSync().buffer, { append: true });
          out.push(hex(g.bytesSync()));
          f.writeSync(new Uint8Array([9]), { encoding: 'base64' });
          out.push(hex(f.bytesSync()));
          f.writeSync('AQID\n', { encoding: 'base64' });
          out.push(hex(f.bytesSync()));
          out.push(err(() => f.writeSync('A', { encoding: 'base64' })));
          out.push(hex(f.bytesSync()));
          return JSON.stringify(out);
        })()
        """
    );

    var file = FileSystemLocation.Parse(Path.Combine(temp.Path, "a.bin"), isDirectory: false, PathFlavors.Current);
    Assert.Equal(
        new[]
        {
          "7a61c5bcc3b3c582c487",
          "010203",
          "020304",
          "020304",
          "020304020304",
          "09",
          "010203",
          $"ERR_UNABLE_TO_WRITE_BASE64_DATA|Unable to write base64 data to a file: {file.Uri}",
          "010203",
        },
        result);
  }

  // The facade converts other typed arrays to a Uint8Array. The vitest facade tests cover that
  // conversion against src/File.ts. This test covers what native code accepts.
  [Fact]
  public void NativeWriteTakesAUint8ArrayOverTypedArrayBytesAndRejectsTheTypedArray()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    using var host = ExpoModuleTestHost.Create(ExpoModulesProvider_ExpoFileSystemDotnet.Register);

    var result = EvalList(
        host,
        $$"""
        (() => {
          {{Prelude(DirUri(temp.Path))}}
          const view = new Int16Array(new Uint8Array([0, 1, 2, 3, 4, 5, 6, 7]).buffer, 2, 2);
          const bytes = new Uint8Array(view.buffer, view.byteOffset, view.byteLength);
          const accepted = new m.FileSystemFile(dir + 'a.bin');
          accepted.writeSync(bytes);
          const rejected = new m.FileSystemFile(dir + 'b.bin');
          return JSON.stringify([hex(accepted.bytesSync()), err(() => rejected.writeSync(view)), String(rejected.exists)]);
        })()
        """
    );

    Assert.Equal(
        new[] { "02030405", Mismatch + "object\n→ Caused by: Value is not a Uint8Array.", "false" },
        result);
  }

  [Fact]
  public async Task InvalidContentAndOptionsFailBeforeTheCall()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    File.WriteAllText(Path.Combine(temp.Path, "a.txt"), "keep");
    using var host = ExpoModuleTestHost.Create(ExpoModulesProvider_ExpoFileSystemDotnet.Register);
    var dir = DirUri(temp.Path);
    const string NotAView = "object\n→ Caused by: Value is not a Uint8Array.";
    const string BadEncoding =
        "(none)|'hex' is not a valid ExpoFileSystemDotnet.EncodingType value. It must be one of: 'utf8', 'base64'";

    var sync = EvalList(
        host,
        $$"""
        (() => {
          {{Prelude(dir)}}
          const f = new m.FileSystemFile(dir + 'a.txt');
          return JSON.stringify([
            err(() => f.writeSync(5)),
            err(() => f.writeSync({})),
            err(() => f.writeSync(null)),
            err(() => f.writeSync(undefined)),
            err(() => f.writeSync(new DataView(new ArrayBuffer(2)))),
            err(() => f.writeSync('x', { encoding: 'hex' })),
            err(() => f.writeSync('x', { append: null })),
            err(() => f.writeSync('x', { encoding: null })),
          ]);
        })()
        """
    );
    var rejected = await EvalListAsync(
        host,
        $$"""
        (() => {
          {{Prelude(dir)}}
          const f = new m.FileSystemFile(dir + 'a.txt');
          return Promise.all([
            rejection(f.write(5)),
            rejection(f.write(new DataView(new ArrayBuffer(2)))),
            rejection(f.write('x', { encoding: 'hex' })),
          ]).then(JSON.stringify);
        })()
        """
    );

    Assert.Equal(
        new[]
        {
          Mismatch + "number",
          Mismatch + NotAView,
          Mismatch + "null",
          Mismatch + "undefined",
          Mismatch + NotAView,
          BadEncoding,
        },
        sync[..6]);
    Assert.Matches(@"^\(none\)\|.*Value is not a boolean\.$", sync[6]);
    Assert.Matches(@"^\(none\)\|.*Value is not a string\.$", sync[7]);
    Assert.Equal(new[] { Mismatch + "number", Mismatch + NotAView, BadEncoding }, rejected);
    Assert.Equal("keep", File.ReadAllText(Path.Combine(temp.Path, "a.txt")));
  }

  [Fact]
  public async Task AsyncWriteCapturesTheContentAtTheCall()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    using var host = ExpoModuleTestHost.Create(ExpoModulesProvider_ExpoFileSystemDotnet.Register);

    var result = await EvalListAsync(
        host,
        $$"""
        (() => {
          {{Prelude(DirUri(temp.Path))}}
          const f = new m.FileSystemFile(dir + 'a.bin');
          const view = new Uint8Array([1, 2]);
          const p = f.write(view);
          view[0] = 9;
          const buffer = new Uint8Array([3, 4]).buffer;
          return p.then((value) => {
            const q = f.write(buffer, { append: true });
            new Uint8Array(buffer)[0] = 9;
            return q.then(() => JSON.stringify([String(value), hex(f.bytesSync())]));
          });
        })()
        """
    );

    Assert.Equal(new[] { "undefined", "01020304" }, result);
  }

  // Generated glue owns a leaf parameter and releases it once, after the call or after the Task.
  // The module must not dispose it as well.
  [Fact]
  public async Task WriteLeavesParameterLeavesToTheBinding()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    // A host initializes the native buffer interface that ArrayBuffer and Uint8Array use.
    using var host = ExpoModuleTestHost.Create(ExpoModulesProvider_ExpoFileSystemDotnet.Register);
    var path = Path.Combine(temp.Path, "a.bin");
    var file = new FileSystemFile(FileSystemLocation.Parse(path, isDirectory: false, PathFlavors.Current));
    using var view = Uint8Array.CopyFrom([1, 2]);
    using var buffer = ArrayBuffer.CopyFrom([3]);

    file.WriteSync(new WriteContent.View(view));
    await file.Write(new WriteContent.Buffer(buffer), new WriteOptions(Append: true));
    await file.Write(new WriteContent.View(view), new WriteOptions(Append: true));

    Assert.Equal(2, view.Length);
    Assert.Equal(1, buffer.ByteLength);
    Assert.Equal(new byte[] { 1, 2, 3, 1, 2 }, File.ReadAllBytes(path));
  }

  [Fact]
  public void WriteFailuresKeepTheirKind()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    Directory.CreateDirectory(Path.Combine(temp.Path, "app"));
    File.WriteAllText(Path.Combine(temp.Path, "app", "a.txt"), "A");
    using var bundle = FileSystemRoots.OverrideBundleForTests(Path.Combine(temp.Path, "app"));
    using var host = ExpoModuleTestHost.Create(ExpoModulesProvider_ExpoFileSystemDotnet.Register);
    var dir = DirUri(temp.Path);

    var result = EvalList(
        host,
        $$"""
        (() => {
          {{Prelude(dir)}}
          return JSON.stringify([
            err(() => new m.FileSystemFile(dir + 'missing/a.txt').writeSync('x')),
            err(() => new m.FileSystemFile(dir + 'app/a.txt').writeSync('x')),
          ]);
        })()
        """
    );

    Assert.Equal(
        $"(none)|Could not find a part of the path '{Path.Combine(temp.Path, "missing", "a.txt")}'.",
        result[0]);
    Assert.Equal($"ERR_MISSING_PERMISSION|Missing permission for uri: {dir}app/a.txt", result[1]);
    Assert.Equal("A", File.ReadAllText(Path.Combine(temp.Path, "app", "a.txt")));
  }

  [Fact]
  public async Task CopyAndMoveResolveAndUpdateTheUri()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    File.WriteAllText(Path.Combine(temp.Path, "a.txt"), "A");
    Directory.CreateDirectory(Path.Combine(temp.Path, "d"));
    using var host = ExpoModuleTestHost.Create(ExpoModulesProvider_ExpoFileSystemDotnet.Register);
    var dir = DirUri(temp.Path);

    var result = await EvalListAsync(
        host,
        $$"""
        (() => {
          {{Prelude(dir)}}
          const a = new m.FileSystemFile(dir + 'a.txt');
          const d = new m.FileSystemDirectory(dir + 'd');
          const missing = new m.FileSystemDirectory(dir + 'missing');
          const results = [];
          return a.copy(d, { overwrite: undefined })
            .then((copied) => { results.push(String(copied)); return a.move(d, { overwrite: true }); })
            .then((moved) => {
              results.push(String(moved), a.uri);
              return rejection(a.move(missing));
            })
            .then((failure) => {
              results.push(failure, a.uri);
              return rejection(new m.FileSystemFile(dir + 'd/a.txt').copy(missing, {}));
            })
            .then((failure) => JSON.stringify(results.concat([failure])));
        })()
        """
    );

    Assert.Equal(
        new[]
        {
          "undefined",
          "undefined",
          $"{dir}d/a.txt",
          "ERR_DESTINATION_DOES_NOT_EXIST|The destination path does not exist",
          $"{dir}d/a.txt",
          "ERR_DESTINATION_DOES_NOT_EXIST|The destination path does not exist",
        },
        result);
    Assert.Equal("A", File.ReadAllText(Path.Combine(temp.Path, "d", "a.txt")));
    Assert.False(File.Exists(Path.Combine(temp.Path, "a.txt")));
  }

  [Fact]
  public void BundleTreeRejectsMovesButIsACopySource()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    Directory.CreateDirectory(Path.Combine(temp.Path, "app"));
    File.WriteAllText(Path.Combine(temp.Path, "app", "a.txt"), "A");
    Directory.CreateDirectory(Path.Combine(temp.Path, "out"));
    using var bundle = FileSystemRoots.OverrideBundleForTests(Path.Combine(temp.Path, "app"));
    using var host = ExpoModuleTestHost.Create(ExpoModulesProvider_ExpoFileSystemDotnet.Register);
    var dir = DirUri(temp.Path);

    var result = EvalList(
        host,
        $$"""
        (() => {
          {{Prelude(dir)}}
          const a = new m.FileSystemFile(dir + 'app/a.txt');
          const out = new m.FileSystemDirectory(dir + 'out');
          const results = [
            err(() => a.moveSync(out)),
            err(() => new m.FileSystemFile(dir + 'out/b.txt').copySync(new m.FileSystemDirectory(dir + 'app'))),
          ];
          a.copySync(out);
          results.push(hex(a.bytesSync()));
          return JSON.stringify(results);
        })()
        """
    );

    Assert.Equal(
        new[]
        {
          $"ERR_MISSING_PERMISSION|Missing permission for uri: {dir}app/a.txt",
          $"ERR_MISSING_PERMISSION|Missing permission for uri: {dir}app/",
          "41",
        },
        result);
    Assert.Equal("A", File.ReadAllText(Path.Combine(temp.Path, "app", "a.txt")));
    Assert.Equal("A", File.ReadAllText(Path.Combine(temp.Path, "out", "a.txt")));
  }
}
