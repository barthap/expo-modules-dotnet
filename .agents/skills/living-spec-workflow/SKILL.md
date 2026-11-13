---
name: living-spec-workflow
description: "Use for repo-local planning and spec-driven work in expo-modules-csharp: auditing the codebase, finding improvements, deciding what to build next, writing roadmap briefs, reconciling the Notion Roadmap, implementing a roadmap item (EMC-<n>), creating or updating living specs, writing delta specs, planning implementation, validating code against specs, or maintaining the Superpowers/OpenSpec-style workflow for the portable C# / JSI bridge. Applies to ABI changes, Expo.JSI wrapper semantics, ownership/scoped refs, host functions, promises, scheduler/runtime behavior, Hermes testhost work, Expo.ModulesCore planning, and NativeAOT/HostFXR work."
---

# Living Spec Workflow

Use this skill to plan work and keep code, tests, and docs/specs aligned. It
owns the whole chain, from finding work to closing it:

```text
discover/brief -> Notion row + brief -> brainstorm -> delta spec (Notion) ->
Spec approved -> plan (local) -> implement -> verify -> merge into docs/specs
-> PR (In review) -> merged (Done)
```

The delivery half is the repo-local wrapper around the normal Superpowers flow.

It adapts OpenSpec/Superspec ideas to this repo without requiring upstream
OpenSpec or Superspec commands.

## Dependencies

- Required: this repo's `AGENTS.md` rules and `docs/specs/`.
- Required for tracking: the Notion MCP and the "Notion tracking" section of
  the git-ignored `AGENTS.local.md`. Read
  [references/notion.md](references/notion.md) before any mode that reads or
  writes roadmap items. If Notion is not connected, stop and tell the owner.
- Expected for normal feature work: global Superpowers skills. Use them for
  brainstorming, plan writing, TDD, code review, and verification when
  available. When they are unavailable, follow the equivalent workflow
  manually and say that the dependency was unavailable.
- Optional: OpenSpec/Superspec CLI tooling. Do not require it for ordinary repo
  work unless the user explicitly asks.

Do not vendor the one-off OpenSpec generate-specs skill into this repo. It was
used only to shape the initial living-spec migration.

## Roadmap And Briefs

The Notion Roadmap database is the only backlog and status tracker. Do not
track items in git: no status tables, no brief files, no `plans/README.md`.
The committed `docs/roadmap.md` is a short public page (direction, shipped,
planned) with no IDs or statuses.

- Every planned item is one Notion row, `EMC-<n>`. The row page body has a
  short `## Summary` for the owner. The brief is the row's `Agent brief`
  child page, and the delta spec its `Delta spec` child page.
- Ideas without a brief are rows with Status `Idea`.
- A brief says what and why. The step-by-step plan is the local, git-ignored
  `docs/work/EMC-<n>-<slug>/plan.md`, written in deliver mode.
- Use the `notion-tracker` agent for large reads and writes, so page content
  stays out of the main context.

## Modes

Pick the mode from the request:

- **Discover**: "audit", "what next", "find improvements", optionally with a
  focus or `quick`/`deep`. Read-only on source; writes Notion rows.
- **Brief**: "brief <description>", or "plan X" when X has no row. Writes one
  row with its brief.
- **Reconcile**: refresh the Notion rows against code, specs, branches, and
  PRs, and refresh the public `docs/roadmap.md` lists.
- **Deliver**: "implement X", "start EMC-<n>", "work on X from the roadmap", or
  any behavior change. Runs the delta spec workflow below.

For discover, brief, and reconcile, read
[references/discover.md](references/discover.md) first. It links the audit
playbook and the brief template.

If the request is to implement something with no row, ask whether to write a
brief first. A small, well-scoped change can go straight to deliver mode;
create its row (Status `In progress`) when you start.

## Source Priority

When sources conflict, prefer:

1. Current code and ABI.
2. Current tests.
3. Current living specs in `docs/specs/`.
4. Closed specs, plans, and spike results: Notion Done rows and git history
   (older local copies may sit in the git-ignored `docs/archive/`).
5. Learning guides and historical references.

## Request Classification

Classify the task before writing:

- Improvement audit or direction: discover mode.
- Current-state audit: update living specs from code/tests/docs.
- Proposed behavior change: create a delta spec before code.
- Docs maintenance: update front-door docs and remove stale ones.

Ask only when capability scope is ambiguous or when multiple plausible package
boundaries exist.

## Delta Spec Workflow

For non-trivial behavior changes, run this sequence. `<ID>` is `EMC-<n>` and
`<dir>` is `docs/work/EMC-<n>-<slug>/`.

1. **Start.** Read `AGENTS.md`, `AGENTS.local.md`, `docs/specs/README.md`,
   and the relevant `docs/specs/<capability>.md`. Run `notion-tracker`
   `pull <ID>`, then read `<dir>brief.md`. Run the brief's drift check if it
   has one. A row with no brief (a small change started directly in deliver
   mode) skips the pull and the drift check. Treat the brief's open design
   questions as brainstorming input, not as approved decisions.
2. **Branch.** Work on `impl/EMC-<n>-<slug>`, created from `main`. Do not use
   worktrees unless explicitly asked. Run `notion-tracker`
   `set <ID> "Status=In progress" "Branch=impl/EMC-<n>-<slug>"`.
3. **Brainstorm.** Use Superpowers brainstorming to refine the feature unless
   the user explicitly says the design is already approved.
4. **Delta spec.** Write the approved design as `<dir>spec.md`, framed as a
   delta against the current living specs. Use no title line, start the top
   headings at `###`, and do not hard-wrap paragraphs (see
   [references/notion.md](references/notion.md)). Run `notion-tracker`
   `push-spec <ID> <dir>spec.md`. The delta spec is never committed.
5. **Independent review.** Run this step when the delta spec changes the C
   ABI, handle ownership or lifetime, or runtime scheduling, or when the owner
   asks for it. Other delta specs skip it. See "Independent Delta Spec
   Review" below. Push the delta spec again if the review changed it.
6. **Approval gate.** The owner approves in chat or ticks "Spec approved" in
   Notion. On approval in chat, run `set <ID> "Spec approved=__YES__"`. Do not
   plan while it is unticked. If the delta spec later changes in a material
   way, untick it and ask again.
7. **Refresh.** Run `pull <ID>` again before planning. The owner may have
   edited the page. Notion wins on any difference.
8. **Plan.** Use Superpowers writing-plans and save the plan as
   `<dir>plan.md`. The plan stays local; never commit it or copy it to
   Notion.
9. **Implement.** Use TDD or the closest practical equivalent for the change.
   Commit in plan/task slices as work becomes verified instead of waiting to
   split the entire tree retroactively.
10. **Verify** with repo-owned commands.
11. **Merge** accepted deltas into `docs/specs/` on the same branch. The PR
    diff of `docs/specs/` is how reviewers read the delta. Keep the reasons
    too, see "Reasons In Living Specs" below.
12. **PR.** After the owner approves the push and the PR, run
    `set <ID> "Status=In review" "PR=<url>"`. Add deferred follow-ups as new rows
    (`Idea`, or `Backlog` with a brief) that depend on this one.
13. **Done.** The owner merges on GitHub. Reconcile mode, or the owner, sets
    Status to `Done`. The local `<dir>` can be deleted after that.

Before any commit, check staged content for local absolute paths, usernames,
machine names, private hostnames, machine-specific install paths, and Notion
IDs or URLs.

For an approved implementation slice, merging the accepted delta into
`docs/specs/` is part of the work, not a separate branch-finishing choice. Do it
before final handoff so code, tests, and living specs close together. Stop and
ask only if the implementation materially diverged from the approved spec, if
the living-spec update would add new unapproved requirements, or if the user
asked for docs-only design work.

Do not offer "write only the delta spec" as a normal milestone option. A delta
spec is the first artifact in an implementation slice, not a parking lot for
architecture thoughts. Write one only when the next intended steps are the
plan, implementation, verification, and merge into `docs/specs/`, unless the
user explicitly asks for docs-only design work. If implementation is not
intended yet, stay in brainstorming and summarize decisions in the
conversation.

Artifact meanings:

- Delta spec (`<dir>spec.md`, Notion `Delta spec` child page): the Superpowers
  design artifact. It SHOULD include goal, scope, accepted design, and delta
  requirements/scenarios against `docs/specs/*.md`. Use `ADDED`, `MODIFIED`,
  and `REMOVED` headings when they make the delta easier to merge, but do not
  create separate OpenSpec proposal/task files.
- Plan (`<dir>plan.md`): the Superpowers implementation plan. Local and
  transient. The Superpowers SDD `review-package` script takes this path.

## Independent Delta Spec Review

The session that wrote a delta spec is not an independent reviewer of it.

- Dispatch one fresh-context subagent. Give it the path of `<dir>spec.md` and
  the paths of the living specs the delta changes. Give it nothing else from
  this session: no summary, no conclusions.
- Ask for unresolved problems in correctness, coherence, feasibility, and
  missing cases, each with the section it is about. The last line of the
  review is `READY` or `NOT READY`.
- Cross-model review: when Codex is enabled in the user's global agent
  instructions, also get a review from Codex with the same input and the same
  prompt. Neither reviewer sees the other's review. When Codex is disabled,
  skip this and say that only one model family reviewed.
- Fix the findings you agree with. Report every finding to the owner with what
  you did about it, including the ones you did not act on.
- Reviewers advise and the owner decides. A `READY` line does not tick "Spec
  approved".
- Never report a review that did not run, and never count a failed or partial
  response as a review.

## Reasons In Living Specs

The delta spec and the brief stay in Notion, so `docs/specs/` is the only
place a later reader finds why a requirement is the way it is.

- For each decision in the delta spec that a later reader could undo or
  "simplify", write one or two sentences of reason next to the requirement it
  explains.
- Where a rejected option looks attractive, name it and say why it lost.
- Write reasons as current state: no item IDs, no change history, no "we
  decided".
- Everything else from the delta spec stays in Notion.

## Superpowers Integration Rules

Use Superpowers normally, with these repo overrides:

- If brainstorming says to write the design under `docs/superpowers/specs/`,
  write it as `docs/work/EMC-<n>-<slug>/spec.md` and push it to Notion
  instead. Do not commit it.
- If writing-plans says to save plans under `docs/superpowers/plans/`, save the
  plan as `docs/work/EMC-<n>-<slug>/plan.md` instead. Do not commit it.
- If a Superpowers skill says to commit, push, create a worktree, or open a PR,
  follow `AGENTS.md` and user instructions instead. In this repo, do not use
  worktrees, pushes, PRs, comments, or publishing without explicit approval.
- Preserve Superpowers review gates: get user approval for the delta spec before
  planning (the "Spec approved" checkbox), and verify before claiming
  completion.- After implementation, do not leave the delta spec as the only source of
  truth. Merge accepted requirements into `docs/specs/`.
- Invoke branch-finishing choices only after implementation, verification,
  and living-spec merge are complete. Branch disposition is a user choice;
  living-spec sync for accepted behavior is not.

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
scripts/test-managed.sh
scripts/format.sh --check --all
git diff --check
```

For module-layer work, also check the v2 hot path:

```sh
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" packages/expo-modules-dotnet/managed/packages
```

The only expected matches are assertions in `Expo.ModulesCore.Generator.Tests`
that generated code does not contain these patterns.

Never claim completion without fresh verification evidence.
