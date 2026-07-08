# Plan 004: Remove redundant string copy in createString and make ABI UTF-8 validation consistent

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving to the
> next step. If anything in the "STOP conditions" section occurs, stop and
> report — do not improvise. When done, update the status row for this plan
> in `docs/plans/README.md` — unless a reviewer dispatched you and told you they
> maintain the index.
>
> **Drift check (run first)**: `git diff --stat 0f6fc760..HEAD -- packages/expo-modules-dotnet/native/`
> If any in-scope file changed since this plan was written, compare the
> "Current state" excerpts against the live code before proceeding; on a
> mismatch, treat it as a STOP condition.

## Status

- **Priority**: P2
- **Effort**: S
- **Risk**: LOW
- **Depends on**: docs/plans/003-bridge-edge-case-tests.md (UTF-8 corpus is the safety net)
- **Category**: perf + correctness
- **Planned at**: commit `0f6fc760`, 2026-07-08

## Why this matters

Two small hygiene issues in the C++ bridge. (1) `createString` builds a
temporary `std::string` before handing bytes to Hermes, which copies again —
one avoidable allocation+copy on every string crossing C#→JS, a hot path.
(2) UTF-8 validation at the ABI is inconsistent: `createString` validates
incoming bytes, but the four `jsi::PropNameID::forUtf8` call sites accept the
same kind of managed byte input unvalidated. In practice C# always sends valid
UTF-8, so this is consistency hardening: same input class, same contract.

## Current state

All in `packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp`:

- Line ~758–768, inside `createString`:

  ```cpp
  if (!isValidUtf8(data, length)) {
    return makeErrorResult(42, "String data is not valid UTF-8.");
  }
  try {
    const char *text = length == 0 ? "" : reinterpret_cast<const char *>(data);
    auto value =
      jsi::Value(runtimeHandle->runtime(),
                 jsi::String::createFromUtf8(runtimeHandle->runtime(),
                                             std::string(text, static_cast<size_t>(length))));
  ```

  `jsi::String::createFromUtf8` has an overload taking
  `(Runtime&, const uint8_t*, size_t)` — the `std::string` temp is unnecessary.
- UTF-8 validator: `isValidUtf8(const uint8_t*, int32_t)` at lines 429–510.
  It already handles `data == nullptr && length > 0` and `length < 0`.
- Unvalidated `PropNameID::forUtf8` sites taking managed `name`/`name_len`
  input (each already has null / negative-length guards before it):
  - line ~1448 — `objectSetProperty`
  - line ~1478 — `objectGetProperty`
  - line ~1907 — host object property-names path (inside a loop; validate the
    incoming name bytes once per name)
  - line ~2010 — host function creation (`functionName`)
- Error convention at these sites: numbered error codes via `makeError` /
  `makeErrorResult` with a short message; codes are unique per site (see 42/43/44
  in `createString`, 23–26 / 49–52 around the property functions). New
  validation errors must follow the same pattern with new, unused codes —
  grep `makeError` to find the current maximum before picking codes.
- Repo workflow: a delta spec under `docs/changes/<yyyy-mm-dd-slug>/spec.md`
  is OPTIONAL for this plan (operator decision, 2026-07-08) — but if you want
  a scaffold, drafting the contract wording there first is a good way to not
  get lost; fold it into the living spec at the end and delete the draft or
  leave it, either is fine. The hard requirement is the outcome:
  `docs/specs/runtime-and-abi.md` (owns the C ABI contract) MUST contain the
  new validation contract before branch handoff (Step 4).

## Commands you will need

| Purpose | Command | Expected on success |
|---|---|---|
| Managed tests | `scripts/test-managed.sh` | all pass |
| Format | `scripts/format.sh --check --all` | exit 0 (clang-format covers the .cpp) |

`scripts/test-managed.sh` rebuilds the native testhost, so C++ changes are
compiled and exercised by it. Requires prebuilt Hermes
(`scripts/build-hermes-macos.sh` once).

## Scope

**In scope** (create/modify only):
- `packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp`
- `docs/specs/runtime-and-abi.md` (add the validation contract)
- Managed test additions ONLY if a property-name path is reachable with raw
  bytes from the test assembly (same constraint as Plan 003 Step 2)
- `docs/plans/README.md` (status row)

**Out of scope** (do NOT touch):
- `native/include/expo_jsi.h` — no ABI signature changes; validation is
  internal behavior.
- Managed packages (`Expo.JSI`, `Expo.ModulesCore`) — no C# changes.
- The `isValidUtf8` implementation itself — reuse as-is; improving the
  validator is not this plan.
- Any other `ExpoJsiBridge.cpp` function.

## Git workflow

- Branch: `advisor/004-bridge-string-hygiene`
- Commit style: `perf(jsi): avoid extra copy in createString` and
  `fix(jsi): validate UTF-8 at PropNameID call sites` (separate commits)
- Do NOT push or open a PR unless the operator instructed it.

## Steps

### Step 1: Remove the temp copy in createString

Replace the `std::string(text, ...)` construction with the
`createFromUtf8(runtime, data, static_cast<size_t>(length))` overload
(handle `length == 0` with a valid empty creation — check what the overload
does with a null pointer + zero length; if unsafe, keep a `static const uint8_t`
empty buffer). Delete the now-unused `text` variable.

**Verify**: `scripts/test-managed.sh` → all pass (includes Plan 003's UTF-8
boundary corpus).

### Step 2: Validate at the four PropNameID sites

At each site (~1448, ~1478, ~1907, ~2010), after the existing null/length
guards, add:

```cpp
if (!isValidUtf8(reinterpret_cast<const uint8_t *>(name), name_len)) {
  return makeError(<new unique code>, "Property name is not valid UTF-8.");
}
```

(adapt variable names / `makeError` vs `makeErrorResult` / message noun —
"Function name", "Property name" — to each site; for the ~1907 loop site,
validate each name's bytes where they enter from the managed side, not inside
the per-property JS loop if the data was already validated at entry). Pick
error codes above the current maximum; keep each site's code unique.

**Verify**: `scripts/test-managed.sh` → all pass.

### Step 3: Managed regression tests (conditional)

If Plan 003 Step 2 found a raw-bytes path into the ABI from the test assembly,
add the same invalid-byte cases against `objectSetProperty`/`objectGetProperty`
name input. Otherwise skip — the managed API cannot produce invalid UTF-8.

**Verify**: filtered test run passes, or step skipped with the reason noted in
the commit message.

### Step 4: Update living spec + format

Add the accepted contract sentence(s) to `docs/specs/runtime-and-abi.md`
(section covering string/property-name input). Run
`scripts/format.sh` if the check flags files, then re-check.

**Verify**: `scripts/format.sh --check --all` → exit 0;
`grep -n "UTF-8" docs/specs/runtime-and-abi.md` → shows the new contract line.

## Test plan

Plan 003's boundary corpus is the regression net for Step 1. Step 2 is covered
by existing property/function tests (valid names keep working) plus Step 3's
invalid-byte cases when reachable. Full gate: `scripts/test-managed.sh`.

## Done criteria

- [ ] `grep -n "std::string(text" packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp` → no matches.
- [ ] All four `PropNameID::forUtf8` sites preceded by an `isValidUtf8` check
      (verify: `grep -B8 "PropNameID::forUtf8" .../ExpoJsiBridge.cpp | grep -c isValidUtf8` ≥ 4, adjusting for the loop site).
- [ ] `scripts/test-managed.sh` exits 0.
- [ ] `scripts/format.sh --check --all` exits 0.
- [ ] `docs/specs/runtime-and-abi.md` updated with the validation contract.
- [ ] No files outside in-scope list modified (`git status`).
- [ ] `docs/plans/README.md` status row updated.

## STOP conditions

Stop and report back (do not improvise) if:

- Plan 003 is not DONE (check `docs/plans/README.md`) — the safety net must exist first.
- The `createFromUtf8(Runtime&, const uint8_t*, size_t)` overload does not
  exist in the vendored/linked JSI headers (check the jsi.h the testhost
  compiles against) — do not add one.
- Any existing test fails after Step 2 — something legitimately sends bytes
  the validator rejects; that contradicts this plan's core assumption.
- The `~1907` site's data flow doesn't match the description (names not coming
  directly from managed input) — report the actual flow.

## Maintenance notes

- Any future ABI entry accepting UTF-8 bytes must call `isValidUtf8` at entry —
  the spec line added in Step 4 is the enforcement hook for review.
- If profiling later shows validation cost on hot paths, the right fix is a
  faster validator (e.g. simdutf), not removing validation — note kept here
  deliberately.
- Reviewer: confirm error codes are unique (grep `makeError` codes) and the
  empty-string path in Step 1 is exercised by a test.
