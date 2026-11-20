using System;
using System.Collections.Generic;
using Expo.JSI;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Tests.Fixtures;

namespace Expo.ModulesCore.Tests.Modules;

public sealed record ConstantInfo(string Name, int Size);

[ExpoModule("ConstantProbe")]
public sealed partial class ConstantProbeModule
{
  private int constantCalls;
  private int plainCalls;
  private int flakyCalls;

  [JS]
  [Constant]
  public int Count => ++constantCalls;

  [JS]
  public int Plain => ++plainCalls;

  [JS]
  public int ConstantCalls => constantCalls;

  [JS]
  public int PlainCalls => plainCalls;

  [JS]
  public bool FailFlaky { get; set; }

  [JS]
  public int FlakyCalls => flakyCalls;

  [JS]
  [Constant]
  public int Flaky
  {
    get
    {
      flakyCalls++;
      if (FailFlaky)
      {
        throw new InvalidOperationException("flaky constant");
      }
      return 7;
    }
  }

  [JS]
  [Constant]
  public ConstantInfo Info => new("probe", 2);

  [JS]
  [Constant]
  public IReadOnlyList<int> Numbers => new[] { 1, 2, 3 };

  [JS]
  [Constant]
  public int Unread => ++constantCalls;
}

[ExpoSharedObject]
public sealed partial class ConstantThing : SharedObject
{
  private readonly int seed;
  private int tagCalls;

  [JS]
  public ConstantThing(int seed)
  {
    this.seed = seed;
  }

  [JS]
  [Constant]
  public int Tag => seed * 10 + ++tagCalls;

  [JS]
  public int TagCalls => tagCalls;

  [JS]
  public int Plain => seed;

  [JS]
  public int Echo(int value) => value;
}

[ExpoModule("ConstantThings", Classes = new[] { typeof(ConstantThing) })]
public sealed partial class ConstantThingsModule
{
}

internal sealed class ConstantFixture : IDisposable
{
  private DotnetRuntimeContext? context;

  private ConstantFixture(HermesRuntimeFixture fixture)
  {
    Fixture = fixture;
  }

  public HermesRuntimeFixture Fixture { get; }

  public static ConstantFixture Create()
  {
    var fixture = new ConstantFixture(HermesRuntimeFixture.Create());
    try
    {
      fixture.RegisterNewContext();
      return fixture;
    }
    catch
    {
      fixture.Dispose();
      throw;
    }
  }

  // Registers the test assembly's modules on a fresh context and binds `m` to the probe module
  // and `things` to the shared-object module.
  public void RegisterNewContext()
  {
    Fixture.Runtime.Execute(runtime =>
    {
      context = new DotnetRuntimeContext(runtime);
      using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules);
      using var setup = Fixture.Evaluate(
          "globalThis.m = globalThis._expoDotnet.modules.ConstantProbe; " +
          "globalThis.things = globalThis._expoDotnet.modules.ConstantThings; undefined",
          "constant-setup.js"
      );
      return true;
    });
  }

  public void TearDownContext()
  {
    Fixture.Runtime.Execute(_ =>
    {
      context!.Dispose();
      context = null;
      return true;
    });
  }

  public string Eval(string source) =>
      Fixture.Runtime.Execute(_ =>
      {
        using var result = Fixture.Evaluate(source, "constant-run.js");
        return result.AsString();
      });

  public void Run(string source) =>
      Fixture.Runtime.Execute(_ =>
      {
        using var result = Fixture.Evaluate(source, "constant-run.js");
        return true;
      });

  public void Dispose()
  {
    if (context is not null)
    {
      TearDownContext();
    }
    Fixture.Dispose();
  }
}
