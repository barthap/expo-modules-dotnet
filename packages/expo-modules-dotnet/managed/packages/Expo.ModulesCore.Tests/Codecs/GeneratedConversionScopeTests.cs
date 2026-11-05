using System;
using System.Collections.Generic;
using Expo.ModulesCore.Codecs;
using Xunit;

namespace Expo.ModulesCore.Tests.Codecs;

public sealed class GeneratedConversionScopeTests
{
  [Fact]
  public void EmptyScopeDisposesWithoutFailure()
  {
    new GeneratedConversionScope().Dispose();
  }

  [Fact]
  public void OneLeafReleasesOnce()
  {
    var scope = new GeneratedConversionScope();
    var leaf = new RecordingLeaf();
    scope.Register(leaf);

    scope.Dispose();

    Assert.Equal(1, leaf.DisposeCount);
  }

  [Fact]
  public void SecondScopeDisposeReleasesNothingAgain()
  {
    var scope = new GeneratedConversionScope();
    var a = new RecordingLeaf();
    var b = new RecordingLeaf();
    scope.Register(a);
    scope.Register(b);

    scope.Dispose();
    scope.Dispose();

    Assert.Equal(1, a.DisposeCount);
    Assert.Equal(1, b.DisposeCount);
  }

  [Fact]
  public void DuplicateRegistrationDisposesEachRegistrationInOrder()
  {
    // Register appends without a duplicate scan, so the scope disposes a leaf once per
    // registration. Leaf Dispose is idempotent, which makes the repeat a no-op on the real leaves.
    var order = new List<RecordingLeaf>();
    var scope = new GeneratedConversionScope();
    var a = new RecordingLeaf(order);
    var b = new RecordingLeaf(order);
    scope.Register(a);
    scope.Register(b);
    scope.Register(a);

    scope.Dispose();

    Assert.Equal([a, b, a], order);
    Assert.Equal(2, a.DisposeCount);
    Assert.Equal(1, b.DisposeCount);
  }

  [Fact]
  public void EqualButDistinctLeavesBothRelease()
  {
    // Two distinct wrappers can compare equal while owning separate native references, so each
    // one must release.
    var scope = new GeneratedConversionScope();
    var a = new RecordingLeaf();
    var b = new RecordingLeaf();
    Assert.True(a.Equals(b));
    scope.Register(a);
    scope.Register(b);

    scope.Dispose();

    Assert.Equal(1, a.DisposeCount);
    Assert.Equal(1, b.DisposeCount);
  }

  [Fact]
  public void LeavesReleaseInRegistrationOrder()
  {
    var order = new List<RecordingLeaf>();
    var scope = new GeneratedConversionScope();
    var first = new RecordingLeaf(order);
    var second = new RecordingLeaf(order);
    var third = new RecordingLeaf(order);
    scope.Register(first);
    scope.Register(second);
    scope.Register(third);

    scope.Dispose();

    Assert.Equal([first, second, third], order);
  }

  [Fact]
  public void RegistrationAfterDisposalThrows()
  {
    var scope = new GeneratedConversionScope();
    scope.Dispose();
    var leaf = new RecordingLeaf();

    Assert.Throws<ObjectDisposedException>(() => scope.Register(leaf));

    // The scope does not take ownership of a leaf it refuses.
    Assert.Equal(0, leaf.DisposeCount);
  }

  [Fact]
  public void ThrowingLeafIsAggregatedAndOthersStillRelease()
  {
    var scope = new GeneratedConversionScope();
    var before = new RecordingLeaf();
    var failure = new InvalidOperationException("leaf failed");
    var throwing = new RecordingLeaf(failure: failure);
    var after = new RecordingLeaf();
    scope.Register(before);
    scope.Register(throwing);
    scope.Register(after);

    var aggregate = Assert.Throws<AggregateException>(scope.Dispose);

    Assert.Same(failure, Assert.Single(aggregate.InnerExceptions));
    Assert.Equal(1, before.DisposeCount);
    Assert.Equal(1, throwing.DisposeCount);
    Assert.Equal(1, after.DisposeCount);

    scope.Dispose();

    Assert.Equal(1, before.DisposeCount);
    Assert.Equal(1, throwing.DisposeCount);
    Assert.Equal(1, after.DisposeCount);
  }

  [Fact]
  public void NestedAggregateFailureIsFlattened()
  {
    var scope = new GeneratedConversionScope();
    var a = new InvalidOperationException("a");
    var b = new InvalidOperationException("b");
    scope.Register(new RecordingLeaf(failure: new AggregateException(a, b)));

    var aggregate = Assert.Throws<AggregateException>(scope.Dispose);

    Assert.Equal([a, b], aggregate.InnerExceptions);
  }

  private sealed class RecordingLeaf(List<RecordingLeaf>? order = null, Exception? failure = null) : IDisposable
  {
    public int DisposeCount { get; private set; }

    public void Dispose()
    {
      DisposeCount++;
      order?.Add(this);
      if (failure is not null)
      {
        throw failure;
      }
    }

    // Every RecordingLeaf compares equal, so a scope that compared leaves with Equals would merge distinct leaves.
    public override bool Equals(object? obj) => obj is RecordingLeaf;

    public override int GetHashCode() => 0;
  }
}
