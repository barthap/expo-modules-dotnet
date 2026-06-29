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
2. Implement through Superpowers or an equivalent repo-local plan.
3. Verify with repo-owned commands.
4. Merge accepted deltas into `docs/specs/`.
5. Archive or remove transient planning artifacts.

The repo-local skill is `.agents/skills/living-spec-workflow/SKILL.md`.
This workflow is the project override for Superpowers artifact locations:
delta specs and plans are `docs/changes/<yyyy-mm-dd-slug>/spec.md` and
`docs/changes/<yyyy-mm-dd-slug>/plan.md`, not `docs/superpowers/` artifacts.
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

- Keep the portable core headless unless the task explicitly asks for a platform adapter.
- Do not introduce RNW, WinUI, AppKit, or packaging dependencies into the portable core.
- Do not expose raw `jsi::Runtime`, `jsi::Value`, or `jsi::Object` layouts to C#.
- Do not use runtime hot-path reflection for v2 generated bindings.
- Prefer HostFXR for early development, but keep ABI and generated bindings NativeAOT-compatible.
- Do not create GitHub PRs, publish packages, or post comments without explicit user approval.
- Do not commit local absolute paths, usernames, machine names, private hostnames,
  concrete local repo paths, or machine-specific install paths. Use repo-relative
  paths or placeholders such as `<repo>`, `<dotnet-root>`, and
  `<windows-test-machine>` in committed docs and examples.

## Verification

Run the Hermes-backed JSI test suite with `scripts/test-jsi.sh`.

Before finishing code changes, run `scripts/format.sh --check --all`. If it
fails because files need formatting, run `scripts/format.sh` and then repeat
the check.

Each spike must record:

- hypothesis
- commands run
- expected result
- actual result
- artifacts
- ownership/lifetime findings
- scheduler findings
- stop/go decision
