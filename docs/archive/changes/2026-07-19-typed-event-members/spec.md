# Typed Event Members

## Goal

Replace stringly typed event emission at new authoring sites with generated,
awaitable event members that bind each JavaScript event name to its payload
type and compile-time codec.

```csharp
[ExpoModule]
public sealed partial class DownloadModule : Module
{
  [Event]
  public partial Func<ProgressEvent, Task> OnProgress { get; }

  public Task TickAsync() => OnProgress(new ProgressEvent(50));
}

public readonly record struct ProgressEvent(int Percent);
```

The JavaScript event name is `onProgress`. The generated delegate returns the
real dispatch `Task`, so authored code can await delivery and observe target
lookup, encoding, scheduling, and teardown failures. JavaScript listener
exceptions retain the existing Expo-compatible isolation rule: one failing
listener does not fail dispatch or prevent later listeners.

## Accepted Design

### Authoring surface

- A payload-less event uses `Func<Task>`.
- A single-payload event uses `Func<T, Task>` where `T` has an event-safe
  compile-time codec.
- `[Event]` lowercases only the first character of the C# property name.
  `[Event("name")]` preserves the explicit name verbatim. The `On` prefix is
  not stripped.
- The property is an instance, getter-only partial-property definition. The
  containing module is a top-level, non-generic partial class. This first slice
  rejects containers the generator cannot reproduce safely instead of emitting
  invalid partial declarations.
- The explicit event name must be non-null and non-blank. File-local module
  types, explicit-interface properties, ref returns, authored partial-property
  implementations, and property modifiers the generator cannot reproduce are
  rejected instead of producing invalid generated source.
- Apart from an accessibility modifier, `partial` is the only supported event
  property modifier. In particular, `new`, `virtual`, `abstract`, `override`,
  `sealed`, `required`, `extern`, and `unsafe` event properties are rejected.
- `Action` and `Action<T>` are deliberately unsupported. A void delegate would
  discard the existing asynchronous dispatch `Task`; the runtime has no global
  scheduler-failure sink that could make every dropped failure observable.
- `[Events]` and `SendEventAsync` remain supported as the migration and
  interop path.

### Generated state and dispatch

The generator emits the partial-property implementation, one delegate backing
field per event, and a generated initialization member on the module partial
class. The generated provider constructs the dispatch delegates and injects
them through that initializer. This keeps generated record codecs private to
the provider while the authored module partial stores only its typed delegates
and the context identity needed for rebind validation.

Generated registration initializes the event members with the owning
`DotnetRuntimeContext` before an `OnCreate` hook can run, and also ensures an
already-registered module instance is bound to the same context. Each property
returns one cached delegate for that module instance. Repeating initialization
with the same context preserves the existing delegate objects; binding the same
module instance to a different context fails.

The delegate dispatches through the existing `ModuleEventEmitter`. Work runs
inline when the caller already owns runtime access, uses synchronous runtime
execution when the host supports it, and otherwise schedules through the
existing asynchronous runtime-task path. The returned `Task` reaches a terminal
state only after dispatch and generated payload cleanup finish. No generated
event path blocks on asynchronous runtime work, drops a `Task`, uses reflection,
or changes the native ABI.

After initialization, invoking a generated delegate always returns a non-null
`Task`. Errors found while retaining a payload, reading a disposed context,
looking up a target, or executing the synchronous scheduler path fault or
cancel that task; they do not escape directly from delegate invocation. The
only intentional synchronous error is reading the generated property before
registration has initialized it.

Access during the authored constructor, before generated initialization has
completed, fails with a clear `InvalidOperationException`. `OnCreate` runs after
initialization. Both supported constructor strategies, parameterless and
`DotnetRuntimeContext`, use the same generated initialization regardless of
whether the authored type inherits `Module`. Emitting from `OnCreate` can still
fail because JavaScript event target attachment has not completed, matching the
existing string-based event path. After context teardown, awaiting a cached
event delegate fails or cancels without touching stale JSI state.

### Payload ownership

Ordinary payloads are captured until scheduled encoding runs. Authors must not
mutate captured mutable state until the returned `Task` completes.

Direct `JavaScriptValue` and `ArrayBuffer` payloads are advanced but supported.
The generated delegate never captures a scoped ref and never consumes the
caller-owned wrapper. `ArrayBuffer.Retain()` is runtime-independent, so
generated glue synchronously retains an invocation-owned lease before returning
the dispatch task and releases it when that task terminates. The caller SHALL
NOT race that retain operation with disposal, but may dispose the original once
invocation returns. `JavaScriptValue.Retain()` is runtime-affine: generated glue
retains and encodes its invocation copy only inside scheduled runtime access.
The caller therefore keeps the original `JavaScriptValue` alive until the
returned task completes. In both cases the original remains caller-owned and
usable after successful dispatch.

Composite payloads whose generated codec would consume or defer a nested
`JavaScriptValue` or `ArrayBuffer` are rejected in this slice because generated
glue cannot deep-retain arbitrary records, lists, or dictionaries without a new
codec ownership contract. Callback-containing payloads are also rejected
because callback codecs are decode-only. `JavaScriptObject` has no generated
module codec; this change does not add one and does not prevent a future optional
advanced codec.

The generator performs this event-safety classification before resolving the
payload codec or mutating the module's generated-record-codec collection. The
recursive check follows only actual codec inputs, including the selected record
constructor parameters, list elements, and dictionary values; unrelated
computed record properties do not make an otherwise safe record invalid. A
rejected payload therefore cannot leave an invalid callback `Encode` call or
other partial codec source behind.

## Scope

In scope:

- `EventAttribute` and generated `Func<Task>` / `Func<T, Task>` properties;
- typed-event generator models, diagnostics, partial type source, and provider
  initialization;
- payload-less, scalar, record, direct `JavaScriptValue`, and direct
  `ArrayBuffer` dispatch;
- event-name merging with legacy declarations and observing hooks;
- example-module migration, Hermes-backed tests, generator tests, and docs.

Out of scope:

- removing `[Events]` or `SendEventAsync`;
- changing JavaScript listener/prototype semantics or the typed JS facade;
- multiple event arguments, cancellation-token parameters, generated
  TypeScript, SharedObject payloads, views, or a `JavaScriptObject` codec;
- nested or generic event-bearing module classes;
- native ABI or platform-adapter changes.

## Alternatives Rejected

1. `Action` / `Action<T>` with `_ = EmitAsync(...)`: concise, but task faults
   can be silently lost.
2. Blocking `Action` / `Action<T>` with `GetAwaiter().GetResult()`: observes
   faults, but can deadlock an async-only runtime scheduler and makes an event
   call unexpectedly blocking.
3. A runtime-wide unhandled-task sink: preserves void delegates, but expands
   the host contract and still gives authored code no per-invocation completion
   or cancellation signal.

The accepted awaitable delegate uses the scheduler and failure semantics that
already exist instead of adding a parallel error channel.

## Delta Requirements

### ADDED Requirement: Typed Event Members Declare Names and Payloads

`[Event]` SHALL be valid on an instance, getter-only partial property whose type
is exactly `Func<Task>` or `Func<T, Task>`. The containing module SHALL be a
top-level, non-generic partial class. `T` SHALL have an event-safe compile-time
codec. The generated implementation SHALL return one cached delegate per module
instance.

The default JavaScript event name SHALL lowercase only the first C# property
character. An explicit `[Event(name)]` SHALL be used verbatim. The generator
SHALL NOT strip an `On` prefix.

#### Scenario: Payload event is generated

- **GIVEN** a module declares
  `[Event] public partial Func<ProgressEvent, Task> OnProgress { get; }`
- **WHEN** generated registration creates the module
- **THEN** `OnProgress` SHALL return a cached delegate
- **AND** invoking and awaiting it SHALL dispatch `onProgress`
- **AND** the payload SHALL be encoded with the generated `ProgressEvent` codec

#### Scenario: Payload-less event is generated

- **GIVEN** a module declares
  `[Event] public partial Func<Task> OnReady { get; }`
- **WHEN** authored code invokes and awaits `OnReady()`
- **THEN** the module SHALL dispatch `onReady` without a payload argument

#### Scenario: Explicit event name is preserved

- **GIVEN** a module declares
  `[Event("StatusChanged")] public partial Func<string, Task> OnStatus { get; }`
- **WHEN** generated registration declares the event
- **THEN** JavaScript SHALL observe `StatusChanged` verbatim
- **AND** it SHALL NOT receive `onStatus` as an alias

### ADDED Requirement: Typed Event Tasks Carry Dispatch Outcomes

Generated event delegates SHALL dispatch through the existing
`ModuleEventEmitter` and return its completion as a `Task`. They SHALL run
inline during current runtime access, use existing synchronous scheduling when
available, and otherwise use asynchronous runtime scheduling. They SHALL NOT
block waiting for asynchronous scheduling and SHALL NOT discard dispatch tasks.

The returned task SHALL complete only after target lookup, payload encoding,
JavaScript listener iteration, and generated ownership cleanup complete. Target
lookup, encoding, scheduling, and teardown failures SHALL fault or cancel that
task instead of being swallowed. Individual JavaScript listener exceptions
SHALL retain the existing EventEmitter isolation behavior: they SHALL NOT fault
the dispatch task or prevent later listeners from running.

Once its property has been initialized, every delegate invocation SHALL return
a non-null task. Failures detected before asynchronous scheduling or before the
first internal `await`, including payload retention, disposed-context access,
target lookup, and synchronous scheduler execution, SHALL fault or cancel that
task and SHALL NOT escape directly from `Func.Invoke`.

#### Scenario: Off-runtime invocation is awaited

- **GIVEN** authored managed code invokes a generated event delegate without
  owning runtime access
- **WHEN** the host requires asynchronous runtime scheduling
- **THEN** dispatch SHALL be scheduled through the owning runtime
- **AND** the returned task SHALL represent the scheduled operation
- **AND** authored code SHALL be able to await its success, failure, or
  cancellation

#### Scenario: Codec throws

- **GIVEN** event target lookup or payload encoding throws
- **WHEN** authored code awaits the generated delegate
- **THEN** the returned task SHALL surface that failure
- **AND** generated glue SHALL NOT invoke a global unhandled-error path

#### Scenario: Immediate dispatch validation fails

- **GIVEN** an initialized event delegate receives a disposed direct payload or
  its runtime context has already been disposed
- **WHEN** authored code invokes the delegate without awaiting it yet
- **THEN** invocation SHALL return a non-null faulted or canceled task
- **AND** the failure SHALL NOT escape synchronously from `Func.Invoke`

#### Scenario: JavaScript listener throws

- **GIVEN** one JavaScript listener throws while handling a typed event
- **WHEN** the existing EventEmitter iterates listeners
- **THEN** later listeners SHALL still run
- **AND** the listener exception SHALL NOT fault the returned dispatch task

#### Scenario: Cached delegate survives context teardown

- **GIVEN** authored code retains a generated event delegate
- **WHEN** its `DotnetRuntimeContext` is disposed before a later invocation
- **THEN** awaiting that invocation SHALL fail or cancel loudly
- **AND** it SHALL NOT access a disposed target or stale JSI handle

### ADDED Requirement: Event Members Are Initialized Before Lifecycle Hooks

Generated registration SHALL initialize all typed-event backing delegates with
the owning runtime context before invoking a newly created module's `OnCreate`
hook. Initialization SHALL be idempotent for the same module/context pair and
SHALL fail if the same module instance is rebound to a different context.
Both supported constructor strategies, parameterless and
`DotnetRuntimeContext`, SHALL use the same generated event behavior, including
for modules that do not inherit `Module`.

The generated provider SHALL construct and inject the dispatch delegates. The
module partial SHALL store and expose those delegates but SHALL NOT reference
provider-private generated record codecs. Repeated same-context initialization
SHALL preserve the originally injected delegate identities.

#### Scenario: Lifecycle hook reads an event member

- **GIVEN** a module declares a typed event and an `OnCreate` hook
- **WHEN** generated registration constructs the module
- **THEN** event-member initialization SHALL finish before `OnCreate` runs
- **AND** the hook SHALL receive the same cached delegate later returned by the
  property

#### Scenario: Constructor reads an event member too early

- **GIVEN** an authored constructor accesses a generated event property before
  registration can initialize it
- **WHEN** the getter runs
- **THEN** it SHALL throw a clear `InvalidOperationException`
- **AND** it SHALL NOT return `null` or an unbound delegate

### MODIFIED Requirement: Event Declarations Merge for Registration and Hooks

Typed `[Event]` names SHALL merge with legacy `[Events]` names before generated
registration decides whether to create a NativeModule, attaches declared event
names, reserves observing-hook member names, and validates
`OnStartObserving`/`OnStopObserving` hooks.

Legacy `[Events]` declarations and `SendEventAsync` SHALL retain their existing
behavior. A name declared by more than one typed member, or by both `[Event]`
and `[Events]`, SHALL fail compilation instead of being deduplicated by source
order.

#### Scenario: Typed event enables listeners and observing hooks

- **GIVEN** a module declares only `[Event] ... OnProgress`
- **AND** declares observing hooks for `onProgress`
- **WHEN** generated registration installs the module
- **THEN** it SHALL use the NativeModule event-emitter prototype
- **AND** attach `onProgress` as a declared event
- **AND** invoke observing hooks under the existing first-listener and
  last-listener rules

#### Scenario: Typed and legacy declarations collide

- **GIVEN** `[Events("onStatus")]` and `[Event] ... OnStatus` occur on one
  module
- **WHEN** the generator analyzes the module
- **THEN** it SHALL emit the typed-event duplicate-name diagnostic
- **AND** it SHALL NOT silently merge the duplicate declarations

### ADDED Requirement: Typed Event Payload Ownership Is Explicit

Generated typed-event dispatch SHALL capture no scoped ref. For an ordinary
payload, authored code SHALL keep mutable captured state stable until the
returned task completes.

For a direct `ArrayBuffer`, generated glue SHALL retain an invocation-owned
lease synchronously before returning the task. The original wrapper SHALL
remain caller-owned and usable. The retained lease SHALL remain alive until
dispatch reaches a terminal state and SHALL then release exactly once on
success, failure, or cancellation.

For a direct `JavaScriptValue`, generated glue SHALL retain and encode an
invocation copy only while executing on the owning runtime. The caller SHALL
keep the original wrapper alive until the returned task reaches a terminal
state. Generated glue SHALL dispose only its runtime-created retained copy and
SHALL NOT consume the caller's original wrapper.

Composite payloads containing nested transfer-sensitive wrappers and any
payload containing a decode-only JavaScript callback codec SHALL fail at build
time in this slice.

The generator SHALL classify event payload safety before invoking general codec
resolution or adding generated record codecs. Recursive classification SHALL
follow the fields actually encoded by each composite codec. Rejected callback
records, lists, or dictionaries SHALL NOT leave generated callback `Encode`
calls or unrelated C# compiler diagnostics in provider output.

#### Scenario: Direct JavaScriptValue payload is retained on the runtime

- **GIVEN** authored code invokes a typed event with an owned
  `JavaScriptValue`
- **WHEN** dispatch is scheduled for later runtime execution
- **THEN** the caller SHALL keep the original wrapper alive until the returned
  task completes
- **AND** generated glue SHALL retain and encode an independent invocation copy
  only during runtime access
- **AND** terminal cleanup SHALL release only the invocation copy
- **AND** the caller's original SHALL remain usable after successful dispatch

#### Scenario: Direct ArrayBuffer payload owns a scheduling lease

- **GIVEN** authored code invokes a typed event with an owned `ArrayBuffer`
- **WHEN** dispatch is scheduled for later runtime execution
- **THEN** generated glue SHALL synchronously retain an independent lease
  before returning the task
- **AND** the caller MAY dispose the original after invocation returns
- **AND** terminal cleanup SHALL release the retained lease exactly once

#### Scenario: Composite wrapper payload is rejected

- **GIVEN** a typed event payload is a record, list, or dictionary containing
  `JavaScriptValue` or `ArrayBuffer`
- **WHEN** the generator analyzes the event
- **THEN** it SHALL report the unsupported event-payload diagnostic
- **AND** it SHALL NOT imply a deep-retention contract that generated codecs do
  not provide

#### Scenario: Callback composite is rejected before codec generation

- **GIVEN** an event payload is a record, list, or dictionary whose encoded
  value contains `JavaScriptCallback`
- **WHEN** the generator validates event safety
- **THEN** it SHALL report `EXPOJSI019`
- **AND** generated source SHALL NOT contain a callback codec `Encode` call
- **AND** the consuming compilation SHALL NOT receive a secondary generated-C#
  error for that rejected payload

### ADDED Requirement: Invalid Typed Events Are Build Diagnostics

Typed-event validation SHALL use the next free diagnostic IDs after
`EXPOJSI017`:

| ID | Condition |
| --- | --- |
| `EXPOJSI018` | An `[Event]` property has a null/blank explicit name; is static, indexed, non-partial, has an authored implementation/body or setter, is an explicit-interface or ref-return member, is also `[JS]`, has unsupported member modifiers, has a delegate type other than `Func<Task>` / `Func<T, Task>`, or is declared in an unsupported file-local/nested/generic/non-partial module container. |
| `EXPOJSI019` | A typed-event payload has no encode-capable event codec, contains a callback codec, or contains a nested transfer-sensitive wrapper. |
| `EXPOJSI020` | Two typed declarations, or a typed and legacy declaration, resolve to the same JavaScript event name. |

Legacy-only invalid or duplicate `[Events]` declarations SHALL continue to use
`EXPOJSI009`. Other existing diagnostic meanings SHALL remain unchanged.

#### Scenario: Void delegate is rejected

- **GIVEN** a module declares `[Event] public partial Action<string> OnStatus`
- **WHEN** the generator analyzes the property
- **THEN** it SHALL report `EXPOJSI018`
- **AND** it SHALL explain that an awaitable `Func<T, Task>` is required

#### Scenario: Event source shape cannot be reproduced

- **GIVEN** an `[Event]` property has an authored implementation, explicit
  interface, ref return, unsupported modifier, or belongs to an unsupported
  module container
- **WHEN** the generator analyzes the property
- **THEN** it SHALL report `EXPOJSI018` naming the unsupported shape
- **AND** it SHALL NOT emit a partial declaration that fails later C# compilation

#### Scenario: Explicit event name is invalid

- **GIVEN** `[Event]` receives a null, empty, or whitespace-only explicit name
- **WHEN** the generator analyzes the property
- **THEN** it SHALL report `EXPOJSI018`
- **AND** it SHALL NOT fall back to the implicit property name

#### Scenario: Payload codec is unavailable

- **GIVEN** a valid-shaped typed event uses an unsupported payload type
- **WHEN** the generator analyzes the property
- **THEN** it SHALL report `EXPOJSI019` naming the property and payload type
- **AND** it SHALL NOT emit reflection or dynamic conversion

## Verification Requirements

Generator tests SHALL cover extraction, lower-camel and explicit names,
payload-less and payload delegate source, cached initialization before
`OnCreate`, both constructor strategies, all `EXPOJSI018`-`020` branches,
legacy diagnostic stability, and generated-source compilation. They SHALL also
cover constructor-time getter failure; repeated same-context registration with
stable delegate identity; different-context rebind rejection; a module with
distinct typed and legacy event names; record payload compilation without
widening provider-private codec visibility; and record/list/dictionary callback
rejection without invalid callback `Encode` output or secondary compiler
diagnostics.

Hermes-backed tests SHALL cover scalar, payload-less, and record delivery;
observing hooks; legacy coexistence; returned-task codec/teardown failures;
listener-error isolation; stable delegate identity; and direct
`JavaScriptValue` and `ArrayBuffer` release counts across scheduled success,
failure, and context teardown. Tests SHALL prove no caller-owned wrapper is
consumed and no retained invocation copy or lease leaks or releases twice.

Final verification SHALL run the generator suite, canonical managed suite,
mobile typecheck, format check, reflection and owned-wrapper scans, documentation
checks, staged privacy scan, and an independent full-range review.
