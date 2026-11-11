using System.Text.Json;
using Expo.ModulesCore;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Testing;
using Xunit;

namespace ExpoFileSystemDotnet.Tests;

public sealed class FileSystemBindingTests
{
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
          const absent = ['write', 'writeSync', 'bytes', 'bytesSync', 'copy', 'copySync',
            'move', 'moveSync', 'open', 'type'];
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
}
