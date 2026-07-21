# Promise Long-Lived Capability State Delta Spec

## Goal

Move native Promise capability state into the runtime-owned long-lived object
collection so capabilities remain teardown-tracked until disposal or runtime
teardown, while preserving the existing managed Promise API and settlement
scheduling behavior.

This delta extends `docs/specs/promises.md`.

## Scope

This change covers native Promise capability storage, terminal registration,
settlement state, exactly-once lifetime cleanup, managed handle leasing,
teardown races, and testhost accounting. It does not change the public managed
Promise API, generated module bindings, or the scheduling and disposal ordering
of asynchronous Promise settlement.

The existing `expo_jsi.h` Promise entry signatures remain unchanged. The
opaque Promise handle may change its internal representation, but raw JSI
layouts SHALL NOT cross the C ABI.

## Accepted Design

- A runtime-owned `PromiseEntry` holds the Promise object and resolver
  functions as a `LongLivedObject`. The opaque Promise handle refers to a
  wrapper that coordinates the entry, its runtime state, and its collection
  identifier.
- Settlement and lifetime termination use separate states. Successful
  settlement without pending cleanup clears the resolver functions but
  retains the Promise object and collection entry until opaque-handle disposal
  or runtime teardown. An atomic lifetime-terminal transition coordinates
  release and abandonment.
- Promise construction completes before registration because the
  user-replaceable JavaScript `Promise` constructor may execute arbitrary code.
  Registration uses a new failable `tryAdd` operation after that constructor
  returns.
- The collection becomes terminal when it is swept or invalidated without a
  runtime. `tryAdd` rejects later Promise entries. The existing `add()`
  contract and its current callers remain unchanged.
- Settlement transitions from Active to Settling before invoking a resolver.
  No Promise-entry mutex is held while JavaScript resolver code runs. Resolver
  failure restores Active when no lifetime cleanup is pending; otherwise the
  pending cleanup completes after the resolver call unwinds.
- `JavaScriptPromise` internally leases its native handle for `AsValue`,
  `Resolve`, and `Reject`. Disposal rejects new leases and releases the native
  handle immediately or after the last in-flight call exits, without blocking
  reentrant disposal.
- Disposing a Promise always forwards native handle release. Native release
  requests collection removal through `RuntimeState` and its executor instead
  of destroying JSI state synchronously from an arbitrary managed thread.
- Dedicated release and abandonment counters expose Promise-entry terminal
  outcomes through the native testhost and both managed test fixtures. A
  separate testhost observation records release calls made without current
  connector runtime access.

## ADDED Requirements

### Requirement: Runtime-Owned Promise Capability State

Every successfully created native Promise capability SHALL have one
`PromiseEntry` registered in its runtime's long-lived object collection until
explicit disposal or runtime teardown removes that entry. Settlement SHALL NOT
remove the entry.

The opaque Promise handle SHALL coordinate the registered entry without
exposing a raw JSI object or changing an existing `expo_jsi.h` Promise entry
signature.

#### Scenario: Promise capability is registered

- **GIVEN** the JavaScript `Promise` constructor completes while the runtime's
  long-lived collection remains active
- **WHEN** native Promise capability creation succeeds
- **THEN** the capability's `PromiseEntry` SHALL be registered in that
  runtime-owned collection
- **AND** the returned opaque handle SHALL refer to that registered entry

#### Scenario: Settled capability remains registered

- **GIVEN** a registered Promise capability resolves or rejects successfully
- **WHEN** managed code retains the capability without disposing it
- **THEN** its Promise entry SHALL remain in the runtime-owned collection
- **AND** its Promise object SHALL remain available to `AsValue`
- **AND** the remaining-entry count SHALL continue to include that entry

#### Scenario: Capability reaches runtime teardown

- **GIVEN** a settled or unresolved Promise capability remains undisposed
- **WHEN** runtime teardown drains the long-lived object collection while JSI
  access remains available
- **THEN** the collection SHALL release the Promise entry on the runtime path
- **AND** the capability SHALL NOT retain JSI state beyond that teardown

#### Scenario: Runtime is invalidated without JSI access

- **GIVEN** a Promise capability remains registered
- **WHEN** the long-lived collection is invalidated without runtime access
- **THEN** the collection SHALL abandon the Promise entry without accessing
  JSI
- **AND** the capability SHALL NOT retain an entry-to-runtime reference cycle

### Requirement: Terminal Promise Registration

The long-lived object collection SHALL expose a failable `tryAdd` operation
for Promise registration. It SHALL become terminal under the same lock that
guards a sweep or invalidation, and `tryAdd` SHALL reject registration after
that terminal transition.

The existing `add()` operation and the behavior of its existing ArrayBuffer
and weak-object callers SHALL remain unchanged.

#### Scenario: User Promise constructor triggers coordinated preparation

- **GIVEN** a user-replaced JavaScript `Promise` constructor re-enters the
  bridge's coordinated `PrepareRuntimeForInvalidation` path during Promise
  creation
- **AND** that preparation terminalizes and drains the runtime's long-lived
  collection
- **WHEN** construction returns and native code attempts to register the new
  Promise entry
- **THEN** `tryAdd` SHALL reject registration because the collection is
  terminal
- **AND** Promise capability creation SHALL fail
- **AND** zero Promise entries SHALL remain registered
- **AND** no entry-to-runtime reference cycle SHALL remain

#### Scenario: Registration or opaque-handle allocation fails

- **GIVEN** the JavaScript Promise object and resolver functions have been
  captured
- **WHEN** registration fails or registration or opaque-handle allocation
  throws
- **THEN** native code SHALL roll back every partially created Promise entry
  and handle
- **AND** no Promise entry SHALL remain in the collection
- **AND** no entry-to-runtime reference cycle SHALL remain

#### Scenario: Registration races with collection termination

- **GIVEN** Promise creation and collection termination proceed concurrently
- **WHEN** registration and the terminal transition contend for the collection
  lock
- **THEN** the entry SHALL either be registered before termination and drained
  by it, or registration SHALL fail
- **AND** the entry SHALL NOT be added after the terminal collection has been
  drained

### Requirement: Promise Settlement State

Promise settlement SHALL use state separate from entry lifetime termination.
The entry SHALL transition from Active to Settling before resolver invocation.
No Promise-entry mutex or other entry lock SHALL remain held while JavaScript
resolver code runs.

Successful settlement without pending lifetime cleanup SHALL clear the resolve
and reject functions, retain the Promise object, transition settlement state
to Settled, and leave the entry registered until disposal or runtime teardown.

#### Scenario: Promise settles successfully

- **GIVEN** a registered Promise capability in Active settlement state
- **WHEN** native code resolves or rejects it on the runtime path
- **THEN** the entry SHALL transition to Settling before calling the selected
  JavaScript resolver
- **AND** the resolver SHALL run without an entry lock held
- **AND** successful completion SHALL clear both resolver functions
- **AND** settlement state SHALL become Settled
- **AND** the Promise object and collection entry SHALL remain available until
  disposal or runtime teardown

#### Scenario: Thenable getter re-enters settlement

- **GIVEN** a Promise is resolved with a thenable whose getter synchronously
  re-enters the same capability
- **WHEN** the reentrant call observes Settling state
- **THEN** the reentrant call SHALL complete without blocking or deadlock
- **AND** it SHALL NOT invoke either resolver again
- **AND** the outer resolver SHALL remain responsible for completing the
  settlement transition

#### Scenario: Resolver throws without pending cleanup

- **GIVEN** a Promise entry transitions from Active to Settling
- **WHEN** its JavaScript resolver throws and no release or abandonment is
  pending
- **THEN** settlement state SHALL return to Active after the resolver call
  unwinds
- **AND** the resolver error SHALL be surfaced with the existing semantics
- **AND** a later settlement attempt SHALL be allowed to retry

#### Scenario: Resolver throws with pending cleanup

- **GIVEN** a resolver is executing in Settling state
- **WHEN** release or abandonment becomes pending and the resolver then throws
- **THEN** JSI state SHALL remain intact until the resolver call unwinds
- **AND** the pending lifetime-terminal action SHALL complete afterward,
  outside entry locks
- **AND** the resolver error SHALL still be surfaced with the existing
  semantics

#### Scenario: Re-entry behavior is verified

- **GIVEN** Promise resolution may convert a JavaScript callback exception
  into Promise rejection
- **WHEN** a test exercises synchronous thenable re-entry
- **THEN** the callback SHALL record its outcomes instead of relying on
  assertions thrown from inside the callback
- **AND** the test SHALL assert those recorded outcomes after the outer
  resolver returns

### Requirement: Managed Promise Handle Leasing

Without changing its public API, `JavaScriptPromise` SHALL synchronize access
to its native opaque handle. `AsValue`, `Resolve`, and `Reject` SHALL acquire an
in-flight handle lease before calling native code and release that lease when
the native call exits.

Disposal SHALL atomically reject new leases and SHALL forward native handle
release exactly once, either immediately when no lease is active or after the
last in-flight lease exits.

#### Scenario: Dispose races with an in-flight call

- **GIVEN** `AsValue`, `Resolve`, or `Reject` holds a native-handle lease
- **WHEN** another thread disposes the same `JavaScriptPromise`
- **THEN** disposal SHALL reject subsequent lease attempts
- **AND** it SHALL defer native opaque-handle release until the in-flight call
  exits
- **AND** the in-flight native call SHALL NOT observe a released handle

#### Scenario: Dispose re-enters from resolver execution

- **GIVEN** `Resolve` or `Reject` holds a lease while its JavaScript resolver
  executes
- **WHEN** resolver code synchronously disposes the same managed Promise
- **THEN** disposal SHALL mark native handle release as pending
- **AND** it SHALL return without waiting for the active lease
- **AND** the last lease exit SHALL forward native handle release exactly once

#### Scenario: Operation starts after disposal

- **GIVEN** a `JavaScriptPromise` has rejected new handle leases for disposal
- **WHEN** managed code calls `AsValue`, `Resolve`, or `Reject`
- **THEN** the operation SHALL fail through the existing disposed-wrapper
  behavior
- **AND** it SHALL NOT call native code with the released handle

### Requirement: Exactly-Once Promise Entry Lifetime Termination

Explicit disposal, runtime release, and abandonment SHALL share one atomic
lifetime-terminal transition distinct from settlement state. Exactly one
terminal source SHALL remove or abandon the entry and account for its outcome;
later release sources SHALL be no-ops.

#### Scenario: Promise is disposed before or after settlement

- **GIVEN** a registered Promise capability in Active or Settled state
- **WHEN** managed code disposes its wrapper and no resolver call is active
- **THEN** native code SHALL request entry removal through `RuntimeState` and
  its executor
- **AND** the runtime callback SHALL remove and release the entry exactly once
- **AND** disposal SHALL NOT destroy JSI state synchronously on an arbitrary
  managed thread

#### Scenario: Teardown begins during resolver execution

- **GIVEN** a Promise entry is in Settling state
- **WHEN** runtime release or abandonment reaches it
- **THEN** lifetime cleanup SHALL become pending
- **AND** the Promise object and resolver state SHALL remain intact until the
  active resolver call exits
- **AND** the pending release or abandonment SHALL then clear JSI state and
  terminate the entry exactly once outside entry locks

#### Scenario: Promise is disposed more than once

- **GIVEN** a Promise capability has already reached a lifetime-terminal state
- **WHEN** the managed wrapper is disposed again or disposed after teardown
- **THEN** the later release SHALL be a no-op
- **AND** the entry SHALL NOT be freed or counted twice

#### Scenario: Settlement races with teardown

- **GIVEN** Promise settlement is queued while runtime teardown begins
- **WHEN** settlement, release, or abandonment reaches the entry
- **THEN** settlement state and lifetime state SHALL coordinate without
  clearing JSI state used by an active resolver call
- **AND** exactly one lifetime operation SHALL remove or abandon the entry
- **AND** the race SHALL NOT double-free JSI state or leave a registered entry

### Requirement: Off-Runtime Promise Disposal

Managed Promise disposal SHALL be supported when the calling thread cannot
access `connector.runtime()`. Native opaque-handle release SHALL only request
collection removal through `RuntimeState` and its executor; it SHALL NOT
require direct JSI access from the disposing thread.

The testhost `countedReleasePromise` wrapper SHALL record both the release call
and any off-runtime observation, then SHALL always forward the release to the
underlying native API.

#### Scenario: Promise is disposed off the runtime thread

- **GIVEN** a managed Promise wrapper is disposed while
  `connector.runtime()` is unavailable to the calling thread
- **WHEN** `countedReleasePromise` observes the release
- **THEN** it SHALL increment its Promise-release call observation
- **AND** it SHALL record the off-runtime observation
- **AND** it SHALL forward the native handle release instead of returning
  early
- **AND** `RuntimeState` SHALL arrange one runtime release or abandonment
  according to its teardown state

#### Scenario: Off-runtime disposal is verified

- **GIVEN** a Promise capability is disposed from outside runtime access
- **WHEN** the testhost reports disposal and long-lived entry counters
- **THEN** the native release call and off-runtime observation SHALL both be
  visible
- **AND** the entry SHALL reach exactly one release or abandonment
- **AND** no Promise entry SHALL remain after executor drain or teardown

### Requirement: Promise Entry Accounting

The native runtime test hooks SHALL expose dedicated Promise-entry release and
abandonment counters. The native testhost and both managed testhost fixtures
SHALL carry those counters without combining them with other long-lived entry
kinds. Settlement SHALL NOT increment either counter or reduce the
remaining-entry count. Counters SHALL record lifetime-terminal release or
abandonment only.

#### Scenario: Settled entry remains accounted as live

- **GIVEN** a registered Promise capability settles successfully
- **WHEN** long-lived counters are observed before disposal or teardown
- **THEN** neither the Promise release counter nor the Promise abandonment
  counter SHALL increase for that entry
- **AND** the remaining-entry count SHALL still include that Promise entry

#### Scenario: Promise entry is released on the runtime path

- **GIVEN** a settled or unsettled Promise entry is removed while runtime
  access remains available
- **WHEN** its terminal release runs
- **THEN** the Promise release counter SHALL increase exactly once
- **AND** the Promise abandonment counter SHALL remain unchanged for that
  entry
- **AND** the remaining-entry count SHALL no longer include that entry

#### Scenario: Promise entry is abandoned

- **GIVEN** a Promise entry cannot be released with runtime access
- **WHEN** its terminal abandonment runs
- **THEN** the Promise abandonment counter SHALL increase exactly once
- **AND** the Promise release counter SHALL remain unchanged for that entry
- **AND** the remaining-entry count SHALL no longer include that entry

#### Scenario: Late disposal follows a terminal outcome

- **GIVEN** release or abandonment has already been counted for a Promise
  entry
- **WHEN** its opaque or managed handle is disposed later
- **THEN** neither Promise counter SHALL increase again
- **AND** the remaining-entry count SHALL stay at zero

## MODIFIED Requirements

### Requirement: Async Managed Promise Helper

`JavaScriptRuntime.CreatePromise(Func<CancellationToken,
Task<JavaScriptPromiseResult>>)` SHALL preserve its current settlement
scheduling and scheduled-callback disposal ordering while native capability
storage moves into the runtime-owned collection.

#### Scenario: Async promise settlement ordering is preserved

- **GIVEN** an asynchronous managed Promise operation completes
- **WHEN** its settlement work runs
- **THEN** settlement SHALL occur on the existing scheduled runtime callback
- **AND** the scheduler SHALL release the native Promise capability during the
  same runtime access path
- **AND** it SHALL NOT release the capability from an arbitrary managed
  continuation thread

#### Scenario: Scheduled settlement work is dropped

- **GIVEN** asynchronous Promise settlement owns a result state and a native
  Promise capability
- **WHEN** its queued runtime callable is destroyed without invocation
- **THEN** the existing claim-or-abandon behavior for the owned result SHALL
  remain unchanged
- **AND** the Promise capability entry SHALL still reach one terminal release
  or abandonment through runtime teardown
