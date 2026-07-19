# ArrayBuffer And Binary Codec Delta Spec

## Goal

Add production `ArrayBuffer` support across the portable C++/C ABI bridge,
low-level `Expo.JSI` wrappers, and generated `Expo.ModulesCore` bindings.

The change preserves zero-copy access when storage has an explicit owner,
makes copying visible in the API, and defines deterministic runtime teardown
for JavaScript-heap-backed buffers before generated modules can retain binary
data across asynchronous work.

## Evidence And Baseline

- The ABI and managed value-kind enums already identify JavaScript
  `ArrayBuffer` values, and native value-kind detection already recognizes
  them.
- `Expo.JSI` does not yet expose an ArrayBuffer wrapper, mutable-buffer handle,
  or scoped byte-access API.
- `Expo.ModulesCore` does not yet generate bindings for `ArrayBuffer`,
  `byte[]`, `Span<byte>`, or `ReadOnlySpan<byte>`.
- The repository's JSI version exposes `ArrayBuffer::tryGetMutableBuffer`,
  allowing native-owned storage to be retained independently of its original
  JavaScript object.
- Upstream Expo distinguishes native-backed storage from JavaScript-heap-backed
  storage, scopes JavaScript-heap byte access to the owning runtime, and uses a
  runtime-owned long-lived-object collection for retained JSI state.
- Existing generated async results keep owned `JavaScriptValue` wrappers alive
  until Promise settlement, but the result carrier has no explicit abandonment
  cleanup when runtime scheduling never claims the result.

## Scope

### Included

- Add a generic runtime-owned collection for long-lived JSI state in the
  portable native `Expo.JSI` runtime boundary.
- Add opaque ArrayBuffer and retained MutableBuffer ABI handles without
  exposing JSI layouts to managed code.
- Add scoped and owned low-level `JavaScriptArrayBuffer` wrappers plus a
  runtime-neutral owned `JavaScriptMutableBuffer` wrapper in `Expo.JSI`.
- Add the module-facing `Expo.ModulesCore.ArrayBuffer` abstraction with
  JavaScript-backed and native-backed storage.
- Add explicit allocation, copying, byte access, retention, and disposal
  semantics.
- Add generated argument and return handling for `ArrayBuffer`, `byte[]`,
  `Span<byte>`, and `ReadOnlySpan<byte>`.
- Add narrow exactly-once cleanup for owned async return state, including the
  existing `Task<JavaScriptValue>` path.
- Verify identity, aliasing, copying, detachment, resizing, cross-runtime use,
  asynchronous access, and runtime teardown with Hermes-backed tests.
- Record the deferred Promise capability migration in the roadmap and planning
  index.

### Excluded

- Accepting TypedArray views where an `ArrayBuffer` argument is declared.
- Generated codecs for typed arrays, `Memory<byte>`, `ReadOnlyMemory<byte>`,
  `IMemoryOwner<byte>`, streams, or `SharedArrayBuffer`.
- Migrating `JavaScriptPromise` capability ownership onto the new long-lived
  state collection in this change.
- Pinning arbitrary managed arrays for JavaScript lifetime or adding a
  managed-array-backed MutableBuffer.
- Implicit copies when a JavaScript-backed buffer is detached, resized, or
  returned to a different runtime.
- Synchronization for concurrent native byte access. Callers remain
  responsible for avoiding data races.
- Platform-specific binary APIs or changes to authored Expo modules outside
  the reusable bridge and its tests.

## Accepted Design

### Package Boundary

`Expo.JSI` owns JSI mechanics, opaque native handles, runtime affinity,
long-lived JSI state, and low-level ArrayBuffer and MutableBuffer wrappers.

`Expo.ModulesCore` owns the module-facing `ArrayBuffer` abstraction, convenient
binary codecs, generated invocation lifetimes, and source-generator
diagnostics. No ModulesCore concept is added to native C++.

### Storage Kinds

`Expo.ModulesCore.ArrayBuffer` has two semantic storage kinds. The distinction
is internal and is not exposed as a public backing-kind property.

#### Native-backed storage

Native-backed storage owns a retained `shared_ptr<jsi::MutableBuffer>` through
an opaque native handle. It covers both bridge-allocated buffers and
MutableBuffer storage recovered from a JavaScript ArrayBuffer.

- The storage is runtime-neutral and may be encoded into any live JavaScript
  runtime.
- Returning it creates a new JavaScript ArrayBuffer object over the same
  MutableBuffer. JavaScript object identity is not preserved.
- The original and newly created JavaScript objects alias the same bytes, so
  mutation through either view is observable through the other.
- Detaching the original JavaScript object does not invalidate the retained
  MutableBuffer storage.
- No synchronization is implied by shared ownership.

#### JavaScript-backed storage

JavaScript-backed storage retains the original JavaScript ArrayBuffer identity
and its originating runtime through a runtime-owned long-lived-state entry.

- Byte access occurs only inside a valid access frame for the originating
  runtime.
- Returning the buffer to that runtime preserves JavaScript strict identity.
- Returning it to a different runtime fails. The bridge never schedules
  cross-runtime work or performs an implicit copy.
- `Copy` or `CopyAsync` produces explicit native-backed storage that can cross
  runtimes.

### Decode Selection

Generated decoding accepts an actual JavaScript `ArrayBuffer` only. A TypedArray
view is an ordinary object for this contract and is rejected.

The decode chain is:

```text
JavaScriptValueRef
  -> JavaScriptArrayBufferRef
  -> try to retain MutableBuffer
     -> success: NativeBacked JavaScriptMutableBuffer storage
     -> unavailable: retain original ArrayBuffer as JavaScriptBacked storage
```

The bridge attempts MutableBuffer recovery before retaining an owned JSI
ArrayBuffer. Native-backed decoding therefore does not keep the original JSI
object handle.

### Runtime-owned Long-lived State

The native runtime state owns a generic long-lived-state collection. JS-backed
ArrayBuffer state is its first consumer; the design is reusable by Promise
capabilities and other retained JSI state later.

- The collection strongly owns registered entries.
- An entry refers back to its runtime state without creating a strong ownership
  cycle.
- Managed `Retain()` duplicates ownership of the existing storage entry without
  cloning or touching the JSI object and is safe away from the runtime thread.
- Releasing the last managed lease requests removal and JSI release on the
  originating runtime path.
- A copy-safe scheduled-release token observes both execution and dropped-work
  destruction. Executed work releases on the runtime; dropped work moves the
  entry to an explicit deferred-release state that is drained by the next
  runtime access or by teardown.
- The scheduled-release token strongly retains the runtime-state tombstone, not
  a raw collection, entry, connector, or runtime pointer. Runtime-handle release
  clears borrowed runtime/executor access but the invalid runtime state remains
  safe for a late token invocation or final-copy destruction to observe.
- A release request racing runtime teardown releases or invalidates the entry
  exactly once.
- Once teardown invalidates an entry, managed access fails loudly and later
  disposal is an idempotent no-op.

Runtime shutdown follows these ordered phases when JSI is still available:

1. Mark the runtime state as closing so new scheduled or retained work is
   rejected.
2. Sweep long-lived JSI entries on the runtime path while the runtime remains
   valid.
3. Invalidate the borrowed runtime pointer and scheduler state.
4. Complete managed and adapter teardown.

Hosts that report only late invalidation SHALL invalidate entries without
touching stale JSI. Each affected entry SHALL atomically detach its retained JSI
payload into an intentional no-destructor quarantine, keep only invalid
metadata visible to managed handles, and increment an abandonment counter
exactly once. Late handle disposal SHALL never destroy the quarantined JSI
payload. The leak is bounded to entries still live when that runtime misses
live-JSI cleanup. It is the explicit safe fallback and must not be disguised as
ordinary disposal.

### Low-level Expo.JSI Surface

`Expo.JSI` exposes:

- `JavaScriptArrayBufferRef`, a scoped non-owning view obtained from
  `JavaScriptValueRef`;
- `JavaScriptArrayBuffer`, an owned runtime-affine wrapper that participates in
  long-lived-state teardown;
- `JavaScriptMutableBuffer`, an owned runtime-neutral wrapper over retained
  MutableBuffer storage;
- runtime conversion from a retained MutableBuffer into an owned JavaScript
  ArrayBuffer/value.

All native state crosses the C ABI as opaque handles. A raw byte pointer may be
observed by managed code only while the corresponding wrapper invokes a
synchronous span callback. No JSI object layout crosses the ABI.

Successful ArrayBuffer and MutableBuffer handle results also carry the checked
logical byte length captured while the source is valid. Managed wrappers store
that length, so `ByteLength` never requires a later runtime access or raw byte
projection. For JavaScript-backed storage the snapshot remains the logical
length; later access still rejects detachment or a changed physical length.

### Module-facing Surface

`Expo.ModulesCore.ArrayBuffer` is a sealed explicitly owned wrapper implementing
`IDisposable`. Its public surface includes:

```csharp
public int ByteLength { get; }

public static ArrayBuffer Allocate(int byteLength);
public static ArrayBuffer CopyFrom(ReadOnlySpan<byte> bytes);

public ArrayBuffer Retain();

public ArrayBuffer Copy();
public Task<ArrayBuffer> CopyAsync(CancellationToken cancellationToken = default);

public byte[] ToArray();
public Task<byte[]> ToArrayAsync(CancellationToken cancellationToken = default);
```

It also exposes mutable and read-only callback-based access, with void and
result-returning overloads:

```csharp
WithBytes(...);
WithBytesAsync(...);
WithReadOnlyBytes(...);
WithReadOnlyBytesAsync(...);
```

The callbacks receive `Span<byte>` or `ReadOnlySpan<byte>` and complete
synchronously. No callback may return a Task that captures the span.

- `Allocate` validates a non-negative `int` length and returns zero-filled
  native-backed storage.
- `CopyFrom` copies immediately into independent native-backed storage.
- There is no `byte[]` constructor, `Wrap(byte[])`, or uninitialized allocation
  API.
- Zero-length buffers are supported.
- Buffers larger than `int.MaxValue` are rejected at the managed boundary
  because `byte[]` and span projections cannot represent them.
- `Copy` always returns independent native-backed storage.
- `ToArray` always returns an independent managed array.
- Operations on disposed or invalidated wrappers fail before touching native
  memory.

### Scoped And Asynchronous Byte Access

- Native-backed synchronous callbacks run directly on the calling thread.
- Native-backed async callbacks also run inline and return a completed Task;
  they do not introduce `Task.Run` or arbitrary thread-pool dispatch.
- JavaScript-backed synchronous callbacks require an active access frame for
  the originating runtime and fail when called elsewhere.
- JavaScript-backed async callbacks schedule one synchronous callback onto the
  originating runtime and complete with its result, exception, or cancellation.
- Low-level async access retains a scheduling lease until the runtime callback
  runs or scheduling fails, is canceled, is dropped, or is invalidated. Every
  completion path releases that lease exactly once.
- A span never crosses an `await`, scheduled callback boundary, or wrapper
  disposal boundary.
- Callback-based access provides no locking. Concurrent mutation is a caller
  responsibility.

### Detachment And Resizing

Decode captures an immutable logical `ByteLength` after rejecting an already
detached JavaScript ArrayBuffer.

Before every JavaScript-backed access or same-runtime encoding, native code
verifies that the original ArrayBuffer is still attached and still has exactly
the captured size. Detachment or any resize invalidates that operation. The
bridge throws instead of silently copying, truncating, or extending the logical
view. Generated async functions surface that failure as Promise rejection.

Native-backed storage remains valid independently of later changes to the
original JavaScript object.

### Explicit Module Ownership

Generated glue owns a decoded `ArrayBuffer` argument for the full authored
invocation:

- until a synchronous method returns or throws; or
- until an asynchronous method's Task completes, faults, or is canceled.

Authored code borrows that wrapper during the invocation. It must not dispose
or store it unless it first calls `Retain()` and assumes responsibility for the
retained wrapper.

An authored `ArrayBuffer` return transfers one owned wrapper to generated glue.
Pass-through code returns `argument.Retain()` rather than returning the
glue-owned argument itself. The codec borrows the returned wrapper while
encoding; generated glue disposes it after encoding or abandonment.

### Binary Codec Matrix

| Authored type | Argument semantics | Return semantics |
| --- | --- | --- |
| `ArrayBuffer` | Retain selected backing storage; generated glue owns the wrapper | Transfer wrapper ownership; preserve JS identity or native byte aliasing |
| `byte[]` | Copy before invoking authored code | Copy exactly once into native-backed storage |
| `Span<byte>` | Mutable zero-copy borrow for synchronous methods only | Copy immediately into native-backed storage |
| `ReadOnlySpan<byte>` | Read-only zero-copy borrow for synchronous methods only | Copy immediately into native-backed storage |

Span handling is generator-specialized instead of forcing ref structs through
the existing `IJavaScriptCodec<T>` abstraction. A generated async method with a
span parameter fails compilation with an actionable generator diagnostic.
An exported method may declare at most one `Span<byte>` or
`ReadOnlySpan<byte>` parameter. This restriction applies only to those ref-like
span parameters; `ArrayBuffer`, `byte[]`, and ordinary parameters remain
unrestricted in arity. Supporting multiple simultaneous spans would require a
grouped byte-access primitive whose delegate receives all spans in one callback
frame. Nesting the current callbacks is invalid because the inner lambda would
capture the outer ref-struct parameter (`CS9108`). Synchronous span return
values are encoded before the span can escape and always copy.

Ordinary managed arrays cannot be moved into JSI storage. A `byte[]` return does
not imply unique ownership, and adopting its allocation would require
long-lived managed pinning. Modules that need a move-capable or zero-copy return
allocate and fill `ArrayBuffer` directly.

### Owned Async Result Cleanup

`JavaScriptPromiseResult` gains a narrow owned-state lease used by generated
async wrapper returns. The lease has an exactly-once state transition:

```text
pending -> claimed by runtime settlement -> encoded/transferred/released
pending -> abandoned before runtime settlement -> released
```

- Runtime-side settlement claims the state once before encoding.
- Codec failure after claim releases the owned state.
- Scheduling failure, cancellation before claim, or runtime teardown abandons
  and releases the state.
- Struct copies or competing cleanup paths cannot release the state twice.
- The existing `Task<JavaScriptValue>` generated return path uses the same
  mechanism so it no longer leaks when settlement scheduling never claims the
  returned wrapper.

This does not migrate native Promise capability handles to the long-lived-state
collection and does not change Promise settlement ordering.

## Delta Requirements

### MODIFIED Requirement: Runtime Teardown Preserves Live-JSI Cleanup

Runtime invalidation SHALL distinguish closing from invalid state so retained
JSI state can be swept while the originating runtime is still valid.

#### Scenario: Runtime closes with live ArrayBuffer state

- **GIVEN** the runtime-owned collection contains JavaScript-backed ArrayBuffer
  entries
- **WHEN** a host begins teardown while JSI remains available
- **THEN** native SHALL reject new retained or scheduled work
- **AND** sweep the entries on the runtime path
- **AND** release each retained JSI payload exactly once before invalidating the
  borrowed runtime pointer

#### Scenario: Release races runtime closing

- **GIVEN** the last managed ArrayBuffer lease is released while runtime
  teardown is sweeping the collection
- **WHEN** the scheduled release and teardown observe the same entry
- **THEN** exactly one path SHALL release or invalidate it
- **AND** neither path SHALL touch stale JSI

#### Scenario: Scheduled release is dropped while runtime remains active

- **GIVEN** releasing the last managed lease queued runtime work
- **AND** the executor drops that work without invoking it
- **WHEN** the runtime next receives valid access or begins teardown
- **THEN** the dropped-work token SHALL expose the entry as deferred
- **AND** native SHALL release it once on that valid runtime path
- **AND** it SHALL NOT remain permanently hidden in a queued state

#### Scenario: Queued release outlives the runtime handle

- **GIVEN** a scheduled-release token remains queued
- **WHEN** the runtime handle is released before that token executes or drops
- **THEN** the token SHALL retain only lifetime-safe runtime-state tombstone
  ownership
- **AND** later invocation or destruction SHALL observe invalid state without
  adding another release or abandonment transition
- **AND** it SHALL NOT touch a connector, runtime, collection, or entry through
  a dangling pointer

#### Scenario: Host reports only late invalidation

- **GIVEN** the host reports invalidation after JSI is unusable
- **WHEN** the runtime state invalidates outstanding entries
- **THEN** managed wrappers SHALL become unusable
- **AND** cleanup SHALL NOT invoke JSI or dereference the stale runtime
- **AND** retained JSI payload destruction SHALL be intentionally quarantined
  and counted once
- **AND** later wrapper disposal SHALL be safe and idempotent

### ADDED Requirement: ArrayBuffer ABI Uses Opaque Ownership

The ABI SHALL expose ArrayBuffer and MutableBuffer operations only through
opaque handles and checked result structures.

#### Scenario: Handle result captures logical length

- **GIVEN** native successfully retains, allocates, copies, or clones an
  ArrayBuffer or MutableBuffer handle
- **WHEN** it returns the checked handle result
- **THEN** the result SHALL include the checked `int32_t` logical byte length
- **AND** the managed wrapper SHALL snapshot that length without a later JSI
  access

#### Scenario: Zero-length storage is projected

- **GIVEN** a successful byte-span result has length zero
- **WHEN** its native data pointer is null
- **THEN** managed byte access SHALL construct an empty span
- **AND** it SHALL NOT treat the null pointer as an error

#### Scenario: MutableBuffer is recovered

- **GIVEN** native receives a scoped JavaScript ArrayBuffer backed by a JSI
  MutableBuffer
- **WHEN** managed code requests retained native storage
- **THEN** native SHALL return an opaque handle owning a
  `shared_ptr<jsi::MutableBuffer>`
- **AND** managed code SHALL NOT observe the shared pointer or JSI layout

#### Scenario: JavaScript heap storage is retained

- **GIVEN** `tryGetMutableBuffer` is unavailable for a scoped JavaScript
  ArrayBuffer
- **WHEN** managed code retains that buffer
- **THEN** native SHALL register the original JSI object in the originating
  runtime's long-lived-state collection
- **AND** return an opaque owned ArrayBuffer handle

#### Scenario: Native-backed value is created

- **GIVEN** managed code owns a MutableBuffer handle
- **WHEN** it encodes that storage into a live JavaScript runtime
- **THEN** native SHALL create a JavaScript ArrayBuffer over the same shared
  storage
- **AND** return an owned value handle

### MODIFIED Requirement: Managed JSI Wrappers Include ArrayBuffer

`Expo.JSI` SHALL expose scoped and owned JavaScript ArrayBuffer wrappers plus an
owned runtime-neutral MutableBuffer wrapper.

#### Scenario: Scoped ArrayBuffer is inspected

- **GIVEN** an active `JavaScriptValueRef` contains an attached ArrayBuffer
- **WHEN** managed code converts it to `JavaScriptArrayBufferRef`
- **THEN** the ref SHALL remain bounded by the current handle scope
- **AND** byte access SHALL remain bounded by the current runtime access frame

#### Scenario: Scoped ArrayBuffer escapes intentionally

- **GIVEN** a scoped JavaScript ArrayBuffer has no recoverable MutableBuffer
- **WHEN** managed code calls `Retain`
- **THEN** it SHALL receive an owned runtime-affine
  `JavaScriptArrayBuffer`
- **AND** disposal SHALL participate in runtime-owned long-lived-state release

#### Scenario: Retained MutableBuffer escapes the runtime frame

- **GIVEN** a scoped JavaScript ArrayBuffer exposes a MutableBuffer
- **WHEN** managed code retains that storage
- **THEN** it SHALL receive an owned `JavaScriptMutableBuffer`
- **AND** its bytes SHALL remain alive without retaining the original JSI
  object

### ADDED Requirement: ModulesCore ArrayBuffer Preserves Explicit Ownership

`Expo.ModulesCore` SHALL expose an explicitly owned module-facing
`ArrayBuffer` whose backing is selected during decoding.

#### Scenario: JavaScript-backed argument is returned

- **GIVEN** an authored method receives a JavaScript-backed `ArrayBuffer`
- **AND** it returns a retained copy to the originating runtime
- **WHEN** generated glue encodes the return
- **THEN** JavaScript strict identity SHALL be preserved
- **AND** generated glue SHALL release its argument and return leases exactly
  once

#### Scenario: Native-backed argument is returned

- **GIVEN** an authored method receives native-backed storage
- **WHEN** it returns a retained copy
- **THEN** generated glue SHALL create a new JavaScript ArrayBuffer object over
  the same MutableBuffer
- **AND** mutation through either JavaScript object SHALL affect the shared
  bytes

#### Scenario: JavaScript-backed buffer targets another runtime

- **GIVEN** an owned JavaScript-backed buffer originated in runtime A
- **WHEN** code attempts to encode it into runtime B
- **THEN** encoding SHALL fail
- **AND** the bridge SHALL NOT copy or schedule work on runtime A implicitly

### ADDED Requirement: Scoped Byte Access Does Not Escape

Module-facing byte access SHALL expose spans only to synchronous callbacks
whose execution is bounded by valid storage access.

#### Scenario: JavaScript-backed bytes are accessed asynchronously

- **GIVEN** code calls `WithBytesAsync` on JavaScript-backed storage
- **WHEN** the originating runtime executes the scheduled work
- **THEN** the callback SHALL receive a span only for that synchronous runtime
  callback
- **AND** the returned Task SHALL complete with the callback result, exception,
  or cancellation

#### Scenario: Native-backed async access is requested

- **GIVEN** code calls an async byte-access method on native-backed storage
- **WHEN** the method validates the wrapper before observing cancellation
- **THEN** an already-disposed wrapper SHALL throw `ObjectDisposedException`
  synchronously even when the token is pre-canceled
- **AND** for a live wrapper, a pre-canceled token SHALL return a canceled Task
  without invoking the callback
- **AND** otherwise it SHALL invoke the synchronous callback inline
- **AND** return an already-completed successful or faulted Task
- **AND** callback exceptions SHALL NOT escape synchronously

#### Scenario: JavaScript-backed storage changed

- **GIVEN** JavaScript detached or resized a retained JavaScript-backed buffer
- **WHEN** managed code next accesses or encodes it
- **THEN** the operation SHALL fail before exposing a span or value
- **AND** generated async dispatch SHALL reject its Promise

### ADDED Requirement: Generated Binary Codecs Make Copies Explicit

Generated bindings SHALL support the accepted binary types with their defined
ownership and copy semantics.

#### Scenario: byte array argument is decoded

- **GIVEN** a generated method declares a `byte[]` parameter
- **WHEN** JavaScript supplies an ArrayBuffer
- **THEN** generated dispatch SHALL copy its current bytes into an independent
  managed array before invoking authored code

#### Scenario: byte array is returned

- **GIVEN** an authored method returns `byte[]`
- **WHEN** generated dispatch encodes the result
- **THEN** it SHALL copy the bytes exactly once into native-backed storage
- **AND** it SHALL NOT pin or adopt the managed array

#### Scenario: synchronous span argument is decoded

- **GIVEN** a generated synchronous method declares `Span<byte>` or
  `ReadOnlySpan<byte>`
- **WHEN** JavaScript supplies an ArrayBuffer
- **THEN** generated dispatch SHALL invoke the authored method inside the
  corresponding scoped byte callback
- **AND** the span SHALL not escape that callback

#### Scenario: multiple span parameters are declared

- **GIVEN** a generated method declares more than one parameter whose type is
  `Span<byte>` or `ReadOnlySpan<byte>`
- **WHEN** the generator analyzes the method
- **THEN** it SHALL emit `EXPOJSI013` naming the method
- **AND** it SHALL explain that this slice supports at most one span parameter
- **AND** it SHALL not restrict the number of `ArrayBuffer`, `byte[]`, or
  ordinary parameters
- **AND** it SHALL not emit nested callbacks that capture a ref struct

#### Scenario: async span parameter is declared

- **GIVEN** a generated async method declares `Span<byte>` or
  `ReadOnlySpan<byte>`
- **WHEN** the generator analyzes the method
- **THEN** it SHALL emit `EXPOJSI012`
- **AND** it SHALL not emit invalid async binding code

#### Scenario: synchronous span is returned

- **GIVEN** an authored synchronous method returns `Span<byte>` or
  `ReadOnlySpan<byte>`
- **WHEN** generated dispatch encodes the result
- **THEN** it SHALL copy the bytes immediately into native-backed storage
- **AND** the returned JavaScript ArrayBuffer SHALL not retain the span's owner

### MODIFIED Requirement: Async Owned Results Are Released Exactly Once

Generated async wrapper returns SHALL transfer their owned state through a
copy-safe result lease that is claimed or abandoned exactly once.

#### Scenario: Runtime claims an ArrayBuffer result

- **GIVEN** an authored `Task<ArrayBuffer>` completes successfully
- **WHEN** the Promise scheduler reaches the originating runtime
- **THEN** it SHALL claim and encode the owned wrapper
- **AND** release the wrapper after encoding or codec failure

#### Scenario: Runtime never claims an owned result

- **GIVEN** an authored `Task<ArrayBuffer>` or `Task<JavaScriptValue>` produced
  an owned result
- **AND** settlement scheduling fails, is canceled before claim, or is
  abandoned during runtime teardown
- **WHEN** the scheduler completes that failure path
- **THEN** it SHALL release the owned result exactly once

#### Scenario: Queued settlement work is dropped

- **GIVEN** an owned async result was produced
- **AND** the executor drops its queued settlement callback
- **WHEN** native releases the scheduled task context without invoking it
- **THEN** the scheduling Task SHALL fault
- **AND** scheduler cleanup SHALL abandon and release the owned result exactly
  once

### MODIFIED Requirement: Hermes Testhost Exposes Deterministic Lifetime Controls

The Hermes testhost SHALL expose test-only preparation, executor pause/resume,
queue-observation, task-drop, bridge-runtime-handle release, byte-length
snapshot validation, and ArrayBuffer release/abandon counters needed by this
change. ArrayBuffer-named counters measure the first consumer of the generic
long-lived-state collection; they SHALL NOT imply that the collection itself is
ArrayBuffer-specific.

## Deferred Follow-up

Multiple simultaneous span parameters remain deferred. The restriction may be
lifted only with a grouped access primitive that presents all spans to one
synchronous delegate; nested callbacks and implicit copies are not acceptable
substitutes.

After this change establishes the runtime-owned collection, a focused follow-up
SHALL migrate retained `JavaScriptPromise` capability state onto it. Promise
settlement scheduling remains separate from capability lifetime. The follow-up
must cover unresolved-promise teardown, settlement/teardown races, and
idempotent late disposal, and must be recorded in `docs/roadmap.md` and the
implementation-plan index.
