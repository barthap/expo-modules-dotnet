using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

public sealed class JavaScriptMicrotaskTests
{
  [Fact]
  public void QueueMicrotaskRunsAfterScriptEvaluationCheckpoint()
  {
    using var fixture = HermesRuntimeFixture.Create();

    using (fixture.Evaluate(
        """
        globalThis.done = false;
        globalThis.promiseValue = 0;
        queueMicrotask(function () {
          globalThis.done = true;
          globalThis.promiseValue = 42;
        });
        0;
        """,
        "promise-microtask.js"
    ))
    {
    }

    fixture.WaitUntilIdle();

    using var done = fixture.Evaluate("globalThis.done", "promise-microtask-done.js");
    using var value = fixture.Evaluate("globalThis.promiseValue", "promise-microtask-value.js");
    fixture.Runtime.Execute(_ =>
    {
      Assert.True(done.AsBool());
      Assert.Equal(42, value.AsDouble());
      return true;
    });
  }
}
