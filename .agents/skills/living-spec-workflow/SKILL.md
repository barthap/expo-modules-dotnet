---
name: living-spec-workflow
description: "Use for repo-local spec-driven work in expo-modules-csharp: creating or updating living specs, writing delta specs, planning implementation, validating code against specs, archiving old docs, or maintaining the Superpowers/OpenSpec-style workflow for the portable C# / JSI bridge. Applies to ABI changes, Expo.JSI wrapper semantics, ownership/scoped refs, host functions, promises, scheduler/runtime behavior, Hermes testhost work, Expo.ModulesCore planning, NativeAOT/HostFXR proofs, and docs/spec archival."
---

# Living Spec Workflow

Use this skill to keep code, tests, and docs/specs aligned. It is the repo-local
wrapper around the normal Superpowers flow:

```text
brainstorm feature -> write delta spec -> write plan -> implement -> verify ->
merge accepted delta into docs/specs -> archive/remove transient artifacts
```

It adapts OpenSpec/Superspec ideas to this repo without requiring upstream
OpenSpec or Superspec commands.

## Dependencies

- Required: this repo's `AGENTS.md` rules and `docs/specs/`.
- Expected for normal feature work: global Superpowers skills. Use them for
  brainstorming, plan writing, TDD, code review, and verification when
  available. When they are unavailable, follow the equivalent workflow
  manually and say that the dependency was unavailable.
- Optional: OpenSpec/Superspec CLI tooling. Do not require it for ordinary repo
  work unless the user explicitly asks.

Do not vendor the one-off OpenSpec generate-specs skill into this repo. It was
used only to shape the initial living-spec migration.

## Source Priority

When sources conflict, prefer:

1. Current code and ABI.
2. Current tests.
3. Current living specs in `docs/specs/`.
4. Archived specs, plans, and spike results under `docs/archive/`.
5. Learning guides and historical references.

## Request Classification

Classify the task before writing:

- Current-state audit: update living specs from code/tests/docs.
- Proposed behavior change: create a delta spec before code.
- Spike/proof: record hypothesis, commands, expected/actual results,
  artifacts, ownership/lifetime findings, scheduler findings, and stop/go
  decision.
- Docs/archive maintenance: update front-door docs and archive stale artifacts.

Ask only when capability scope is ambiguous or when multiple plausible package
boundaries exist.

## Delta Spec Workflow

For non-trivial behavior changes, run this sequence:

1. Read `AGENTS.md`, `AGENTS.local.md` if present, `docs/specs/README.md`, and
   the relevant `docs/specs/<capability>.md`.
2. Use Superpowers brainstorming to refine the feature unless the user
   explicitly says the design is already approved.
3. Write the approved design as `docs/changes/<yyyy-mm-dd-slug>/spec.md`, not
   under `docs/superpowers/`. This is the normal Superpowers design spec, but
   framed as a delta against the current living specs.
4. Use Superpowers writing-plans after the delta spec is approved. Save the
   plan as `docs/changes/<yyyy-mm-dd-slug>/plan.md` unless the user asks for a
   different location.
5. Implement through TDD or the closest practical equivalent for the change.
6. Verify with repo-owned commands.
7. Merge accepted deltas into `docs/specs/`.
8. Archive or remove transient planning artifacts. Plans are not durable
   current-state docs after implementation.

For an approved implementation slice, merging the accepted delta into
`docs/specs/` is part of the work, not a separate branch-finishing choice. Do it
before final handoff so code, tests, and living specs close together. Stop and
ask only if the implementation materially diverged from the approved spec, if
the living-spec update would add new unapproved requirements, or if the user
asked for docs-only design work.

Do not offer "write only the delta spec" as a normal milestone option. A
`docs/changes/<yyyy-mm-dd-slug>/spec.md` file is the first artifact in an
implementation slice, not a parking lot for architecture thoughts. Create one
only when the next intended steps are `plan.md`, implementation, verification,
and merge into `docs/specs/`, unless the user explicitly asks for docs-only
design work. If implementation is not intended yet, stay in brainstorming and
summarize decisions in the conversation instead of creating transient repo
artifacts.

The change directory layout is:

```text
docs/changes/<yyyy-mm-dd-slug>/
  spec.md
  plan.md
```

Artifact meanings:

- `spec.md`: the Superpowers design artifact. It SHOULD include goal, scope,
  accepted design, and delta requirements/scenarios against `docs/specs/*.md`.
  Use `ADDED`, `MODIFIED`, and `REMOVED` headings when they make the delta
  easier to merge, but do not create separate OpenSpec proposal/task files.
- `plan.md`: the Superpowers implementation plan. It is transient and MUST be
  removed or archived after the accepted delta is merged into `docs/specs/`.

## Superpowers Integration Rules

Use Superpowers normally, with these repo overrides:

- If brainstorming says to write the design under `docs/superpowers/specs/`,
  write it as `docs/changes/<yyyy-mm-dd-slug>/spec.md` instead.
- If writing-plans says to save plans under `docs/superpowers/plans/`, save the
  plan as `docs/changes/<yyyy-mm-dd-slug>/plan.md` instead.
- If a Superpowers skill says to commit, push, create a worktree, or open a PR,
  follow `AGENTS.md` and user instructions instead. In this repo, do not use
  worktrees, pushes, PRs, comments, or publishing without explicit approval.
- Preserve Superpowers review gates: get user approval for the delta spec before
  planning, and verify before claiming completion.
- After implementation, do not leave the change spec as the only source of
  truth. Merge accepted requirements into `docs/specs/`.
- Invoke branch-finishing choices only after implementation, verification,
  living-spec merge, and transient artifact cleanup are complete. Branch
  disposition is a user choice; living-spec sync for accepted behavior is not.

## Living Spec Format

Write current specs under `docs/specs/<capability>.md`:

```markdown
# Capability Name

## Purpose

...

## Requirements

### Requirement: Name

The system SHALL/SHOULD/MAY ...

#### Scenario: Name
- **GIVEN** ...
- **WHEN** ...
- **THEN** ...
```

Use RFC 2119 keywords for requirement strength. Keep each requirement atomic.
Ground requirements in code, tests, or accepted design. Do not invent
aspirational `SHALL`s unless the artifact is explicitly a future-facing delta.

## Repo Boundaries

- Keep the portable core headless unless the task explicitly asks for an
  adapter.
- Do not introduce RNW, WinUI, AppKit, packaging, or platform adapter
  dependencies into the portable core.
- C++ owns JSI mechanics. C# owns module logic. A C ABI with opaque handles
  connects them.
- Do not expose raw JSI layouts to C#.
- Do not accept runtime hot-path reflection, `object?[]`, JSON, or dynamic
  invocation as the normal v2 generated-binding path.
- Keep module behavior proof code temporary under `Expo.JSI.Tests/Modules`
  until `Expo.ModulesCore` and `Expo.ModulesCore.Tests` exist.

## Git And Publishing Rules

- Traditional git branches are allowed when the user asks.
- Do not use git worktrees unless the user explicitly asks.
- Do not push, open PRs, publish packages, post GitHub comments, or submit
  reviews without explicit approval.
- Do not commit local absolute paths, usernames, machine names, private
  hostnames, or machine-specific install paths.

## Verification

For docs/spec-only changes:

```sh
git diff --check
rg "self[-]contained planning package|planning[ ]artifacts,[ ]not[ ]implementation|expo[-]modules[-]windows[-]core|Phase[ ]1:[ ]clean[ ]separate[ ]research[ ]repo|create[ ]a[ ]clean[ ]local[ ]research[ ]repository" docs/README.md docs/specs docs/roadmap.md AGENTS.md .agents/skills
```

For code changes:

```sh
scripts/test-jsi.sh
scripts/format.sh --check --all
git diff --check
```

For module-layer work, also check the v2 hot path:

```sh
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" managed/packages
```

Never claim completion without fresh verification evidence.
