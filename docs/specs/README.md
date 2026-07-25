# Living Specs

This directory is the authoritative current-state specification for the
portable C# / JSI bridge. It is generated from and maintained against current
code, tests, and accepted implementation decisions.

Source priority when specs and other materials disagree:

1. Current implementation and ABI.
2. Current tests.
3. Current living specs.
4. Archived specs, plans, and spike results.
5. Learning guides and historical references.

## Cross-cutting rules

Read these before designing any capability, because they constrain every spec
below:

- **Prefer .NET built-ins over new ABI surface.** The ABI carries only host
  identity, host-supplied policy, and host-owned handles. Filesystem I/O, HTTP,
  hashing, culture, and time belong in managed code. Normative in
  `### Requirement: ABI Carries Only Host Knowledge`
  (`runtime-and-abi.md`), and repeated as a constraint in `AGENTS.md`.
- **Opaque handles only.** Raw `jsi::Runtime`, `jsi::Value`, and `jsi::Object`
  layouts never reach C# — `### Requirement: Opaque Handle Boundary`
  (`runtime-and-abi.md`).

## Capabilities

- `runtime-and-abi.md`: C ABI contract, opaque handles, function table, and native
  ownership boundary.
- `managed-jsi-wrappers.md`: Low-level `Expo.JSI` managed wrapper surface.
- `ownership-and-scoped-refs.md`: Owned wrapper and scoped ref lifetime model.
- `host-functions-and-errors.md`: Managed host functions and structured error
  propagation.
- `runtime-scheduling.md`: Runtime task scheduling, sync execution, and headless
  executor behavior.
- `promises.md`: Promise capability and promise-value wrappers.
- `hermes-testhost.md`: Hermes-backed native testhost and managed test suite.
- `modules-core-boundary.md`: `Expo.ModulesCore` package boundary,
  generated-binding helpers, and module test ownership.
- `dotnet-autolinking.md`: .NET Expo module discovery, app-level aggregator
  generation, build, and artifact staging contract.

The public Expo adapter package currently lives at
`packages/expo-modules-dotnet`. It owns the autolinkable React Native package
surface, reusable native bridge code, managed core packages, and testhost used
by these specs. Runnable example apps live under `apps/`; `experiments/` is
reserved for narrow smoke proofs.

## Change Workflow

Future feature work should produce a delta spec first. After implementation and
verification, merge the accepted delta into these living specs and archive or
remove transient planning artifacts. See the repo-local
`living-spec-workflow` skill under `.agents/skills/`.
