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

    var values = host.Runtime.Execute(_ =>
    {
      using var result = host.Evaluate(
          "(() => { const c = globalThis._expoDotnet.modules.ExponentConstants; " +
          "return [Object.getOwnPropertyNames(c).sort().join(','), c.platform, " +
          "c.executionEnvironment, c.nativeAppVersion, c.nativeBuildVersion, " +
          "String(c.expoVersion)].join('|'); })()",
          "expo-constants-values.js"
      );
      return result.AsString();
    });

    var expectedPlatform = OperatingSystem.IsMacOS() ? "macos" : "windows";
    Assert.Equal(
        "executionEnvironment,expoVersion,nativeAppVersion,nativeBuildVersion,platform,sessionId" +
        $"|{expectedPlatform}|bare|1.0|21|null",
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

  private static string ReadSession(ExpoModuleTestHost host) => host.Runtime.Execute(_ =>
  {
    using var value = host.Evaluate(
        "globalThis._expoDotnet.modules.ExponentConstants.sessionId",
        "expo-constants-session.js"
    );
    return value.AsString();
  });
}
