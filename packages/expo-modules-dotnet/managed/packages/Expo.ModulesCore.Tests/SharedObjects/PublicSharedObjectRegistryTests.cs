using Expo.JSI;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.SharedObjects;

public sealed class PublicSharedObjectRegistryTests
{
  [Fact]
  public void ExactRuntimeTypeIsRequiredBeforeLookupOrAllocation()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var registry = new SharedObjectRegistry(runtime);
      using var registration = SharedObjectClassRegistration.Create(
          registry,
          typeof(PublicTestSharedObject)
      );
      var steps = new List<SharedObjectPairingStep>();
      registry.PairingStepForTesting = steps.Add;
      var mismatched = new OtherPublicTestSharedObject();

      Assert.Throws<InvalidOperationException>(
          () => registry.GetOrCreateJavaScriptObject(mismatched, registration)
      );

      Assert.Empty(steps);
      Assert.Equal(0, registry.Count);

      using var otherRegistration = SharedObjectClassRegistration.Create(
          registry,
          typeof(OtherPublicTestSharedObject)
      );
      using var paired = registry.GetOrCreateJavaScriptObject(mismatched, otherRegistration);
      Assert.Equal(1, registry.Count);
      Assert.Same(mismatched, registry.ResolveManaged(paired));
      return true;
    });
  }

  [Fact]
  public void RepeatEncodeReturnsStrictlyEqualObjectAndLocksOutsideTheGate()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var registry = new SharedObjectRegistry(runtime);
      using var registration = SharedObjectClassRegistration.Create(
          registry,
          typeof(PublicTestSharedObject)
      );
      var instance = new PublicTestSharedObject();
      using var first = registry.GetOrCreateJavaScriptObject(instance, registration);

      var observed = new List<(SharedObjectPairingStep Step, bool GateEntered)>();
      registry.PairingStepForTesting = step => observed.Add((step, registry.IsGateEnteredForTesting));
      using var second = registry.GetOrCreateJavaScriptObject(instance, registration);

      using var firstValue = first.AsValue();
      using var secondValue = second.AsValue();
      Assert.True(runtime.StrictEquals(firstValue, secondValue));
      Assert.Equal(1, registry.Count);

      var lockStep = Assert.Single(observed);
      Assert.Equal(SharedObjectPairingStep.ExistingWeakObjectLocked, lockStep.Step);
      Assert.False(lockStep.GateEntered);
      return true;
    });
  }

  [Fact]
  public void AnotherContextCannotPairTheSameManagedInstance()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var registryA = new SharedObjectRegistry(runtime);
      using var registryB = new SharedObjectRegistry(runtime);
      using var registrationA = SharedObjectClassRegistration.Create(
          registryA,
          typeof(PublicTestSharedObject)
      );
      using var registrationB = SharedObjectClassRegistration.Create(
          registryB,
          typeof(PublicTestSharedObject)
      );
      var stepsB = new List<SharedObjectPairingStep>();
      registryB.PairingStepForTesting = stepsB.Add;
      var instance = new PublicTestSharedObject();

      InvalidOperationException? reservedRejection = null;
      registryA.PairingStepForTesting = step =>
      {
        if (step == SharedObjectPairingStep.InstanceObjectCreated && reservedRejection is null)
        {
          reservedRejection = Assert.Throws<InvalidOperationException>(
              () => registryB.GetOrCreateJavaScriptObject(instance, registrationB)
          );
        }
      };
      using var paired = registryA.GetOrCreateJavaScriptObject(instance, registrationA);
      Assert.NotNull(reservedRejection);

      Assert.Throws<InvalidOperationException>(
          () => registryB.GetOrCreateJavaScriptObject(instance, registrationB)
      );
      Assert.Throws<InvalidOperationException>(
          () => registryB.GetOrCreateJavaScriptObject(new PublicTestSharedObject(), registrationA)
      );

      Assert.Empty(stepsB);
      Assert.Equal(0, registryB.Count);
      Assert.Equal(1, registryA.Count);
      Assert.Equal(0, instance.ReleaseCount);
      Assert.Same(instance, registryA.ResolveManaged(paired));
      return true;
    });
  }

  [Fact]
  public void ReservationRejectsReentrantDuplicatePairingWithoutDuplicateState()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var registry = new SharedObjectRegistry(runtime);
      using var registration = SharedObjectClassRegistration.Create(
          registry,
          typeof(PublicTestSharedObject)
      );
      var instance = new PublicTestSharedObject();
      var reentrantRejections = 0;
      registry.PairingStepForTesting = step =>
      {
        if (step == SharedObjectPairingStep.NativeStateAttached)
        {
          Assert.Throws<InvalidOperationException>(
              () => registry.GetOrCreateJavaScriptObject(instance, registration)
          );
          reentrantRejections++;
        }
      };

      using var paired = registry.GetOrCreateJavaScriptObject(instance, registration);

      Assert.Equal(1, reentrantRejections);
      Assert.Equal(1, registry.Count);
      Assert.Equal(0, instance.ReleaseCount);

      registry.PairingStepForTesting = null;
      using var again = registry.GetOrCreateJavaScriptObject(instance, registration);
      using var pairedValue = paired.AsValue();
      using var againValue = again.AsValue();
      Assert.True(runtime.StrictEquals(pairedValue, againValue));
      Assert.Same(instance, registry.ResolveManaged(paired));
      return true;
    });
  }

  [Fact]
  public void InstanceTrackedThroughInternalRouteIsRejectedWithoutDuplicateState()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var registry = new SharedObjectRegistry(runtime);
      using var registration = SharedObjectClassRegistration.Create(
          registry,
          typeof(PublicTestSharedObject)
      );
      var instance = new PublicTestSharedObject();
      using var internallyPaired = registry.GetOrCreateJavaScriptObject(
          (ISharedObjectLifetime)instance
      );
      var steps = new List<SharedObjectPairingStep>();
      registry.PairingStepForTesting = steps.Add;

      var rejection = Assert.Throws<InvalidOperationException>(
          () => registry.GetOrCreateJavaScriptObject(instance, registration)
      );
      Assert.Contains("another pairing route", rejection.Message);

      // The rejection must leave the instance unpaired; a leaked reservation would surface
      // here as an "already being paired" failure instead of the same clean rejection.
      var repeatRejection = Assert.Throws<InvalidOperationException>(
          () => registry.GetOrCreateJavaScriptObject(instance, registration)
      );
      Assert.Equal(rejection.Message, repeatRejection.Message);

      Assert.Empty(steps);
      Assert.Equal(1, registry.Count);
      Assert.Equal(0, instance.ReleaseCount);
      Assert.Same(instance, registry.ResolveManaged(internallyPaired));

      using var reencoded = registry.GetOrCreateJavaScriptObject((ISharedObjectLifetime)instance);
      using var pairedValue = internallyPaired.AsValue();
      using var reencodedValue = reencoded.AsValue();
      Assert.True(runtime.StrictEquals(pairedValue, reencodedValue));

      var fresh = new PublicTestSharedObject();
      using var freshPaired = registry.GetOrCreateJavaScriptObject(fresh, registration);
      Assert.Equal(2, registry.Count);
      Assert.Same(fresh, registry.ResolveManaged(freshPaired));
      return true;
    });
  }

  [Fact]
  public void PairingWorkAndOnReleaseRunOutsideTheRegistryGate()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var registry = new SharedObjectRegistry(runtime);
      using var registration = SharedObjectClassRegistration.Create(
          registry,
          typeof(PublicTestSharedObject)
      );
      var observed = new List<(SharedObjectPairingStep Step, bool GateEntered)>();
      registry.PairingStepForTesting = step => observed.Add((step, registry.IsGateEnteredForTesting));
      bool? gateEnteredDuringRelease = null;
      var instance = new PublicTestSharedObject(
          () => gateEnteredDuringRelease = registry.IsGateEnteredForTesting
      );

      using var value = registry.GetOrCreateJavaScriptObject(instance, registration);
      Assert.Equal(
          new[]
          {
            SharedObjectPairingStep.InstanceObjectCreated,
            SharedObjectPairingStep.NativeStateAttached,
            SharedObjectPairingStep.WeakObjectCreated,
          },
          observed.Select(step => step.Step)
      );
      Assert.All(observed, step => Assert.False(step.GateEntered));

      registry.ReleaseFromJavaScript(value);
      Assert.Equal(1, instance.ReleaseCount);
      Assert.False(gateEnteredDuringRelease ?? true);
      return true;
    });
  }

  [Fact]
  public void OrdinaryFirstEncodeFailureRollsBackAndRetrySucceeds()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var registry = new SharedObjectRegistry(runtime);
      using var registration = SharedObjectClassRegistration.Create(
          registry,
          typeof(PublicTestSharedObject)
      );
      var instance = new PublicTestSharedObject();
      registry.PairingStepForTesting = step =>
      {
        if (step == SharedObjectPairingStep.WeakObjectCreated)
        {
          throw new InvalidOperationException("encode failure");
        }
      };

      var failure = Assert.Throws<AggregateException>(
          () => registry.GetOrCreateJavaScriptObject(instance, registration)
      );
      var encodeFailure = Assert.IsType<InvalidOperationException>(
          Assert.Single(failure.InnerExceptions)
      );
      Assert.Equal("encode failure", encodeFailure.Message);
      Assert.Equal(0, registry.Count);
      Assert.Equal(0, instance.ReleaseCount);

      registry.PairingStepForTesting = null;
      using var paired = registry.GetOrCreateJavaScriptObject(instance, registration);
      Assert.Equal(1, registry.Count);
      Assert.Same(instance, registry.ResolveManaged(paired));
      return true;
    });

    fixture.WaitUntilIdle();
    Assert.Equal(0u, fixture.Counters.LongLivedObjectsRemaining);
  }

  [Fact]
  public void ConstructorOwnedFailureIsTerminalOnceAndRejectsRetry()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var registry = new SharedObjectRegistry(runtime);
      using var registration = SharedObjectClassRegistration.Create(
          registry,
          typeof(PublicTestSharedObject)
      );
      bool? gateEnteredDuringRelease = null;
      var instance = new PublicTestSharedObject(
          () => gateEnteredDuringRelease = registry.IsGateEnteredForTesting
      );
      registry.PairingStepForTesting = step =>
      {
        if (step == SharedObjectPairingStep.WeakObjectCreated)
        {
          throw new InvalidOperationException("constructor pairing failure");
        }
      };

      // The rollback clears the attached NativeState, which re-enters the registry release
      // path for the never-committed reservation; that re-entry must converge on the same
      // exactly-once terminal release instead of repeating or repairing it.
      var failure = Assert.Throws<AggregateException>(
          () => registry.PairConstructorOwnedInstance(instance, registration)
      );
      Assert.Contains(
          failure.InnerExceptions,
          inner => inner is InvalidOperationException { Message: "constructor pairing failure" }
      );
      Assert.Equal(1, instance.ReleaseCount);
      Assert.False(gateEnteredDuringRelease ?? true);
      Assert.Equal(0, registry.Count);

      registry.PairingStepForTesting = null;
      Assert.Throws<InvalidOperationException>(
          () => registry.GetOrCreateJavaScriptObject(instance, registration)
      );
      Assert.Throws<InvalidOperationException>(
          () => registry.PairConstructorOwnedInstance(instance, registration)
      );
      Assert.Equal(1, instance.ReleaseCount);

      var freshInstance = new PublicTestSharedObject();
      using var constructed = registry.PairConstructorOwnedInstance(freshInstance, registration);
      using var encoded = registry.GetOrCreateJavaScriptObject(freshInstance, registration);
      using var constructedValue = constructed.AsValue();
      using var encodedValue = encoded.AsValue();
      Assert.True(runtime.StrictEquals(constructedValue, encodedValue));
      Assert.Same(freshInstance, registry.ResolveManaged(constructed));
      return true;
    });
  }

  [Fact]
  public void ExplicitReleaseSurfacesOnReleaseFailureAndStaysIdempotent()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var registry = new SharedObjectRegistry(runtime);
      using var registration = SharedObjectClassRegistration.Create(
          registry,
          typeof(PublicTestSharedObject)
      );
      var instance = new PublicTestSharedObject(
          () => throw new InvalidOperationException("hook failure")
      );
      using var value = registry.GetOrCreateJavaScriptObject(instance, registration);
      using var valueHandle = value.AsValue();
      using var global = runtime.Global();
      global.SetProperty("__publicShared", valueHandle);

      using var caught = fixture.Evaluate(
          "(() => { try { globalThis.__publicShared.release(); return 'no error'; }"
              + " catch (error) { return String(error.message || error); } })()",
          "public-shared-release.js"
      );
      Assert.Contains("hook failure", caught.AsString());
      Assert.Equal(1, instance.ReleaseCount);
      Assert.Equal(0, registry.Count);

      fixture.Evaluate("globalThis.__publicShared.release()", "public-shared-release-again.js")
          .Dispose();
      Assert.Equal(1, instance.ReleaseCount);
      return true;
    });
  }

  [Fact]
  public void CollectionCleanupSwallowsOnReleaseFailure()
  {
    using var fixture = HermesRuntimeFixture.Create();
    PublicTestSharedObject? instance = null;
    SharedObjectRegistry? registry = null;
    SharedObjectClassRegistration? registration = null;

    fixture.Runtime.Execute(runtime =>
    {
      registry = new SharedObjectRegistry(runtime);
      registration = SharedObjectClassRegistration.Create(registry, typeof(PublicTestSharedObject));
      instance = new PublicTestSharedObject(
          () => throw new InvalidOperationException("collection hook failure")
      );
      using var value = registry.GetOrCreateJavaScriptObject(instance, registration);
      return true;
    });

    fixture.CollectGarbageForTesting();
    fixture.WaitUntilIdle();

    Assert.Equal(1, instance!.ReleaseCount);
    Assert.Equal(0, registry!.Count);
    registration!.Dispose();
    registry.Dispose();
  }

  [Fact]
  public void TeardownAggregatesOnReleaseFailuresAndContinues()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      var registry = new SharedObjectRegistry(runtime);
      using var registration = SharedObjectClassRegistration.Create(
          registry,
          typeof(PublicTestSharedObject)
      );
      var failing = new PublicTestSharedObject(
          () => throw new InvalidOperationException("teardown hook failure")
      );
      var succeeding = new PublicTestSharedObject();
      using var first = registry.GetOrCreateJavaScriptObject(failing, registration);
      using var second = registry.GetOrCreateJavaScriptObject(succeeding, registration);

      var aggregate = Assert.Throws<AggregateException>(registry.Dispose);

      Assert.Contains(
          aggregate.InnerExceptions,
          inner => inner is InvalidOperationException { Message: "teardown hook failure" }
      );
      Assert.Equal(1, failing.ReleaseCount);
      Assert.Equal(1, succeeding.ReleaseCount);
      Assert.Equal(0, registry.Count);
      return true;
    });
  }

  [Fact]
  public void ContextDisposalDuringInFlightReservationCancelsCommit()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      var context = new DotnetRuntimeContext(runtime);
      var registry = context.SharedObjects;
      using var registration = SharedObjectClassRegistration.Create(
          registry,
          typeof(PublicTestSharedObject)
      );
      var instance = new PublicTestSharedObject();
      registry.PairingStepForTesting = step =>
      {
        if (step == SharedObjectPairingStep.WeakObjectCreated)
        {
          context.Dispose();
        }
      };

      var failure = Assert.Throws<AggregateException>(
          () => registry.GetOrCreateJavaScriptObject(instance, registration)
      );

      Assert.Contains(
          failure.InnerExceptions,
          inner => inner is InvalidOperationException cancelled &&
              cancelled.Message.Contains("torn down")
      );
      Assert.Equal(0, registry.Count);
      Assert.Equal(0, instance.ReleaseCount);
      Assert.Throws<ObjectDisposedException>(() => _ = context.SharedObjects);

      using var freshContext = new DotnetRuntimeContext(runtime);
      using var freshRegistration = SharedObjectClassRegistration.Create(
          freshContext.SharedObjects,
          typeof(PublicTestSharedObject)
      );
      using var paired = freshContext.SharedObjects.GetOrCreateJavaScriptObject(
          instance,
          freshRegistration
      );
      Assert.Same(instance, freshContext.SharedObjects.ResolveManaged(paired));
      Assert.Equal(0, instance.ReleaseCount);
      return true;
    });
  }

  private sealed class PublicTestSharedObject(Action? onRelease = null) : SharedObject
  {
    private int releaseCount;

    public int ReleaseCount => Volatile.Read(ref releaseCount);

    protected override void OnRelease()
    {
      Interlocked.Increment(ref releaseCount);
      onRelease?.Invoke();
    }
  }

  private sealed class OtherPublicTestSharedObject : SharedObject
  {
  }
}
