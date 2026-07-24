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
- `apps/mobile-app` is the accepted NativeAOT React Native integration example
  app. It validates the adapter seam and now participates in the shared
  `DotnetRuntimeContext` lifecycle model; production mobile-adapter hardening
  (trimming/export audits, platform-specific constraints) continues in P3.
- `apps/desktop-app` is the accepted Expo Desktop / React Native macOS
  integration example app. It validates HostFXR-managed module registration
  and the generated synchronous module path on the React Native 0.81 macOS
  lane, with runtime teardown owned by the package adapter.
- `apps/desktop-app` also contains a React Native Windows example app. Direct
  MSBuild validates the Windows adapter, HostFXR artifact staging, and app
  output layout on the React Native Windows 0.81 lane. Windows now follows the
  shared `DotnetRuntimeContext` lifecycle shape. Windows verification on
  2026-07-23 passed direct MSBuild, RNW CLI build/deploy and launch, and a
  live-app rebuild attempt without reproducing a PDB lock.

## Priority Roadmap

### P0: Cross-Host Lifecycle And Scheduler Evidence

Status: complete. The portable lifecycle/scheduler contract is now derived from
mobile, macOS, and Windows host evidence and implemented across the shared
managed core, headless testhost, and platform adapters. There are no active P0
roadmap items after this milestone; remaining work is P1+ production hardening,
autolinking, prebuild/config-plugin integration, and broader module APIs.

Completed scope:

1. **React Native macOS lifecycle/scheduler validation**
   - `apps/desktop-app` mounts the bridge into a real React Native macOS host.
   - Generated synchronous C# module functions run as direct JSI host functions.
   - HostFXR and NativeAOT loader paths resolve app-composed `expo_dotnet_*`
     entry points.
   - The macOS adapter owns one active runtime record and tears down the managed
     `DotnetRuntimeContext` from module invalidation.

2. **React Native Windows lifecycle/scheduler validation**
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

3. **Module package metadata** (complete)
   - Dotnet Expo module packages declare `platforms: ["dotnet"]` and
     `dotnet.projects` metadata consumed by the autolinking CLI.

4. **Self-contained ABI errors** (complete)
   - Complete: `expo_jsi_error` messages are self-contained and released through
     explicit callbacks instead of pointing into `thread_local` storage.

5. **camelCase JS naming defaults and `[JS]` properties** (complete)
   - `[JS]` members and record fields default to lower-camel JavaScript names
     (explicit `[JS("name")]` overrides); `[JS]` on an instance property
     exposes a JavaScript accessor property, writable when the C# property has
     a setter.

### P2: Interactive Module Capabilities

1. **Function calling from C#** (complete)
    - `JavaScriptFunction` exposes `Call`, `CallWithThis`, and
      `CallAsConstructor` over native function-call ABI entries.
    - `Expo.ModulesCore` supports retained `JavaScriptCallback<TResult>` and
      `JavaScriptCallback<TArgs, TResult>` parameters for generated modules,
      with value-tuple argument codecs and callback-specific generator
      diagnostics.

2. **Async module methods / promises** (complete)
    - Generate promise-returning bindings for `Task` / `Task<T>` methods after
      cross-host scheduler semantics are known.

3. **Events / EventEmitter** (complete)
    - Module-to-JS event emission ships via typed `[Event]` partial properties
      (`Func<Task>` / `Func<T, Task>`, awaitable dispatch that surfaces codec,
      scheduling, and teardown failures), built on function calling, async
      scheduling, and lifecycle-safe teardown. The string-based `[Events]`
      attribute and `Module.SendEventAsync` remain as the migration/interop
      path.

4. **Typed JS facade base classes** (complete)
    - `DotnetModule` / `DotnetEventEmitter` typed base classes (plus
      `EventsMap` / `EventSubscription` types) are exported from
      `expo-modules-dotnet` for module facades to extend.

### P2/P3: Richer Runtime Surface

1. **ArrayBuffer / binary data** (complete)
    - Opaque ABI storage, runtime-owned lifetime, low-level wrappers,
      module-facing ArrayBuffer ownership, and generated `byte[]` / span
      codecs are implemented. The approved delta is archived as provenance;
      current behavior is defined by the living specs.

2. **Promise long-lived-state migration** (P2 follow-up)
    - Unify retained Promise capability state with the runtime-owned
      long-lived-state collection introduced by ArrayBuffer without changing
      settlement scheduling. Cover unresolved-promise teardown,
      settlement/teardown races, and idempotent late disposal.

3. **HostObject / NativeState / SharedObject**
    - NativeState is complete as a generic, type-indexed object state primitive
      and backs ModulesCore EventEmitter identity.
    - HostObject is complete as a generic low-level property interceptor
      primitive in `Expo.JSI`.
    - The SharedObject design spike is complete (GO): opaque weak-object ABI
      handles, deterministic Hermes GC evidence, and an internal per-context
      `SharedObjectRegistry` with reference-identity round trips and
      exactly-once terminal release are implemented and specced. The public
      authoring surface (`SharedObject`, `[ExpoSharedObject]`, generator
      bindings, `SharedRef`, TypeScript facade) is the remaining follow-up
      slice, following upstream class/prototype instances with hidden
      registry-backed native identity.

4. **Lazy module initialization**
    - Complete: `_expoDotnet.modules` is a one-stage HostObject registry.
      Generated default registration records lazy module definitions, and a
      module object plus authored module instance are created on first read of
      the registered root module property.
    - Future optimization: if profiling shows root module property reads are
      too expensive, move to a two-stage lazy shell model where root access
      returns a cached shell and the real module object materializes on first
      shell access.

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

4. **Shared native bridge build composition**
   - Define a reusable source-set mechanism for the portable JSI bridge across
     Android CMake, Apple forward sources, testhost CMake, Hermes console CMake,
     and Windows MSBuild. This is build-system cleanup only; it must not change
     the ABI or runtime lifetime contract.

## Backlog: ABI Extensions

These are planned ABI additions required for real module support. Each requires
coordinated native C++ implementation, C ABI function-pointer additions, and
managed wrapper surface.

- **P2 — Function calling from C#**: complete. `call_function`,
  `call_with_this`, and `call_as_constructor` now back managed
  `JavaScriptFunction` calls and retained generated-module callbacks.
- **P3 — Script evaluation**: `evaluate_javascript` — needed for dev tooling and
  dynamic code paths.
- **P2/P3 — ArrayBuffer**: complete. Opaque ABI handles, runtime-owned
  lifetime, low-level wrappers, ModulesCore storage, and generated binary
  codecs now cover the initial production slice.
- **P2/P3 — HostObject**: complete. Property interceptor pattern backs lazy
  `_expoDotnet.modules` and future dynamic property access.
- **P2/P3 — NativeState**: complete. Generic type-indexed object state supports
  hidden managed state identity without exposing raw JSI layouts or managed
  object pointers.
- **P2 — Events / EventEmitter**: complete. Typed `[Event]` members and the
  legacy `[Events]` / `SendEventAsync` path cover module-to-JS event emission.
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
- **P2/P3 — binary codecs** (complete): `ArrayBuffer`, `byte[]`,
  `Span<byte>`, `ReadOnlySpan<byte>`, `Memory<byte>`, and
  `ReadOnlyMemory<byte>` are supported. Memory codecs use independent
  two-way copies and encode only the current logical slice.
- **P2/P3 — SharedObject references**: Typed handles to shared native state.
  The internal identity registry spike is GO; the codec/generator surface is
  the follow-up slice.
- **P2/P3 — Record shape extensions**: Record fields now default to
  lower-camel JavaScript names. Remaining: explicit per-field naming,
  non-positional constructors, unknown-field validation, and cyclic record
  graphs.

## Backlog: Module System

### Future authored module packages

The first authored C# packages are separate Windows/macOS work items. They use
the normal `_expoDotnet.modules` registry and do not yet claim upstream Expo
package compatibility or install aliases into `globalThis.expo.modules`.

1. [`expo-asset-dotnet`](plans/022-expo-asset-dotnet.md): native cache/download
   operation behind Expo's existing JavaScript asset resolution.
2. [`expo-constants-dotnet`](plans/023-expo-constants-dotnet.md): typed host
   metadata with explicit provenance.
3. [`expo-file-system-dotnet`](plans/024-expo-file-system-dotnet.md): local
   `Paths`, `File`, and `Directory` operations for Windows/macOS.
4. [`expo-crypto-dotnet`](plans/025-expo-crypto-dotnet.md): random and digest
   operations with ArrayBuffer range-aware native bindings.

- **P1 — Module-authored lifecycle hooks** (complete): `[OnCreate]` and
  `[OnDestroy]` are generator-backed managed callbacks on top of the
  runtime-scoped `DotnetRuntimeContext` / `ModuleRegistry` owner.
- **P2/P3 — Lazy module initialization**: complete. Modules instantiate on
  first `_expoDotnet.modules.<name>` access instead of eagerly at default
  provider registration.
- **P1 — `expo-module.config.json`** (complete): Package metadata for dotnet
  Expo module libraries is parsed by `expo-modules-dotnet-autolinking`.
- **P3 — Autolinking** (implemented for macOS/Windows/iOS/Android):
  Build-time discovery and aggregation of dotnet Expo module packages into the
  generated `ExpoDotnetHost` provider is implemented. Mobile app builds stage
  the NativeAOT aggregator through the iOS config plugin/Podfile helper and
  Android Gradle hook.
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
- **P3 — Mobile scheduler priority no-op**: `apps/mobile-app` routes
  through React Native `CallInvoker`, which has no priority lane, so
  `JsiRuntimeTaskPriority` is advisory/no-op for that example app.
- **P2 — Promise long-lived JSI state**: ArrayBuffer plan 006 introduces a
  runtime-owned long-lived-state collection. After it lands, migrate retained
  Promise capability state onto that collection without changing settlement
  scheduling. Cover unresolved-promise teardown, settlement/teardown races,
  and idempotent late disposal.

## Backlog: Dev Tooling

- **P3 — Structured error display**: Separate message and stack fields in error
  propagation for cleaner DevTools integration.
- **P3 — Development-only verbose errors**: Compile-time or runtime flag to
  control error verbosity across the ABI boundary.

## Backlog: Platform Adapters

- **P1 — Desktop packaging cleanup**: The macOS HostFXR and NativeAOT-capable
  example app lives in `apps/desktop-app`. Lifecycle teardown now follows
  `DotnetRuntimeContext`; managed module autolinking now owns desktop artifact
  staging. Remaining production work is packaging cleanup.
- **P1 — Expo Desktop prebuild integration**: If `apps/desktop-app` moves from
  a checked-in macOS project to an `expo-desktop` prebuild flow, preserve the
  current native wiring through an Expo config plugin. The plugin should own the
  managed artifact build phase, `EXPO_DOTNET_LOADER` build setting,
  `ExpoModulesDotnetLoader` `Info.plist` entry, `Managed` folder resource, and
  any macOS Podfile/autolinking shim still required by the supported
  `expo-desktop` / React Native macOS lane.
- **P1 — Windows build/deploy reliability** (complete for the current RNW
  0.81 / VS 2026 lane): Direct MSBuild, RNW CLI build/deploy and launch, and a
  live-app rebuild attempt were exercised on 2026-07-23 without reproducing a
  PDB lock or launch failure. No in-repo reliability workaround was warranted.
- **P2 — Windows `ReactNativeDir` resolution** (deferred until expo-desktop
  supports Windows prebuild): The package contains an app-root Node resolver
  and generated-props helper, but standard Expo prebuild does not execute
  Windows mods and the RNW CLI does not consume Expo config-plugin mods. It is
  therefore not a supported current build path. Require expo-desktop to provide
  Windows prebuild/mod execution before making that resolver active. The
  adapter's isolated `ReactNativeVersion.h` proof remains, with RNW owning JSI
  and CallInvoker include paths and ArrayBuffer selection remaining
  declaration-based rather than version-macro based.
- **P3 — View adapters**: Platform-specific native view creation, prop mapping,
  event routing. Platform-gated — no view concepts in the portable core.
- **P3 — NativeAOT for iOS and Android**: The current example app lives under
  `apps/mobile-app` and now uses shared runtime-context teardown plus mobile
  autolinking. Production mobile work still needs trimming/export audits and
  platform-specific constraints.

## Archive Map

- Initial planning docs: `docs/archive/agent-plan/`
- Completed proof notes: `docs/archive/spike-results/`
- Historical Superpowers specs and plans: `docs/archive/superpowers/`

Archived documents are useful for rationale and implementation history, but
they are not authoritative over current code, tests, or `docs/specs/`.
