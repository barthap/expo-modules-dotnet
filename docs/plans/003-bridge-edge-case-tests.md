# Plan 003: Add edge-case tests for UTF-8 validation, promise settlement, and generator diagnostics

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving to the
> next step. If anything in the "STOP conditions" section occurs, stop and
> report — do not improvise. When done, update the status row for this plan
> in `docs/plans/README.md` — unless a reviewer dispatched you and told you they
> maintain the index.
>
> **Drift check (run first)**: `git diff --stat 0f6fc760..HEAD -- packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp packages/expo-modules-dotnet/managed/packages/`
> If any in-scope file changed since this plan was written, compare the
> "Current state" excerpts against the live code before proceeding; on a
> mismatch, treat it as a STOP condition.

## Status

- **Priority**: P2
- **Effort**: M
- **Risk**: LOW
- **Depends on**: none (Plan 001 recommended first so CI runs these)
- **Category**: tests
- **Planned at**: commit `0f6fc760`, 2026-07-08

## Why this matters

Three bridge-critical mechanisms have only happy-path coverage. The hand-rolled
87-line UTF-8 validator guards every string crossing C#→JS; an overlong-encoding
or surrogate-range bug there means invalid strings reaching the JS engine. The
native promise settle guard prevents double resolve/reject but is untested for
repeated settlement. The Roslyn generator reports diagnostics for malformed
module definitions, but diagnostic paths are sparsely tested, so a regression
would silently accept invalid modules. Per repo architecture, the C++ bridge is
verified through the managed test suite — these are managed (xUnit) and
generator tests, not a new C++ test framework.

## Current state

- `packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp:429-510`
  — `isValidUtf8(const uint8_t *data, int32_t length)`. Branch structure:
  ASCII (≤0x7F); 2-byte (0xC2–0xDF); 3-byte split into 0xE0 (second byte
  0xA0–0xBF), 0xE1–0xEC / 0xEE–0xEF, 0xED (second byte 0x80–0x9F, i.e. rejects
  UTF-16 surrogates); 4-byte split into 0xF0 (second byte 0x90–0xBF),
  0xF1–0xF3, 0xF4 (second byte 0x80–0x8F, i.e. caps at U+10FFFF). Bytes
  0xC0/0xC1 and ≥0xF5 fall through to `return false`. Called from
  `createString` (line 758) and the error-message path (line 806).
- Managed entry point for exercising it: string creation on the runtime —
  see existing tests in
  `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/`
  (e.g. `JavaScriptValueTests.cs`, `JavaScriptPrimitiveTests.cs`). Note: C#
  `string` → UTF-8 encoding always produces valid UTF-8, so invalid-byte cases
  must go through whatever raw-bytes API exists (check `Expo.JSI` for a
  byte-span string creation path or the interop layer in
  `Expo.JSI/Interop/ExpoJsiApi.cs`). If NO managed path can deliver raw invalid
  bytes to `createString`, cover the valid/boundary corpus from managed tests
  and record the invalid-bytes gap in the test file as a comment — do not add
  new ABI surface for testability (STOP condition if you think it's needed).
- `ExpoJsiBridge.cpp:91-142` — `PromiseHandle` with `resolve()`/`reject()`
  guarded by `settled_`:

  ```cpp
  void resolve(jsi::Runtime &runtime, const jsi::Value &value) {
    if (settled_ || !resolve_.has_value()) { return; }
    resolve_->call(runtime, value);
    settled_ = true;
    resolve_.reset();
    reject_.reset();
  }
  ```

  Managed tests live in
  `Expo.JSI.Tests/Runtime/JavaScriptPromiseTests.cs` (xUnit, uses
  `Expo.JSI.Tests.Fixtures`). Missing cases: resolve-then-resolve,
  resolve-then-reject, reject-then-resolve — each should be a no-op for the
  second settlement, observable from JS (`then`/`catch` fires exactly once
  with the first value).
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
  — emits diagnostics (around lines 76–98: unsupported constructors, malformed
  module attributes, invalid lifecycle hooks — read the file and enumerate all
  `Diagnostic` descriptors it defines). Tests:
  `Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs` with
  `GeneratorTestHost.cs` harness. Existing tests are mostly positive cases;
  each diagnostic descriptor should have at least one test proving it fires
  with the right ID on a minimal offending module source.
- Repo conventions: xUnit `[Fact]`/`[Theory]`, file-per-subject under
  `Expo.JSI.Tests/Runtime/`, namespaces like `Expo.JSI.Tests.Runtime`.
  Read the repo skill note: `.agents/skills/` and
  `docs/specs/promises.md`, `docs/specs/managed-jsi-wrappers.md` describe
  intended semantics — tests must assert spec-described behavior.

## Commands you will need

| Purpose | Command | Expected on success |
|---|---|---|
| Managed tests (all) | `scripts/test-managed.sh` | all pass |
| Filtered | `scripts/test-managed.sh --filter FullyQualifiedName~JavaScriptPromiseTests` | subset passes |
| Format | `scripts/format.sh --check --all` | exit 0 |

`scripts/test-managed.sh` forwards extra args to `dotnet test`. It requires a
prebuilt Hermes (`scripts/build-hermes-macos.sh` once, cached under `build/hermes`).

## Scope

**In scope** (create/modify only):
- `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptPromiseTests.cs` (extend)
- A new or existing string test file under `Expo.JSI.Tests/Runtime/` for the
  UTF-8 boundary corpus (e.g. extend `JavaScriptValueTests.cs` or create
  `JavaScriptStringUtf8Tests.cs`)
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs` (extend)
- `docs/plans/README.md` (status row)

**Out of scope** (do NOT touch):
- `ExpoJsiBridge.cpp` and any file under `native/` — tests only. If a test
  exposes a real validator/settlement bug, report it (Plan 004 touches the
  validator call sites; a validator logic bug is new information for the operator).
- `Expo.JSI` / `Expo.ModulesCore` production code, the ABI header, the generator.
- Adding new C ABI entries or managed APIs "for testability".

## Git workflow

- Branch: `advisor/003-bridge-edge-case-tests`
- Commit style: `test(jsi): cover UTF-8 boundaries and promise settlement` /
  `test(generator): cover diagnostic paths`
- Do NOT push or open a PR unless the operator instructed it.

## Steps

### Step 1: UTF-8 boundary corpus (valid side)

Add `[Theory]` cases asserting round-trip of boundary codepoints through
string creation and read-back: U+0000, U+007F, U+0080, U+07FF, U+0800, U+D7FF,
U+E000, U+FFFD, U+FFFF, U+10000, U+10FFFF, plus a mixed multi-byte string.
(C# strings encode these to exactly the byte patterns the validator branches on.)

**Verify**: `scripts/test-managed.sh --filter FullyQualifiedName~Utf8` (adjust
filter to your test class name) → new tests pass.

### Step 2: UTF-8 invalid-bytes cases — only if a raw-bytes path exists

Search `Expo.JSI` for a public/internal API that passes raw bytes to
`createString` (e.g. `ReadOnlySpan<byte>` overload, or an internal interop
helper callable from the test assembly — check `InternalsVisibleTo`). If found,
add cases: overlong `C0 80`, overlong `E0 80 80`, surrogate `ED A0 80`,
out-of-range `F4 90 80 80`, bare continuation `80`, truncated `E2 82`,
`C1 BF`, `F5 80 80 80` — each must be rejected with the bridge's error (assert
the thrown exception/error message contains "not valid UTF-8"). If no such
path exists, add a comment block in the test file documenting the gap and move on.

**Verify**: filtered run passes; note in commit message whether invalid-bytes
cases were coverable.

### Step 3: Promise double-settlement tests

In `JavaScriptPromiseTests.cs`, model on the existing
`CreatePromiseCreatesJavaScriptVisiblePromise` pattern. Add: resolve twice →
JS `then` observes first value once; resolve then reject → no `catch` fires;
reject then resolve → `catch` fires once with first reason. Use the JS side
(evaluate script attaching handlers, count invocations via a global) so the
guarantee is observed where it matters.

**Verify**: `scripts/test-managed.sh --filter FullyQualifiedName~JavaScriptPromiseTests` → all pass.

### Step 4: Generator diagnostic coverage

Enumerate all `DiagnosticDescriptor`s in `ExpoModulesGenerator.cs`. For each
descriptor lacking a test in `ExpoModulesGeneratorTests.cs`, add a test
compiling a minimal offending module source through `GeneratorTestHost` and
asserting the diagnostic ID is reported (and that no code is emitted or the
module is skipped — assert current behavior).

**Verify**: `scripts/test-managed.sh --filter FullyQualifiedName~ExpoModulesGeneratorTests` → all pass.

### Step 5: Full suite + format

**Verify**: `scripts/test-managed.sh` → all pass;
`scripts/format.sh --check --all` → exit 0 (run `scripts/format.sh` on your new
files if it flags them, then re-check).

## Test plan

Covered in Steps 1–4. Structural exemplars: `JavaScriptPromiseTests.cs`
(runtime fixtures), `ExpoModulesGeneratorTests.cs` + `GeneratorTestHost.cs`
(generator harness).

## Done criteria

- [ ] `scripts/test-managed.sh` exits 0 with new tests included.
- [ ] Boundary-codepoint theory exists (≥10 cases).
- [ ] Three double-settlement tests exist and pass.
- [ ] Every generator `DiagnosticDescriptor` has ≥1 firing test.
- [ ] `scripts/format.sh --check --all` exits 0.
- [ ] No files outside in-scope list modified (`git status`).
- [ ] `docs/plans/README.md` status row updated.

## STOP conditions

Stop and report back (do not improvise) if:

- Any new test FAILS against current code — that is a discovered bug in the
  validator, settlement guard, or generator; report the failing case verbatim.
- Covering invalid UTF-8 seems to require adding managed or ABI surface.
- A double-settlement test can only be written by calling native resolve/reject
  handles in a way the managed API doesn't expose — document the gap instead.
- Generator diagnostics turn out to be reported through a mechanism
  `GeneratorTestHost` can't capture.

## Maintenance notes

- Plan 004 (bridge string hygiene) assumes this corpus exists as its safety
  net — land this first.
- If the UTF-8 validator is ever replaced (e.g. by simdutf or engine-side
  validation), keep this corpus; it's implementation-independent.
- Reviewer: check the double-settlement tests observe behavior from JS, not
  just "no exception thrown" from C#.
