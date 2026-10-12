@AGENTS.md

## Hard rule: the global `improve` skill

This repo's planning rules override the global `improve` skill. Requests to
audit the codebase, find improvements, decide what to build next, or write
implementation plans go to the repo-local `living-spec-workflow` skill
(discover, brief, and reconcile modes).

If `improve` is invoked here anyway, follow it only as far as these rules
allow:

- `docs/roadmap.md` is the only backlog and status tracker. Never create or
  update `plans/README.md`, `docs/plans/README.md`, or `advisor-plans/`.
- Write briefs to `docs/plans/NNN-<slug>.md` with the template in
  `.agents/skills/living-spec-workflow/references/brief-template.md`, and add
  a matching roadmap row.
- Implementation goes through the `living-spec-workflow` deliver mode, not
  `improve execute`.
