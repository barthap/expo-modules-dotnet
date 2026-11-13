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

- `runtime-and-abi.md`: C ABI contract, opaque handles, function table, native
  ownership boundary, and host-supplied app metadata.
- `managed-jsi-wrappers.md`: Low-level `Expo.JSI` managed wrapper surface.
- `ownership-and-scoped-refs.md`: Owned wrapper and scoped ref lifetime model.
- `host-functions-and-errors.md`: Managed host functions and structured error
  propagation.
- `runtime-scheduling.md`: Runtime task scheduling, sync execution, and headless
  executor behavior.
- `promises.md`: Promise capability and promise-value wrappers.
- `hermes-testhost.md`: Hermes-backed native testhost and managed test suite.
- `modules-core-boundary.md`: `Expo.ModulesCore` package boundary, module
  registry, runtime context, and module test ownership.
- `modules-core-functions-and-properties.md`: Generated sync and async
  functions, `[JS]` properties, module errors, and signature diagnostics.
- `modules-core-records-and-enums.md`: Record and string enum codecs.
- `modules-core-codecs.md`: Typed conversion policy, leaf release protocol,
  binary codecs, and named unions.
- `modules-core-nullable-and-collection-codecs.md`: Nullable reference and
  collection codecs.
- `modules-core-events.md`: Typed events on modules and shared objects.
- `modules-core-shared-objects.md`: Shared-object registry, constructors,
  members, codecs, and release.
- `dotnet-autolinking.md`: .NET Expo module discovery, app-level aggregator
  generation, build, and artifact staging contract.

## Authored modules

Each authored module has its own spec under `modules/`:

- `modules/expo-asset.md`: `packages/expo-asset-dotnet`.
- `modules/expo-constants.md`: `packages/expo-constants-dotnet`.
- `modules/expo-crypto.md`: `packages/expo-crypto-dotnet`.
- `modules/expo-file-system.md`: `packages/expo-file-system-dotnet`.

The public Expo adapter package currently lives at
`packages/expo-modules-dotnet`. It owns the autolinkable React Native package
surface, reusable native bridge code, managed core packages, and testhost used
by these specs. Runnable example apps live under `apps/`; `experiments/` is
reserved for narrow smoke proofs.

## References To Requirements

A requirement name is unique across all specs. Two forms point to a
requirement, and `scripts/check-spec-refs.sh` checks both.

In code, a `@ref` comment gives the spec path and the requirement name on one
line:

```cs
// @ref docs/specs/ownership-and-scoped-refs.md "Boundary Leaf Ownership Is Explicit"
```

The name is optional: a `@ref` with only the path points to the whole spec. Use
the comment syntax of the language. Add a `@ref` where a reader could
"simplify" the code and break the requirement, and on code that looks wrong
without the reason. Do not add one to code that is plain to read. A `@ref` says
nothing else: put an explanation in an ordinary comment next to it.

In prose, cite the heading in backticks. The spec file can follow in
parentheses or after "in":

```markdown
See `### Requirement: Opaque Handle Boundary` (`runtime-and-abi.md`).
```

The check fails when:

- a `@ref` does not have this form, names a file that is not a spec, or names a
  requirement that the spec does not have;
- a prose citation names a requirement that no spec has, or a spec that does
  not have it;
- a `docs/specs/` path in any tracked file does not exist;
- two requirements have the same name.

## Where A Requirement Goes

Put a new requirement in the spec for its capability, not in the spec that was
open. Create a new spec file when no spec fits, and add it to the list above. A
spec for an authored module goes under `modules/`. Split a spec when it passes
about 800 lines and its parts change independently.

## Change Workflow

Future feature work should produce a delta spec first. After implementation and
verification, merge the accepted delta into these living specs and archive or
remove transient planning artifacts. See the repo-local
`living-spec-workflow` skill under `.agents/skills/`.
