using Expo.ModulesCore;

namespace ExpoCryptoDotnet;

// The upstream expo-crypto@58.0.7 ExpoCrypto native module. Digests, random bytes, and UUIDs use
// .NET built-ins only, so the module has no platform branch and no ABI surface.
[ExpoModule("ExpoCrypto")]
public sealed partial class ExpoCryptoModule : Module
{
  public ExpoCryptoModule(DotnetRuntimeContext context)
      : base(context)
  {
  }
}
