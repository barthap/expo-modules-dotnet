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
- `modules-core-boundary.md`: Current module-layer boundary and temporary proof
  placement.

## Change Workflow

Future feature work should produce a delta spec first. After implementation and
verification, merge the accepted delta into these living specs and archive or
remove transient planning artifacts. See the repo-local
`living-spec-workflow` skill under `.agents/skills/`.
