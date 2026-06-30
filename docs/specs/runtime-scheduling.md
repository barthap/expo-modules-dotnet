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
types in managed code.

#### Scenario: React Native connector schedules work
- **GIVEN** native platform glue has a borrowed React Native Hermes runtime and
  scheduling callbacks
- **WHEN** managed code schedules runtime work through `JavaScriptRuntime`
- **THEN** the React Native connector SHALL route the work through the injected
  callbacks
- **AND** sync execution SHALL be reported as supported only when the platform
  host can execute safely on the runtime thread

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
