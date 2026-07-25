# AGENTS.md

## Project Rule

This repo is the portable C# / JSI bridge successor to the previous
`expo-modules-windows` prototype.

Core architecture rule:

```text
C++ owns JSI mechanics.
C# owns module logic.
A C ABI with opaque handles connects them.
```

## Maturity

This is production-ready software. Do not plan or run spikes, proofs of
concept, or design-only prototypes anymore; the exploratory phase is over.
Anything planned is planned to ship: complete, polished features only.
No partial features, no temporary shortcuts, and no "simple
hardcoding/shortcut is sufficient for this slice" compromises — if a
shortcut looks necessary, stop and raise it instead of shipping it.

## Before Working

Read:

1. `docs/README.md`
2. `docs/specs/README.md`
3. The specific living spec under `docs/specs/<capability>.md` relevant to the
   task

Use `docs/references/previous-windows-prototype.md` only for historical context.
Use `docs/archive/` only for provenance or old proof evidence; it is not
authoritative over current code, tests, or `docs/specs/`.

For non-trivial behavior changes, use the repo-local living spec workflow:

1. Create or update a delta spec before implementation.
2. Commit the approved delta spec.
3. Create and commit the implementation plan.
4. Implement through Superpowers or an equivalent repo-local plan with focused
   verified commits.
5. Verify with repo-owned commands.
6. Merge accepted deltas into `docs/specs/`.
7. Archive or remove transient planning artifacts.

The repo-local skill is `.agents/skills/living-spec-workflow/SKILL.md`.
This workflow is the project override for Superpowers artifact locations:
delta specs and plans are `docs/changes/<yyyy-mm-dd-slug>/spec.md` and
`docs/changes/<yyyy-mm-dd-slug>/plan.md`, not `docs/superpowers/` artifacts.
Before committing any repo artifact, check that staged content does not contain
local absolute paths, usernames, machine names, private hostnames, concrete
local repo paths, or machine-specific install paths.
Do not create a delta spec as a standalone milestone unless the user explicitly
asks for docs-only design work; a normal delta spec implies plan,
implementation, verification, and merge into `docs/specs/`.
For an approved implementation slice, updating `docs/specs/` is part of the
work before branch handoff. Ask first only if the implementation diverged from
the approved spec or the docs update would introduce new unapproved
requirements.

If `AGENTS.local.md` exists, read it after this file. It is gitignored and may
contain machine-specific paths or local workflow notes. Do not commit it.

## Constraints

- Keep managed core packages and reusable native bridge code portable and
  headless unless the task explicitly asks for platform adapter work.
- Keep package boundaries clear:
  - `packages/expo-modules-dotnet` owns the public Expo adapter, TurboModule
    installer, reusable JSI bridge, managed core packages, and testhost.
  - Authored .NET modules such as `packages/example-module` own module C# code,
    JS facade code, and `expo-module.config.json` autolinking metadata, but not
    React Native installer glue or NativeAOT artifact staging.
  - `apps/*` are runnable example apps; `experiments/*` are narrow smoke proofs.
- Do not introduce RNW, WinUI, AppKit, or host packaging dependencies into the
  managed core or reusable native bridge unless that work is explicitly scoped
  to platform adapter work.
- Do not expose raw `jsi::Runtime`, `jsi::Value`, or `jsi::Object` layouts to C#.
- Prefer .NET built-ins over new ABI surface. The C ABI SHALL carry only what the
  managed runtime cannot know by itself: host identity, host-supplied policy, and
  host-owned handles. Anything portable .NET can compute from inputs it already
  holds — filesystem I/O, HTTP, hashing, culture, time — stays in managed code.
  Any plan or delta spec proposing new ABI surface SHALL name which of those three
  categories the value falls into and why .NET cannot answer it. Normative in
  `### Requirement: ABI Carries Only Host Knowledge` in
  `docs/specs/runtime-and-abi.md`.
- Do not use runtime hot-path reflection for v2 generated bindings.
- Treat HostFXR as a development loader; keep ABI and generated bindings
  NativeAOT-compatible.
- NativeAOT artifact staging SHALL go through
  `packages/expo-modules-dotnet-autolinking`; do not add manual per-app staging
  scripts outside that CLI.
- Do not create GitHub PRs, publish packages, or post comments without explicit user approval.
- Do not commit local absolute paths, usernames, machine names, private hostnames,
  concrete local repo paths, or machine-specific install paths. Use repo-relative
  paths or placeholders such as `<repo>`, `<dotnet-root>`, and
  `<windows-test-machine>` in committed docs and examples.

## Verification

Run the Hermes-backed managed test suite with `scripts/test-managed.sh`.

For workspace/package changes, run `pnpm install --frozen-lockfile` or the
repo-selected pnpm install command. For mobile JavaScript changes, run
`pnpm --filter mobile-app typecheck`.

Before finishing code changes, run `scripts/format.sh --check --all`. If it
fails because files need formatting, run `scripts/format.sh` and then repeat
the check.
