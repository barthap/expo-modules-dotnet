# Brief Template

Adapted from the `improve` skill's handoff plan template by shadcn (MIT).

A brief says what to build and why, with enough evidence that a fresh agent can
start deliver mode without this session. It is not an implementation plan:
deliver mode brainstorms the design, writes the delta spec, and writes the
step-by-step local plan. Do not write ordered implementation steps in a brief.

A brief is the `## Brief` section of a Notion Roadmap row. The row title is
the brief's title, so the brief has no title line, and its headings start at
`###`. See [notion.md](notion.md) for the page layout.

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
