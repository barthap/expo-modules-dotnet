# Architecture Review: Integration Pain Points

Reviewed: 2026-06-30.

This document captures architectural findings from reviewing the portable C# /
JSI bridge against the requirements of real React Native integration. Each
finding describes the concern, why it matters, and possible solutions to explore.

Findings are ordered by risk. Implementation-progress items (missing codecs,
missing ABI surface for known future features) are excluded — those belong on
the roadmap, not here.

## Finding 1: Handle Allocation Cost On Hot Paths

### Concern

Every ABI value operation allocates a `ValueHandle` on the heap via
`ValueHandle::owned()`, which calls
`std::make_unique<facebook::jsi::Value>(...)`. That is two heap allocations per
value: one for the `ValueHandle` itself and one for the inner `unique_ptr`
payload.

For a generated sync function with three parameters and a return value, the
minimum allocation count per call is roughly:

```
3 argument borrows + 3 argument value reads + 1 return value encode ≈ 7 allocations
```

Upstream Expo Swift wrappers hold `jsi::Value` inline in stack or non-copyable
structs, avoiding per-call heap allocations for synchronous decode paths.

This will not cause correctness problems, but it will appear in profiling for
hot-path modules (sensor data, animation drivers, high-frequency bridge calls).

### Why It Matters

The allocation pattern is baked into the `ValueHandle` design. The longer the
ABI grows around this shape, the harder it becomes to change without a
coordinated native + managed rewrite.

### Possible Solutions

#### A. Arena / Pool Allocator For ValueHandle

Introduce a per-runtime or per-task arena that bulk-allocates `ValueHandle`
objects. The `runTask` boundary in the executor is a natural scope: allocate
from the arena during the task, release the entire arena after the microtask
checkpoint.

Pros:
- Does not change the ABI shape or opaque handle contract.
- Individual `new` / `delete` calls are replaced by bump-pointer allocation.
- Cleanup is amortized to one bulk release per task boundary.

Cons:
- Requires careful scoping so handles allocated inside a task are not used after
  the task ends. The existing scoped-ref model already enforces this for
  borrowed handles, so the pattern is familiar.
- Owned handles that escape the task boundary (e.g., host-function return
  values) need special treatment — either a separate allocation path or explicit
  "promote to heap" before escape.

#### B. Inline Value Encoding For Primitives (Tagged Pointers)

Primitives (bool, number, undefined, null) do not need a heap-allocated
`jsi::Value`. Their data fits in the handle pointer itself using a tagged
pointer or a small discriminated union.

The handle would carry the value directly for primitives and only heap-allocate
for reference types (object, string, function). The managed side already checks
`ValueKind` before reading, so the ABI contract would not change — only the
internal representation.

Pros:
- Eliminates allocations entirely for the most common sync-function argument
  types (`double`, `bool`).
- Transparent to C# — the handle is still an opaque `nint`.

Cons:
- Requires platform-specific tagged-pointer encoding and careful NaN-boxing or
  union layout.
- `release_value` must distinguish inline-encoded handles from heap-allocated
  ones (no-op vs `delete`).
- More complex to implement and debug than the arena approach.

#### C. Batch Argument Decode

Add a single ABI function that decodes all arguments at once:

```c
typedef expo_jsi_error (*expo_jsi_decode_arguments_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_arguments_handle arguments,
  uint32_t count,
  expo_jsi_value_kind *out_kinds,
  double *out_numbers,
  uint8_t *out_bools,
  expo_jsi_value_handle *out_handles);
```

The generated sync dispatch code could call this once instead of N individual
`get_argument_value` calls. Primitive values would be written directly into
caller-provided buffers; reference types would get handles in `out_handles`.

Pros:
- One ABI crossing instead of N for argument decode.
- Primitives avoid handle allocation entirely.
- Reference-type handles are still opaque.

Cons:
- Adds a new ABI function with a wider signature.
- Generated code must handle the split between inline primitives and handles.
- Only helps the argument-decode path, not general value creation.

#### D. Combination

Arena allocation (A) for general handle allocation plus inline primitives (B)
for the most common types. The arena handles the reference-type allocations; the
tagged-pointer path avoids them for primitives. This gives the best performance
but the highest implementation cost.

### Recommendation

Start with (A) — arena allocation scoped to the runtime task boundary. It has
the best cost/disruption ratio: no ABI shape change, no tagged-pointer
complexity, and it addresses the allocation pressure for all handle types. If
profiling later shows that primitive argument decode is still a bottleneck,
layer (B) or (C) on top.

---

## Finding 2: Module Instance Lifecycle And Runtime Reload

Status: solved by the runtime lifecycle milestone. `DotnetRuntimeContext` is now
the runtime-scoped managed owner for module instances and generated host-function
registrations, and Android, iOS, macOS, and Windows adapters call the managed
teardown path from their runtime/module invalidation hooks. See
`docs/specs/modules-core-boundary.md`, `docs/specs/runtime-and-abi.md`, and
`docs/specs/runtime-scheduling.md` for the accepted contract.

### Concern

This was the original concern before the runtime lifecycle work:

When React Native reloads (Fast Refresh, full dev reload), the JS runtime is
destroyed and a new one is created. On the native side,
`JsiRuntimeConnector::invalidate()` is called, which shuts down the executor
and releases queued work.

At the time, there was no mechanism to notify the managed side that:
- Its module instances should be torn down.
- GCHandle pins for host-function callback contexts should be released.
- Module state accumulated during the previous runtime session is stale.

After a reload, the old module instances and their pinned handles could remain
alive on the managed heap. The new runtime would get fresh module registrations
(assuming the adapter called `Register` again), but the old managed objects were
not cleaned up deterministically.

### Why It Matters

This affects the `JsiRuntimeConnector` interface — the adapter seam that every
real RN host must implement. If the teardown protocol is not designed into the
interface before adapter work starts, every adapter will need to invent its own
cleanup mechanism, or the managed side will silently leak on every reload.

In development builds, reloads happen frequently. Leaked module instances
accumulate memory and may hold native resources (file handles, network
connections, platform service references).

### Possible Solutions

#### A. Managed Teardown Callback On The Connector

Extend the adapter install protocol to accept a managed teardown callback. The
adapter calls this callback before destroying the runtime:

```c
typedef void (*expo_managed_teardown_fn)(void *managed_context);
```

The portable install function registers this callback with the connector. When
`invalidate()` is called, the connector invokes the teardown callback before
shutting down the executor. The managed teardown releases module instances, frees
GCHandle pins, and clears the module registry.

This keeps teardown as an explicit protocol step rather than relying on GC
finalization timing.

#### B. Runtime-Scoped Module Registry

Make `ModuleRegistry` runtime-scoped rather than static. Each runtime install
creates a new registry instance. The registry holds all module instances and
their associated host-function contexts. When the runtime is torn down, the
registry disposes all its contents.

The registry would be the "managed context" passed to the teardown callback in
option A.

#### C. Weak Runtime References In Module Instances

Give each module instance a weak reference to its runtime. When the runtime is
invalidated, module methods that try to use it fail loudly instead of touching
stale native handles. This is a safety net, not a cleanup mechanism — it
prevents use-after-free but does not release resources.

### Recommendation

Combine (A) and (B). Design the teardown callback into the connector interface
now, and make the module registry runtime-scoped so teardown has a clear scope
to clean up. Add (C) as a defense-in-depth safety check.

Implemented as `DotnetRuntimeContext`, app-composed `expo_dotnet_*` lifecycle
entry points, adapter-owned runtime records, and invalidation-before-teardown
ordering across the React Native host adapters.

---

## Finding 3: Dev Tooling — C# Stack Traces Across The ABI

Status: solved. Managed exceptions now forward `ex.ToString()` (full stack trace)
as the error message across the ABI. The native host-function trampoline wraps
this in a `JSError`, so C# stack traces appear in LogBox and DevTools. See commit
43dc2f94 ("Forward managed stack traces across ABI").

### Concern

When a managed host-function callback throws, the exception is caught in
`InvokeHostFunction` and forwarded to the native side as a structured error. The
native host-function trampoline converts this into a `JSError` with the error
message.

However, the managed exception's stack trace is lost in this translation. The
`JSError` that reaches JavaScript contains only the message string, not the C#
call stack. In React Native, this means:
- LogBox shows "Managed host function failed" with no C# context.
- DevTools console shows a JS error with no indication of where in C# the
  failure occurred.
- Debugging requires printf/console logging on the managed side.

### Why It Matters

This is low-hanging fruit with outsized DX impact. C# exceptions already
contain full stack traces. The existing error propagation path is the right
place to carry this information — the infrastructure exists, only the content
needs to change.

### Possible Solutions

#### A. Include Full Exception ToString In Error Message

In `InvokeHostFunction`, replace:

```csharp
return new ExpoJsiValueResult(0, 0, context?.CaptureException(ex) ?? default);
```

with a path that captures `ex.ToString()` (which includes the full stack trace)
as the error message. The native side already forwards the error message into
`JSError`, so the C# stack trace would appear in LogBox and DevTools
automatically.

Cost: minimal code change. Risk: error messages become longer, which is fine
for dev builds but may need truncation for production.

#### B. Structured Error With Separate Stack Field

Extend `expo_jsi_error` to carry an optional stack trace pointer alongside the
message. The native `JSError` constructor could set the `stack` property of the
JavaScript Error object to the C# stack trace.

This would give DevTools a proper stack trace field instead of a long message
string, and it would be possible to format them separately.

Cost: ABI change (new fields in `expo_jsi_error`). Benefit: cleaner separation
of message and stack.

#### C. Development-Only Verbose Errors

Use a compile-time or runtime flag to control whether full stack traces cross
the ABI. In debug builds, include `ex.ToString()`. In release builds, include
only `ex.Message`.

Cost: conditional logic in the error path. Benefit: avoids leaking internal
stack traces in production.

### Recommendation

Start with (A) — include `ex.ToString()` as the error message. It is the
simplest change and immediately improves the debugging experience. Consider (C)
as a follow-up to avoid verbose errors in production. (B) is a future
refinement if structured stack traces become important for tooling integration.

---

## Finding 4: `thread_local` Error Message Lifetime

### Concern

In `ExpoJsiBridge.cpp`, error messages are stored in a `thread_local
std::string`:

```cpp
thread_local std::string lastErrorMessage;
```

The `expo_jsi_error` struct's `message` pointer points into this thread-local
string. If managed code makes another ABI call on the same thread before reading
the error message from a previous call, the thread-local is overwritten and the
previous message pointer dangles or points to unrelated content.

The current code reads errors immediately after each ABI call, which is correct.
But this is a latent hazard: a subtle reordering in generated code, or a future
ABI function that internally calls another ABI function, could silently corrupt
error context.

### Why It Matters

This is a correctness hazard in the ABI design, not a current bug. It becomes
more dangerous as the ABI grows and generated code becomes more complex.

### Possible Solutions

#### A. Copy Error Message Into The Result Struct

Make `expo_jsi_error` own its message by copying it into a caller-provided
buffer or by allocating a dedicated error string with a paired release callback
(similar to `expo_jsi_string_result`).

```c
typedef struct expo_jsi_error {
  int32_t code;
  const char *message;
  int32_t message_len;
  void *release_context;
  expo_jsi_release_string_fn release;
} expo_jsi_error;
```

Pros:
- Eliminates the hazard class entirely.
- Error messages are self-contained and safe to read at any time.

Cons:
- ABI-breaking change (struct layout changes).
- Every error path now allocates a string. For errors that are immediately
  thrown as managed exceptions, this is fine. For out-parameter errors on
  success paths (where `code == 0`), no allocation is needed.

#### B. Document And Enforce Immediate Read

Keep the current design but add a clear contract: the error message pointer is
valid only until the next ABI call on the same thread. Add assertions or tests
that verify generated code reads errors immediately.

Pros:
- No ABI change.

Cons:
- The hazard remains. Documentation does not prevent bugs in generated code.

#### C. Per-Call Error Buffer

Pass a caller-owned error buffer into each ABI function. The native side copies
the error message into the caller's buffer (with truncation if too long). The
managed side allocates a stack buffer for each ABI call.

Pros:
- No thread-local state.
- No heap allocation for errors.

Cons:
- Every ABI function signature gets an additional buffer parameter.
- Fixed-size buffers risk truncation.

### Recommendation

(A) is the cleanest long-term fix. Since `expo_jsi_error` is an internal ABI
type (not exposed to module authors), the breaking change is contained. The
allocation cost on error paths is negligible — errors are exceptional by
definition.

If the ABI change is too disruptive right now, (B) is acceptable as a
short-term measure with a documented invariant, but it should be promoted to (A)
before the ABI stabilizes.

---

## Side Note: `executeSync` Cross-Thread Blocking

The `HermesConsoleRuntimeExecutor::executeSync` implementation blocks the
calling thread with a condition variable when called from a non-runtime thread.
In a real React Native host, cross-thread blocking can deadlock against the RN
bridge queue or UI thread.

This is not a real architectural risk because `HermesConsoleRuntimeExecutor` is
a test-only implementation. Real RN adapters will implement the
`JsiRuntimeConnector` interface with host-provided scheduling and will return
`false` from `canExecuteSync()` when cross-thread sync is unsafe. The
`canExecuteSync` gate already protects the managed side.

Worth noting if someone tries to reuse the console executor's cross-thread
blocking pattern in a real adapter — that would be a mistake.

---

## Summary

| # | Finding | Status | Risk | Effort | Recommendation |
|---|---------|--------|------|--------|----------------|
| 1 | Handle allocation cost | Open | Medium | Medium | Arena allocator scoped to task boundary |
| 2 | Module lifecycle / reload teardown | Solved | High | Medium | `DotnetRuntimeContext` + managed teardown callback |
| 3 | C# stack traces lost across ABI | Solved | Low | Low | `ex.ToString()` forwarded across ABI (43dc2f94) |
| 4 | `thread_local` error message lifetime | Open | Medium | Low | Copy error into result struct |
