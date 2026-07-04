# Async Module Functions

## Goal

Allow authored `[JS]` module methods that return `Task` or `Task<T>` to be
called from JavaScript as promise-returning functions.

## Scope

This change updates the `Expo.ModulesCore` generated-binding layer and its
tests. It reuses the existing `Expo.JSI` promise capability, promise
settlement, and runtime scheduling APIs instead of adding a module-specific
promise implementation.

In scope:

- `[JS] async Task Foo()` and equivalent `Task`-returning methods.
- `[JS] async Task<T> Foo()` and equivalent `Task<T>`-returning methods when
  `T` already has a generated return codec.
- Promise rejection for argument-count, argument-codec, authored method,
  faulted-task, and canceled-task failures.

Out of scope:

- Promise-valued authored parameters.
- Module-author access to low-level `JavaScriptPromise` objects.
- Custom cancellation-token injection into authored module methods.
- New public async attributes or explicit async flags on `[JS]`.
- Platform-adapter changes beyond using the existing runtime scheduling path.

## Accepted Design

The generator infers async module functions from the authored return type. A
`Task` return type generates a JavaScript function that returns a Promise and
resolves with `undefined`. A `Task<T>` return type generates a JavaScript
function that returns a Promise and resolves with `T` encoded by the same typed
codec machinery used for synchronous returns.

Generated async host functions create and return the JavaScript Promise during
the original JSI host-function call. They decode all arguments synchronously
inside that call, because `this` and argument values are scoped refs that cannot
be captured across `await`. After decoding, generated code calls the authored
method and lets the existing promise scheduler settle the promise on the
runtime path when the returned task completes.

All generated async-call failures reject the returned Promise instead of
throwing synchronously. This includes argument-count failures, codec failures,
exceptions thrown before the authored method returns a task, faulted tasks, and
canceled tasks. This matches the expected JavaScript contract for async module
functions: callers can handle both validation and runtime failures with
`await`/`.catch`.

Sync `[JS]` functions keep their current direct-call behavior. The async path
is added beside the sync generated-function helper rather than replacing it.

## Delta Requirements

### ADDED Requirement: Async Function Generation Returns Promises

Generated async function glue SHALL expose authored `[JS]` methods returning
`Task` or `Task<T>` as JavaScript functions that return Promises.

#### Scenario: Task async function resolves undefined

- **GIVEN** a generated provider registers a module with an authored `[JS]`
  method returning `Task`
- **WHEN** JavaScript calls the generated function and the task completes
  successfully
- **THEN** the function SHALL return a JavaScript Promise
- **AND** the Promise SHALL resolve with JavaScript `undefined`

#### Scenario: Task of T async function resolves encoded value

- **GIVEN** a generated provider registers a module with an authored `[JS]`
  method returning `Task<T>`
- **AND** `T` has a supported generated return codec
- **WHEN** JavaScript calls the generated function and the task completes with a
  result
- **THEN** the function SHALL return a JavaScript Promise
- **AND** the Promise SHALL resolve with the result encoded through the
  generated codec for `T`

#### Scenario: Unsupported Task of T result type is reported

- **GIVEN** an authored `[JS]` method returns `Task<T>`
- **AND** `T` does not have a supported generated return codec
- **WHEN** the generator analyzes the method
- **THEN** it SHALL report the same unsupported-return diagnostic shape used for
  unsupported synchronous return types

### ADDED Requirement: Async Function Arguments Are Captured Before Await

Generated async function glue SHALL decode JavaScript arguments before the
host-function callback returns and SHALL NOT capture scoped JavaScript argument
or `this` refs across asynchronous continuations.

#### Scenario: Async function receives supported arguments

- **GIVEN** a generated async function has supported authored parameters
- **WHEN** JavaScript calls the generated function
- **THEN** generated dispatch SHALL validate the argument count during the
  host-function callback
- **AND** decode each argument through the generated parameter codec during the
  host-function callback
- **AND** pass only decoded managed values into the authored async method

### ADDED Requirement: Async Function Failures Reject Promises

Generated async function glue SHALL reject the returned Promise for generated
dispatch failures, authored-method failures, faulted tasks, and canceled tasks.

#### Scenario: Argument validation fails

- **GIVEN** JavaScript calls a generated async function with an unsupported
  argument count or value
- **WHEN** generated dispatch validates or decodes the arguments
- **THEN** the generated function SHALL return a JavaScript Promise
- **AND** the Promise SHALL reject with a JavaScript `Error`
- **AND** the validation or codec failure SHALL NOT escape as a synchronous
  JavaScript throw

#### Scenario: Authored async method throws before returning a task

- **GIVEN** JavaScript calls a generated async function
- **WHEN** the authored method throws before returning its task
- **THEN** the generated function SHALL return a JavaScript Promise
- **AND** the Promise SHALL reject with a JavaScript `Error`

#### Scenario: Authored async task fails

- **GIVEN** JavaScript calls a generated async function
- **WHEN** the authored task faults or is canceled
- **THEN** the generated function SHALL return a JavaScript Promise
- **AND** the Promise SHALL reject with a JavaScript `Error`

### MODIFIED Requirement: Sync Function Generation Uses Direct Calls

Generated synchronous function glue SHALL continue to decode arguments, call
authored methods directly, and encode return values through typed helpers.

#### Scenario: Synchronous function does not use async promise dispatch

- **GIVEN** a generated provider registers a module with a non-`Task` `[JS]`
  method
- **WHEN** JavaScript calls the generated function
- **THEN** the function SHALL keep the existing synchronous direct-call
  behavior
- **AND** it SHALL NOT wrap the result in a Promise
