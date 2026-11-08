@AGENTS.md

## Hard rule: the global `improve` skill

This repo's planning rules override the global `improve` skill. Requests to
audit the codebase, find improvements, decide what to build next, or write
implementation plans go to the repo-local `living-spec-workflow` skill
(discover, brief, and reconcile modes).

If `improve` is invoked here anyway, follow it only as far as these rules
allow:

- The Notion Roadmap database is the only backlog and status tracker. Never
  create tracking files in git, such as `plans/README.md` or `advisor-plans/`.
- Write a brief as a Notion Roadmap row through `living-spec-workflow`, with
  the template in
  `.agents/skills/living-spec-workflow/references/brief-template.md`.
- Implementation goes through the `living-spec-workflow` deliver mode, not
  `improve execute`.
