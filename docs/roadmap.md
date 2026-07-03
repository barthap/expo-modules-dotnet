# High-Level Roadmap

This roadmap is derived from the archived planning docs, spike results, and the
current living specs. The living specs in `docs/specs/` are authoritative for
current behavior; archived docs provide provenance.

## Current Baseline

- The low-level C ABI is
  `packages/expo-modules-dotnet/native/include/expo_jsi.h`.
- The low-level managed package is
  `packages/expo-modules-dotnet/managed/packages/Expo.JSI`.
- The Hermes-backed managed verification path is `scripts/test-managed.sh`.
- The generated-binding helper package is
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore`.
- Module dispatch and conversion coverage lives under
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests`.
- `apps/mobile-app` is an accepted NativeAOT React Native integration proof. It
  validates the adapter seam and now participates in the shared
  `DotnetRuntimeContext` lifecycle model; it is still not a full production
  mobile adapter.
- `apps/desktop-app` is an accepted Expo Desktop / React Native macOS
  integration proof. It validates HostFXR-managed module registration and the
  generated synchronous module path on the React Native 0.81 macOS lane, with
  runtime teardown owned by the package adapter.
- `apps/desktop-app` also contains a React Native Windows proof project. Direct
  MSBuild validates the Windows adapter, HostFXR artifact staging, and app
  output layout on the React Native Windows 0.81 lane. Windows now follows the
  shared `DotnetRuntimeContext` lifecycle shape; the RNW CLI build/deploy path
  still has VS 2026/PDB locking follow-up work.

## Priority Roadmap

### P0: Cross-Host Lifecycle And Scheduler Evidence

Status: complete. The portable lifecycle/scheduler contract is now derived from
mobile, macOS, and Windows host evidence and implemented across the shared
managed core, headless testhost, and platform adapters. There are no active P0
roadmap items after this milestone; remaining work is P1+ production hardening,
autolinking, prebuild/config-plugin integration, and broader module APIs.

Completed scope:

1. **React Native macOS lifecycle/scheduler proof**
   - `apps/desktop-app` mounts the bridge into a real React Native macOS host.
   - Generated synchronous C# module functions run as direct JSI host functions.
   - HostFXR and NativeAOT loader paths resolve app-composed `expo_dotnet_*`
     entry points.
   - The macOS adapter owns one active runtime record and tears down the managed
     `DotnetRuntimeContext` from module invalidation.

2. **React Native Windows lifecycle/scheduler proof**
   - `apps/desktop-app/windows` and `packages/expo-modules-dotnet/windows`
     validate the RNW adapter, HostFXR artifact staging, and direct MSBuild app
     output.
   - The Windows adapter uses the same app-composed `expo_dotnet_*` lifecycle
     entry points and owns one managed `DotnetRuntimeContext` per RNW runtime.
   - RNW `InstanceDestroyed` is accepted as a late no-JSI teardown signal for
     the current context teardown semantics.

3. **Cross-host runtime lifecycle contract**
   - `DotnetRuntimeContext` owns runtime-scoped module instances,
     host-function registration state, and managed teardown for one JavaScript
     runtime.
   - Host adapters invalidate borrowed runtime holders before managed teardown
     and release opaque runtime handles after teardown.
   - Generated synchronous module functions remain direct JSI host functions;
     host schedulers are used for async runtime work only.

4. **Teardown contract implementation**
   - Managed tests cover runtime-context teardown and stale scheduled work.
   - The headless Hermes testhost models runtime invalidation.
   - Android, iOS, macOS, and Windows adapters use the shared runtime-context
     lifecycle model.

### P1: Authoring Path And Error Quality

1. **Source generator hardening** (complete)
   - Keep generated bindings direct-call and reflection-free.
   - Strengthen diagnostics, generated source inspection, and provider shape
     stability for synchronous `[JS]` functions.

2. **Minimal codec expansion** (complete)
   - Complete: null / undefined / void return semantics, nullable value types,
     generic numeric primitive codecs, enums, simple records, and
     `Dictionary<string, T>` / `IReadOnlyDictionary<string, T>`.
   - Keep ArrayBuffer, SharedObject, and NativeState out of this slice.

3. **Module package metadata**
   - Define enough `expo-module.config.json` / dotnet package metadata for
     future autolinking to consume, without implementing full autolinking yet.

4. **Self-contained ABI errors**
   - Replace `thread_local` error message lifetime with self-contained error
     results before the ABI grows much further.

### P2: Interactive Module Capabilities

1. **Function calling from C#**
    - Add `call_function` / `call_as_constructor` support for retained JS
      callbacks and later event delivery.

2. **Async module methods / promises**
    - Generate promise-returning bindings for `Task` / `Task<T>` methods after
      cross-host scheduler semantics are known.

3. **Events / EventEmitter**
    - Build module-to-JS event emission on top of function calling, async
      scheduling, and lifecycle-safe teardown.

### P2/P3: Richer Runtime Surface

1. **ArrayBuffer / binary data**
    - Add binary transfer wrappers and ABI support for file, camera, crypto,
      WebSocket, and data-heavy modules.

2. **HostObject / NativeState / SharedObject**
    - Add the object/state primitives needed for SharedObject, SharedRef, lazy
      module access, and dynamic property surfaces.

3. **Lazy module initialization**
    - Instantiate modules on first JS access once HostObject and lifecycle
      semantics are ready.

### P3: Optimization And Tooling Polish

1. **Handle allocation optimization**
    - Revisit arena/pool allocation or primitive inline representations when
      profiling shows handle allocation pressure on hot paths.

2. **Structured DevTools error fields**
    - Split message and stack fields after simple C# stack trace propagation is
      working.

3. **Scheduler priority semantics**
    - Keep priority advisory for hosts that cannot honor it; implement real
      priority only when a host scheduler exposes that capability.

## Backlog: ABI Extensions

These are planned ABI additions required for real module support. Each requires
coordinated native C++ implementation, C ABI function-pointer additions, and
managed wrapper surface.

- **P2 — Function calling from C#**: `call_function`, `call_as_constructor` —
  needed for retained JS callbacks, later event emission, SharedObject
  lifecycle, and calling JS callbacks from managed code.
- **P3 — Script evaluation**: `evaluate_javascript` — needed for dev tooling and
  dynamic code paths.
- **P2/P3 — ArrayBuffer**: Wrapper and ABI for binary data transfer — needed by
  camera, file system, crypto, WebSocket binary, and data-heavy modules.
- **P2/P3 — HostObject**: Property interceptor pattern — needed for
  SharedObject, lazy module initialization, and dynamic property access.
- **P2/P3 — NativeState**: Attach native data to JS objects — needed for
  SharedObject and SharedRef patterns.
- **P2 — Events / EventEmitter**: Module-to-JS event emission — needed by nearly
  every interactive module, but lower priority than generated async methods.
- **P2/P3 — `instanceof` checks**: Generalized beyond current Promise/Error —
  needed for type-safe record deserialization and custom class detection.

## Backlog: Type Codec Extensions

These are planned type conversions for the source generator and codec layer.
Each requires a codec implementation, a generator case, and possibly underlying
ABI support.

- **P1 — `int` / integer types** (complete): Signed and unsigned integer
  primitives are supported through the generic number codec.
- **P1 — Nullable types** (complete for value primitives): `T?` support exists
  for optional parameters and return values over supported value codecs.
- **P2/P3 — `byte[]` / `ReadOnlyMemory<byte>`**: Binary data transfer (depends
  on ArrayBuffer ABI).
- **P2/P3 — SharedObject references**: Typed handles to shared native state
  (depends on NativeState ABI).
- **P2/P3 — Record shape extensions**: Custom field naming, non-positional
  constructors, unknown-field validation, and cyclic record graphs.

## Backlog: Module System

- **P1 — Module-authored lifecycle hooks**: `onCreate`, `onDestroy`, and future
  authored resource cleanup hooks on top of the runtime-scoped
  `DotnetRuntimeContext`. The P0 runtime-context teardown owner exists; authored
  module lifecycle callbacks still need API design.
- **P2/P3 — Lazy module initialization**: Modules instantiated on first JS
  access instead of eagerly at registration (depends on HostObject ABI).
- **P1 — `expo-module.config.json`**: Package metadata for dotnet Expo module
  libraries; define metadata before full autolinking.
- **P3 — Autolinking**: Build-time discovery and aggregation of dotnet Expo
  module packages into an app-level provider.
- **P1/P3 — Installer TurboModule shape**: The JavaScript-facing installer has a
  typed `TurboModule` spec today, but the native Android/iOS/macOS/Windows
  installer glue is intentionally hand-written. Revisit only if a concrete
  React Native codegen need appears for the installer surface.

## Backlog: Architecture Improvements

Items identified during architecture review. See
`docs/assorted/architecture-review.md` for detailed analysis and solution
options.

- **P3 — Handle allocation cost**: Arena or pool allocator for `ValueHandle` to
  reduce per-call heap allocation pressure on hot paths (Finding 1).
- **P1 — `thread_local` error message lifetime**: Make error results
  self-contained instead of pointing into thread-local storage (Finding 4).
- **P3 — Mobile scheduler priority no-op**: `apps/mobile-app` routes
  through React Native `CallInvoker`, which has no priority lane, so
  `JsiRuntimeTaskPriority` is advisory/no-op for that proof.

## Backlog: Dev Tooling

- **P3 — Structured error display**: Separate message and stack fields in error
  propagation for cleaner DevTools integration.
- **P3 — Development-only verbose errors**: Compile-time or runtime flag to
  control error verbosity across the ABI boundary.

## Backlog: Platform Adapters

- **P1 — Desktop packaging cleanup**: The macOS HostFXR and NativeAOT-capable
  proof lives in `apps/desktop-app`. Lifecycle teardown now follows
  `DotnetRuntimeContext`; remaining production work is desktop artifact staging
  polish, managed module autolinking handoff, and packaging cleanup.
- **P1 — Expo Desktop prebuild integration**: If `apps/desktop-app` moves from
  a checked-in macOS project to an `expo-desktop` prebuild flow, preserve the
  current native wiring through an Expo config plugin. The plugin should own the
  managed artifact build phase, `EXPO_DOTNET_LOADER` build setting,
  `ExpoModulesDotnetLoader` `Info.plist` entry, `Managed` folder resource, and
  any macOS Podfile/autolinking shim still required by the supported
  `expo-desktop` / React Native macOS lane.
- **P1 — Windows build/deploy reliability**: Initial Windows adapter and direct
  MSBuild proof live in `apps/desktop-app` and
  `packages/expo-modules-dotnet/windows`. Lifecycle teardown now follows
  `DotnetRuntimeContext`; remaining production work includes RNW CLI launch
  reliability and VS/PDB locking issues.
- **P3 — View adapters**: Platform-specific native view creation, prop mapping,
  event routing. Platform-gated — no view concepts in the portable core.
- **P3 — NativeAOT for iOS and Android**: The current proof lives under
  `apps/mobile-app` and now uses shared runtime-context teardown. Production
  mobile work still needs trimming/export audits and platform-specific
  constraints.

## Archive Map

- Initial planning docs: `docs/archive/agent-plan/`
- Completed proof notes: `docs/archive/spike-results/`
- Historical Superpowers specs and plans: `docs/archive/superpowers/`

Archived documents are useful for rationale and implementation history, but
they are not authoritative over current code, tests, or `docs/specs/`.
