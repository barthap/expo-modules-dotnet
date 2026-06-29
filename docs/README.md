# Portable C# / JSI Bridge Docs

Last refreshed: 2026-06-29.

This directory contains the current documentation for the portable C# / JSI
bridge. The authoritative current-state specs live in `docs/specs/`. Historical
plans, spike notes, and one-slice Superpowers artifacts live in `docs/archive/`.

The architecture rule is non-negotiable:

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

- `native/include/expo_jsi.h` defines the C ABI function table and opaque
  handles.
- `native/testhost/` builds the Hermes-backed testhost used by managed tests.
- `managed/packages/Expo.JSI/` contains the low-level C# wrapper package.
- `managed/packages/Expo.JSI.Tests/` contains the Hermes-backed test suite.
- `experiments/hostfxr-smoke/`, `experiments/nativeaot-smoke/`, and
  `experiments/hermes-console-hostfxr/` preserve standalone loader and proof
  experiments.
- `docs/specs/` contains the living spec baseline.
- `docs/archive/` contains historical planning, spike, and execution artifacts.

`Expo.JSI` is the current low-level wrapper package. It contains runtime,
value, object, array, function, promise, error, scoped-ref, host-function, and
runtime-task APIs over the ABI. It is intentionally below the module DSL layer.

`Expo.ModulesCore` does not exist yet. Module dispatch and conversion tests
under `managed/packages/Expo.JSI.Tests/Modules/` are temporary proofs. Move that
behavior to `Expo.ModulesCore.Tests` when the module package is introduced.

## Reading Order

For current implementation work:

1. `docs/specs/README.md`
2. The specific capability spec under `docs/specs/<capability>.md`
3. `docs/ownership-mental-model.md` for wrapper/ref lifetime work
4. `docs/roadmap.md` for forward direction
5. `docs/archive/` only when historical rationale or proof evidence is needed

For background learning material:

1. `docs/learning-guide/01-dotnet-interop-basics.md`
2. `docs/learning-guide/02-jsi-wrapper-model.md`
3. `docs/learning-guide/03-source-generators-and-v2-api.md`
4. `docs/learning-guide/04-platform-adapters-and-views.md`

The learning guides are educational. When they conflict with current code,
tests, or `docs/specs/`, the current implementation and living specs win.

## Spec Workflow

This repo uses a living-spec workflow:

1. Brainstorm the feature through Superpowers or the equivalent manual flow.
2. Write the approved design as `docs/changes/<yyyy-mm-dd-slug>/spec.md`.
3. Write the implementation plan as `docs/changes/<yyyy-mm-dd-slug>/plan.md`.
4. Implement and verify behavior with repo-owned commands.
5. Merge accepted deltas into `docs/specs/`.
6. Archive or remove transient planning artifacts.

A change spec is for a milestone that is ready to continue into planning and
implementation. For pure exploration, keep decisions in the conversation or
promote them directly into durable docs only when that is the actual docs task.
In the normal workflow, a delta spec implies the plan, implementation,
verification, and living-spec merge will follow.

For an implemented milestone, updating `docs/specs/` is part of the work before
branch handoff. Merge, PR, keep-as-is, and discard decisions happen after the
code, tests, and living specs already reflect the accepted behavior.

Use the repo-local `living-spec-workflow` skill in `.agents/skills/` for the
exact workflow and guardrails.

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
rg "self[-]contained planning package|planning[ ]artifacts,[ ]not[ ]implementation|expo[-]modules[-]windows[-]core|Phase[ ]1:[ ]clean[ ]separate[ ]research[ ]repo|create[ ]a[ ]clean[ ]local[ ]research[ ]repository" docs/README.md docs/specs docs/roadmap.md AGENTS.md .agents/skills
```

Any match should be intentional and explained by context.
