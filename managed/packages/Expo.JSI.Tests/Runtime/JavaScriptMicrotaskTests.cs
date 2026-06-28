using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

public sealed class JavaScriptMicrotaskTests
{
  [Fact]
  public void PromiseThenRunsAfterScriptEvaluationCheckpoint()
  {
    using var fixture = HermesRuntimeFixture.Create();

    using (fixture.Evaluate(
        """
        globalThis.done = false;
        globalThis.promiseValue = 0;
        Promise.resolve(42).then(function (value) {
          globalThis.done = true;
          globalThis.promiseValue = value;
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
