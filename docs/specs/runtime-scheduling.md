# Runtime Scheduling

## Purpose

Specify how managed code schedules work onto a JavaScript runtime and how the
headless Hermes runtime loop executes queued work.

## Requirements

### Requirement: Scheduled Runtime Work

`JavaScriptRuntime` SHALL expose asynchronous runtime work APIs that route
through the native runtime task ABI.

#### Scenario: Managed code schedules work
- **GIVEN** managed code calls `ScheduleAsync`
- **WHEN** native accepts the task
- **THEN** the task SHALL run on the JavaScript runtime path and complete the
  returned managed `Task`

#### Scenario: Managed code schedules work with a result
- **GIVEN** managed code calls `ExecuteAsync<T>`
- **WHEN** the scheduled body completes
- **THEN** the returned managed `Task<T>` SHALL complete with the body result,
  cancellation, or exception

### Requirement: Synchronous Execution Capability

Synchronous runtime execution SHALL be gated by a passive capability check and
SHALL fail loudly when unsupported.

#### Scenario: Host does not support sync execution
- **GIVEN** `CanExecuteSync` returns false
- **WHEN** managed code calls `Execute`
- **THEN** managed code SHALL throw `NotSupportedException`

#### Scenario: Host supports sync execution
- **GIVEN** `CanExecuteSync` returns true
- **WHEN** managed code calls `Execute`
- **THEN** native SHALL execute the runtime task synchronously and managed code
  SHALL return the body result

### Requirement: React Native Runtime Scheduling Adapter

React Native hosts SHALL adapt runtime scheduling through injected New
Architecture scheduling primitives instead of embedding React Native-specific
types in managed code. The native React Native connector SHALL hold the
borrowed `facebook::jsi::Runtime` together with a React Native `CallInvoker`
inside an explicit runtime-state holder. The raw runtime pointer SHALL be
non-owning; the holder and its invalidation state SHALL be the lifetime
primitive used by connector executors or longer-lived native values.
The current implementation evidence is the `apps/mobile-app` proof and the
`apps/desktop-app` React Native macOS and Windows proofs. They route through
React Native call-invoker primitives, which do not expose task priorities.

#### Scenario: React Native connector schedules work
- **GIVEN** native platform glue has a borrowed React Native Hermes runtime and
  `CallInvoker`
- **WHEN** managed code schedules runtime work through `JavaScriptRuntime`
- **THEN** the React Native connector SHALL route asynchronous work through
  `CallInvoker::invokeAsync`
- **AND** sync execution SHALL route through `CallInvoker::invokeSync` when the
  borrowed runtime and invoker are still valid
- **AND** task priority SHALL be treated as advisory when the host scheduling
  primitive cannot honor it

#### Scenario: React Native macOS registers sync modules from the host function
- **GIVEN** React Native macOS exposes the installer TurboModule to JavaScript
- **WHEN** JavaScript calls `ExpoModulesDotnetInstaller.installModules()`
- **THEN** the macOS adapter MAY capture the current
  `facebook::jsi::Runtime` from that TurboModule host function
- **AND** it SHALL create the managed runtime handle without requiring
  `CallInvoker::invokeSync`
- **AND** generated synchronous module functions SHALL run as direct JSI host
  functions inside the current JavaScript call

#### Scenario: React Native Windows registers sync modules from the RNW runtime callback
- **GIVEN** React Native Windows exposes the installer native module and active
  Hermes runtime to adapter initialization
- **WHEN** the Windows adapter registers generated C# module functions
- **THEN** it SHALL create the managed runtime handle without requiring
  `CallInvoker::invokeSync`
- **AND** generated synchronous module functions SHALL run as direct JSI host
  functions inside the current JavaScript call
- **AND** RNW CLI build/deploy issues SHALL be recorded as toolchain evidence,
  not as proof that direct JSI host functions are unsupported

### Requirement: Runtime Task Context Ownership

Native runtime-task scheduling SHALL own the managed task context after the ABI
call accepts it, including failure paths that occur after native wraps the
callback.

#### Scenario: Scheduling fails after native takes context
- **GIVEN** managed code passes a task context to the scheduling ABI
- **WHEN** native returns an error after taking ownership
- **THEN** native SHALL release the task context through the provided release
  callback

### Requirement: Headless Hermes Loop

The headless testhost SHALL provide a runtime loop that processes queued tasks
and performs Hermes microtask checkpoints.

#### Scenario: Promise microtasks are queued
- **GIVEN** JavaScript schedules promise microtasks during evaluation or
  host-function execution
- **WHEN** the headless runtime loop reaches an idle point
- **THEN** it SHALL checkpoint microtasks so tests observe settled JavaScript
  promise behavior
