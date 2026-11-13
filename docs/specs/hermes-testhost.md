# Hermes Testhost

## Purpose

Specify the native Hermes-backed test host and managed test workflow used to
verify the portable core.

## Requirements

### Requirement: Platform-Aware Hermes Prebuilt

The repo-local Hermes prebuilt root SHALL support platform-specific native link
layouts while preserving a common header root. Every platform SHALL build
Hermes V1, the engine line that React Native ships, from the commit pinned in
`scripts/hermes-ref.txt`. The pin SHALL be the full SHA of the Hermes release
tag that the React Native version used by `apps/mobile-app` ships. macOS SHALL
use `hermesvm.framework`, which `scripts/build-hermes-macos.sh` stages from the
Hermes build tree into the prebuilt root together with the `hermes` headers. Windows SHALL use the official Hermes `hermesvm` shared
library built from `scripts/hermes-ref.txt` and staged with `jsi.lib`,
`hermesvm.lib`, and `hermesvm.dll`. Linux SHALL use the shared
`libhermesvm.so` built via `scripts/build-hermes-linux.sh`; the shared
library path is embedded in `libexpo_jsi_testhost.so` via rpath at CMake
configure time, so no `LD_LIBRARY_PATH` management is required at runtime.

#### Scenario: Linux Hermes prebuilt is staged

- **GIVEN** a Linux developer has CMake, Ninja, Clang, and libicu-dev available
- **WHEN** the developer runs `scripts/build-hermes-linux.sh`
- **THEN** the script SHALL build the official Hermes `hermesvm` target as a
  shared library
- **AND** it SHALL install `include/hermes/hermes.h`, `include/hermes/Public/`,
  `include/jsi/jsi.h`, and `lib/libhermesvm.so` under the configured prebuilt
  root; jsi is either embedded statically into `libhermesvm.so` or, when the
  toolchain builds it shared, staged as `lib/libjsi.so` beside it — the script
  and `cmake/ExpoHermesPrebuilt.cmake` handle both layouts
- **AND** the CI cache key SHALL be
  `hermes-Linux-<hash of scripts/hermes-ref.txt>`

#### Scenario: Windows Hermes prebuilt is staged
- **GIVEN** a Windows developer has VS 2026 C++ tooling and CMake available
- **WHEN** the developer runs `scripts/build-hermes-windows.ps1`
- **THEN** the script SHALL build the official Hermes `hermesvm` target
- **AND** it SHALL stage `include/hermes/hermes.h`, `include/jsi/jsi.h`,
  `lib/win32/<arch>/jsi.lib`, `lib/win32/<arch>/hermesvm.lib`, and
  `bin/win32/<arch>/hermesvm.dll` under the configured prebuilt root

#### Scenario: Upstream Intl build is unavailable
- **GIVEN** the supported Windows Hermes lane cannot build upstream Intl
  artifacts with the current Hermes revision
- **WHEN** `scripts/build-hermes-windows.ps1` runs without `-EnableIntl`
- **THEN** it SHALL build the official shared Hermes runtime with Intl disabled
- **AND** this choice SHALL remain isolated to the headless Hermes proof and
  testhost setup

### Requirement: Canonical Test Runners

The repository SHALL use platform-paired Hermes-backed managed test runners. On
macOS and Linux, `scripts/test-managed.sh` SHALL build the native Hermes
testhost and run managed test projects (the testhost library name is
`.dylib` on macOS and `.so` on Linux, resolved via `uname` at runtime). On
Windows, `scripts/test-managed.ps1` SHALL build the Windows native Hermes
testhost and run the same managed test projects. All runners SHALL pass
`EXPO_JSI_TESTHOST_LIBRARY` to managed tests.

#### Scenario: macOS / Linux test runner executes

- **GIVEN** a developer runs `scripts/test-managed.sh`
- **WHEN** the script runs on macOS or Linux
- **THEN** it SHALL deterministically discover
  `packages/*/dotnet/*.Tests/*.Tests.csproj`
- **AND** it SHALL build one native Hermes testhost, pass
  `EXPO_JSI_TESTHOST_LIBRARY` to managed tests, and run
  `Expo.ModulesCore.Generator.Tests`, `Expo.JSI.Tests`,
  `Expo.ModulesCore.Tests`, and the discovered authored-module test projects
- **AND** the testhost library name SHALL be `libexpo_jsi_testhost.dylib` on
  macOS and `libexpo_jsi_testhost.so` on Linux

#### Scenario: macOS / Linux runner selects test projects

- **GIVEN** a developer passes one or more repo-relative test project paths
  with `--project`
- **WHEN** `scripts/test-managed.sh` validates the selection
- **THEN** it SHALL reject a missing, duplicate, outside-repository, symlink,
  or non-`*.Tests.csproj` path before native setup
- **AND** it SHALL build the shared native testhost path once and run only the
  selected test projects

#### Scenario: Windows test runner executes
- **GIVEN** a Windows Hermes prebuilt exists under the configured
  `HERMES_PREBUILT_ROOT`
- **WHEN** a developer runs `scripts/test-managed.ps1`
- **THEN** it SHALL build the native Windows Hermes testhost
- **AND** it SHALL deterministically discover
  `packages/*/dotnet/*.Tests/*.Tests.csproj`
- **AND** it SHALL run `Expo.ModulesCore.Generator.Tests`, `Expo.JSI.Tests`,
  `Expo.ModulesCore.Tests`, and the discovered authored-module test projects
- **AND** it SHALL pass the built testhost DLL through
  `EXPO_JSI_TESTHOST_LIBRARY`

#### Scenario: Windows runner selects test projects

- **GIVEN** a developer passes one or more repo-relative test project paths
  with `-Project`
- **WHEN** `scripts/test-managed.ps1` validates the selection
- **THEN** it SHALL reject a missing, duplicate, outside-repository, symlink,
  or non-`*.Tests.csproj` path before native setup
- **AND** it SHALL build the shared native testhost path once and run only the
  selected test projects

### Requirement: Canonical Benchmark Runners

The repository SHALL provide platform-paired call-path benchmark runners:
`scripts/bench-managed.sh` on macOS and Linux and `scripts/bench-managed.ps1`
on Windows. Each runner SHALL build the managed projects in Release and the
native testhost with an optimized build type. It SHALL use a testhost build
directory that is separate from the test runners' directory, and SHALL pass
`EXPO_JSI_TESTHOST_LIBRARY` to the benchmark. The benchmark module SHALL go
through the generator like an authored module. The output SHALL NOT name the
machine. The benchmark project SHALL run with tiered compilation off, so every
sample runs fully optimized code. The test runners SHALL build the benchmark
project, because `Expo.ModulesCore.Tests` references it, and SHALL NOT run it.

Before each timed sample the benchmark SHALL collect garbage in .NET and in
Hermes and wait until the testhost executor is idle, so deferred work of one
sample does not land in the next sample or in a control row. A row whose
slowest sample is more than 1.20 times its median SHALL be marked noisy, and the
benchmark SHALL then exit with a non-zero code.

Reasons to keep these as they are:

- The benchmark needs optimized builds on both sides, and a Debug testhost
  gives numbers that mislead.
- The separate build directory exists because the test runner deletes its own
  on every run.
- With tiered compilation on, the first samples after the warm-up run
  unoptimized code until a background JIT batch completes, so the median
  depends on which sample that step lands in. One measured run reported a
  median 55% above the steady value with no noisy mark. Fully optimized code
  with no dynamic profile data is also nearer to NativeAOT than the tiered
  steady state is.

#### Scenario: A developer runs the benchmark

- **GIVEN** a developer runs a benchmark runner
- **WHEN** the run completes
- **THEN** it SHALL print one row per case with time, managed bytes, handle
  counts, the native control, and the bridge cost

#### Scenario: A sample set is noisy

- **GIVEN** the spread of a row is above 1.20
- **WHEN** the benchmark prints its table
- **THEN** it SHALL mark the row as noisy
- **AND** it SHALL exit with a non-zero code

#### Scenario: A change breaks the benchmark project

- **GIVEN** a change makes the benchmark project fail to build
- **WHEN** a developer runs a test runner
- **THEN** the test runner SHALL fail at build

### Requirement: Headless Hermes Console Runners

The headless Hermes console proof SHALL have platform-paired runners. The
bash runner `scripts/run-hermes-console-app.sh` SHALL support macOS and
Linux hosts. HostFXR SHALL select the matching nethost pack and build the
managed app without a runtime-specific publish. NativeAOT SHALL select the
host runtime identifier and published library name per platform. The Windows
HostFXR runner SHALL be `scripts/run-hermes-console-app.ps1`.

#### Scenario: Linux console proof runs both loaders
- **GIVEN** a Linux host with a Linux Hermes prebuilt destroot
- **WHEN** a developer runs `scripts/run-hermes-console-app.sh` with
  `EXPO_JSI_DOTNET_LOADER` set to `hostfxr` or `nativeaot`
- **THEN** HostFXR SHALL build the managed console app without a runtime
  identifier and select the matching nethost pack
- **AND** NativeAOT SHALL publish the managed console app for the Linux host
  runtime identifier and load `HermesConsoleApp.so`
- **AND** the proof SHALL exercise the same registration behavior as the
  macOS console proof

#### Scenario: Windows HostFXR console proof runs
- **GIVEN** a Windows Hermes prebuilt exists under the configured
  `HERMES_PREBUILT_ROOT`
- **WHEN** a developer runs `scripts/run-hermes-console-app.ps1`
- **THEN** it SHALL build the managed console app project
- **AND** it SHALL build the native console executable through CMake
- **AND** it SHALL run the executable unless `-NoRun` is provided
- **AND** the proof SHALL exercise the same opaque C ABI and generated module
  registration behavior as the macOS console proof

### Requirement: Native Testhost

The native testhost SHALL create a Hermes runtime and expose test-only exports
used by managed tests. It lives under
`packages/expo-modules-dotnet/native/testhost` so native and managed test
linking can reference adapter-owned bridge code directly.

#### Scenario: Managed fixture creates runtime
- **GIVEN** a managed test calls `HermesRuntimeFixture.Create`
- **WHEN** the native testhost successfully creates a runtime
- **THEN** the fixture SHALL expose a managed `JavaScriptRuntime` backed by the
  native API table and runtime handle

### Requirement: Low-Level Test Ownership

`Expo.JSI.Tests` SHALL focus on low-level runtime, value, ABI, ownership,
host-function, scheduling, promise, and testhost behavior.

#### Scenario: Runtime behavior changes
- **GIVEN** a change modifies low-level wrapper or native ABI behavior
- **WHEN** tests are added or updated
- **THEN** coverage SHALL live under
  `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests` unless the
  behavior belongs to the future module layer

### Requirement: Module-Layer Testhost Ownership

`Expo.ModulesCore.Testing` SHALL own the repo-local public Hermes runtime and
module test host used by authored module tests. It SHALL remain non-packable in
v1. `Expo.JSI.Tests` SHALL remain independent of `Expo.ModulesCore.Testing`.

#### Scenario: Authored module test uses the testhost
- **GIVEN** an authored package needs Hermes-backed generated module behavior
- **WHEN** its `.Tests` project creates an `ExpoModuleTestHost`
- **THEN** it SHALL use `Expo.ModulesCore.Testing`
- **AND** it SHALL pass its generated provider registration explicitly

#### Scenario: Low-level JSI test remains independent
- **GIVEN** `Expo.JSI.Tests` verifies low-level ABI and wrapper behavior
- **WHEN** module-layer testhost support exists
- **THEN** `Expo.JSI.Tests` SHALL retain its low-level fixture
- **AND** it SHALL NOT reference `Expo.ModulesCore.Testing`

### Requirement: Test Hosts Pass App Directories Through Without Managing Them

`ExpoModuleTestHost` SHALL offer a `Create` overload that accepts an
`AppDirectories` model and makes it observable through the runtime context inside
the existing registration callback. The existing overload SHALL stay source- and
binary-compatible and SHALL mean both directories are unconfigured.

The test host SHALL NOT create, clean, or lifetime-manage a directory. A test that
needs real files on disk owns that fixture itself.

#### Scenario: A test supplies directories
- **GIVEN** a test calls the directory-aware overload with configured values
- **WHEN** the registration callback runs
- **THEN** the runtime context SHALL return those values
- **AND** the test host SHALL NOT have created either directory

#### Scenario: The existing overload keeps working
- **GIVEN** an existing test calls the current `Create` overload
- **WHEN** it reads either directory accessor
- **THEN** it SHALL observe the unconfigured state
- **AND** the existing overload's signature SHALL be unchanged

### Requirement: Module Test Ownership

Framework module behavior SHALL live in `Expo.ModulesCore.Tests`; behavior
specific to an authored module SHALL live in that package's `.Tests` project.

#### Scenario: Module behavior is tested
- **GIVEN** a test proves generated binding, codec, registry, lifecycle, event,
  callback, or shared-object behavior independent of one authored package
- **WHEN** the behavior is above low-level `Expo.JSI`
- **THEN** coverage SHALL live under
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests`

#### Scenario: Authored module behavior is tested
- **GIVEN** a test proves behavior defined by one authored module package
- **WHEN** the test is added
- **THEN** coverage SHALL live in that package's `.Tests` project

### Requirement: Deterministic Runtime Queue Controls

The Hermes testhost SHALL expose test-only pause/resume, queue-observation,
drop-next, drop-queued, and bridge-handle-release controls. These controls
SHALL use condition-variable barriers rather than sleeps or polling so lifetime
tests can force release-versus-teardown orderings. The testhost counters SHALL
report ArrayBuffer release and abandonment separately from generic collection
metrics. They SHALL also report owned value handles swept at preparation, owned
value handles abandoned, and owned value handles still tracked.

#### Scenario: A queued release is dropped
- **GIVEN** the executor is paused and a release task is observed in its queue
- **WHEN** the test drops that task without invoking it
- **THEN** the copied callable SHALL be destroyed and its captured lifetime
  token SHALL remain safe
- **AND** the next valid runtime access SHALL drain deferred release work once

#### Scenario: The bridge handle is released before queued work runs
- **GIVEN** a queued callable retains only runtime-state tombstone ownership
- **WHEN** the test releases the bridge handle before resuming the executor
- **THEN** invocation or destruction of the callable SHALL abandon stale JSI
  payloads without dereferencing the connector or runtime

#### Scenario: Fixture disposal models abrupt scheduler shutdown
- **GIVEN** an active runtime callback and queued managed work
- **WHEN** the testhost fixture releases its runtime
- **THEN** it SHALL invalidate the bridge handle and stop the executor without
  waiting for a JSI-safe sweep
- **AND** queued work SHALL fault or release its task context without running
- **AND** tests that require a JSI-safe long-lived-object sweep SHALL call the
  explicit prepare-for-invalidation control before connector invalidation

#### Scenario: Test proves owned value teardown
- **GIVEN** a test holds an owned wrapper over a non-primitive value
- **WHEN** it runs `prepare -> invalidate -> release runtime handle` and then
  disposes the wrapper
- **THEN** the counters SHALL report one swept value, zero abandoned values, and
  zero tracked values
- **AND** with bare invalidation instead of preparation, they SHALL report zero
  swept values and one abandoned value

### Requirement: Deterministic Garbage Collection And Weak Counters

The testhost SHALL expose a synchronous test-only Hermes garbage-collection
control that runs on its runtime executor. Its counters SHALL report weak
object releases and abandonments separately, together with the number of
remaining long-lived entries.

#### Scenario: Test collects a weak referent
- **GIVEN** no JavaScript or managed strong reference keeps a weak referent
  alive
- **WHEN** a test requests collection and waits for the executor to become
  idle
- **THEN** locking the weak reference SHALL report no object without relying
  on elapsed time

#### Scenario: Test proves prepared teardown
- **GIVEN** long-lived weak work is pending
- **WHEN** a test needs the JSI-safe teardown proof
- **THEN** it SHALL use `prepare -> invalidate -> managed teardown`
- **AND** assert zero remaining entries after release or abandonment
- **AND** it SHALL treat bare invalidation as abrupt shutdown only, not as a
  production-order proof

### Requirement: Value Handle Creation Counters

The testhost counters SHALL report owned value handles created linked, owned
value handles created unlinked, and borrowed value handles created. They SHALL
count each value handle at the point where the counted API table, or the
testhost's own script evaluation, returns it to managed code. A handle is
linked when the bridge links it into the per-runtime owned-value list, which it
does for every owned handle that does not hold undefined, null, a boolean, or a
number. The borrowed `this` handle that the bridge creates inside a
host-function call SHALL NOT be counted, and the benchmark documentation SHALL
state it.

Reason to keep next to the requirement: counting in the testhost table keeps
the production bridge free of diagnostics. Counting in the bridge looks more
complete, but it adds test-only code to production for one constant handle per
call. The classification in the testhost mirrors the bridge, so a test compares
the linked counter with the tracked-owned-values counter to catch drift.

#### Scenario: A record decode creates a known number of handles

- **GIVEN** a generated function takes a record with 12 fields
- **WHEN** one call decodes the record
- **THEN** the counters SHALL report an exact, asserted number of handles of
  each kind
- **AND** a record with one more field SHALL create exactly one more owned
  handle

#### Scenario: Created handles match released values

- **GIVEN** a call has completed
- **WHEN** a test compares the counters
- **THEN** owned handles created SHALL equal values released, plus the handle
  that native frees without `release_value`

#### Scenario: The linked count matches the tracked count

- **GIVEN** a test holds non-primitive owned handles
- **WHEN** it reads the counters
- **THEN** the linked counter SHALL equal the tracked-owned-values counter

### Requirement: Native Benchmark Controls

The testhost SHALL expose a test-only export that installs native-only control
functions, one per benchmark shape and direction. A control SHALL do the same
traversal or construction in C++ through JSI, SHALL create no value handle, and
SHALL NOT call managed code.

Reason to keep next to the requirement: without the control, the numbers show
whether a shape is slow but not how much of the time the bridge adds, so they
cannot choose between fixes.

#### Scenario: A control runs next to a managed case

- **GIVEN** a benchmark case has a control
- **WHEN** the benchmark measures both
- **THEN** the control SHALL leave the handle counters at zero
- **AND** the table SHALL show the control time and the difference as the
  bridge cost
