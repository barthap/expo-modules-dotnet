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
- `apps/mobile-app` is an accepted NativeAOT React Native integration
  proof. It validates the adapter seam, but it is not a production mobile
  adapter and still leaves reload-safe teardown unresolved.

## Next Development Direction

1. Stabilize the low-level `Expo.JSI` ABI and wrapper surface.
   - Keep value/object/array/function operations on value handles.
   - Keep promise capability ownership separate.
   - Preserve scoped-ref and owned-wrapper lifetime rules.

2. Extend `Expo.ModulesCore`.
   - Keep module DSL and generated-binding helper concepts out of `Expo.JSI`.
   - Keep module proof coverage in `Expo.ModulesCore.Tests`.
   - Keep generated-looking code direct-call and reflection-free.

3. Build the source generator after the hand-written shape is stable.
   - Emit the direct-call provider shape already proven by tests.
   - Report unsupported signatures as diagnostics.

4. Revisit NativeAOT compatibility.
   - Audit exported entry points, trimming, and generated-binding constraints.
   - Keep HostFXR as a development loader, not runtime architecture.

5. Add platform adapters only after the portable module layer is stable.
   - RNW and React Native macOS (via expo-desktop) are the primary host targets.
   - React Native macOS and view adapters stay explicitly platform-gated.
   - Treat `apps/mobile-app` as proof evidence for the adapter seam, not
     as the production mobile adapter baseline.

## Backlog: ABI Extensions

These are planned ABI additions required for real module support. Each requires
coordinated native C++ implementation, C ABI function-pointer additions, and
managed wrapper surface.

- **Function calling from C#**: `call_function`, `call_as_constructor` — needed
  for event emission, SharedObject lifecycle, calling JS callbacks from managed
  code.
- **Script evaluation**: `evaluate_javascript` — needed for dev tooling and
  dynamic code paths.
- **ArrayBuffer**: Wrapper and ABI for binary data transfer — needed by camera,
  file system, crypto, WebSocket binary, and data-heavy modules.
- **HostObject**: Property interceptor pattern — needed for SharedObject, lazy
  module initialization, and dynamic property access.
- **NativeState**: Attach native data to JS objects — needed for SharedObject
  and SharedRef patterns.
- **Property enumeration**: `getPropertyNames` / `getOwnPropertyNames` — needed
  for record conversion and object iteration.
- **Events / EventEmitter**: Module-to-JS event emission — needed by nearly
  every interactive module.
- **`instanceof` checks**: Generalized beyond current Promise/Error — needed for
  type-safe record deserialization and custom class detection.

## Backlog: Type Codec Extensions

These are planned type conversions for the source generator and codec layer.
Each requires a codec implementation, a generator case, and possibly underlying
ABI support.

- **`int` / integer types**: Most common parameter type in practice (currently
  only `double` is supported).
- **`Dictionary<string, T>`**: Object-to-dictionary conversion for record-like
  parameters.
- **Record types**: Structured C# types mapped to/from JS objects via generated
  property-level codecs.
- **Nullable types**: `T?` support for optional parameters and return values.
- **`byte[]` / `ReadOnlyMemory<byte>`**: Binary data transfer (depends on
  ArrayBuffer ABI).
- **Enums**: Mapped to/from JS string or number values.
- **SharedObject references**: Typed handles to shared native state (depends on
  NativeState ABI).
- **Nested record types**: Records containing other records or collections.

## Backlog: Module System

- **Module instance lifecycle**: `onCreate`, `onDestroy`, reload-safe teardown
  — needed for resource cleanup and dev-reload safety. See
  `docs/assorted/architecture-review.md` Finding 2. The
  `apps/mobile-app` proof confirms this as the first production
  integration blocker.
- **Lazy module initialization**: Modules instantiated on first JS access
  instead of eagerly at registration (depends on HostObject ABI).
- **`expo-module.config.json`**: Package metadata for dotnet Expo module
  libraries.
- **Autolinking**: Build-time discovery and aggregation of dotnet Expo module
  packages into an app-level provider.
- **TurboModule integration**: Participation in React Native's TurboModule
  infrastructure for codegen'd bridging. The current `apps/mobile-app`
  proof validates the install hook but does not define production lifecycle
  semantics.

## Backlog: Architecture Improvements

Items identified during architecture review. See
`docs/assorted/architecture-review.md` for detailed analysis and solution
options.

- **Handle allocation cost**: Arena or pool allocator for `ValueHandle` to
  reduce per-call heap allocation pressure on hot paths (Finding 1).
- **`thread_local` error message lifetime**: Make error results self-contained
  instead of pointing into thread-local storage (Finding 4).
- **C# stack traces across ABI**: Include full exception stack trace in error
  messages forwarded to JS for dev tooling visibility (Finding 3).
- **Mobile scheduler priority no-op**: `apps/mobile-app` routes through
  React Native `CallInvoker`, which has no priority lane, so
  `JsiRuntimeTaskPriority` is advisory/no-op for that proof.

## Backlog: Dev Tooling

- **C# stack traces in LogBox / DevTools**: Forward managed exception stack
  traces through the ABI error path so they appear in React Native dev tools.
- **Structured error display**: Separate message and stack fields in error
  propagation for cleaner DevTools integration.
- **Development-only verbose errors**: Compile-time or runtime flag to control
  error verbosity across the ABI boundary.

## Backlog: Platform Adapters

- **RNW adapter**: Runtime installation, scheduler mapping, Windows lifecycle,
  expo-desktop integration.
- **React Native macOS adapter**: Reuse headless core, macOS scheduler and
  lifecycle services.
- **View adapters**: Platform-specific native view creation, prop mapping, event
  routing. Platform-gated — no view concepts in the portable core.
- **NativeAOT for iOS and Android**: The current proof lives under
  `apps/mobile-app`. Production mobile work still needs reload-safe
  teardown, trimming/export audits, and platform-specific constraints.

## Archive Map

- Initial planning docs: `docs/archive/agent-plan/`
- Completed proof notes: `docs/archive/spike-results/`
- Historical Superpowers specs and plans: `docs/archive/superpowers/`

Archived documents are useful for rationale and implementation history, but
they are not authoritative over current code, tests, or `docs/specs/`.
