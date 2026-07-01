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
  validates the adapter seam, but it is not a production mobile adapter and
  still leaves reload-safe teardown unresolved.
- `apps/desktop-app` is an accepted Expo Desktop / React Native macOS
  integration proof. It validates HostFXR-managed module registration and the
  generated synchronous module path on the React Native 0.81 macOS lane.

## Priority Roadmap

### P0: Cross-Host Lifecycle And Scheduler Evidence

These items are first because runtime lifecycle and scheduler behavior must be
derived from real React Native hosts before the portable teardown contract is
frozen. The `apps/mobile-app` proof is useful evidence, but it uses newer
React Native / Expo versions than the near-term production targets.

1. **React Native macOS lifecycle/scheduler proof** — completed proof evidence
   lives in `apps/desktop-app`; remaining work is production lifecycle cleanup,
   reload teardown, and broader scheduler coverage.
   - Mount the portable bridge into a real React Native macOS / Expo 54-era host.
   - Install one generated synchronous C# module into the real Hermes runtime.
   - Identify runtime install, reload, invalidation, and teardown hooks.
   - Map scheduler primitives and sync execution support honestly.
   - Compare findings against `apps/mobile-app`.
   - Exclude autolinking, views, broad packaging polish, and Windows/RNW work.

2. **React Native Windows lifecycle/scheduler proof**
   - Repeat the same narrow proof shape against RNW / Expo 54 after the Windows
     development environment is ready.
   - Focus on runtime install, scheduler mapping, sync execution behavior,
     teardown hooks, and RNW-specific packaging/build constraints.
   - Compare against macOS and the mobile proof before changing the portable
     contract.

3. **Cross-host runtime lifecycle contract**
   - Define the portable teardown contract from macOS, RNW, and mobile evidence.
   - Cover runtime-scoped module registry ownership, host-function pins,
     managed teardown callbacks, stale async work, passive sync capability, and
     priority semantics.

4. **Teardown contract implementation**
   - Implement the agreed contract in the portable core, testhost, and host
     adapters.
   - Verify that runtime invalidation releases managed module state and
     host-function pins before stale JSI access can occur.

### P1: Authoring Path And Error Quality

5. **Source generator hardening**
   - Keep generated bindings direct-call and reflection-free.
   - Strengthen diagnostics, generated source inspection, and provider shape
     stability for synchronous `[JS]` functions.

6. **Minimal codec expansion**
   - Add the types needed for ordinary small modules: integer types, nullable
     primitives, enums, simple records, and `Dictionary<string, T>`.
   - Keep ArrayBuffer, SharedObject, and NativeState out of this slice.

7. **Module package metadata**
   - Define enough `expo-module.config.json` / dotnet package metadata for
     future autolinking to consume, without implementing full autolinking yet.

8. **C# stack traces across the ABI**
   - Preserve managed exception stack traces through host-function error
     propagation so LogBox and DevTools expose useful C# context.

9. **Self-contained ABI errors**
   - Replace `thread_local` error message lifetime with self-contained error
     results before the ABI grows much further.

### P2: Interactive Module Capabilities

10. **Function calling from C#**
    - Add `call_function` / `call_as_constructor` support for retained JS
      callbacks and later event delivery.

11. **Async module methods / promises**
    - Generate promise-returning bindings for `Task` / `Task<T>` methods after
      cross-host scheduler semantics are known.

12. **Events / EventEmitter**
    - Build module-to-JS event emission on top of function calling, async
      scheduling, and lifecycle-safe teardown.

### P2/P3: Richer Runtime Surface

13. **ArrayBuffer / binary data**
    - Add binary transfer wrappers and ABI support for file, camera, crypto,
      WebSocket, and data-heavy modules.

14. **HostObject / NativeState / SharedObject**
    - Add the object/state primitives needed for SharedObject, SharedRef, lazy
      module access, and dynamic property surfaces.

15. **Lazy module initialization**
    - Instantiate modules on first JS access once HostObject and lifecycle
      semantics are ready.

### P3: Optimization And Tooling Polish

16. **Handle allocation optimization**
    - Revisit arena/pool allocation or primitive inline representations when
      profiling shows handle allocation pressure on hot paths.

17. **Structured DevTools error fields**
    - Split message and stack fields after simple C# stack trace propagation is
      working.

18. **Scheduler priority semantics**
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
- **P1/P2 — Property enumeration**: `getPropertyNames` /
  `getOwnPropertyNames` — needed for record conversion and object iteration.
- **P2 — Events / EventEmitter**: Module-to-JS event emission — needed by nearly
  every interactive module, but lower priority than generated async methods.
- **P2/P3 — `instanceof` checks**: Generalized beyond current Promise/Error —
  needed for type-safe record deserialization and custom class detection.

## Backlog: Type Codec Extensions

These are planned type conversions for the source generator and codec layer.
Each requires a codec implementation, a generator case, and possibly underlying
ABI support.

- **P1 — `int` / integer types**: Most common parameter type in practice
  (currently only `double` is supported).
- **P1 — `Dictionary<string, T>`**: Object-to-dictionary conversion for
  record-like parameters.
- **P1 — Record types**: Structured C# types mapped to/from JS objects via
  generated property-level codecs.
- **P1 — Nullable types**: `T?` support for optional parameters and return
  values.
- **P2/P3 — `byte[]` / `ReadOnlyMemory<byte>`**: Binary data transfer (depends
  on ArrayBuffer ABI).
- **P1 — Enums**: Mapped to/from JS string or number values.
- **P2/P3 — SharedObject references**: Typed handles to shared native state
  (depends on NativeState ABI).
- **P2/P3 — Nested record types**: Records containing other records or
  collections.

## Backlog: Module System

- **P0 — Module instance lifecycle**: `onCreate`, `onDestroy`, reload-safe
  teardown — needed for resource cleanup and dev-reload safety. See
  `docs/assorted/architecture-review.md` Finding 2. The
  `apps/mobile-app` proof confirms this as the first production
  integration blocker.
- **P2/P3 — Lazy module initialization**: Modules instantiated on first JS
  access instead of eagerly at registration (depends on HostObject ABI).
- **P1 — `expo-module.config.json`**: Package metadata for dotnet Expo module
  libraries; define metadata before full autolinking.
- **P3 — Autolinking**: Build-time discovery and aggregation of dotnet Expo
  module packages into an app-level provider.
- **P0/P3 — TurboModule integration**: Participation in React Native's
  TurboModule infrastructure for codegen'd bridging. The current
  `apps/mobile-app` proof validates the install hook, but production
  integration remains later than the lifecycle/scheduler proofs.

## Backlog: Architecture Improvements

Items identified during architecture review. See
`docs/assorted/architecture-review.md` for detailed analysis and solution
options.

- **P3 — Handle allocation cost**: Arena or pool allocator for `ValueHandle` to
  reduce per-call heap allocation pressure on hot paths (Finding 1).
- **P1 — `thread_local` error message lifetime**: Make error results
  self-contained instead of pointing into thread-local storage (Finding 4).
- **P1 — C# stack traces across ABI**: Include full exception stack trace in
  error messages forwarded to JS for dev tooling visibility (Finding 3).
- **P3 — Mobile scheduler priority no-op**: `apps/mobile-app` routes
  through React Native `CallInvoker`, which has no priority lane, so
  `JsiRuntimeTaskPriority` is advisory/no-op for that proof.

## Backlog: Dev Tooling

- **P1 — C# stack traces in LogBox / DevTools**: Forward managed exception stack
  traces through the ABI error path so they appear in React Native dev tools.
- **P3 — Structured error display**: Separate message and stack fields in error
  propagation for cleaner DevTools integration.
- **P3 — Development-only verbose errors**: Compile-time or runtime flag to
  control error verbosity across the ABI boundary.

## Backlog: Platform Adapters

- **P0 — React Native macOS adapter**: The first macOS HostFXR proof lives in
  `apps/desktop-app`. Production follow-up still needs reload-safe lifecycle
  services, teardown, and broader scheduler evidence.
- **P0 — RNW adapter**: Runtime installation, scheduler mapping, Windows
  lifecycle, expo-desktop integration. The first RNW slice is the
  lifecycle/scheduler proof, not full production packaging polish.
- **P3 — View adapters**: Platform-specific native view creation, prop mapping,
  event routing. Platform-gated — no view concepts in the portable core.
- **P3 — NativeAOT for iOS and Android**: The current proof lives under
  `apps/mobile-app`. Production mobile work still needs reload-safe
  teardown, trimming/export audits, and platform-specific constraints.

## Archive Map

- Initial planning docs: `docs/archive/agent-plan/`
- Completed proof notes: `docs/archive/spike-results/`
- Historical Superpowers specs and plans: `docs/archive/superpowers/`

Archived documents are useful for rationale and implementation history, but
they are not authoritative over current code, tests, or `docs/specs/`.
