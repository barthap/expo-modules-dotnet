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

  private static string DirUri(string path) =>
      FileSystemLocation.Parse(path, isDirectory: true, PathFlavors.Current).Uri;

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
}
