# High-Level Roadmap

This roadmap is derived from the archived planning docs, spike results, and the
current living specs. The living specs in `docs/specs/` are authoritative for
current behavior; archived docs provide provenance.

## Current Baseline

- The low-level C ABI is `native/include/expo_jsi.h`.
- The low-level managed package is `managed/packages/Expo.JSI`.
- The Hermes-backed verification path is `scripts/test-jsi.sh`.
- Temporary generated-looking module proof code lives under
  `managed/packages/Expo.JSI.Tests/Modules`.
- `Expo.ModulesCore` does not exist yet.

## Next Development Direction

1. Stabilize the low-level `Expo.JSI` ABI and wrapper surface.
   - Keep value/object/array/function operations on value handles.
   - Keep promise capability ownership separate.
   - Preserve scoped-ref and owned-wrapper lifetime rules.

2. Introduce `Expo.ModulesCore`.
   - Move module DSL and generated-binding helper concepts out of `Expo.JSI`.
   - Move temporary module proof coverage into `Expo.ModulesCore.Tests`.
   - Keep generated-looking code direct-call and reflection-free.

3. Build the source generator after the hand-written shape is stable.
   - Emit the direct-call provider shape already proven by tests.
   - Report unsupported signatures as diagnostics.

4. Revisit NativeAOT compatibility.
   - Audit exported entry points, trimming, and generated-binding constraints.
   - Keep HostFXR as a development loader, not runtime architecture.

5. Add platform adapters only after the portable module layer is stable.
   - RNW is the first likely host target.
   - React Native macOS and view adapters stay explicitly platform-gated.

## Archive Map

- Initial planning docs: `docs/archive/agent-plan/`
- Completed proof notes: `docs/archive/spike-results/`
- Historical Superpowers specs and plans: `docs/archive/superpowers/`

Archived documents are useful for rationale and implementation history, but
they are not authoritative over current code, tests, or `docs/specs/`.
