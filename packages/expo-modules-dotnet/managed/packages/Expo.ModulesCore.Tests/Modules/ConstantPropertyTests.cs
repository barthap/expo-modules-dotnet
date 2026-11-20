using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Modules;

// A constant runs its authored getter on the first successful read and then lives on the JavaScript
// receiver as plain data. These tests encode why: a repeated read must not cross into C#, and every
// failure path must stay retryable and leak-free.
// @ref docs/specs/modules-core-functions-and-properties.md "Constant Properties Read Their Getter Once"
public sealed class ConstantPropertyTests
{
  private const string Strict = "'use strict'; ";

  [Fact]
  public void ThreeReadsRunTheGetterOnce()
  {
    using var fixture = ConstantFixture.Create();

    Assert.Equal("1,1,1,1", fixture.Eval("[m.count, m.count, m.count, m.constantCalls].join(',')"));
  }

  [Fact]
  public void DescriptorChangesFromAccessorToDataAfterFirstRead()
  {
    using var fixture = ConstantFixture.Create();
    const string describe =
        "(() => { const d = Object.getOwnPropertyDescriptor(m, 'count'); " +
        "return [typeof d.get, typeof d.set, 'value' in d ? d.value : '-', d.enumerable, " +
        "String(d.writable), d.configurable].join(',') })()";

    Assert.Equal("function,undefined,-,true,undefined,true", fixture.Eval(describe));
    fixture.Run("m.count; undefined");
    Assert.Equal("undefined,undefined,1,true,false,true", fixture.Eval(describe));
  }

  [Fact]
  public void AssignmentToCachedConstantThrowsAndKeepsValue()
  {
    using var fixture = ConstantFixture.Create();

    Assert.Equal(
        "TypeError:1:1",
        fixture.Eval(
            Strict +
            "const first = m.count; let name = 'none'; " +
            "try { m.count = 99 } catch (e) { name = e.constructor.name } " +
            "[name, m.count, m.constantCalls].join(':')"
        )
    );
  }

  [Fact]
  public void ThrowingGetterIsReadAgainAndStaysAnAccessor()
  {
    using var fixture = ConstantFixture.Create();

    Assert.Equal(
        "Error:true:7:7:true:2",
        fixture.Eval(
            "m.failFlaky = true; let first = 'none'; " +
            "try { m.flaky } catch (e) { first = (e instanceof Error) ? 'Error' : 'other' } " +
            "const stillAccessor = typeof Object.getOwnPropertyDescriptor(m, 'flaky').get; " +
            "m.failFlaky = false; const second = m.flaky; const third = m.flaky; " +
            "[first, stillAccessor === 'function', second, " +
            "Object.getOwnPropertyDescriptor(m, 'flaky').value, " +
            "third === 7, m.flakyCalls].join(':')"
        )
    );
  }

  [Fact]
  public void ObjectConstantKeepsIdentity()
  {
    using var fixture = ConstantFixture.Create();

    Assert.Equal(
        "true:true:probe:3",
        fixture.Eval(
            "[m.info === m.info, m.numbers === m.numbers, m.info.name, m.numbers.length].join(':')"
        )
    );
  }

  [Fact]
  public void FrozenReceiverKeepsAccessorAndReturnsValue()
  {
    using var fixture = ConstantFixture.Create();

    Assert.Equal(
        "1:2:false:2",
        fixture.Eval(
            "const frozen = Object.freeze(Object.create(m)); " +
            "const a = frozen.count; const b = frozen.count; " +
            "[a, b, Object.prototype.hasOwnProperty.call(frozen, 'count'), m.constantCalls].join(':')"
        )
    );
  }

  [Fact]
  public void NonExtensibleModuleObjectStillCaches()
  {
    using var fixture = ConstantFixture.Create();

    Assert.Equal(
        "true:1:1:1",
        fixture.Eval(
            "Object.preventExtensions(m); const a = m.count; const b = m.count; " +
            "[Object.getOwnPropertyDescriptor(m, 'count').value === a, a, b, m.constantCalls].join(':')"
        )
    );
  }

  [Fact]
  public void ThrowingDefineFailsReadAndReleasesEveryWrapper()
  {
    // Both reads fail inside the host function, so native receives the thrown error instead of a
    // returned value and does not release that one handle. The baseline is a getter that throws
    // before the cache runs. A leak in CacheConstant or in the encoded value's failure path makes
    // the define-failure read leave more unreleased handles than the baseline.
    using var baseline = ConstantFixture.Create();
    baseline.Run("m.failFlaky = true; m.plain; undefined");
    baseline.Fixture.ResetCounters();
    baseline.Run("try { m.flaky } catch (e) { } undefined");
    baseline.Fixture.WaitUntilIdle();
    var baselineCounters = baseline.Fixture.Counters;
    var baselineUnreleased =
        baselineCounters.OwnedValuesCreatedLinked + baselineCounters.OwnedValuesCreatedUnlinked -
        baselineCounters.ReleasedValues;

    using var fixture = ConstantFixture.Create();
    fixture.Run("m.plain; undefined");
    fixture.Fixture.ResetCounters();
    var outcome = fixture.Eval(
        "const original = Reflect.defineProperty; let result; " +
        "Reflect.defineProperty = () => { throw new Error('define failed') }; " +
        "try { try { m.count; result = 'no throw' } catch (e) { result = (e instanceof Error) + ':' + e.message.includes('define failed') } } " +
        "finally { Reflect.defineProperty = original } " +
        "result + ':' + typeof Object.getOwnPropertyDescriptor(m, 'count').get"
    );
    fixture.Fixture.WaitUntilIdle();

    Assert.Equal("true:true:function", outcome);
    var c = fixture.Fixture.Counters;
    var unreleased = c.OwnedValuesCreatedLinked + c.OwnedValuesCreatedUnlinked - c.ReleasedValues;
    Assert.Equal(baselineUnreleased, unreleased);
  }

  [Fact]
  public void FirstReadReleasesEveryTemporaryWrapper()
  {
    using var fixture = ConstantFixture.Create();
    fixture.Run("m.plain; m.constantCalls; undefined");

    fixture.Fixture.ResetCounters();
    fixture.Run("m.count; undefined");
    fixture.Fixture.WaitUntilIdle();

    // The encoded value goes back to native as the host function result, and native frees it with
    // delete instead of release_value, so one created handle is not counted as released.
    var c = fixture.Fixture.Counters;
    Assert.Equal(
        c.OwnedValuesCreatedLinked + c.OwnedValuesCreatedUnlinked - 1,
        c.ReleasedValues
    );
  }

  [Fact]
  public void DerivedObjectCachesOnItself()
  {
    using var fixture = ConstantFixture.Create();

    Assert.Equal(
        "1:1:true:function:1",
        fixture.Eval(
            "const derived = Object.create(m); const a = derived.count; const b = derived.count; " +
            "[a, b, Object.prototype.hasOwnProperty.call(derived, 'count'), " +
            "typeof Object.getOwnPropertyDescriptor(m, 'count').get, m.constantCalls].join(':')"
        )
    );
  }

  [Fact]
  public void DetachedGetterCachesOnItsReceiver()
  {
    using var fixture = ConstantFixture.Create();

    Assert.Equal(
        "1:true:1:true",
        fixture.Eval(
            "const get = Object.getOwnPropertyDescriptor(m, 'count').get; const other = {}; " +
            "const value = get.call(other); " +
            "[value, Object.prototype.hasOwnProperty.call(other, 'count'), other.count, " +
            "typeof Object.getOwnPropertyDescriptor(m, 'count').get === 'function'].join(':')"
        )
    );
  }

  [Fact]
  public void NonObjectReceiverCachesNothing()
  {
    using var fixture = ConstantFixture.Create();

    Assert.Equal(
        "1:2:function",
        fixture.Eval(
            "const get = Object.getOwnPropertyDescriptor(m, 'count').get; " +
            "const a = get.call(undefined); const b = get.call(5); " +
            "[a, b, typeof Object.getOwnPropertyDescriptor(m, 'count').get].join(':')"
        )
    );
  }

  [Fact]
  public void UnmarkedGetterRunsOnEveryRead()
  {
    using var fixture = ConstantFixture.Create();

    Assert.Equal(
        "1,2,3:3:function",
        fixture.Eval(
            "[m.plain, m.plain, m.plain].join(',') + ':' + m.plainCalls + ':' + " +
            "typeof Object.getOwnPropertyDescriptor(m, 'plain').get"
        )
    );
  }

  [Fact]
  public void SharedObjectInstancesCacheTheirOwnValues()
  {
    using var fixture = ConstantFixture.Create();

    Assert.Equal(
        "11,11:21,21:1:1",
        fixture.Eval(
            "const a = new things.ConstantThing(1); const b = new things.ConstantThing(2); " +
            "[a.tag, a.tag].join(',') + ':' + [b.tag, b.tag].join(',') + ':' + a.tagCalls + ':' + b.tagCalls"
        )
    );
  }

  [Fact]
  public void SharedObjectConstantIsOwnAndNonEnumerable()
  {
    using var fixture = ConstantFixture.Create();

    Assert.Equal(
        "false:undefined:false:true:false:true:function",
        fixture.Eval(
            "const a = new things.ConstantThing(1); const before = Object.prototype.hasOwnProperty.call(a, 'tag'); " +
            "a.tag; const d = Object.getOwnPropertyDescriptor(a, 'tag'); " +
            "const proto = Object.getOwnPropertyDescriptor(things.ConstantThing.prototype, 'tag'); " +
            "[before, String(d.get), d.enumerable, Object.keys(a).indexOf('tag') < 0, d.writable, d.configurable, " +
            "typeof proto.get].join(':')"
        )
    );
  }

  [Fact]
  public void SharedObjectConstantReadBeforeReleaseSurvivesRelease()
  {
    using var fixture = ConstantFixture.Create();

    Assert.Equal(
        "11:11:1",
        fixture.Eval(
            "const a = new things.ConstantThing(1); const before = a.tag; const calls = a.tagCalls; a.release(); " +
            "[before, a.tag, calls].join(':')"
        )
    );
  }

  [Fact]
  public void SharedObjectConstantNotReadBeforeReleaseFails()
  {
    using var fixture = ConstantFixture.Create();

    var outcome = fixture.Eval(
        "const a = new things.ConstantThing(1); a.release(); const failures = []; " +
        "for (const read of [() => a.tag, () => a.plain, () => a.echo(1)]) { " +
        "try { read(); failures.push('no throw') } catch (e) { failures.push(e instanceof Error ? 'Error' : 'other') } } " +
        "failures.join(',')"
    );

    Assert.Equal("Error,Error,Error", outcome);
  }

  [Fact]
  public void CachedModuleConstantSurvivesContextTeardown()
  {
    using var fixture = ConstantFixture.Create();
    fixture.Run("globalThis.cachedValue = m.count; undefined");

    fixture.TearDownContext();

    Assert.Equal("1", fixture.Eval("String(m.count)"));
    var unread = fixture.Eval(
        "(() => { try { return String(m.unread) } catch (e) { return e instanceof Error ? 'Error' : 'other' } })()"
    );
    Assert.Equal("Error", unread);
  }

  [Fact]
  public void RegistrationOnNewContextReplacesCachedConstant()
  {
    using var fixture = ConstantFixture.Create();
    fixture.Run("globalThis.old = m; m.count; undefined");
    fixture.TearDownContext();

    fixture.RegisterNewContext();

    Assert.Equal(
        "function:1:1:2",
        fixture.Eval(
            "const d = Object.getOwnPropertyDescriptor(m, 'count'); const kind = typeof d.get; " +
            "const first = m.count; const second = m.count; " +
            "[kind, first, second, m.constantCalls + 1].join(':')"
        )
    );
  }
}
