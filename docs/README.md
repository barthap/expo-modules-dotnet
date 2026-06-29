# Portable C# / JSI Bridge Docs

Last refreshed: 2026-06-29.

This directory is the working documentation package for the portable C# / JSI
bridge in this repository. The early `agent-plan/` and `learning-guide/` files
started as planning material before implementation existed. The repository now
contains a real low-level bridge slice, so future agents should treat these docs
as a current orientation guide and treat newer specs, plans, spike results, and
tests as implementation evidence.

The architecture rule is still non-negotiable:

```text
C++ owns JSI mechanics.
C# owns module logic.
A C ABI with opaque handles connects them.
```

C# must not wrap raw C++ `jsi::Runtime`, `jsi::Value`, `jsi::Object`, or
`jsi::Function` layouts. It receives opaque handles through `expo_jsi_api`.
The native bridge owns the real JSI objects and enforces lifetime, thread, and
exception rules.

## Current Repository State

The implemented core is no longer a docs-only research plan:

- `native/include/expo_jsi.h` defines the production-oriented C ABI function
  table and opaque handles.
- `native/testhost/` builds a Hermes-backed test host used by managed tests.
- `managed/packages/Expo.JSI/` contains the low-level C# wrapper package.
- `managed/packages/Expo.JSI.Tests/` contains the Hermes-backed test suite.
- `experiments/hostfxr-smoke/`, `experiments/nativeaot-smoke/`, and
  `experiments/hermes-console-hostfxr/` preserve standalone loader and proof
  experiments.
- `docs/spike-results/` records completed proof evidence.
- `docs/superpowers/specs/` and `docs/superpowers/plans/` are newer than the
  original agent plan and often contain the most precise implementation intent.

`Expo.JSI` is the current production-oriented low-level wrapper package. It
contains runtime, value, object, array, function, promise, error, scoped-ref,
host-function, and runtime-task APIs over the ABI. It is intentionally below
the module DSL layer.

`Expo.ModulesCore` does not exist yet in this repository. Module dispatch and
conversion tests under `managed/packages/Expo.JSI.Tests/Modules/` are temporary
proofs. Move that behavior to `Expo.ModulesCore.Tests` when the module package
is introduced.

## Reading Order

For future agents:

1. `agent-plan/01-architecture.md`
   Defines the current architecture, implemented boundaries, ownership rules,
   and next direction.
2. `agent-plan/02-research-spikes.md`
   Re-baselines the original spike plan against what has actually landed.
3. `agent-plan/03-implementation-phases.md`
   Describes the current roadmap from the implemented `Expo.JSI` base toward
   `Expo.ModulesCore`, generator work, AOT checks, and adapters.
4. `agent-plan/04-verification-and-stop-gates.md`
   Defines verification commands and decision gates for this repo.
5. `agent-plan/05-repo-strategy.md`
   Explains why development now happens in this repo and how experiments should
   be promoted.

For detailed implementation context, read the relevant newer spec or plan under
`docs/superpowers/` before editing code. For loader or proof evidence, read the
matching result under `docs/spike-results/`.

For background learning material:

1. `learning-guide/01-dotnet-interop-basics.md`
2. `learning-guide/02-jsi-wrapper-model.md`
3. `learning-guide/03-source-generators-and-v2-api.md`
4. `learning-guide/04-platform-adapters-and-views.md`

The learning guides may still use earlier names or simplified examples. When
they conflict with current code, current code plus the newer specs win.

## Scope

Current in-scope work:

- maintaining the `expo_jsi` C ABI and opaque-handle model;
- improving `Expo.JSI` low-level wrappers and ownership semantics;
- extending the Hermes-backed test host and managed test suite;
- keeping temporary generated-looking module proofs clearly temporary;
- designing and then implementing `Expo.ModulesCore` as the higher-level module
  DSL and generated-binding package;
- keeping HostFXR and NativeAOT loader concerns separate from runtime wrapper
  semantics;
- adding thin platform adapters only after the headless core boundary is clear.

Current out-of-scope work unless explicitly requested:

- introducing RNW, WinUI, AppKit, packaging, or host-app dependencies into the
  portable core;
- building the full source generator before the generated-looking shape is
  stable;
- making views part of the first portable module package;
- using runtime reflection, `object?[]`, or JSON as the normal v2 invocation
  path;
- publishing to GitHub, opening PRs, or posting comments without approval.

## Key Current Decisions

`Expo.JSI` stays low-level. It exposes wrapper types over the ABI and should not
grow module DSL concerns.

The module layer belongs in a future `Expo.ModulesCore` package. Generated
bindings should decode `JavaScriptArguments`, call authored C# methods directly,
and encode return values through `Expo.JSI` wrappers.

The ABI has moved toward value handles for ordinary values, objects, arrays, and
functions. Promise capability remains a separate handle. C# still exposes typed
wrappers such as `JavaScriptObject`, `JavaScriptArray`, `JavaScriptFunction`,
and `JavaScriptPromise`.

Scoped refs such as `JavaScriptValueRef`, `JavaScriptObjectRef`, and
`JavaScriptArrayRef` are for temporary inspection inside a runtime execution
frame or host-function callback. Owned wrappers are disposable and may escape
when runtime/thread rules permit.

HostFXR is an experiment and development loader, not the runtime architecture.
`Expo.JSI` should not contain HostFXR-specific code. NativeAOT compatibility is
a continuing constraint on ABI and generated-binding design.

## Verification

Run the canonical Hermes-backed suite after code changes:

```sh
scripts/test-jsi.sh
```

Before finishing code changes, run:

```sh
scripts/format.sh --check --all
```

If formatting fails because files need updates, run:

```sh
scripts/format.sh
scripts/format.sh --check --all
```

Docs-only changes should at least run:

```sh
git diff --check
rg "self[-]contained planning package|planning[ ]artifacts,[ ]not[ ]implementation|expo[-]modules[-]windows[-]core|Phase[ ]1:[ ]clean[ ]separate[ ]research[ ]repo|create[ ]a[ ]clean[ ]local[ ]research[ ]repository" docs/README.md docs/agent-plan
```

Any match should be intentional and explained by context.
