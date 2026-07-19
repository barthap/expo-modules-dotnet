# Ownership And Scoped Refs

## Purpose

Define the lifetime model for owned wrappers and scoped refs in `Expo.JSI`.

## Requirements

### Requirement: Owned Wrappers Release Handles

Owned wrappers SHALL release their native handles exactly once unless ownership
is explicitly detached for native return handling. Disposal SHALL atomically
relinquish the owned handle so duplicate `Dispose` calls are safe. This does not
make disposal concurrent with member access safe: wrappers are single-owner, and
code that needs independent concurrent ownership SHALL call `Retain` first.

#### Scenario: Value wrapper is disposed
- **GIVEN** a `JavaScriptValue` owns a native value handle
- **WHEN** `Dispose` is called
- **THEN** the wrapper SHALL call the ABI release function and mark itself
  disposed

#### Scenario: Value wrapper is detached
- **GIVEN** a host-function callback returns an owned `JavaScriptValue`
- **WHEN** managed code detaches the value for native return handling
- **THEN** the wrapper SHALL stop owning the handle and native code SHALL become
  responsible for releasing it exactly once

### Requirement: Scoped Refs Are Temporary

Scoped refs SHALL be valid only during an active runtime access frame, such as
`JavaScriptRuntime.Execute`, scheduled runtime work, or a host-function
callback.

#### Scenario: Host function reads an argument
- **GIVEN** a managed host-function callback receives `JavaScriptArguments`
- **WHEN** it calls `GetValue`
- **THEN** it SHALL receive a `JavaScriptValueRef` that is valid only inside
  the callback frame

#### Scenario: Scoped ref escapes intentionally
- **GIVEN** managed code needs a value ref after the current frame returns
- **WHEN** it calls `Retain`
- **THEN** it SHALL receive an owned `JavaScriptValue` that follows owned
  wrapper disposal rules

### Requirement: Scoped Traversal Uses Handle Scope

Temporary handles produced by ref traversal SHALL be tracked by the active
handle scope and released when that scope exits.

#### Scenario: Object ref reads a property
- **GIVEN** a `JavaScriptObjectRef` inside an active handle scope
- **WHEN** it reads a property
- **THEN** the returned `JavaScriptValueRef` SHALL be tracked by the active
  scope instead of requiring a disposable intermediate wrapper

#### Scenario: Invalid ref is used
- **GIVEN** a default ref or a ref whose scope has ended
- **WHEN** managed code tries to use it
- **THEN** managed code SHALL fail loudly before touching native memory

### Requirement: Runtime Handles Are Borrowed

`JavaScriptRuntime` SHALL NOT own the native runtime handle.

#### Scenario: Runtime wrapper is created
- **GIVEN** native creates or owns a JavaScript runtime
- **WHEN** managed code wraps it
- **THEN** native remains responsible for keeping the runtime and API table
  valid for the wrapper lifetime

### Requirement: NativeState Registry Owns Managed State Tokens

Each managed runtime context SHALL own an internal NativeState registry that
maps `(type id, registry id, generation)` token tuples to managed state objects.
Native object state SHALL store only those token tuples and release callbacks,
not raw managed object pointers.

#### Scenario: Managed state is resolved
- **GIVEN** native returns a NativeState token tuple for a JavaScript object
- **WHEN** managed code resolves it as `TState`
- **THEN** the registry SHALL validate runtime liveness, type id, registry id,
  generation, and managed object type before returning the state object
- **AND** `GetNativeState<TState>()` SHALL fail loudly when the entry is missing
  or stale
- **AND** `TryGetNativeState<TState>()` SHALL return `false` and `null` when the
  entry is missing or stale

#### Scenario: Managed state is released by native callback
- **GIVEN** a native object state entry is destroyed, replaced, or cleared
- **WHEN** native invokes the managed release callback
- **THEN** the registry SHALL remove the matching token idempotently
- **AND** duplicate or late release callbacks after registry disposal SHALL be
  safe no-ops
- **AND** the callback SHALL swallow managed exceptions before returning across
  the unmanaged boundary

#### Scenario: Runtime context disposes native state registry
- **GIVEN** a runtime context owns live NativeState registry entries
- **WHEN** the runtime context is disposed
- **THEN** the registry SHALL invalidate all entries
- **AND** stale native tokens SHALL no longer resolve to managed state

### Requirement: Binary Borrowing And Ownership

ArrayBuffer byte callbacks SHALL borrow native storage only for the duration
of the synchronous callback. Managed `byte[]` values SHALL be copied at codec
boundaries. Span return values SHALL be copied immediately because a span has
no owner that JavaScript can retain. No arbitrary managed array SHALL be pinned
for a long-lived or asynchronous operation.

#### Scenario: A span is projected into a module call
- **GIVEN** a generated synchronous method has one `Span<byte>` or
  `ReadOnlySpan<byte>` parameter
- **WHEN** the method is invoked
- **THEN** the generator SHALL borrow bytes through one scoped callback
- **AND** it SHALL reject asynchronous span parameters and more than one span
  parameter with diagnostics

#### Scenario: A native-backed wrapper is retained
- **GIVEN** a module retains or returns a native-backed `ArrayBuffer`
- **WHEN** the wrapper is copied
- **THEN** the copy SHALL retain the shared MutableBuffer storage
- **AND** mutation through either wrapper SHALL remain visible to the other
