using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Testing;
using Xunit;

namespace ExpoCryptoDotnet.Tests;

// Hermes-backed tests of the generated ExpoCrypto module. JavaScript ported from upstream
// expo-crypto passes the upstream raw strings ('SHA-1', 'hex') and expects the upstream digests,
// so these tests pin exact values and messages.
public sealed class ExpoCryptoModuleTests
{
  // The tail of the decode error for DigestAlgorithm (String Enums Cross As Declared JavaScript Values).
  private const string AlgorithmChoices = "It must be one of: 'MD5', 'SHA-1', 'SHA-256', 'SHA-384', 'SHA-512'";

  private const string Sha256OfExpo = "f5e5cae536b49d394e1e72d4368d64b00a23298ec5ae11f3a9102a540e2532dc";

  // Upstream android/src/test/java/expo/modules/crypto/ExpectedEncodingResults.kt, input "Expo".
  public static TheoryData<string, string, string> UpstreamVectors => new()
  {
    { "MD5", "c29f23f279126757ba18ec74d0d27cfa", "wp8j8nkSZ1e6GOx00NJ8+g==" },
    { "SHA-1", "c275355dc46ac171633935033d113e3872d595e5", "wnU1XcRqwXFjOTUDPRE+OHLVleU=" },
    { "SHA-256", "f5e5cae536b49d394e1e72d4368d64b00a23298ec5ae11f3a9102a540e2532dc", "9eXK5Ta0nTlOHnLUNo1ksAojKY7FrhHzqRAqVA4lMtw=" },
    { "SHA-384", "aa356c88afdbd8a7a5a9c3133541af0035e4b04e82438a223aa1939240ccb6b3ca28294b5ac0f42703b15183f4c016fc", "qjVsiK/b2KelqcMTNUGvADXksE6CQ4oiOqGTkkDMtrPKKClLWsD0JwOxUYP0wBb8" },
    { "SHA-512", "f1924f3e61aac4e4caa7fd566591b8abb541b0e642cd7bf0c71573267cfacf1b6dafe905bd6e42633bfba67c59774e070095e19a7c2078ac18ccd23245d76f1c", "8ZJPPmGqxOTKp/1WZZG4q7VBsOZCzXvwxxVzJnz6zxttr+kFvW5CYzv7pnxZd04HAJXhmnwgeKwYzNIyRddvHA==" },
  };

  // The module registers only under _expoDotnet.modules: no Expo global, no AES module.
  [Fact]
  public void ModuleRegistersOnlyUnderExpoDotnetModules()
  {
    using var host = CreateHost();

    Assert.Equal(
        "object,undefined,false",
        Run(host, "return [typeof m, typeof globalThis.expo, 'ExpoCryptoAES' in globalThis._expoDotnet.modules].join(',');"));
  }

  [Theory]
  [MemberData(nameof(UpstreamVectors))]
  public void DigestStringMatchesUpstreamVectors(string algorithm, string hex, string base64)
  {
    using var host = CreateHost();

    Assert.Equal(hex, Run(host, $"return m.digestString('{algorithm}', 'Expo', {{ encoding: 'hex' }});"));
    Assert.Equal(base64, Run(host, $"return m.digestString('{algorithm}', 'Expo', {{ encoding: 'base64' }});"));
  }

  // Upstream DigestOptions defaults encoding to hex, through a record field default.
  [Fact]
  public void DigestStringEncodingDefaultsToHex()
  {
    using var host = CreateHost();

    Assert.Equal(
        $"{Sha256OfExpo},{Sha256OfExpo}",
        Run(host, "return [m.digestString('SHA-256', 'Expo', {}), m.digestString('SHA-256', 'Expo', { encoding: undefined })].join(',');"));
  }

  // UTF-8 of "hé€😀" is 68 c3 a9 e2 82 ac f0 9f 98 80; the emoji is outside the BMP.
  [Fact]
  public void DigestStringHashesUtf8Bytes()
  {
    using var host = CreateHost();

    Assert.Equal(
        "39d15ee696a6f5bd03a5220ddcac864c6f6c2833b3ae973bfa10e202056fd284",
        Run(host, """return m.digestString('SHA-256', 'hé€😀', {});"""));
  }

  // Records core behavior, not module behavior: Hermes reads a lone surrogate as U+FFFD
  // (UTF-8 ef bf bd) before managed code sees the string, so it hashes like '�'.
  [Fact]
  public void DigestStringOfALoneSurrogateHashesTheReplacementCharacter()
  {
    using var host = CreateHost();

    Assert.Equal(
        "83d544ccc223c057d2bf80d3f2a32982c32c3c0db8e2674820da5064783fb097,true",
        Run(host, """
            const lone = m.digestString('SHA-256', '\uD800', {});
            return [lone, lone === m.digestString('SHA-256', '�', {})].join(',');
            """));
  }

  // MD2 and MD4 are in the JavaScript enum, but upstream Android native code rejects them too.
  [Theory]
  [InlineData("MD2")]
  [InlineData("MD4")]
  [InlineData("SHA-224")]
  [InlineData("SHA1")]
  [InlineData("sha-256")]
  public void DigestStringRejectsAlgorithmsOutsideTheNativeEnum(string algorithm)
  {
    using var host = CreateHost();

    Assert.Equal(
        $"true|false|'{algorithm}' is not a valid ExpoCryptoDotnet.DigestAlgorithm value. {AlgorithmChoices}",
        Catch(host, $"m.digestString('{algorithm}', 'Expo', {{}})"));
  }

  [Fact]
  public void DigestStringRejectsNonStringAlgorithmsAndUnknownEncodings()
  {
    using var host = CreateHost();

    Assert.EndsWith("Value is not a string.", Catch(host, "m.digestString(1, 'Expo', {})"), StringComparison.Ordinal);
    Assert.Equal(
        "true|false|'utf8' is not a valid ExpoCryptoDotnet.CryptoEncoding value. It must be one of: 'hex', 'base64'",
        Catch(host, "m.digestString('SHA-256', 'Expo', { encoding: 'utf8' })"));
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

  // Runs call and describes what it throws: "<is Error>|<has code>|<message>".
  private static string Catch(ExpoModuleTestHost host, string call) =>
      Run(host, $"try {{ {call}; return 'no error'; }} catch (e) {{ return [e instanceof Error, 'code' in e, e.message].join('|'); }}");
}
