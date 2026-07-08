# Plan 005: Mark Events/EventEmitter complete in the roadmap, add an autolinking CLI README, add platform table to root README

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving to the
> next step. If anything in the "STOP conditions" section occurs, stop and
> report — do not improvise. When done, update the status row for this plan
> in `docs/plans/README.md` — unless a reviewer dispatched you and told you they
> maintain the index.
>
> **Drift check (run first)**: `git diff --stat 0f6fc760..HEAD -- docs/roadmap.md packages/expo-modules-dotnet-autolinking/`
> If any in-scope file changed since this plan was written, compare the
> "Current state" excerpts against the live code before proceeding; on a
> mismatch, treat it as a STOP condition.

## Status

- **Priority**: P3
- **Effort**: S
- **Risk**: LOW
- **Depends on**: none
- **Category**: docs
- **Planned at**: commit `0f6fc760`, 2026-07-08

## Why this matters

Two concrete doc gaps. (1) `docs/roadmap.md` still lists "Events /
EventEmitter" as pending while sibling items carry "(complete)" markers — but
the feature shipped (`Module.SendEventAsync` exists and `example-module` uses
`[Events]`). A stale roadmap misdirects planning in a repo whose workflow
leans on living docs. (2) `packages/expo-modules-dotnet-autolinking` — the
mandated path for all NativeAOT artifact staging — has no README; the only
orientation is a dense 200+ line living spec.

## Current state

- `docs/roadmap.md` around lines 100–115 (P2 Interactive Module Capabilities):

  ```markdown
  2. **Async module methods / promises** (complete)
      - Generate promise-returning bindings for `Task` / `Task<T>` methods after
        cross-host scheduler semantics are known.
  3. **Events / EventEmitter**
      - Build module-to-JS event emission on top of function calling, async
        scheduling, and lifecycle-safe teardown.
  ```

  Items 1 and 2 in that list end with "(complete)"; item 3 does not.
- Implementation evidence (verify before editing):
  - `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Module.cs:32`
    — `protected Task SendEventAsync(string eventName, ...)` and a generic
    `SendEventAsync<TCodec, T>` overload at line 38.
  - `packages/example-module/dotnet/ExampleModule/ExampleMathModule.cs` — uses
    the `[Events("onStatus")]` attribute and calls `SendEventAsync`.
- Roadmap house style: read the whole file first; completed items either gain
  "(complete)" plus sub-bullets describing what shipped (see how items 1–2 and
  the "HostObject / NativeState" entry phrase completion) — match that style.
- `packages/expo-modules-dotnet-autolinking/` — no `README.md`. Facts for the
  README (verify against the sources listed):
  - `package.json`: name `expo-modules-dotnet-autolinking`, description
    "Autolinking tool for .NET-backed Expo modules", bin
    `expo-modules-dotnet-autolinking`, scripts `build`/`test`/`typecheck`.
  - Commands (from `src/commands/`): `generate`, `resolve`, `build`, `stage`,
    `link` — read each `register*Command` for its one-line purpose and options.
  - Normative contract: `docs/specs/dotnet-autolinking.md` — the README links
    to it rather than duplicating it.
- Repo constraint (`AGENTS.md`): committed docs must not contain local absolute
  paths, usernames, machine names, or machine-specific install paths — use
  repo-relative paths or placeholders like `<repo>`.

## Commands you will need

| Purpose | Command | Expected on success |
|---|---|---|
| Whitespace check | `git diff --check` | clean |
| Docs regression greps | `rg "self[-]contained planning package|planning[ ]artifacts,[ ]not[ ]implementation|expo[-]modules[-]windows[-]core" docs/README.md docs/specs docs/roadmap.md AGENTS.md` | no unexplained matches |
| Format | `scripts/format.sh --check --all` | exit 0 (prettier covers md if configured) |

(The rg command comes from `docs/README.md`'s docs-only change checklist.)

## Scope

**In scope** (create/modify only):
- `docs/roadmap.md` — the Events/EventEmitter entry only
- `packages/expo-modules-dotnet-autolinking/README.md` (create)
- Root `README.md` — add the platform support table (Step 3 content is
  operator-provided, 2026-07-08; do not invent additional rows)
- `docs/plans/README.md` (status row)

**Out of scope** (do NOT touch):
- Other roadmap entries (ArrayBuffer, SharedObject, Lazy init, etc.) — their
  status is the operator's call, not this plan's.
- `docs/specs/*` — no spec changes; the README links, not duplicates.
- Any code, `package.json`, or the general module-authoring guide (a separate
  direction item, deliberately not in this plan).

## Git workflow

- Branch: `advisor/005-docs-refresh`
- Commit style: `docs: mark events complete in roadmap` and
  `docs(autolinking): add package README` (repo examples:
  `docs(hostobject): document lazy module host objects`)
- Do NOT push or open a PR unless the operator instructed it.

## Steps

### Step 1: Update the roadmap entry

Change the "Events / EventEmitter" heading to include "(complete)" and rewrite
its sub-bullet(s) in past/shipped phrasing consistent with items 1–2 (e.g.
module-to-JS event emission via `SendEventAsync` and the `[Events]` attribute,
with lifecycle-safe teardown). First verify the evidence files above still
show the API; keep the edit minimal — do not restructure the section.

**Verify**: `rg -n "Events / EventEmitter" docs/roadmap.md` shows the entry
with "(complete)"; `git diff --check` clean.

### Step 2: Write the autolinking README

Create `packages/expo-modules-dotnet-autolinking/README.md` with: one-paragraph
purpose (discovers .NET-backed Expo modules, generates the app-level
aggregator, builds and stages NativeAOT/HostFXR artifacts); a command table
(`generate`, `resolve`, `build`, `stage`, `link` — one line each, sourced from
reading `src/commands/*.ts`); a short "development" section (`pnpm build`,
`pnpm test`, `pnpm typecheck` within the package); and a pointer:
"Normative contract: `docs/specs/dotnet-autolinking.md`" (repo-relative link
`../../docs/specs/dotnet-autolinking.md`). No absolute paths, no usernames.

**Verify**: file exists; every command name in the table matches a
`register*Command` in `src/commands/` (`rg "export function register" packages/expo-modules-dotnet-autolinking/src/commands/`); relative link resolves (`ls packages/expo-modules-dotnet-autolinking/../../docs/specs/dotnet-autolinking.md`).

### Step 3: Platform support table in root README

Add a "Platform support" section to the root `README.md` (place it near the
top, after the project description — read the README first and match its
heading style). Content is operator-specified:

| Platform | HostFXR (dev loader) | NativeAOT |
|---|---|---|
| Windows | yes | yes |
| macOS | yes | yes |
| Android | no | yes |
| iOS | no | yes |

With a note: Mono AOT support for Android/iOS is planned for the future, and
HostFXR is a development-time loader (per `docs/specs/runtime-and-abi.md`
positioning). Adjust wording to the README's voice; keep the facts exactly as
in the table.

**Verify**: `rg -n "Platform support" README.md` → section exists;
table renders (4 platform rows).

### Step 4: Docs checklist

Run the docs-only checklist from `docs/README.md`: `git diff --check` and the
rg regression grep (see Commands table). Run `scripts/format.sh --check --all`;
if it flags the new md files, run `scripts/format.sh` and re-check.

**Verify**: all three commands exit 0 / clean.

## Test plan

Docs-only; verification is the greps and checklist above. No test code.

## Done criteria

- [ ] `rg -n "Events / EventEmitter" docs/roadmap.md` → entry marked "(complete)".
- [ ] `packages/expo-modules-dotnet-autolinking/README.md` exists; command table
      matches `src/commands/` exports; spec link path resolves.
- [ ] Root `README.md` has the platform support table (Windows/macOS:
      HostFXR + NativeAOT; Android/iOS: NativeAOT only, Mono AOT planned).
- [ ] `git diff --check` clean; regression rg from `docs/README.md` shows no
      unexplained matches; `scripts/format.sh --check --all` exits 0.
- [ ] No absolute paths/usernames in any touched file (`rg -n "/Users/|barthap" README.md docs/roadmap.md packages/expo-modules-dotnet-autolinking/README.md` → no matches).
- [ ] No files outside in-scope list modified (`git status`).
- [ ] `docs/plans/README.md` status row updated.

## STOP conditions

Stop and report back (do not improvise) if:

- `Module.cs` no longer contains `SendEventAsync`, or `ExampleMathModule.cs`
  no longer uses `[Events]` — the completion evidence is gone.
- The roadmap section has been restructured since `0f6fc760` and the entry no
  longer matches the excerpt.
- A command exists in `src/commands/` whose purpose you cannot determine from
  its source — do not guess in the README.

## Maintenance notes

- When SharedObject / ArrayBuffer work starts (roadmap P2/P3), the same
  "(complete)" discipline applies — reviewers should treat roadmap staleness
  as a doc bug.
- New CLI commands must be added to the README table; cheap review check.
- Deferred deliberately: a full module-authoring guide (tutorial for writing
  .NET Expo modules). Tracked as a direction finding in `docs/plans/README.md`, not
  planned here.
