# Plan 012: Typed JS facade base classes (`DotnetModule`, `DotnetEventEmitter`)

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving to the
> next step. If anything in the "STOP conditions" section occurs, stop and
> report — do not improvise. When done, update the status row for this plan
> in `docs/plans/README.md` — unless a reviewer dispatched you and told you
> they maintain the index.
>
> **Drift check (run first)**:
> `git diff --stat 6b7fefff..HEAD -- packages/expo-modules-dotnet/src packages/example-module/src docs/specs/modules-core-boundary.md`
> Plan 005's branch may add `docs/module-authoring-guide.md` guidance around
> this area; that is expected. If `packages/expo-modules-dotnet/src/index.ts`
> or `packages/example-module/src/index.ts` no longer match the "Current
> state" excerpts, treat it as a STOP condition.

## Status

- **Priority**: P2
- **Effort**: M
- **Risk**: LOW–MEDIUM (public TS API addition; no native/ABI changes)
- **Depends on**: 005 (authoring guide exists; its §8 forward-looking note
  gets replaced by real documentation at the end of this plan)
- **Category**: DX / API
- **Planned at**: commit `6b7fefff`, 2026-07-19 (operator-requested,
  2026-07-19 review of plan 005)

## Why this matters

Module facades currently type the native module as a hand-rolled plain object
(`requireDotnetModule<T>` with an inline `type T = { ... }`), and each module
re-declares its own `EventSubscription` and untyped `addListener` signature.
Upstream Expo solves this with base classes: `NativeModule<TEventsMap>
extends EventEmitter<TEventsMap>` in `expo-modules-core`, and module facades
write `declare class X extends NativeModule<XEvents> { ... }`. The operator
wants the same shape here: dedicated `DotnetModule` / `DotnetEventEmitter`
types exported from `expo-modules-dotnet`, with authored module facades
extending them. This gives every module typed `addListener`/`removeListener`
for free, removes the per-module `EventSubscription` duplication, and matches
the API surface the runtime already installs (the JS-side prototype provides
`addListener`, `removeListener`, `removeAllListeners`, `emit`,
`listenerCount` — see `EventEmitterPrototype.cs`).

## Current state

- `packages/expo-modules-dotnet/src/index.ts` — the entire public JS surface
  (41 lines). Exports only `requireDotnetModule<T>(name: string): T`, which
  installs via the TurboModule installer and reads
  `globalThis._expoDotnet.modules[name]`. No class exports, no event types.
- `packages/example-module/src/index.ts` — declares
  `export type EventSubscription = { remove(): void }` (lines 14–16) and an
  inline object type `ExampleModule` with a hand-written
  `addListener(eventName: 'onStatus', listener: (payload: string) => void): EventSubscription`
  member (lines 18–32). Facade functions delegate to
  `requireDotnetModule<ExampleModule>('ExampleModule')`.
- Runtime reality (do not change it; the TS types must mirror it):
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/EventEmitterPrototype.cs`
  installs host functions `addListener` (arity 2), `removeListener` (2),
  `removeAllListeners` (1), `emit` (1+), `listenerCount` (1) on the shared
  prototype, and invokes authored `startObserving`/`stopObserving` hooks.
  Modules declaring `[Events]` get this prototype;
  `docs/specs/modules-core-boundary.md` (search `requireDotnetModule`, around
  line 686) is the owning spec.
- Upstream pattern to mirror (read for shape, not to copy code):
  `expo-modules-core`'s `src/ts-declarations/EventEmitter.ts` and
  `NativeModule.ts` — `EventEmitter<TEventsMap extends EventsMap>` with
  `EventsMap = Record<string, (...args: any[]) => void>` and
  `EventSubscription = { remove(): void }`; `NativeModule<TEventsMap>
  extends EventEmitter<TEventsMap>`. Consumers write
  `declare class NativeX extends NativeModule<XEvents> { ...methods }` and
  pass it to `requireNativeModule<NativeX>('X')` (see
  `expo-clipboard`'s `src/ExpoClipboard.ts` in the upstream repo for the
  consumer shape). Do NOT copy upstream implementation internals — only the
  type surface; our runtime backing is `_expoDotnet`, not `global.expo`.
- Repo constraint (`AGENTS.md`): this is a public API change to the adapter
  package → the living-spec workflow applies (delta spec before
  implementation, merge into `docs/specs/` at the end). Repo-local skill:
  `.agents/skills/living-spec-workflow/SKILL.md`.
- Naming is operator-specified: `DotnetModule`, `DotnetEventEmitter`.

## Design constraints (resolve in the delta spec, not ad hoc)

- The base classes must be usable in `declare class X extends DotnetModule<E>`
  position from consumer packages. That requires `DotnetModule` /
  `DotnetEventEmitter` to exist as exported *values*, not type-only exports
  (a heritage clause references a value). Export real classes whose
  constructor throws (instances are only ever created natively; the class
  exists for typing and `instanceof`-free extension). Type-only
  `export declare class` in a normal `.ts` file is NOT acceptable here — it
  compiles to an import that is `undefined` at runtime under
  `verbatimModuleSyntax`-style emit.
- Method signatures mirror the runtime prototype exactly (names and arities
  above). `addListener` returns `EventSubscription`. `emit` is typed but
  should carry a doc note that it is primarily runtime-internal.
- `requireDotnetModule<T>` keeps its signature; optionally constrain to
  `T extends DotnetModule<any> | object` only if it stays 100%
  backward-compatible with existing plain-object callers. When in doubt,
  leave the constraint off.
- No React Native or expo-modules-core runtime imports added to
  `expo-modules-dotnet`'s new files (portability constraint; the package's
  only RN touchpoint stays the TurboModule installer import that already
  exists).

## Commands you will need

| Purpose | Command | Expected on success |
|---|---|---|
| Adapter tests | `pnpm --filter expo-modules-dotnet test` | vitest green |
| Adapter typecheck | `pnpm --filter expo-modules-dotnet typecheck` | exit 0 |
| Mobile JS typecheck | `pnpm --filter mobile-app typecheck` | exit 0 |
| Managed suite (unaffected; sanity) | `scripts/test-managed.sh` | green |
| Format | `scripts/format.sh --check --all` | exit 0 |
| Whitespace | `git diff --check` | clean |

## Scope

**In scope** (create/modify only):
- `docs/changes/<yyyy-mm-dd>-typed-js-facade/spec.md` and `plan.md` (delta
  spec + implementation plan per the living-spec workflow; get operator
  approval on the delta spec before implementing)
- `packages/expo-modules-dotnet/src/` — new declaration/class file(s) (e.g.
  `ts-declarations/` mirroring upstream layout, or a single `types.ts`) and
  the re-exports from `src/index.ts`
- `packages/expo-modules-dotnet/src/__tests__/` — new/extended vitest tests
- `packages/example-module/src/index.ts` — migrate the facade to
  `declare class ... extends DotnetModule<...>`, drop the local
  `EventSubscription`
- `docs/specs/modules-core-boundary.md` — merge the accepted delta at the end
- `docs/module-authoring-guide.md` — replace §8's "planned direction" note
  with the real pattern
- `docs/plans/README.md` (status row)

**Out of scope** (do NOT touch):
- Anything under `packages/expo-modules-dotnet/managed/` or native code — the
  runtime prototype is already correct; this plan is TS-only.
- The C# generator, ABI, or `Expo.ModulesCore`.
- Other apps' facades beyond `example-module` (mobile-app consumes
  example-module's public functions, which must not change shape).

## Git workflow

- Branch: `advisor/012-typed-js-facade` (branch from `main` after plan 005's
  branch merges, or from the 005 branch if the operator says so — ask which
  base if 005 is still unmerged when you start).
- Commit style: `feat(dotnet-js): add DotnetModule/DotnetEventEmitter facade types`,
  `refactor(example-module): extend DotnetModule in facade`,
  `docs(specs): merge typed-js-facade delta`.
- Do NOT push or open a PR unless the operator instructed it.

## Steps

### Step 1: Delta spec

Read `.agents/skills/living-spec-workflow/SKILL.md` and
`docs/specs/modules-core-boundary.md` (the `requireDotnetModule` section).
Write `docs/changes/<yyyy-mm-dd>-typed-js-facade/spec.md` covering: exported
names (`DotnetEventEmitter<TEventsMap>`, `DotnetModule<TEventsMap>`,
`EventsMap`, `EventSubscription`), method signatures mirroring
`EventEmitterPrototype.cs`, the throwing-constructor value-class decision,
`requireDotnetModule` compatibility, and the example-module migration.
**Present the delta spec to the operator and get approval before Step 2.**

**Verify**: spec file exists; operator approved.

### Step 2: Implement the base classes

Add the class file(s) under `packages/expo-modules-dotnet/src/`, re-export
from `src/index.ts`. Constructor throws with a clear message (instances come
from the native registry). Typed members per the spec.

**Verify**: `pnpm --filter expo-modules-dotnet typecheck` exit 0.

### Step 3: Tests

Extend `packages/expo-modules-dotnet/src/__tests__/` (follow
`index.test.ts`'s vitest + mocked TurboModule pattern): type-level usage —
a `declare class Fixture extends DotnetModule<{ onFoo(payload: string): void }>`
passed through `requireDotnetModule<Fixture>` keeps method and
`addListener('onFoo', ...)` typing (compile-time assertion via `tsc` is
enough; a small `expect-type`-style helper or `// @ts-expect-error` cases for
wrong event names) — plus a runtime test that constructing `DotnetModule`
throws.

**Verify**: `pnpm --filter expo-modules-dotnet test` green; the
`@ts-expect-error` cases fail typecheck when removed (spot-check one).

### Step 4: Migrate example-module facade

Rewrite `packages/example-module/src/index.ts`: import `DotnetModule` and
`EventSubscription` from `expo-modules-dotnet`, declare
`declare class ExampleModuleType extends DotnetModule<{ onStatus(payload: string): void }> { ... }`
(keep the PascalCase record-field mapping exactly as-is), delete the local
`EventSubscription` type but keep re-exporting the name from the new import
so `mobile-app` doesn't break. Public function signatures unchanged.

**Verify**: `pnpm --filter mobile-app typecheck` exit 0;
`rg -n "export type EventSubscription = \{" packages/example-module/src/index.ts`
→ no match (local duplicate gone).

### Step 5: Docs

Merge the accepted delta into `docs/specs/modules-core-boundary.md` (follow
the spec's existing WHEN/THEN house style). Update
`docs/module-authoring-guide.md` §8: replace the "planned direction" note
with the real `extends DotnetModule` pattern and a code excerpt matching the
migrated example-module facade.

**Verify**: `rg -n "DotnetModule" docs/specs/modules-core-boundary.md docs/module-authoring-guide.md`
→ both documented; guide no longer says the types are "planned".

### Step 6: Full verification

`scripts/test-managed.sh` (sanity — must stay green, nothing native
changed), `scripts/format.sh --check --all`, `git diff --check`,
`rg -n "/Users/|barthap"` over all touched files → no matches.

## Done criteria

- [ ] `DotnetModule` / `DotnetEventEmitter` / `EventsMap` /
      `EventSubscription` exported from `expo-modules-dotnet`; signatures
      mirror `EventEmitterPrototype.cs` (5 methods + subscription).
- [ ] Constructing either class throws; extending via `declare class` from a
      consumer package typechecks.
- [ ] example-module facade extends `DotnetModule<...>`; no local
      `EventSubscription`; public function signatures unchanged.
- [ ] Delta spec approved and merged into
      `docs/specs/modules-core-boundary.md`; authoring guide §8 updated.
- [ ] All commands in the table green; no out-of-scope files in `git status`.

## STOP conditions

Stop and report back (do not improvise) if:

- The runtime prototype methods/arities no longer match
  `EventEmitterPrototype.cs` as excerpted — the type surface would be wrong.
- Making the classes real exported values requires adding a runtime
  dependency (react-native, expo-modules-core) to `expo-modules-dotnet`'s
  portable surface.
- The operator rejects or materially changes the delta spec — re-plan, don't
  patch around it.
- example-module migration forces a change to any public function signature
  consumed by `mobile-app`.

## Maintenance notes

- When SharedObject lands (plan 007 follow-up), it will likely want a
  `DotnetSharedObject` sibling — same file layout, same value-class rule.
- If module authoring later moves to separate repos (operator direction,
  2026-07-19), these exports become the package's compatibility surface —
  semver discipline starts mattering here first.
