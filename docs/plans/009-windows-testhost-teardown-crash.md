# 009 — Windows testhost teardown crash (0xC0000005 at process exit)

- **Type:** investigation / spike (diagnose → confirm on Windows → targeted fix)
- **Priority:** P1 (blocks the `native-tests.yml` Windows leg from going green)
- **Effort:** M (native + managed teardown; needs a Windows box to confirm)
- **Written against:** `ae9a6a55` on `advisor/001-ci-checks`
- **Owner note:** requires a Windows machine to capture the faulting stack; the
  advisor cannot reproduce on macOS/arm64.

## Symptom (evidence)

`native-tests.yml` run `28976399006`, job `managed-tests (windows-latest)`
(job id `85984526708`), step 8 "Run managed test suite (Windows)":

```
Expo.ModulesCore.Generator.Tests  Passed! 40/40
Expo.JSI.Tests                    Passed! 118/118
Expo.ModulesCore.Tests            [xUnit.net] [FATAL ERROR] Xunit.Sdk.TestPipelineException
                                  Catastrophic failure: Test process crashed with exit code -1073741819.
                                  Passed! - Failed: 0, Passed: 36, Total: 36, Duration: 430 ms
Exception: scripts/test-managed.ps1:37  (dotnet exited with code 1)
```

- `-1073741819` = `0xC0000005` = **STATUS_ACCESS_VIOLATION** (segfault).
- All 36 `Expo.ModulesCore.Tests` **pass**; the crash is at **process teardown**
  (the `[FATAL]` and `Passed!` lines are ~60 ms apart — the AV coincides with
  assembly/process shutdown, not with any test body).
- Only `Expo.ModulesCore.Tests` crashes. `Expo.JSI.Tests` (118) and the pure
  Generator tests (40) exit cleanly.
- macOS runs the **same** managed suite clean (241/241 locally). Windows-only.
- Reported as passing on the operator's local Windows box previously; fails on
  the GitHub runner. So the trigger is environment/timing-sensitive, not a hard
  logic error in a test body.
- **Confirmed intermittent:** the same Windows leg PASSED on the very next run
  (`ca53bb87`, run built after the Linux fix + dump instrumentation landed).
  Failed on `28976399006`, passed on the re-run → this is a genuine race, not a
  deterministic crash. A single green run does NOT close it; the dump-capture
  instrumentation (below) is armed to catch the next occurrence on CI.
- **Reproduced 2026-07-09 — dump captured.** `native-tests` run `29035088584`
  (branch `codex/windows-native-views`), job `managed-tests (windows-latest)`:
  Generator 44/44, JSI 118/118, ModulesCore 67/67 all pass, then
  `[xUnit.net] [FATAL ERROR] Xunit.Sdk.TestPipelineException` at teardown →
  exit 1. The run uploaded a **`windows-crash-dump` artifact** — this plan is
  now actionable: download the dump (`gh run download 29035088584 -n
  windows-crash-dump`) and analyze the faulting stack on a Windows box
  (WinDbg/cdb) to confirm or refute the teardown-order hypothesis below.
  (The same run's ubuntu failure is unrelated — branch-specific NETSDK1100
  from a new Windows-targeting csproj.)
- **Status 2026-07-09:** IN PROGRESS — dump handed to
  `<windows-test-machine>` for stack analysis.

## Root-cause hypothesis (code-grounded, unconfirmed without a stack)

The Hermes `jsi::Runtime` is **created and destroyed on the connector's executor
thread** — `native/packages/jsi/src/HermesConsoleRuntimeConnector.cpp:150-154`
("the Hermes runtime is ... destroyed here so JSI never migrates between host
threads"). `invalidate()` is what transitions the loop to Stopping and
`join()`s that thread (`.join()` at `HermesConsoleRuntimeConnector.cpp:143`),
which is when the `jsi::Runtime` destructor runs.

Teardown order in `native/testhost/src/ExpoJsiTestHost.cpp:517-527`
(`expo_jsi_testhost_release_runtime`):

```cpp
unregisterRuntimeForCounters(testhost->runtime);
expo::dotnet::releaseRuntimeHandle(testhost->runtime);  // 524
testhost->runtime = nullptr;                            // 525
testhost->connector.invalidate();                       // 526  <-- Hermes dtor runs here
delete testhost;                                        // 527
```

Destroying the Hermes runtime runs finalization for any remaining JS host
objects / host functions, which call **back into managed static callbacks via
reverse function pointers** to free their `GCHandle` contexts:
`Expo.JSI/JavaScriptRuntime.cs:718 ReleaseHostFunctionContext`,
`:816 ReleaseHostObjectContext`, `:835 ReleaseScheduledRuntimeTaskContext`.

**Hypothesis H1 (primary):** for the last fixture disposed in the assembly, this
Hermes teardown → managed-release-callback sequence runs as the .NET process is
already shutting down. On Windows, CLR shutdown / DLL unload ordering means the
managed callback target (or the `hermesvm.dll` / `expo_jsi_testhost.dll` code
around it) is being torn down concurrently → the reverse call dereferences
unloaded/freed code or a freed context → `0xC0000005`. `Expo.ModulesCore.Tests`
is the only suite that leaves managed-backed host objects/functions registered
at teardown (its modules register them); `Expo.JSI.Tests` disposes its objects
explicitly and leaves nothing for Hermes teardown to release into managed.

**Hypothesis H2 (secondary):** teardown-order race independent of process exit —
`releaseRuntimeHandle` (524) runs before the executor thread is quiesced (526);
an in-flight async/scheduled task on the connector thread dereferences the
just-released runtime. The async/callback-heavy modules in `Expo.ModulesCore.Tests`
(`GeneratedAsyncModuleTests`, `GeneratedCallbackModuleTests`,
`GeneratedEventModuleTests`) widen the window; the runner's slower/contended
scheduling makes it fire where a fast local machine does not.

Both point at the same fix family: **quiesce the executor thread and drain/
release all managed-backed contexts BEFORE the runtime handle is released, and
force managed finalization to complete before process exit** — rather than
letting Hermes teardown call into managed during CLR shutdown.

## Step 1 — Confirm on Windows (capture the faulting stack)

Do this first; do not commit a fix on hypothesis alone.

1. Build the CI's Release `hermesvm.dll` the same way the runner does:
   `pwsh scripts/build-hermes-windows.ps1` (matches the failing job).
2. Enable full crash dumps and run the single crashing assembly:
   ```pwsh
   $env:DOTNET_DbgEnableMiniDump = "1"
   $env:DOTNET_DbgMiniDumpType   = "4"   # full dump
   $env:DOTNET_CreateDumpDiagnostics = "1"
   dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests
   ```
3. If it does not reproduce on the first run, loop it (H1/H2 are races):
   run 20–50 times, or run under load. Record the reproduction rate.
4. Open the dump (WinDbg / VS): capture the **faulting thread stack**. The
   decision hinges on where the top native frame is:
   - in `hermesvm.dll` runtime/GC teardown calling a managed function pointer
     → confirms **H1**;
   - on the connector executor thread inside a task callback touching a freed
     runtime → confirms **H2**;
   - in CLR shutdown / assembly-unload machinery → still H1-family.

Record hypothesis / commands / expected / actual / repro-rate / stop-go per the
`AGENTS.md` spike checklist.

## Step 2 — Candidate fixes (choose after the stack is known)

- **Quiesce before release (both hypotheses):** in
  `expo_jsi_testhost_release_runtime`, `invalidate()` (stop + join the executor
  thread, releasing queued contexts) **before** `releaseRuntimeHandle(runtime)`,
  so no task and no Hermes finalizer runs against a half-released runtime.
- **Force finalization before exit (H1):** at assembly teardown for the managed
  suites, after disposing fixtures, `GC.Collect(); GC.WaitForPendingFinalizers();`
  (and ensure every `JavaScriptRuntime` is disposed, not just the testhost
  handle — `HermesRuntimeFixture.Dispose` currently releases only
  `testHostRuntime`) so managed-backed contexts are freed while the runtime and
  DLLs are still fully loaded, not during CLR shutdown.
- **Guard managed callbacks post-invalidate:** make
  `ReleaseHostFunctionContext` / `ReleaseHostObjectContext` /
  `ReleaseScheduledRuntimeTaskContext` no-op-safe if invoked after the runtime
  is invalidated, so a late reverse call cannot dereference freed state.

Prefer the smallest fix that the Step 1 stack proves necessary. A native
teardown-ordering fix (quiesce-before-release) is the most likely correct and
lowest-risk; the managed-finalization fix is a good belt-and-suspenders and is
cheap.

## Scope

- **In scope:** `native/testhost/src/ExpoJsiTestHost.cpp` teardown order;
  `native/packages/jsi/.../HermesConsoleRuntimeConnector.*` quiesce semantics;
  managed release callbacks in `Expo.JSI/JavaScriptRuntime.cs`;
  `HermesRuntimeFixture.Dispose` / assembly teardown in the test projects.
- **Out of scope:** the Linux port (plan 008) and `native-tests.yml` structure;
  test bodies (they pass — do not modify assertions to dodge the crash).

## Done criteria

- Faulting stack captured and attached; hypothesis confirmed.
- `native-tests.yml` `managed-tests (windows-latest)` leg green across at least
  10 consecutive runs (the fix must close a race, not narrow it).
- macOS/Linux suites still pass (`scripts/test-managed.sh`).
- `scripts/format.sh --check --all` clean.

## STOP conditions

- If the stack shows the AV is **inside Hermes** with no managed frame in the
  path (a genuine `hermesvm.dll` teardown bug), STOP and report — that is an
  upstream Hermes issue, not our teardown ordering, and needs a different plan.
- If the crash **cannot** be reproduced on the operator's Windows box after a
  reasonable loop, STOP: it may be a runner-image-specific fault (toolset/ICU).
  Capture the runner's exact `hermesvm.dll` provenance before proposing a fix.
