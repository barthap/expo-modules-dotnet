# Plan 021: Exactly-once owned callback-state disposal for host functions

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving to the
> next step. If anything in the "STOP conditions" section occurs, stop and
> report — do not improvise. When done, update the status row for this plan
> in `docs/plans/README.md` — unless a reviewer dispatched you and told you
> they maintain the index.
>
> **Drift check (run first)**:
> `git diff --stat f2c72f68..HEAD -- packages/expo-modules-dotnet/managed/packages/Expo.JSI packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests docs/specs/`
> If any in-scope file changed since this plan was written, compare the
> "Current state" excerpts against the live code before proceeding; on a
> mismatch, treat it as a STOP condition.

## Status

- **Priority**: P2
- **Effort**: S–M
- **Risk**: MED (lifetime primitive; disposal can fire on a non-runtime
  thread — see plan 009 history)
- **Depends on**: none (unblocks docs/plans/019-sharedobject-events.md)
- **Category**: tech-debt / direction
- **Planned at**: commit `f2c72f68`, 2026-07-22

## Why this matters

Plan 019 (shared-object events) was rolled back because its correct design
needs a primitive `Expo.JSI` does not have. The design keeps listener storage
in the JS heap so JS GC can collect a shared object together with listeners
that capture it; the managed side hands JS a `remove()` host function whose
callback state owns a `JavaScriptWeakObject`. When JS collects that function,
the weak handle must be disposed exactly once — and today nothing disposes
it. Native code already reports host-function destruction to managed code,
but the managed release path frees only the `GCHandle` and an error buffer.
It cannot blindly dispose every callback state either, because several host
functions routinely share one state object (the event-emitter prototype
installs six functions over one state). This plan adds an explicit, opt-in,
exactly-once ownership contract for callback state, with lifecycle tests.
After it lands, 019 resumes.

## Current state

All facts verified at `f2c72f68`.

- `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptRuntime.cs`
  — `CreateHostFunction` (`:414-445`):

  ```csharp
  public JavaScriptFunction CreateHostFunction(
      string name,
      uint parameterCount,
      JavaScriptHostFunction callback,
      object callbackState
  )
  {
    ...
    var callbackContext = new HostFunctionContext(context, callback, callbackState).ToIntPtr();
    var result = context.Api->CreateHostFunctionValue(
        context.RuntimeHandle, nameBytes, parameterCount,
        &InvokeHostFunction, callbackContext, &ReleaseHostFunctionContext);
    if (!result.IsOk)
    {
      HostFunctionContext.Release(callbackContext);   // :439 — creation-failure path
      JsiContext.ThrowNativeError(result.Error, "Failed to create JavaScript host function.");
    }
    return new JavaScriptFunction(context, result.Value);
  }
  ```

  and the native-driven release (`:719-723`):

  ```csharp
  [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
  private static void ReleaseHostFunctionContext(nint callbackContext)
  {
    HostFunctionContext.Release(callbackContext);
  }
  ```

- `packages/expo-modules-dotnet/managed/packages/Expo.JSI/Interop/HostFunctionContext.cs`
  — holds `Context` (the caller's state object, `:26`); `Release(nint)`
  (`:61-73`) releases the captured-error buffer and frees the `GCHandle`,
  and does nothing about resources owned by `Context`:

  ```csharp
  public static void Release(nint pointer)
  {
    if (pointer == 0) { return; }
    var handle = GCHandle.FromIntPtr(pointer);
    if (handle.Target is HostFunctionContext context)
    {
      context.ReleaseLastErrorMessage();
    }
    handle.Free();
  }
  ```

- Native side already reports destruction — no ABI change is needed. The
  release function pointer travels through
  `expo_jsi_release_callback_context_fn`
  (`packages/expo-modules-dotnet/native/include/expo_jsi.h:184`, parameter at
  `:331`) and fires when the host function is destroyed (JS GC of the
  function object, or runtime teardown destroying the Hermes runtime).

- Shared-state precedent that forbids blanket disposal:
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/EventEmitterPrototype.cs:13-30`
  passes the same `state` object to six `CreateHostFunction` calls
  (`addListener`, `removeListener`, `removeAllListeners`, `emit`,
  `listenerCount`, `removeSubscription`). That state is owned by
  `DotnetRuntimeContext`, not by any one function. Disposal must therefore
  be opt-in per `CreateHostFunction` call, and the default (no disposer)
  must preserve today's behavior exactly.

- Threading history you must respect: plan 009 (see
  `docs/plans/009-windows-testhost-teardown-crash.md`) traced a Windows
  `0xC0000005` teardown crash to disposing a promise capability off the
  runtime thread. `ReleaseHostFunctionContext` is called by native code and
  may run on whatever thread destroys the function (GC / runtime-executor /
  teardown thread). The delta spec must state the disposer's thread
  contract explicitly, and the primary intended payload
  (`JavaScriptWeakObject` disposal, for 019) must be proven safe under that
  contract — or routed to a safe path.

- Hermes-backed test machinery: force GC with
  `HermesRuntimeFixture.CollectGarbageForTesting()`
  (`packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Fixtures/HermesRuntimeFixture.cs:91`);
  usage exemplar
  `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptWeakObjectTests.cs:97`.
  Host-function test exemplars:
  `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/HostFunctions/HostFunctionTests.cs`.

Repo conventions that apply:

- **Living-spec workflow is mandatory** (AGENTS.md): this changes `Expo.JSI`
  wrapper semantics. Delta spec at
  `docs/changes/2026-<mm-dd>-hostfunction-owned-state/spec.md` → operator
  approval → `plan.md` → implementation with verified commits → merge into
  `docs/specs/host-functions-and-errors.md` (verified: it owns "Managed Host
  Functions" and "Callback Context Lifetime") → archive. Read
  `.agents/skills/living-spec-workflow/SKILL.md` first.
- AGENTS.md "Maturity": complete polished feature only; no shortcuts — STOP
  and raise instead.
- NativeAOT-compatible: no reflection; `[UnmanagedCallersOnly]` callbacks
  must never let a managed exception escape (match `InvokeHostFunction`'s
  catch-and-report pattern at `JavaScriptRuntime.cs:713-717`).
- Commit style: conventional-commit-ish, e.g.
  `feat(jsi): owned host-function callback-state disposal`.
- Never commit absolute local paths, usernames, or machine names.

## Commands you will need

| Purpose | Command (repo root) | Expected on success |
|---|---|---|
| Managed test suite | `scripts/test-managed.sh` | exit 0 |
| Formatting | `scripts/format.sh --check --all` (run `scripts/format.sh` then re-check if it fails) | exit 0 |

## Suggested executor toolkit

- `.agents/skills/living-spec-workflow/SKILL.md` — mandatory workflow.
- Skill `expo-jsi-managed-handle-lifetime` (if available) — host-function
  callback lifetimes and owned-wrapper pitfalls are exactly its territory.

## Scope

**In scope** (the only files you should modify or create):

- `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptRuntime.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.JSI/Interop/HostFunctionContext.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/HostFunctions/`
  (new lifecycle tests)
- The living spec file that owns host-function lifetime under `docs/specs/`
- `docs/changes/2026-<mm-dd>-hostfunction-owned-state/` (create)
- `docs/plans/README.md` (status row only)

**Out of scope** (do NOT touch, even though they look related):

- Native/C++ code and the C ABI — the release callback already exists; no
  new ABI entries. If the design seems to need one, STOP.
- `HostObjectContext` and `CreateHostObject` — same gap exists there, but it
  has no consumer yet. Record it as deferred follow-up in the delta spec;
  do not implement.
- `EventEmitterPrototype`, `ModuleEventEmitter`, and all `Expo.ModulesCore`
  call sites — they keep passing shared, externally-owned state with no
  disposer. Their observable behavior must not change.
- Plan 019's shared-object event feature itself.

## Git workflow

- Branch: `advisor/021-hostfunction-owned-state` off `development`.
- Commit per step. Do NOT push or open a PR unless the operator instructed it.

## Steps

### Step 1: Delta spec

Write `docs/changes/2026-<mm-dd>-hostfunction-owned-state/spec.md` in the
GIVEN/WHEN/THEN SHALL style, deciding at least:

1. API shape: an optional exactly-once disposer accepted by
   `CreateHostFunction` — recommended shape is an optional parameter or
   overload, e.g.
   `CreateHostFunction(name, parameterCount, callback, callbackState, Action<object>? disposeCallbackState = null)`.
   Omitted disposer ⇒ behavior identical to today.
2. Exactly-once guarantee across ALL release paths: creation failure
   (`JavaScriptRuntime.cs:439`), JS GC of the function object, and runtime
   teardown. Double-release must be a safe no-op.
3. Thread contract for the disposer: on which thread(s) it may run, what it
   may not do (call into the JS runtime), and how a `JavaScriptWeakObject`
   payload is disposed safely under that contract given the plan 009
   off-runtime-thread crash history. If weak-handle disposal is not safe on
   the native release thread, the spec must define the safe route (e.g.
   idempotent late disposal like plan 015's promise-capability contract)
   rather than weakening exactly-once.
4. Disposer exceptions: caught and reported (match `InvokeHostFunction`'s
   pattern), never propagated through the `[UnmanagedCallersOnly]` boundary.
5. Shared vs owned state: passing the same state object to several host
   functions with per-call disposers is caller error or defined behavior —
   pick one and spec it.

Present to the operator for approval before implementing.
**Verify**: spec committed; operator approved.

### Step 2: Implementation plan artifact

Write `docs/changes/<same-folder>/plan.md` mapping steps 3–4 to commits.
**Verify**: committed.

### Step 3: Implement the disposer

Extend `HostFunctionContext` to carry the optional disposer and invoke it
exactly once inside `Release` (interlocked/idempotent guard), before the
`GCHandle` is freed. Extend `CreateHostFunction` per the approved spec. The
creation-failure path at `JavaScriptRuntime.cs:439` must flow through the
same exactly-once logic with no special casing.

**Verify**: `scripts/test-managed.sh` → exit 0 (existing suite unbroken).

### Step 4: Lifecycle tests

New tests in `Expo.JSI.Tests/HostFunctions/` (model after
`HostFunctionTests.cs`; use `HermesRuntimeFixture.CollectGarbageForTesting()`
as in `JavaScriptWeakObjectTests.cs:97`), covering at minimum:

1. Disposer runs exactly once when JS drops the function and GC is forced.
2. Disposer runs exactly once at runtime teardown for a still-referenced
   function.
3. Disposer runs on host-function creation failure.
4. No disposer passed ⇒ nothing new happens (existing shared-state usage
   pattern unaffected).
5. A throwing disposer is contained: no crash, other cleanup still runs.
6. A `JavaScriptWeakObject` held as owned state is disposed safely under
   forced GC and under teardown (this is 019's exact consumer scenario).

**Verify**: `scripts/test-managed.sh` → exit 0, new tests included.

### Step 5: Docs merge and archive

Merge the delta into the owning living spec under `docs/specs/`; archive the
change folder; formatting.

**Verify**: `scripts/format.sh --check --all` → exit 0.

## Test plan

Covered by Step 4 — the exactly-once matrix (GC, teardown, creation failure,
double-release, throwing disposer, weak-object payload) is the deliverable.
All existing host-function and weak-object tests pass unmodified.

## Done criteria

Machine-checkable. ALL must hold:

- [ ] `scripts/test-managed.sh` exits 0 (new lifecycle tests included)
- [ ] `scripts/format.sh --check --all` exits 0
- [ ] Omitting the disposer compiles and behaves identically to today at
      every existing `CreateHostFunction` call site (no call-site edits
      outside tests: `git diff --stat` shows no `Expo.ModulesCore` changes)
- [ ] The living spec under `docs/specs/` contains the merged disposer
      contract, including the thread contract
- [ ] No files outside the in-scope list modified (`git status`)
- [ ] `docs/plans/README.md` status row updated

## STOP conditions

Stop and report back (do not improvise) if:

- The code at the locations in "Current state" doesn't match the excerpts.
- The design turns out to require native/C++ or ABI changes.
- `JavaScriptWeakObject` disposal proves unsafe on the native release thread
  AND no safe idempotent route exists without weakening exactly-once — this
  needs an operator decision, not an improvised compromise.
- Runtime teardown does NOT invoke `ReleaseHostFunctionContext` for live
  host functions (the "native already reports destruction" assumption is
  false for the teardown path).
- The operator rejects or wants substantive changes to the delta spec.
- A step's verification fails twice after a reasonable fix attempt.

## Maintenance notes

- Plan 019 consumes this primitive for its JS-owned `remove()` subscription
  cleanup — its executor relies on the exact spec'd thread contract.
- `HostObjectContext` has the same gap; the delta spec records it as
  deferred. Whoever gives host objects owned state later should mirror this
  contract.
- Reviewer should scrutinize: the interlocked exactly-once guard, the
  creation-failure path reusing it, and that no `[UnmanagedCallersOnly]`
  frame can leak a managed exception.
