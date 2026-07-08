# Plan 006: ArrayBuffer support — design spike, delta spec, and prototype

> **Executor instructions**: This is a DESIGN SPIKE plan, not a build-everything
> plan. The deliverables are a delta spec, a working prototype proving the
> ownership model, and a recorded stop/go decision — not a polished feature.
> Follow steps in order; run every verification command. If anything in the
> "STOP conditions" section occurs, stop and report — do not improvise. When
> done, update the status row in `docs/plans/README.md`.
>
> **Drift check (run first)**: `git diff --stat 0f6fc760..HEAD -- packages/expo-modules-dotnet/native/ packages/expo-modules-dotnet/managed/packages/Expo.JSI/ packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/`
> If in-scope files changed since this plan was written, compare "Current
> state" excerpts against live code; on mismatch, STOP.

## Status

- **Priority**: P2
- **Effort**: M (spike scope; full feature would be L)
- **Risk**: MED — new ABI surface; ownership/lifetime model is the hard part
- **Depends on**: none hard; docs/plans/003 recommended first (test discipline)
- **Category**: direction (design/spike)
- **Planned at**: commit `0f6fc760`, 2026-07-08

## Why this matters

`docs/roadmap.md` lists ArrayBuffer as P2: "binary transfer wrappers and ABI
support for file, camera, crypto, WebSocket, and data-heavy modules". The
value-kind slot already exists end to end (`EXPO_JSI_VALUE_ARRAY_BUFFER = 7`
in the ABI header, `JavaScriptValueKind.ArrayBuffer = 7` in managed code) but
there are ZERO ABI entries or wrappers for creating or reading one. Nearly
every data-heavy module class is blocked on this. The spike's job is to settle
the ownership/copy model — the part that gets expensive to change later — with
a small proven prototype, before committing to the full codec surface.

## Current state

- `packages/expo-modules-dotnet/native/include/expo_jsi.h` — C ABI header,
  ~231 `expo_jsi_` symbols, opaque-handle style (`expo_jsi_value_handle` etc.).
  Only ArrayBuffer trace is the enum value at line ~39. NativeState entries
  (lines ~110–130, ~245–250) are the best precedent for callback-based
  lifetime: `expo_jsi_release_native_state_fn` runs on JS object destruction,
  with a documented threading caveat in the header comment.
- `packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp` —
  implements the ABI. Error convention: numbered `makeError`/`makeErrorResult`
  codes, `try/catch` fencing so no C++ exception crosses the ABI. Match it.
- `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptValueKind.cs:47`
  — `ArrayBuffer = 7`; `Interop/ExpoJsiTypes.cs:15` mirrors it.
- Managed wrapper pattern: owned wrappers + scoped refs per
  `docs/specs/ownership-and-scoped-refs.md`. Interop layer:
  `Expo.JSI/Interop/ExpoJsiApi.cs` (unmanaged function pointers struct).
- Codec pattern (`Expo.ModulesCore/Codecs/`): `readonly struct` implementing
  `IJavaScriptCodec<T>` with three static members — exemplar
  `Codecs/StringCodec.cs`:

  ```csharp
  public readonly struct StringCodec : IJavaScriptCodec<string>
  {
    public static string Decode(JavaScriptValueRef value, JavaScriptRuntime runtime) => value.AsString();
    public static string Decode(JavaScriptValue value, JavaScriptRuntime runtime) => value.AsString();
    public static JavaScriptValue Encode(string value, JavaScriptRuntime runtime) => runtime.CreateString(value);
  }
  ```

- Hard repo constraints (from `AGENTS.md`, non-negotiable in the design):
  - C++ owns JSI mechanics; C# never sees raw `jsi::*` layouts. An
    ArrayBuffer crosses the ABI as an opaque handle + explicit byte-copy or
    pinned-pointer entries — never as a struct mirroring `jsi::ArrayBuffer`.
  - NativeAOT compatible: no reflection, function-pointer interop only
    (match `ExpoJsiApi.cs` style).
- JSI facts the design must handle (verify against the jsi.h the testhost
  compiles against, under the Hermes destroot `build/hermes/source/destroot/include`):
  - `jsi::ArrayBuffer` exposes `size(rt)` and `data(rt)` (raw byte pointer
    valid only while the runtime is alive and on the JS thread).
  - Creating an ArrayBuffer from native memory uses
    `jsi::ArrayBuffer(rt, std::shared_ptr<jsi::MutableBuffer>)` — the buffer
    object owns the bytes; Hermes frees it when the JS object is collected.
  - Hermes may or may not support detaching; do not design around detach.
- Threading/scheduling: all runtime access is scheduled per
  `docs/specs/runtime-scheduling.md`. `data(rt)` pointers must never outlive
  a scheduled callback — this is the central lifetime question of the spike.
- Spike record requirements (from `AGENTS.md`): hypothesis, commands run,
  expected result, actual result, artifacts, ownership/lifetime findings,
  scheduler findings, stop/go decision.

## Commands you will need

| Purpose | Command | Expected on success |
|---|---|---|
| Managed tests | `scripts/test-managed.sh` | all pass (compiles native testhost too) |
| Filtered | `scripts/test-managed.sh --filter FullyQualifiedName~ArrayBuffer` | new tests pass |
| Format | `scripts/format.sh --check --all` | exit 0 |

## Scope

**In scope**:
- `docs/changes/<yyyy-mm-dd>-arraybuffer/spec.md` (create — the delta spec is a
  primary deliverable)
- `packages/expo-modules-dotnet/native/include/expo_jsi.h` (new entries)
- `packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp`
- `packages/expo-modules-dotnet/managed/packages/Expo.JSI/` (new
  `JavaScriptArrayBuffer` wrapper + interop entries)
- `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptArrayBufferTests.cs` (create)
- `docs/specs/runtime-and-abi.md` and `docs/specs/managed-jsi-wrappers.md`
  (merge accepted delta at the end)
- `docs/plans/README.md` (status row)

**Out of scope** (deliberately deferred past the spike):
- The `Expo.ModulesCore` codec (`ByteArrayCodec` / `Memory<byte>` codec) and
  generator support for `byte[]` parameters — design them in the delta spec,
  build them in a follow-up once the ABI model is accepted.
- TypedArray views (Uint8Array etc.) — JS-side sugar, later.
- Zero-copy INTO C# beyond scoped access (see design questions).
- Any platform adapter (`ios/`, `android/`, `windows/`) changes.

## Git workflow

- Branch: `advisor/006-arraybuffer-spike`
- Commit style: `feat(jsi): add ArrayBuffer ABI entries and managed wrapper (spike)`;
  delta spec commit first (`docs: add arraybuffer delta spec`)
- Do NOT push or open a PR unless the operator instructed it.

## Steps

### Step 1: Delta spec draft

Write `docs/changes/<yyyy-mm-dd>-arraybuffer/spec.md` answering, with a chosen
position each (these are the spike's design questions):

1. **C#→JS creation**: copy semantics (`expo_jsi_create_array_buffer(runtime,
   const uint8_t* data, int32_t length)` — bridge copies into a
   `MutableBuffer`) vs pinned zero-copy (managed pins memory, release callback
   unpins — mirror the NativeState release-callback pattern). Recommended
   starting position: COPY for the spike; pinned variant recorded as a future
   extension with its release-callback design sketched.
2. **JS→C# read**: scoped copy-out only (`get_array_buffer_size` +
   `copy_array_buffer_bytes(runtime, handle, uint8_t* dest, int32_t
   dest_len)`), managed side exposes `int Length` and
   `void CopyTo(Span<byte>)` / `byte[] ToArray()`. No persistent pointer ever
   crosses to C#.
3. **Kind detection**: `isArrayBuffer` via the existing value-kind entry
   (verify how kind 7 is currently reported by the bridge's kind function —
   read the implementation; if objects never report kind 7 today, the entry
   must be added/fixed and that fact recorded).
4. **Lifetime rules**: handle is an owned wrapper per
   `ownership-and-scoped-refs.md`; byte access only inside scheduled runtime
   callbacks.

Keep paths repo-relative; no machine-specific content.

**Verify**: file exists; `git diff --check` clean. Get operator approval of
the spec before Step 2 if the operator is available; otherwise proceed with
the recommended positions and flag the spec as "positions taken, pending
review" at the top.

### Step 2: ABI entries + bridge implementation

Add to `expo_jsi.h` (matching existing typedef/function-table style) and
implement in `ExpoJsiBridge.cpp`: create-from-bytes (copy), size, copy-out.
Follow the error-code and try/catch conventions. Update the function table /
API struct the same way the NativeState entries were added (use
`git log --oneline --follow packages/expo-modules-dotnet/native/include/expo_jsi.h`
and read commit `3e016352 feat(jsi): add object native state` as the
worked example of adding ABI surface end to end).

**Verify**: `scripts/test-managed.sh` → existing tests still pass (native
testhost compiles with new entries).

### Step 3: Managed wrapper + interop

Mirror commit `3e016352`'s managed side: add entries to
`Expo.JSI/Interop/ExpoJsiApi.cs`, create `JavaScriptArrayBuffer` owned wrapper
(`Length`, `CopyTo(Span<byte>)`, `ToArray()`), creation API on
`JavaScriptRuntime` (e.g. `CreateArrayBuffer(ReadOnlySpan<byte>)`), kind
handling in the value wrapper (`AsArrayBuffer()` or equivalent following how
`AsString`/function casts are exposed).

**Verify**: `scripts/test-managed.sh` → pass.

### Step 4: Prototype proof tests

`JavaScriptArrayBufferTests.cs` (model on
`Expo.JSI.Tests/Runtime/JavaScriptObjectTests.cs` structure): round-trip
C#→JS (create from bytes, JS reads `byteLength` and byte values via evaluated
script); JS→C# (script creates `new ArrayBuffer(n)` + fills via `Uint8Array`,
C# reads length and copies out); empty buffer; large-ish buffer (e.g. 1 MB)
for a smoke check that copy cost is sane; disposal after runtime teardown
does not crash (follow existing teardown-test patterns in the suite).

**Verify**: `scripts/test-managed.sh --filter FullyQualifiedName~ArrayBuffer`
→ all new tests pass; full suite passes.

### Step 5: Spike record + merge spec

Append the spike record (hypothesis / commands / expected / actual /
artifacts / ownership-lifetime findings / scheduler findings / stop-go) to the
delta spec. If GO: merge accepted contract into
`docs/specs/runtime-and-abi.md` (ABI entries) and
`docs/specs/managed-jsi-wrappers.md` (wrapper semantics); note the deferred
codec/generator work as the follow-up. Run `scripts/format.sh` as needed.

**Verify**: `scripts/format.sh --check --all` → exit 0;
`grep -n "ArrayBuffer" docs/specs/runtime-and-abi.md` → shows new entries.

## Test plan

Step 4 is the test plan. Exemplars: `JavaScriptObjectTests.cs`,
`JavaScriptNativeStateTests.cs` (for release/teardown patterns).

## Done criteria

- [ ] Delta spec exists with all four design questions answered and the spike
      record appended.
- [ ] ABI has create/size/copy-out entries; `grep -c "array_buffer" packages/expo-modules-dotnet/native/include/expo_jsi.h` ≥ 3.
- [ ] `JavaScriptArrayBuffer` wrapper + tests exist; full
      `scripts/test-managed.sh` exits 0.
- [ ] `scripts/format.sh --check --all` exits 0.
- [ ] On GO: living specs updated. On NO-GO: delta spec records why; code
      reverted or clearly marked experimental.
- [ ] `docs/plans/README.md` status row updated.

## STOP conditions

Stop and report back (do not improvise) if:

- The Hermes jsi.h in the prebuilt destroot lacks the
  `MutableBuffer`-based ArrayBuffer constructor — the creation design must be
  rethought against what the engine actually offers.
- Kind detection for ArrayBuffer (enum 7) turns out to be dead code that
  can't be wired without reworking the value-kind ABI contract.
- The prototype requires exposing a raw byte pointer to C# beyond a single
  scheduled callback to be useful — that violates the ownership model and
  needs an operator decision, not improvisation.
- Copy-based round-trip of the 1 MB buffer is pathologically slow (>100 ms on
  the dev machine) — record numbers and stop; the pinned design moves from
  "future" to "required" and that's a spec-level decision.

## Maintenance notes

- The deferred follow-up (ModulesCore `byte[]`/`Memory<byte>` codec +
  generator support) should be planned only after this spike's GO — its shape
  depends entirely on the copy-vs-pinned decision recorded here.
- Reviewer: scrutinize the release/teardown test and the threading caveat
  parity with the NativeState header comment.
- Per the roadmap assessment in `docs/plans/README.md`: an end-to-end NativeAOT
  proof is recommended before shipping this beyond spike status.
