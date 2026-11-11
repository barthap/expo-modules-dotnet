using System.Threading.Tasks;

namespace Expo.ModulesCore.Tests.Errors;

// One failure point per member. The kind argument picks the exception from CodedErrorCases.
[ExpoModule("CodedErrors", Classes = new[] { typeof(CodedErrorEntry) })]
[Events("onPing")]
public sealed partial class CodedErrorsModule : Module
{
  public CodedErrorsModule(DotnetRuntimeContext context)
      : base(context)
  {
  }

  [JS]
  public string Mode { get; set; } = "derived";

  [JS]
  public string FailingGetter => throw CodedErrorCases.Create(Mode);

  [JS]
  public string FailingSetter
  {
    get => string.Empty;
    set => throw CodedErrorCases.Create(value);
  }

  [JS]
  public void Fail(string kind) => throw CodedErrorCases.Create(kind);

  [JS]
  public Task FailBeforeTask(string kind) => throw CodedErrorCases.Create(kind);

  [OnStopObserving("onPing")]
  public void StopObservingPing() => throw new InvalidPathException("observing stopped");
}

[ExpoSharedObject("CodedErrorEntry")]
public sealed partial class CodedErrorEntry : SharedObject
{
  [JS]
  public CodedErrorEntry(string kind)
  {
    if (kind != "none")
    {
      throw CodedErrorCases.Create(kind);
    }
  }

  [JS]
  public void Fail(string kind) => throw CodedErrorCases.Create(kind);
}
