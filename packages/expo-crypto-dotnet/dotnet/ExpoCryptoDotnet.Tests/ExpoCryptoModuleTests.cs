using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Testing;
using Xunit;

namespace ExpoCryptoDotnet.Tests;

// Hermes-backed tests of the generated ExpoCrypto module. JavaScript ported from upstream
// expo-crypto passes the upstream raw strings ('SHA-1', 'hex') and expects the upstream digests,
// so these tests pin exact values and messages.
public sealed class ExpoCryptoModuleTests
{
  // The module registers only under _expoDotnet.modules: no Expo global, no AES module.
  [Fact]
  public void ModuleRegistersOnlyUnderExpoDotnetModules()
  {
    using var host = CreateHost();

    Assert.Equal(
        "object,undefined,false",
        Run(host, "return [typeof m, typeof globalThis.expo, 'ExpoCryptoAES' in globalThis._expoDotnet.modules].join(',');"));
  }

  private static ExpoModuleTestHost CreateHost() =>
      ExpoModuleTestHost.Create(ExpoModulesProvider_ExpoCryptoDotnet.Register);

  // Runs body as a function body with m bound to the ExpoCrypto module; body returns a string.
  private static string Run(ExpoModuleTestHost host, string body) =>
      host.Runtime.Execute(_ =>
      {
        using var value = host.Evaluate(
            "(() => { const m = globalThis._expoDotnet.modules.ExpoCrypto;\n" + body + "\n})()",
            "expo-crypto-module.js"
        );
        return value.AsString();
      });
}
