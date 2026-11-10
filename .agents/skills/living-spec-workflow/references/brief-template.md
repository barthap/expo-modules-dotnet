# Brief Template

Adapted from the `improve` skill's handoff plan template by shadcn (MIT).

A brief says what to build and why, with enough evidence that a fresh agent can
start deliver mode without this session. It is not an implementation plan:
deliver mode brainstorms the design, writes the delta spec, and writes the
step-by-step local plan. Do not write ordered implementation steps in a brief.

A brief is the `Agent brief` child page of a Notion Roadmap row. The row
title is the brief's title, so the brief has no title line, and its headings
start at `###`. The row page body holds a short summary for the owner,
written from the brief (see "Row summary" below). See [notion.md](notion.md)
for the page layout.

## Template

Row title: an imperative phrase that says what will be true after this
lands. Set Priority, Effort, Area, Milestone, and "Depends on" as row
properties too.

```markdown
**Drift check (run first)**: `git diff --stat <planned-at SHA>..HEAD -- <in-scope paths>`
If an in-scope file changed, compare "Current state" with the live code.
A mismatch is a STOP condition.

### Status

- **Priority**: P1 | P2 | P3
- **Effort**: S | M | L
- **Risk**: LOW | MED | HIGH, with one line why
- **Depends on**: `EMC-<n>` IDs, or "none"
- **Category**: bug | security | perf | tests | tech-debt | migration | dx | docs | direction | authored module
- **Planned at**: `<short SHA>`, <YYYY-MM-DD>

### Why this matters

2–5 sentences: the problem, its concrete cost, and what improves.

### Current state

- Relevant files, one line each on their role.
- Short excerpts with `file:line` markers.
- The living specs and requirements this touches, quoted where they constrain
  the work.
- One exemplar file for the convention to follow.

### Constraints

- Package boundaries and architecture rules from `AGENTS.md` that apply.
- If the work may add ABI surface: name the value, which category of
  `### Requirement: ABI Carries Only Host Knowledge` it falls into (host
  identity, host-supplied policy, or host-owned handle), and why .NET cannot
  compute it. A value that fails this test is a STOP condition.

### Scope

**In scope**: packages, files, or behaviors.

**Out of scope**: related-looking work that must not be touched, each with a
reason.

### Open design questions

Decisions deliver mode must settle during brainstorming, each with the
advisor's recommended answer and the evidence for it.

### Done criteria

Machine-checkable. All must hold:

- [ ] `scripts/test-managed.sh` passes with no skips; new tests cover <X>
- [ ] `scripts/format.sh --check --all` passes
- [ ] <item-specific command> → <expected result>
- [ ] Accepted behavior is merged into `docs/specs/<capability>.md`
- [ ] The PR is open and the row is `In review`

### STOP conditions

Stop and report instead of improvising if:

- Current state does not match the excerpts.
- The work needs an out-of-scope change.
- <plan-specific assumption> turns out to be false.

### Maintenance notes

What later work interacts with this, what a reviewer should check, and any
follow-up deliberately deferred (with reason). Deferred follow-ups also
become new rows (`Idea`, or `Backlog` with a brief) that depend on this one.
```

## Quality Bar

- Could an agent that never saw this session start deliver mode from the brief
  and the repo alone?
- Is every done criterion a command with an expected result?
- Are files and symbols named exactly?
- Are the STOP conditions specific to this item's real risks?
- Do "Why this matters" and "Done criteria" alone tell a reviewer what they
  approve?
- Is the planned-at SHA filled in, and do the drift-check paths match Scope?
- No secret values, local absolute paths, usernames, or machine names.
- Does `summary.md` tell the owner what, why, and what is open, with no file
  references?

## Row summary

The brief is for agents. The owner reads the row summary instead, so write
`summary.md` next to `brief.md` for every brief. `push-brief` and `create`
put it under `## Summary` on the row page. The reader knows the project and
C#, but wants to know in about one minute what this is, why it matters, and
what is still open.

Use these bold labels. Leave out a label that has no content.

```markdown
**What:** 1-2 sentences. What is true after this item ships.

**Why:** 1-3 sentences. The problem today and its cost.

**What changes:**

- 2-5 visible changes, for module authors, app developers, or the repo.

**Not included:**

- 1-3 items, each with a short reason.

**Open questions:**

- One per open design question, with the recommended answer in a few words.

**Done when:** One sentence on the evidence (which tests, which docs).

**Depends on / risk:** One line, only for a dependency or a MED/HIGH risk.
```

For an `Idea` row, the summary is 1-4 plain sentences with no labels.

Rules:

- At most 300 words. One idea per sentence, active voice, plain words.
- Use only facts from the brief. Add no new scope or evidence.
- No `file:line` references, file paths, git SHAs, "drift check", "STOP
  condition", "planned at", diagnostic numbers, or instructions to an agent.
- Backticks only for names a module author types or sees, such as an
  attribute (`[Constant]`), a type, or a package name.
- Write "upstream Android" and "upstream iOS" in words. Write other rows as
  the ID plus a few words (`EMC-30`, TypeScript generation).

Check before pushing (must print nothing):

```sh
grep -nE '\.(cs|md|kt|swift|ts|tsx|json|sh|cpp|h):?[0-9]*|/[A-Za-z]+/|STOP|[Dd]rift|[Pp]lanned at|\b[0-9a-f]{7}\b' summary.md
```
