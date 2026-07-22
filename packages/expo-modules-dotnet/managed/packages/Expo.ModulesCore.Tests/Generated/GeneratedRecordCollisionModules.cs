using System;
using System.Threading.Tasks;

namespace N15qp1ec1guuw83
{
  public readonly record struct Progress(int ModuleValue);
}

namespace N1sm951cf6akgv
{
  public readonly record struct Progress(string SharedValue);
}

namespace Expo.ModulesCore.Tests.Generated
{
  [ExpoModule("GeneratedRecordCollision", Classes = new[] { typeof(RecordCollisionEntry) })]
  public sealed partial class GeneratedRecordCollisionModule
  {
    [Event]
    public partial Func<global::N15qp1ec1guuw83.Progress, Task> OnModuleProgress { get; }

    [JS]
    public Task EmitModuleProgressAsync(int value) =>
        OnModuleProgress(new global::N15qp1ec1guuw83.Progress(value));
  }

  [ExpoSharedObject]
  public sealed partial class RecordCollisionEntry : SharedObject
  {
    [JS]
    public RecordCollisionEntry()
    {
    }

    [Event]
    public partial Func<global::N1sm951cf6akgv.Progress, Task> OnSharedProgress { get; }

    [JS]
    public Task EmitSharedProgressAsync(string value) =>
        OnSharedProgress(new global::N1sm951cf6akgv.Progress(value));
  }
}
