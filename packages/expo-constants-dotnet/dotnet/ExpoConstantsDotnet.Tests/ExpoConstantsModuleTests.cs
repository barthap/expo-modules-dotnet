using Expo.ModulesCore;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Testing;
using Xunit;

namespace ExpoConstantsDotnet.Tests;

public sealed class ExpoConstantsModuleTests
{
  [Fact]
  public void PlatformSelectionUsesOnlySupportedOperatingSystems()
  {
    Assert.Equal("windows", ExpoConstantsModule.RequireSupportedPlatform(true, false));
    Assert.Equal("macos", ExpoConstantsModule.RequireSupportedPlatform(false, true));
    Assert.Throws<PlatformNotSupportedException>(
        () => ExpoConstantsModule.RequireSupportedPlatform(false, false)
    );
  }

  [Fact]
  public void GeneratedProviderExposesOnlyTypedReadonlyConstants()
  {
    using var host = ExpoModuleTestHost.Create(
        AppDirectories.Unconfigured,
        new HostAppMetadata("1.0", "21"),
        ExpoModulesProvider_ExpoConstantsDotnet.Register
    );

    if (!IsSupportedDesktop())
    {
      foreach (var property in new[]
               {
                 "platform", "executionEnvironment", "sessionId", "nativeAppVersion",
                 "nativeBuildVersion", "expoVersion", "manifest",
               })
      {
        AssertUnsupportedPlatform(host, property);
      }
      return;
    }

    var values = host.Runtime.Execute(_ =>
    {
      using var result = host.Evaluate(
          "(() => { const c = globalThis._expoDotnet.modules.ExponentConstants; " +
          "return [Object.getOwnPropertyNames(c).sort().join(','), c.platform, " +
          "c.executionEnvironment, c.nativeAppVersion, c.nativeBuildVersion, " +
          "String(c.expoVersion), String(c.manifest)].join('|'); })()",
          "expo-constants-values.js"
      );
      return result.AsString();
    });

    var expectedPlatform = OperatingSystem.IsWindows() ? "windows" : "macos";
    Assert.Equal(
        "executionEnvironment,expoVersion,manifest,nativeAppVersion,nativeBuildVersion,platform,sessionId" +
        $"|{expectedPlatform}|bare|1.0|21|null|null",
        values
    );
  }

  [Fact]
  public void UnavailableVersionsStayNullAndStrictAssignmentFails()
  {
    using var host = ExpoModuleTestHost.Create(
        AppDirectories.Unconfigured,
        HostAppMetadata.Unconfigured,
        ExpoModulesProvider_ExpoConstantsDotnet.Register
    );

    if (!IsSupportedDesktop())
    {
      AssertUnsupportedPlatform(host, "nativeBuildVersion");
      return;
    }

    var result = host.Runtime.Execute(_ =>
    {
      using var value = host.Evaluate(
          "(() => { 'use strict'; const c = globalThis._expoDotnet.modules.ExponentConstants; " +
          "let rejected = false; try { c.nativeBuildVersion = 'bad'; } " +
          "catch (error) { rejected = error instanceof TypeError; } " +
          "return [c.nativeAppVersion === null, c.nativeBuildVersion === null, " +
          "Object.getOwnPropertyDescriptor(c, 'nativeBuildVersion').set === undefined, " +
          "rejected].join(':'); })()",
          "expo-constants-readonly.js"
      );
      return value.AsString();
    });

    Assert.Equal("true:true:true:true", result);
  }

  [Fact]
  public void SessionIdIsStableWithinOneRuntimeAndChangesOnReplacement()
  {
    string firstSession;
    using (var first = ExpoModuleTestHost.Create(
        ExpoModulesProvider_ExpoConstantsDotnet.Register
    ))
    {
      if (!IsSupportedDesktop())
      {
        AssertUnsupportedPlatform(first, "sessionId");
        return;
      }
      firstSession = ReadSession(first);
      Assert.Equal(firstSession, ReadSession(first));
    }

    using var second = ExpoModuleTestHost.Create(
        ExpoModulesProvider_ExpoConstantsDotnet.Register
    );
    var secondSession = ReadSession(second);
    Assert.True(Guid.TryParseExact(firstSession, "D", out _));
    Assert.True(Guid.TryParseExact(secondSession, "D", out _));
    Assert.NotEqual(firstSession, secondSession);
  }

  [Fact]
  public void ManifestReturnsTheAppConfigAndReadsTheFileOnce()
  {
    using var directory = new ResourcesDirectory();
    directory.Write("{\"name\":\"Example\",\"slug\":\"example\"}");
    using var host = CreateHost(directory.Path);
    if (!IsSupportedDesktop())
    {
      AssertUnsupportedPlatform(host, "manifest");
      return;
    }

    var first = ReadManifest(host);
    directory.Delete();
    var second = ReadManifest(host);

    Assert.Equal("{\"name\":\"Example\",\"slug\":\"example\"}", first);
    Assert.Equal(first, second);
  }

  [Fact]
  public void ManifestIsNullWithoutResourcesDirectoryOrFile()
  {
    using var directory = new ResourcesDirectory();
    using var withoutFile = CreateHost(directory.Path);
    using var withoutDirectory = CreateHost(null);
    if (!IsSupportedDesktop())
    {
      return;
    }

    Assert.Equal("null", ReadManifest(withoutFile));
    Assert.Equal("null", ReadManifest(withoutDirectory));
  }

  [Theory]
  [InlineData(new byte[] { 0x7B, 0xFF, 0x7D }, "not valid UTF-8")]
  [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0x7B, 0x7D }, "byte-order mark")]
  [InlineData(new byte[] { 0x5B, 0x5D }, "not a JSON object")]
  [InlineData(new byte[] { 0x7B, 0x22 }, "not a JSON object")]
  public void ManifestFailsWithAnErrorNamingTheFile(byte[] content, string reason)
  {
    using var directory = new ResourcesDirectory();
    directory.WriteBytes(content);
    using var host = CreateHost(directory.Path);
    if (!IsSupportedDesktop())
    {
      return;
    }

    var message = ReadManifestError(host);

    Assert.Contains(directory.FilePath, message);
    Assert.Contains(reason, message);
  }

  [Fact]
  public void ManifestRetriesAfterAFailedRead()
  {
    using var directory = new ResourcesDirectory();
    directory.Write("[]");
    using var host = CreateHost(directory.Path);
    if (!IsSupportedDesktop())
    {
      return;
    }

    Assert.Contains("not a JSON object", ReadManifestError(host));
    directory.Write("{\"name\":\"Fixed\"}");

    Assert.Equal("{\"name\":\"Fixed\"}", ReadManifest(host));
  }

  private static ExpoModuleTestHost CreateHost(string? resourcesDirectory) => ExpoModuleTestHost.Create(
      AppDirectories.Unconfigured,
      new HostAppMetadata(null, null, resourcesDirectory),
      ExpoModulesProvider_ExpoConstantsDotnet.Register
  );

  private static string ReadManifest(ExpoModuleTestHost host) => host.Runtime.Execute(_ =>
  {
    using var value = host.Evaluate(
        "String(globalThis._expoDotnet.modules.ExponentConstants.manifest)",
        "expo-constants-manifest.js"
    );
    return value.AsString();
  });

  private static string ReadManifestError(ExpoModuleTestHost host) => host.Runtime.Execute(_ =>
  {
    using var value = host.Evaluate(
        "(() => { try { void globalThis._expoDotnet.modules.ExponentConstants.manifest; " +
        "return 'no error'; } catch (error) { return String(error.message); } })()",
        "expo-constants-manifest-error.js"
    );
    return value.AsString();
  });

  private sealed class ResourcesDirectory : IDisposable
  {
    public ResourcesDirectory()
    {
      Path = Directory.CreateTempSubdirectory("expo-constants-resources-").FullName;
    }

    public string Path { get; }

    public string FilePath => System.IO.Path.Combine(Path, "app.config");

    public void Write(string content) => File.WriteAllText(FilePath, content);

    public void WriteBytes(byte[] content) => File.WriteAllBytes(FilePath, content);

    public void Delete() => File.Delete(FilePath);

    public void Dispose() => Directory.Delete(Path, recursive: true);
  }

  private static string ReadSession(ExpoModuleTestHost host) => host.Runtime.Execute(_ =>
  {
    using var value = host.Evaluate(
        "globalThis._expoDotnet.modules.ExponentConstants.sessionId",
        "expo-constants-session.js"
    );
    return value.AsString();
  });

  private static bool IsSupportedDesktop() => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

  private static void AssertUnsupportedPlatform(ExpoModuleTestHost host, string property)
  {
    var result = host.Runtime.Execute(_ =>
    {
      using var value = host.Evaluate(
          $"(() => {{ try {{ void globalThis._expoDotnet.modules.ExponentConstants.{property}; " +
          "return false; } catch (error) { return error instanceof Error && " +
          "error.message.includes('supports only Windows and macOS'); } })()",
          "expo-constants-unsupported.js"
      );
      return value.AsBool();
    });

    Assert.True(result, $"Expected {property} to fail with a catchable unsupported-platform error.");
  }
}
