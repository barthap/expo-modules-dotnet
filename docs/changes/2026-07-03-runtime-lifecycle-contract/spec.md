# Runtime Lifecycle Contract Delta Spec

## Goal

Define the production runtime lifecycle and scheduling contract for the
portable C# / JSI bridge.

This milestone moves beyond host proofs. Each React Native host adapter must
create a runtime-scoped managed session for each JavaScript runtime, attach it
to a host-owned borrowed runtime holder, and tear it down deterministically
when the host invalidates or destroys that runtime.

The contract must make async module methods, retained callbacks, events, and
lazy module initialization possible without leaking managed module instances,
host-function callback pins, promise capabilities, or stale runtime work across
reloads.

## Evidence

Current repository evidence:

- `apps/mobile-app` proves iOS and Android NativeAOT module registration into
  real React Native Hermes, but Android keeps install records for process
  lifetime and iOS cleanup is tied to Objective-C module lifetime.
- `apps/desktop-app` proves React Native macOS registration through HostFXR and
  direct generated synchronous host functions, but reload-safe teardown remains
  unresolved.
- The Windows proof reports that RNW can provide `ReactContext`,
  `facebook::jsi::Runtime&`, and `CallInvoker`, but its currently observed
  `InstanceDestroyed` notification may be too late for cleanup that still
  needs live JSI access.
- `ReactNativeRuntimeConnector` already keeps the borrowed runtime pointer
  inside an owned invalidatable holder and captures that holder weakly for
  scheduled work.
- `ModuleRegistry` is currently a static helper, not a runtime-owned module
  session.
- `HostFunctionContext` is pinned by `GCHandle` and released only through the
  native host-function release callback.

Upstream Expo and React Native evidence:

- Android Expo modules tear down from the React Native module invalidation path,
  destroy the app context, clear module registries, cancel queues, and
  deallocate runtime wrappers.
- iOS Expo modules install from the New Architecture runtime initialization
  path, store a borrowed runtime pointer plus scheduler dispatch, attach app
  context native state to the main runtime, and destroy the app context when the
  main runtime native state deallocates.
- `apps/bare-expo/macos` is the relevant macOS example because it enables New
  Architecture in the macOS Podfile and starts React Native through
  `ExpoReactNativeFactory`.
- React Native macOS New Architecture uses the same Expo runtime initialization
  pattern: `RCTHost` calls `didInitializeRuntime`, Expo captures
  `RuntimeSchedulerBinding`, and `AppContext.setRuntime` installs
  `global.expo` and `global.expo.modules`.
- Old Architecture macOS bridge invalidation remains useful background, but it
  is not the production target for this repository.

## Scope

### Included

- Add a portable runtime-session concept above `Expo.JSI` and below generated
  module registration.
- Make generated module registration create or receive a runtime-scoped session
  instead of relying on static helper state.
- Add a managed teardown callback that host adapters can invoke during runtime
  invalidation.
- Track and release managed module instances, host-function callback contexts,
  and managed pins through the runtime session.
- Define stale async work behavior when the runtime is invalidated before the
  work runs or before promise settlement reaches JavaScript.
- Define host adapter responsibilities for runtime acquisition, scheduler
  acquisition, invalidation timing, and handle release.
- Update the headless Hermes testhost so lifecycle behavior can be verified
  without a React Native app.
- Allow the headless Hermes connector/executor to be split into smaller
  runtime-holder, executor-loop, and connector pieces when that makes React
  Native ownership and invalidation behavior easier to model.
- Update mobile, macOS, and Windows adapter contracts to use the same portable
  lifecycle model while preserving platform-specific hooks.

### Excluded

- Do not implement async generated module methods in this milestone.
- Do not implement events or retained JS callback invocation in this milestone.
- Do not implement lazy module initialization or HostObject-backed module
  lookup.
- Do not add platform view APIs.
- Do not require synchronous scheduler support for generated synchronous module
  functions.
- Do not make Old Architecture macOS bridge invalidation the primary production
  macOS lifecycle path.
- Do not solve artifact staging, loader configuration, app package
  registration, full Expo Desktop prebuild, or .NET module autolinking.
- Do not require broad main application host edits such as AppDelegate or
  application-entry rewrites unless a platform has no adapter-owned lifecycle
  hook.

## Accepted Design

### Runtime Session

`Expo.ModulesCore` SHALL introduce a runtime-scoped session object. The exact
type name may change during implementation, but the role is fixed:

- own module instances for one JavaScript runtime;
- own host-function callback registrations for that runtime;
- own managed lifecycle state needed by generated providers;
- expose a deterministic teardown method;
- reject or fault use after teardown;
- be safe to call teardown more than once.

Generated providers SHALL register modules through the session. Static helper
methods may remain as convenience wrappers, but the runtime session is the
production owner.

### Managed Teardown Callback

Module registration entry points SHALL return or register a managed teardown
callback with the native adapter. The callback receives a managed context for
the runtime session and releases managed state for that runtime.

The callback SHALL NOT depend on finalizers or ordinary GC timing.

The callback SHALL be idempotent. If a host calls teardown and then releases
native JSI host-function values later, duplicate host-function context release
must not double-free managed state.

### Host Runtime Holder

Host adapters SHALL keep the borrowed `facebook::jsi::Runtime*` inside an owned
runtime holder that also stores the host scheduler primitive and invalidation
state.

The raw runtime pointer remains non-owning. The holder is the lifetime
primitive. Long-lived native values and scheduled callbacks SHALL capture the
holder weakly or validate it before touching JSI.

Host adapters SHALL rely on React Native package, TurboModule, or native module
lifecycle APIs whenever those APIs can provide runtime install and invalidation
hooks. Main application project changes are outside this lifecycle contract
except for narrow app-local configuration that belongs to future staging,
prebuild, or autolinking work.

### Teardown Ordering

The preferred teardown sequence is:

1. The host reports runtime/module invalidation.
2. The adapter marks the runtime holder invalid so new scheduled work is
   rejected.
3. The adapter invokes the managed teardown callback for the runtime session.
4. The managed session cancels pending managed work and disposes module state.
5. The managed session releases host-function callback pins and managed
   resources.
6. The adapter releases the opaque runtime handle.
7. The adapter drops scheduler and borrowed-runtime references.

If a host cannot provide an invalidation hook while JSI is still usable, the
adapter SHALL use a late-invalidation path:

- mark the runtime holder invalid;
- run managed teardown that does not touch JSI;
- release managed pins and non-JSI resources;
- skip JSI cleanup that requires a live runtime.

The Windows adapter is expected to need this distinction unless an earlier RNW
runtime-teardown hook is found.

### Scheduler Semantics

The portable scheduler contract is:

- async runtime work enters JavaScript only through a host-provided scheduler;
- supported schedulers include React Native `CallInvoker`, `RuntimeScheduler`,
  `RuntimeExecutor`, or a host-specific dispatch thunk;
- task priority is advisory unless the host scheduler proves real priority
  support;
- sync execution remains a passive capability check;
- generated synchronous module functions are direct JSI host functions and do
  not require `invokeSync`;
- accepted work must either run on a valid runtime or complete/fault/release
  during invalidation;
- promise settlement after invalidation must use weak runtime state and must
  not access stale JSI.

Module worker queues are not JavaScript schedulers. Generated async methods in
later milestones should decode JavaScript inputs on the runtime path, run
managed work away from JSI when appropriate, and settle promises back through
the host runtime scheduler.

### Platform Mapping

Android SHALL use the React Native module invalidation path as the production
teardown source. Activity destruction alone is not module teardown.

iOS SHALL follow the Expo New Architecture model: runtime initialization comes
from the host runtime callback, scheduler support comes from
`RuntimeSchedulerBinding` or equivalent, and app/module lifecycle is tied to the
main runtime context.

macOS SHALL follow the New Architecture path used by `apps/bare-expo/macos`:
`RCTHost`/`ExpoReactNativeFactory` runtime initialization and Expo
`AppContext`-style runtime ownership are the production evidence. Old
Architecture bridge notifications may inform fallback behavior only.

Windows SHALL use per-React-context state. `InstanceDestroyed` is acceptable as
a stop-using-runtime signal, but JSI-touching teardown requires an earlier hook
or must be expressed as late teardown that avoids JSI access.

The headless Hermes testhost SHALL model both early teardown and
late-invalidation behavior so managed tests can verify host-independent
semantics. If needed, `HermesConsoleRuntimeConnector` and
`HermesConsoleRuntimeExecutor` MAY be split behind small interfaces or helper
types so tests can exercise runtime-holder invalidation, queued-work release,
and executor shutdown independently.

## Delta Requirements

### ADDED Requirement: Runtime-Scoped Managed Session

Generated module registration SHALL create or receive a runtime-scoped managed
session that owns module instances and host-function callback registrations for
one JavaScript runtime.

#### Scenario: Module registration creates runtime session

- **GIVEN** a host adapter invokes generated module registration for a runtime
- **WHEN** the generated provider defines module functions
- **THEN** module instances and callback registrations SHALL be owned by the
  runtime session
- **AND** the provider SHALL return or register a teardown callback for that
  session

#### Scenario: Runtime session is torn down

- **GIVEN** the runtime session owns module instances and callback pins
- **WHEN** the host invokes the managed teardown callback
- **THEN** the session SHALL release module state and callback pins exactly once
- **AND** future use of that session SHALL fail loudly

### ADDED Requirement: Host-Called Runtime Invalidation

React Native host adapters SHALL call the portable lifecycle teardown path from
their runtime or module invalidation hooks.

#### Scenario: Host invalidates runtime before JSI is destroyed

- **GIVEN** the host reports runtime invalidation while JSI access is still
  valid
- **WHEN** the adapter tears down the runtime session
- **THEN** the adapter SHALL invalidate the runtime holder
- **AND** invoke managed teardown
- **AND** release the opaque runtime handle
- **AND** drop borrowed runtime and scheduler references

#### Scenario: Host invalidates runtime after JSI is no longer usable

- **GIVEN** the host reports invalidation after the runtime can no longer be
  touched
- **WHEN** the adapter tears down the runtime session
- **THEN** managed teardown SHALL avoid JSI access
- **AND** still release managed pins and non-JSI module state
- **AND** stale scheduled work SHALL not touch the runtime

### ADDED Requirement: Stale Scheduled Work Handling

Scheduled runtime work SHALL not keep managed state alive indefinitely after
runtime invalidation.

#### Scenario: Queued work is released during invalidation

- **GIVEN** managed code schedules runtime work
- **AND** the host invalidates the runtime before the work runs
- **WHEN** the adapter releases queued work
- **THEN** the managed task SHALL complete with cancellation or failure
- **AND** captured managed task context SHALL be released

#### Scenario: Async promise completes after invalidation

- **GIVEN** a managed async operation completes after its runtime session is
  torn down
- **WHEN** it attempts to settle a JavaScript promise
- **THEN** settlement SHALL not touch stale JSI
- **AND** the promise capability and managed operation state SHALL be released

### MODIFIED Requirement: React Native Runtime Scheduling Adapter

React Native scheduling SHALL be host-adapted through an invalidatable runtime
holder. Priority remains advisory unless the host scheduler can honor it.
Synchronous module calls SHALL remain direct JSI host functions and SHALL NOT
depend on sync scheduler support.

#### Scenario: Generated synchronous function is called

- **GIVEN** a generated synchronous C# module function is installed as a JSI
  host function
- **WHEN** JavaScript calls that function
- **THEN** the function SHALL execute in the current JSI call
- **AND** it SHALL NOT require `CallInvoker::invokeSync` or equivalent sync
  scheduler support

#### Scenario: Managed code schedules JavaScript work

- **GIVEN** managed code schedules runtime work
- **WHEN** the host has a valid scheduler primitive
- **THEN** the adapter SHALL schedule through that primitive
- **AND** the scheduled callback SHALL validate runtime holder state before
  touching JSI

## Verification

The implementation is accepted when:

- `scripts/test-managed.sh` passes.
- `scripts/format.sh --check --all` passes.
- Managed tests prove runtime-session teardown releases module state and
  callback pins.
- Managed tests prove scheduled work is faulted, cancelled, or released when
  runtime invalidation happens before execution.
- Headless native tests cover early teardown and late invalidation.
- Mobile adapter code no longer keeps Android runtime install records alive for
  process lifetime without invalidation.
- macOS adapter code targets the New Architecture runtime lifecycle path, not
  Old Architecture bridge invalidation as the primary contract.
- Windows adapter notes or implementation distinguish early JSI teardown from
  late no-JSI invalidation.
- `docs/specs/runtime-and-abi.md`, `docs/specs/runtime-scheduling.md`, and
  `docs/specs/modules-core-boundary.md` are updated with accepted lifecycle
  deltas before branch handoff.
