# 02 - Research Spikes

Last refreshed: 2026-06-29.

## Purpose

This file re-baselines the original spike plan against the current repo. Some
early spikes are complete and have real evidence. Later spikes remain planned
work. When this file conflicts with newer specs or tests, the newer evidence
wins and this file should be updated.

Each spike or implementation slice must answer one architectural question,
leave evidence, and either unblock the next slice or stop for review.

## Global Spike Rules

Every spike result note must include:

- date and machine class, without committing local usernames or absolute paths;
- repository-relative files touched;
- branch or commit;
- exact commands run;
- expected result;
- actual result;
- files/artifacts created;
- whether artifacts are disposable or candidates for promotion;
- ownership and lifetime findings;
- scheduler findings where relevant;
- unresolved decisions;
- stop/go decision.

Result notes belong under:

```text
docs/spike-results/YYYY-MM-DD-short-name.md
```

## Current Spike Status

Completed or substantially completed:

- HostFXR loader proof: `docs/spike-results/2026-06-26-hostfxr-loader-proof.md`
- NativeAOT loader proof: `docs/spike-results/2026-06-26-nativeaot-loader-proof.md`
- Hermes dependency probe:
  `docs/spike-results/2026-06-27-hermes-dependency-probe.md`
- Hermes console JSI HostFXR proof:
  `docs/spike-results/2026-06-27-hermes-console-jsi-hostfxr.md`
- Hermes-backed `.NET` test suite:
  `docs/superpowers/specs/2026-06-27-hermes-dotnet-test-suite-design.md`
- Headless runtime executor and runtime loop:
  `docs/superpowers/specs/2026-06-27-headless-runtime-executor-design.md`
  and `docs/superpowers/specs/2026-06-28-headless-runtime-loop-design.md`
- Low-level wrapper expansion for arrays, promises, scoped refs, error
  objects, and ownership docs:
  `docs/superpowers/specs/2026-06-27-javascript-array-generated-conversions-design.md`,
  `docs/superpowers/specs/2026-06-28-javascript-promise-design.md`,
  `docs/superpowers/specs/2026-06-28-jsi-scoped-ref-ownership-design.md`,
  and `docs/superpowers/specs/2026-06-28-jsi-inner-ref-redesign.md`

Current active direction:

- Finish the value-handle slimming direction from
  `docs/superpowers/specs/2026-06-29-jsi-abi-value-handle-slimming-design.md`.
- Introduce the real `Expo.ModulesCore` package when the low-level ABI/wrapper
  shape is stable enough.
- Move temporary module-behavior tests out of `Expo.JSI.Tests/Modules` once
  `Expo.ModulesCore.Tests` exists.

## Spike A: ABI And Wrapper Slimming

Hypothesis:

The ABI can use one ordinary value handle for values, objects, arrays, and
functions while keeping promise capability separate. C# can still expose typed
owned wrappers and scoped refs without seeing raw JSI layouts.

Purpose:

Reduce ABI surface area, remove redundant object/array/function handle
plumbing, and make scoped-ref cleanup easier to reason about.

Primary references:

- `docs/superpowers/specs/2026-06-29-jsi-abi-value-handle-slimming-design.md`
- `native/include/expo_jsi.h`
- `managed/packages/Expo.JSI/`
- `managed/packages/Expo.JSI.Tests/Runtime/`
- `managed/packages/Expo.JSI.Tests/HostFunctions/`

Implementation boundary:

Build only the low-level ABI, native testhost, wrappers, and tests needed for
the value-handle model.

Do not build:

- `Expo.ModulesCore`;
- source generator;
- RNW adapter;
- view adapter.

Expected proof:

- `scripts/test-jsi.sh` passes.
- `scripts/format.sh --check --all` passes.
- No C# code observes raw `facebook::jsi::*` layouts.
- Owned wrapper and scoped-ref tests prove no handle leaks or double releases.

Failure signals:

- C# starts routing raw native layouts or untyped `void *` manually.
- Object/array/function handles become mixed with value handles without a
  temporary migration note.
- Promise capability ownership becomes ambiguous.

Stop/go decision:

- Stop if ownership of any retained or temporary handle cannot be stated.
- Go if all wrappers remain typed in C# and native remains the sole JSI owner.

## Spike B: `Expo.ModulesCore` Package Boundary

Hypothesis:

A higher-level module package can sit above `Expo.JSI`, own authored module DSL
concepts, and keep generated-binding code free of runtime hot-path reflection.

Purpose:

Move module concerns out of `Expo.JSI` and give generated-looking proofs a real
home.

Prerequisites:

- Value-handle direction is stable enough that generated code will not
  immediately churn.
- Temporary module proof behavior under `Expo.JSI.Tests/Modules` is understood.

Implementation boundary:

Build:

- `managed/packages/Expo.ModulesCore/`;
- `managed/packages/Expo.ModulesCore.Tests/`;
- minimal module registration shape;
- initial generated-looking provider code;
- typed conversion tests migrated from the temporary proof area where
  appropriate.

Do not build:

- a Roslyn source generator in the first package-boundary slice;
- platform adapters;
- view support.

Expected proof:

- Low-level `Expo.JSI.Tests` still focus on runtime/ABI/wrapper semantics.
- Module dispatch and conversion tests live in `Expo.ModulesCore.Tests`.
- Generated-looking code calls authored C# methods directly.
- Searches show no normal-path `Assembly.GetTypes`, `MethodInfo.Invoke`,
  `Delegate.DynamicInvoke`, `object?[]`, or JSON value conversion.

Failure signals:

- `Expo.JSI` gains module DSL concepts.
- Module tests stay permanently in `Expo.JSI.Tests/Modules`.
- Generated-looking code falls back to reflection or dynamic invocation.

Stop/go decision:

- Stop if the package boundary is unclear.
- Go if `Expo.ModulesCore` can depend on `Expo.JSI` without reversing the
  dependency or leaking module concerns downward.

## Spike C: Source Generator Prototype

Hypothesis:

A Roslyn source generator can emit the same direct-call provider shape proven by
hand-written generated-looking code.

Purpose:

Replace hand-written generated-looking proof code with generated output while
keeping the runtime path NativeAOT-friendly.

Prerequisites:

- `Expo.ModulesCore` exists.
- At least one generated-looking module path is tested.
- Unsupported type behavior is defined as diagnostics, not runtime guessing.

Implementation boundary:

Build:

- attributes such as `ExpoModule` and `JS`;
- generator project;
- generated provider;
- diagnostics for unsupported parameter and return types;
- generated output tests.

Do not build:

- broad v1 compatibility;
- runtime assembly scanning;
- platform adapters.

Expected proof:

- Generated code decodes `JavaScriptArguments`, invokes authored C# methods
  directly, and encodes return values through `Expo.JSI`.
- Generated output tests pin the intended shape.
- Forbidden reflection/dynamic invocation searches are clean for v2 hot path.

Stop/go decision:

- Stop if the generator compensates for unclear wrapper semantics.
- Go if generated code is equivalent to the already-proven hand-written shape.

## Spike D: NativeAOT Compatibility Audit

Hypothesis:

The ABI and generated-binding path can remain compatible with NativeAOT even
while HostFXR remains useful for experiments.

Purpose:

Prevent HostFXR convenience from becoming a runtime design dependency.

Prerequisites:

- Current low-level ABI and `Expo.ModulesCore` generated-looking path exist.

Implementation boundary:

Build:

- minimal NativeAOT publish or audit target;
- exported entry point and symbol inspection note;
- trimming/AOT risk list.

Do not build:

- Windows NativeAOT proof on macOS;
- RNW adapter;
- production package.

Expected proof:

- Publish either succeeds or fails with a documented AOT blocker.
- Generated-binding path remains free of forbidden hot-path reflection.
- Result note distinguishes loader mechanics from runtime wrapper semantics.

Stop/go decision:

- Stop if NativeAOT requires an ABI redesign.
- Go if blockers are isolated and do not invalidate the generated-binding plan.

## Spike E: Platform Adapter Boundary

Hypothesis:

The headless core can be mounted into RNW first, and later React Native macOS,
through thin adapters that provide host services without owning the C# module
system.

Purpose:

Define the adapter seam after the portable core and module layer are proven.

Prerequisites:

- `Expo.JSI` low-level tests pass.
- `Expo.ModulesCore` generated-binding path is stable.
- User explicitly chooses a host integration target.

Implementation boundary:

Build only after approval:

- adapter service table;
- scheduler mapping to host runtime facilities;
- lifecycle install/uninstall shape;
- optional view adapter interface.

Do not create or modify a real host app without user review.

Failure signals:

- portable core starts depending on RNW, WinUI, AppKit, or packaging;
- view creation leaks into headless module wrappers;
- adapter owns module invocation logic instead of installing the bridge;
- adapter asks C# to hold or call a raw React Native scheduler object.

Stop/go decision:

- Stop for user review before implementing any real RNW or React Native macOS
  adapter.
- Go only when the user chooses the first host integration target.
