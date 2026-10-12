# Plan 002: Add tests for the untested autolinking CLI command layer

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving to the
> next step. If anything in the "STOP conditions" section occurs, stop and
> report — do not improvise. When done, update the status row for this plan
> in `docs/plans/README.md` — unless a reviewer dispatched you and told you they
> maintain the index.
>
> **Drift check (run first)**: `git diff --stat 0f6fc760..HEAD -- packages/expo-modules-dotnet-autolinking/src/`
> If any in-scope file changed since this plan was written, compare the
> "Current state" excerpts against the live code before proceeding; on a
> mismatch, treat it as a STOP condition.

## Status

- **Priority**: P1
- **Effort**: M
- **Risk**: LOW
- **Depends on**: none
- **Category**: tests
- **Planned at**: commit `0f6fc760`, 2026-07-08

## Why this matters

`packages/expo-modules-dotnet-autolinking` is the CLI that owns NativeAOT
artifact staging for .NET Expo modules — a repo constraint says all staging
MUST go through it. Its library layer is tested, but four of five command
registration modules have no tests: argument parsing, option defaults, error
paths, and console output are unverified. A wrong default path or silently
swallowed error here breaks app builds downstream.

## Current state

- Package: `packages/expo-modules-dotnet-autolinking` — TypeScript, commander,
  vitest (`"test": "vitest run"` in its `package.json`). `strict: true` tsconfig.
- Command modules under `src/commands/`, each exporting
  `register<Name>Command(program: Command): void`:
  - `buildCommand.ts` (112 lines) — UNTESTED
  - `linkCommand.ts` (113 lines) — UNTESTED
  - `stageCommand.ts` (94 lines) — UNTESTED
  - `resolveCommand.ts` (15 lines) — UNTESTED
  - `generateCommand.ts` (40 lines) — tested in
    `src/__tests__/generateCommand.test.ts` — this is your structural exemplar.
- Existing tests in `src/__tests__/`: `build.test.ts`, `discovery.test.ts`,
  `e2e.test.ts`, `generateAggregator.test.ts`, `generateCommand.test.ts`,
  `paths.test.ts`, `resolveDotnetModules.test.ts`, `stage.test.ts`. These cover
  the library functions the commands delegate to — do not duplicate that
  coverage; test the command layer (option parsing → correct delegate call →
  output/exit behavior).
- Exemplar pattern from `src/__tests__/generateCommand.test.ts` (top of file):

  ```ts
  vi.mock('../discovery', () => ({
    discoverDotnetManifestAsync: vi.fn(async () => manifest),
    resolveAppRoot: vi.fn(() => appRoot),
  }));
  // ...
  await program.parseAsync(['generate', '--project-root', 'apps/desktop-app'], { from: 'user' });
  expect(generateAggregator).toHaveBeenCalledWith(manifest, {
    outputDir: path.join(appRoot, '.expo', 'dotnet'),
    adapterPackageRoot,
  });
  ```

  Note the pattern: `vi.mock` the library modules, build a commander `program`,
  register the command, `parseAsync` with `{ from: 'user' }`, assert delegate
  calls and console output (`vi.spyOn(console, 'log')`).
- Read each command file fully before writing its tests — the exact options,
  defaults, and delegates are the spec. The living spec
  `docs/specs/dotnet-autolinking.md` describes the expected contract; if a
  command contradicts the spec, that is a STOP condition (report the drift).

## Commands you will need

| Purpose | Command | Expected on success |
|---|---|---|
| Install | `pnpm install --frozen-lockfile` | exit 0 |
| Tests | `pnpm --filter expo-modules-dotnet-autolinking test` | all pass |
| Typecheck | `pnpm --filter expo-modules-dotnet-autolinking typecheck` | exit 0 |
| Single file | `pnpm --filter expo-modules-dotnet-autolinking exec vitest run src/__tests__/linkCommand.test.ts` | pass |

## Scope

**In scope** (the only files you should create/modify):
- `packages/expo-modules-dotnet-autolinking/src/__tests__/buildCommand.test.ts` (create)
- `packages/expo-modules-dotnet-autolinking/src/__tests__/linkCommand.test.ts` (create)
- `packages/expo-modules-dotnet-autolinking/src/__tests__/stageCommand.test.ts` (create)
- `packages/expo-modules-dotnet-autolinking/src/__tests__/resolveCommand.test.ts` (create)
- `docs/plans/README.md` (status row)

**Out of scope** (do NOT touch):
- The command implementations themselves (`src/commands/*.ts`) — this plan is
  characterization tests only. If you find a real bug, write the test that
  documents current behavior, and report the bug — do not fix it here.
- `src/index.ts`, library modules, existing tests.

## Git workflow

- Branch: `advisor/002-cli-command-tests`
- Commit style: `test(autolinking): cover CLI command registration layer`
  (repo uses conventional-commit-ish messages, e.g. `test(dotnet): add TypeScript package tests`)
- Do NOT push or open a PR unless the operator instructed it.

## Steps

### Step 1: `resolveCommand.test.ts` (smallest first)

Read `src/commands/resolveCommand.ts` (15 lines). Mock its delegate(s), assert:
happy path invocation with default options, and any option overrides.

**Verify**: `pnpm --filter expo-modules-dotnet-autolinking exec vitest run src/__tests__/resolveCommand.test.ts` → pass.

### Step 2: `stageCommand.test.ts`

Read `src/commands/stageCommand.ts`. Cover: default option values passed to the
staging library, each declared CLI option changing the delegate call, and error
propagation (make the mocked delegate reject; assert the command surfaces the
failure — non-zero exit / thrown error / `console.error`, whichever the code
does — assert what it DOES, not what it should do).

**Verify**: `... vitest run src/__tests__/stageCommand.test.ts` → pass.

### Step 3: `buildCommand.test.ts`

Same approach for `src/commands/buildCommand.ts`. It is the largest (112
lines); enumerate its options from the source and cover each branch that alters
the delegate call (configuration, output paths, mode flags).

**Verify**: `... vitest run src/__tests__/buildCommand.test.ts` → pass.

### Step 4: `linkCommand.test.ts`

Same for `src/commands/linkCommand.ts` (113 lines).

**Verify**: `... vitest run src/__tests__/linkCommand.test.ts` → pass.

### Step 5: Full suite + typecheck

**Verify**: `pnpm --filter expo-modules-dotnet-autolinking test` → all pass
(existing + new); `pnpm --filter expo-modules-dotnet-autolinking typecheck` → exit 0.

## Test plan

Per command file: (a) default invocation → delegate called with documented
defaults; (b) each CLI option changes the corresponding delegate argument;
(c) delegate failure → command reports the error (assert observed behavior);
(d) console output format asserted where the exemplar does so.
Model all files after `src/__tests__/generateCommand.test.ts`.

## Done criteria

- [ ] Four new test files exist; `ls packages/expo-modules-dotnet-autolinking/src/__tests__/ | grep -c 'Command.test.ts'` ≥ 5.
- [ ] `pnpm --filter expo-modules-dotnet-autolinking test` exits 0.
- [ ] `pnpm --filter expo-modules-dotnet-autolinking typecheck` exits 0.
- [ ] No files outside the in-scope list modified (`git status`).
- [ ] `docs/plans/README.md` status row updated.

## STOP conditions

Stop and report back (do not improvise) if:

- A command's behavior contradicts `docs/specs/dotnet-autolinking.md`
  (spec drift — the team must decide which is right).
- You find a bug that makes a command's happy path fail — report it; do not
  patch `src/commands/`.
- Mocking a delegate requires restructuring the command module (e.g. delegates
  resolved at import time in a way `vi.mock` can't intercept twice).

## Maintenance notes

- New CLI options must land with a test in the matching `*Command.test.ts` —
  reviewers should reject option additions without one.
- If the command layer is later refactored to share an option-parsing helper,
  these tests are the safety net; keep them green through the refactor.
