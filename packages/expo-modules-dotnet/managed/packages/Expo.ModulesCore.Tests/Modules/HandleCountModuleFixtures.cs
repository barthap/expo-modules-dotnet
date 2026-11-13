using System;
using Expo.JSI;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Tests.Fixtures;

namespace Expo.ModulesCore.Tests.Modules;

public sealed record HandleCountRecord12(
    double N1,
    double N2,
    double N3,
    double N4,
    double N5,
    double N6,
    string S1,
    string S2,
    string S3,
    string S4,
    string S5,
    string S6);

public sealed record HandleCountRecord13(
    double N1,
    double N2,
    double N3,
    double N4,
    double N5,
    double N6,
    double N7,
    string S1,
    string S2,
    string S3,
    string S4,
    string S5,
    string S6);

[ExpoModule("HandleCount")]
public sealed partial class HandleCountModule
{
  [JS]
  public void Take12(HandleCountRecord12 value)
  {
  }

  [JS]
  public void Take13(HandleCountRecord13 value)
  {
  }
}

internal sealed class HandleCountFixture : IDisposable
{
  private HandleCountFixture(HermesRuntimeFixture fixture)
  {
    Fixture = fixture;
  }

  public HermesRuntimeFixture Fixture { get; }

  public static HandleCountFixture Create()
  {
    var fixture = HermesRuntimeFixture.Create();
    try
    {
      fixture.Runtime.Execute(runtime =>
      {
        var context = new DotnetRuntimeContext(runtime);
        using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
        ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
        using var setup = fixture.Evaluate(
            "globalThis.m = globalThis._expoDotnet.modules.HandleCount; undefined",
            "handle-count-setup.js"
        );
        return true;
      });
      return new HandleCountFixture(fixture);
    }
    catch
    {
      fixture.Dispose();
      throw;
    }
  }

  public void Run(string source)
  {
    Fixture.Runtime.Execute(_ =>
    {
      using var result = Fixture.Evaluate(source, "handle-count-run.js");
      return true;
    });
  }

  public void Dispose() => Fixture.Dispose();
}
