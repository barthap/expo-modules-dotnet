using Expo.JSI;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Modules;

// The testhost counts every value handle that an entry of the counted API table hands back, and the
// handle that evaluate_script returns. These tests pin what one generated record decode creates.
public sealed class HandleCountTests
{
  // Derivation for one `m.take12(r12)` call, from the generated record decoder and the bridge. The
  // counters are reset after the script that builds r12 and before the call, and the call script
  // ends in `undefined`.
  //   Borrowed: the decoder reads the argument once through get_argument_value, which returns a
  //     borrowed handle. That is 1.
  //   Linked owned: AsObject() on the argument calls value_retain_as and returns an owned object, 1.
  //     GetProperty("s1".."s6") calls object_get_property and returns an owned string each, 6. That
  //     is 7.
  //   Unlinked owned: GetProperty("n1".."n6") returns an owned number each, 6. The binding returns
  //     CreateUndefined(), which calls create_primitive_value, 1. The Run call evaluates a script
  //     whose completion value is `undefined`, and evaluate_script counts that owned handle, 1. That
  //     is 8.
  //   Return handles: the CreateUndefined() result goes back to native as the host function result,
  //     and native frees it with delete, not release_value. That is 1. Every other owned handle is
  //     released through release_value, including the script result, which Run disposes.
  private const uint ExpectedLinked12 = 7;
  private const uint ExpectedUnlinked12 = 8;
  private const uint ExpectedBorrowed12 = 1;
  private const uint ReturnHandles = 1;

  private const string Record12Script =
      "globalThis.r12 = { n1: 1, n2: 2, n3: 3, n4: 4, n5: 5, n6: 6, " +
      "s1: 'a', s2: 'b', s3: 'c', s4: 'd', s5: 'e', s6: 'f' }; " +
      "globalThis.r13 = { ...r12, n7: 7 }; undefined";

  [Fact]
  public void Record12DecodeCreatesExactHandleCounts()
  {
    using var fixture = HandleCountFixture.Create();
    fixture.Run(Record12Script);

    fixture.Fixture.ResetCounters();
    fixture.Run("m.take12(r12); undefined");
    fixture.Fixture.WaitUntilIdle();

    var c = fixture.Fixture.Counters;
    Assert.Equal(ExpectedLinked12, c.OwnedValuesCreatedLinked);
    Assert.Equal(ExpectedUnlinked12, c.OwnedValuesCreatedUnlinked);
    Assert.Equal(ExpectedBorrowed12, c.BorrowedValuesCreated);
  }

  [Fact]
  public void OneMoreNumberFieldCreatesExactlyOneMoreUnlinkedOwnedHandle()
  {
    using var fixture = HandleCountFixture.Create();
    fixture.Run(Record12Script);

    fixture.Fixture.ResetCounters();
    fixture.Run("m.take12(r12); undefined");
    fixture.Fixture.WaitUntilIdle();
    var c12 = fixture.Fixture.Counters;

    fixture.Fixture.ResetCounters();
    fixture.Run("m.take13(r13); undefined");
    fixture.Fixture.WaitUntilIdle();
    var c13 = fixture.Fixture.Counters;

    Assert.Equal(c12.OwnedValuesCreatedLinked, c13.OwnedValuesCreatedLinked);
    Assert.Equal(c12.OwnedValuesCreatedUnlinked + 1, c13.OwnedValuesCreatedUnlinked);
    Assert.Equal(c12.BorrowedValuesCreated, c13.BorrowedValuesCreated);
  }

  [Fact]
  public void OwnedHandlesCreatedEqualReleasedValuesAfterVoidCall()
  {
    using var fixture = HandleCountFixture.Create();
    fixture.Run(Record12Script);

    fixture.Fixture.ResetCounters();
    fixture.Run("m.take12(r12); undefined");
    fixture.Fixture.WaitUntilIdle();

    var c = fixture.Fixture.Counters;
    Assert.Equal(
        c.OwnedValuesCreatedLinked + c.OwnedValuesCreatedUnlinked - ReturnHandles,
        c.ReleasedValues);
  }

  [Fact]
  public void LinkedCountMatchesTrackedOwnedValuesWhileHeld()
  {
    using var fixture = HandleCountFixture.Create();
    var before = fixture.Fixture.Counters.OwnedValuesTracked;

    // One object, one string, and one number. The bridge links the object and the string into its
    // owned-value list and leaves the number out, so a change to that rule changes these counts.
    fixture.Fixture.ResetCounters();
    var held = fixture.Fixture.Runtime.Execute(_ => new[]
    {
      fixture.Fixture.Evaluate("({ held: true })", "handle-count-held-object.js"),
      fixture.Fixture.Evaluate("'held'", "handle-count-held-string.js"),
      fixture.Fixture.Evaluate("42", "handle-count-held-number.js"),
    });
    try
    {
      var c = fixture.Fixture.Counters;
      Assert.Equal(2u, c.OwnedValuesCreatedLinked);
      Assert.Equal(1u, c.OwnedValuesCreatedUnlinked);
      Assert.Equal(2u, c.OwnedValuesTracked - before);
      Assert.Equal(c.OwnedValuesCreatedLinked, c.OwnedValuesTracked - before);
    }
    finally
    {
      fixture.Fixture.Runtime.Execute(_ =>
      {
        foreach (var value in held)
        {
          value.Dispose();
        }
        return true;
      });
    }
  }

  [Fact]
  public void WeakObjectLockCountsTheValueHandleItReturns()
  {
    using var fixture = HandleCountFixture.Create();
    var before = fixture.Fixture.Counters.OwnedValuesTracked;

    // The lock result carries its owned value handle in a result struct of its own, so the counted
    // API table needs a wrapper for it. A locked object is linked into the owned-value list.
    var (target, weak) = fixture.Fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Fixture.Evaluate("({ weak: true })", "handle-count-weak.js");
      var targetObject = value.AsObject();
      return (targetObject, targetObject.CreateWeak());
    });
    JavaScriptObject? locked = null;
    try
    {
      fixture.Fixture.ResetCounters();
      locked = fixture.Fixture.Runtime.Execute(_ => weak.Lock());
      Assert.NotNull(locked);

      var c = fixture.Fixture.Counters;
      Assert.Equal(1u, c.OwnedValuesCreatedLinked);
      Assert.Equal(0u, c.OwnedValuesCreatedUnlinked);
      Assert.Equal(c.OwnedValuesCreatedLinked, c.OwnedValuesTracked - before - 1u);
    }
    finally
    {
      fixture.Fixture.Runtime.Execute(_ =>
      {
        locked?.Dispose();
        weak.Dispose();
        target.Dispose();
        return true;
      });
    }
  }
}
