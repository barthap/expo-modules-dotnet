# Roadmap

This file is the only backlog and status tracker for the repo. The
`living-spec-workflow` skill
(`.agents/skills/living-spec-workflow/SKILL.md`) owns it: discover mode adds
briefs and rows, and deliver mode moves a row to DONE when its slice closes.

Current behavior lives in `docs/specs/`. This file records direction, open
work, and what was decided not to do. It does not describe shipped behavior.

## Direction

The repo is the portable C# / JSI bridge successor to `expo-modules-windows`.
C++ owns JSI mechanics, C# owns module logic, and a C ABI with opaque handles
connects them.

Milestone order:

1. **Bridge and authoring core** (shipped): ABI, managed JSI wrappers,
   lifecycle, scheduling, generated bindings, codecs, events, ArrayBuffer,
   SharedObject, and autolinking across Android, iOS, macOS, and Windows.
2. **Authored modules** (in progress): real `expo-*-dotnet` packages for
   Windows and macOS, in this order: asset, constants, local file system,
   crypto. They exist so expo-desktop can later drop its JS polyfill. They
   register only into `_expoDotnet.modules` for now.
3. **Upstream compatibility** (later): opt-in per-module registration into
   `globalThis.expo.modules`, owned by autolinking policy. Startup ordering
   against expo-desktop is undecided, so this is not a defined milestone yet.
4. **Production hardening**: mobile NativeAOT trimming and export audits,
   desktop packaging, and Expo Desktop prebuild integration.

Standing quality rule: authored modules use no workarounds for missing core
features. Fix the core first, as plans 026 and 027 did.

## Status

Status values: `TODO`, `IN PROGRESS`, `DONE`, `OPEN` (follow-up remains),
`BACKLOG` (nice-to-have), `BLOCKED`, `REJECTED`. Keep cells short; put detail
in the notes below the table.

Open briefs live in `docs/plans/`. Briefs for DONE items move to
`docs/archive/plans/`.

| ID  | Title                                              | Pri   | Effort | Depends on | Status  | Brief                                                                    |
| --- | -------------------------------------------------- | ----- | ------ | ---------- | ------- | ------------------------------------------------------------------------ |
| 024 | `expo-file-system-dotnet` local files core         | P1    | L      | —          | TODO    | [024](plans/024-expo-file-system-dotnet.md)                              |
| 025 | `expo-crypto-dotnet`                               | P1    | M      | 024        | TODO    | [025](plans/025-expo-crypto-dotnet.md)                                   |
| 028 | Annotation-insensitive shared-object boundaries    | P2    | S      | —          | TODO    | [028](plans/028-annotation-insensitive-shared-object-boundaries.md)      |
| 029 | Typed codec conversion descriptors                 | P1    | M      | 028        | TODO    | [029](plans/029-typed-codec-conversion-descriptors.md)                   |
| 030 | Compositional codec resource scope                 | P1    | L      | 029        | TODO    | [030](plans/030-compositional-codec-resource-scope.md)                   |
| 031 | Nullable ArrayBuffer codecs                        | P1    | M      | 030        | TODO    | [031](plans/031-nullable-arraybuffer-codecs.md)                          |
| 032 | Nullable JavaScript callback parameters            | P2    | M      | 031        | TODO    | [032](plans/032-nullable-javascript-callback-parameters.md)              |
| 033 | Nullable concrete SharedObject classes             | P2    | M      | 032        | TODO    | [033](plans/033-nullable-concrete-shared-object-codecs.md)               |
| 018 | Windows build/deploy reliability + `ReactNativeDir` | P1/P2 | L      | —          | OPEN    | [018](plans/018-windows-build-deploy-reliability.md)                     |
| 011 | Hermes prebuilts as downloadable artifacts         | P3    | M      | 008        | BACKLOG | —                                                                        |
| 001 | Fast CI checks workflow                            | P1    | S      | —          | DONE    | [001](archive/plans/001-ci-checks-workflow.md)                           |
| 002 | Autolinking CLI command tests                      | P1    | M      | —          | DONE    | [002](archive/plans/002-autolinking-cli-command-tests.md)                |
| 003 | Bridge edge-case tests                             | P2    | M      | —          | DONE    | [003](archive/plans/003-bridge-edge-case-tests.md)                       |
| 004 | Bridge string hygiene                              | P2    | S      | 003        | DONE    | [004](archive/plans/004-bridge-string-hygiene.md)                        |
| 005 | Docs refresh and module authoring guide            | P3    | M      | —          | DONE    | [005](archive/plans/005-docs-refresh.md)                                 |
| 006 | ArrayBuffer and binary codecs                      | P2    | L      | 003        | DONE    | [archived change](archive/changes/2026-07-10-arraybuffer-codecs/)        |
| 007 | SharedObject design spike                          | P3    | M      | —          | DONE    | [007](archive/plans/007-sharedobject-design-spike.md)                    |
| 008 | Linux testhost + native tests workflow             | P1    | M–L    | 001        | DONE    | [008](archive/plans/008-linux-testhost-native-ci.md)                     |
| 009 | Windows testhost teardown crash                    | P1    | M      | 008        | DONE    | [009](archive/plans/009-windows-testhost-teardown-crash.md)              |
| 010 | Darwin installer lifecycle locking                 | P1    | M      | —          | DONE    | [010](archive/plans/010-darwin-installer-lifecycle-locking.md)           |
| 012 | Typed JS facade base classes                       | P2    | M      | 005        | DONE    | [012](archive/plans/012-typed-js-facade.md)                              |
| 013 | camelCase JS naming + `[JS]` properties            | P2    | M      | —          | DONE    | [013](archive/plans/013-camelcase-js-naming-and-properties.md)           |
| 014 | Typed `[Event]` members                            | P2    | M      | 013        | DONE    | [014](archive/plans/014-typed-event-members.md)                          |
| 015 | Promise state on runtime-owned collection          | P2    | M      | —          | DONE    | [015](archive/plans/015-promise-long-lived-state.md)                     |
| 016 | NativeAOT publish check in CI                      | P1    | S      | —          | DONE    | [016](archive/plans/016-nativeaot-ci-lane.md)                            |
| 017 | SharedObject authoring surface                     | P2    | L      | 015        | DONE    | [017](archive/plans/017-sharedobject-authoring.md)                       |
| 019 | Typed events on shared objects                     | P2    | M      | 017, 021   | DONE    | [019](archive/plans/019-sharedobject-events.md)                          |
| 020 | Console app Linux port + loader CI lane            | P1    | M      | 016        | DONE    | [020](archive/plans/020-console-app-linux-port.md)                       |
| 021 | Owned host-function callback-state disposal        | P2    | S–M    | —          | DONE    | [021](archive/plans/021-hostfunction-owned-state-disposal.md)            |
| 022 | `expo-asset-dotnet`                                | P1    | M      | 027        | DONE    | [022](archive/plans/022-expo-asset-dotnet.md)                            |
| 023 | `expo-constants-dotnet`                            | P1    | M      | —          | DONE    | [023](archive/plans/023-expo-constants-dotnet.md)                        |
| 026 | Nullable reference-type codecs                     | P1    | M      | —          | DONE    | [026](archive/plans/026-nullable-reference-codecs.md)                    |
| 027 | Host-supplied app directories                      | P1    | L      | —          | DONE    | [027](archive/plans/027-host-supplied-app-directories.md)                |

The next free ID is 034.

### Notes on open items

- **024 → 025**: authored-module order is file system, then crypto. Plan 027
  already shipped `context.PersistentFilesDirectory`, so 024 needs no ABI work.
  Plans 028–033 are not prerequisites for either package.
- **028–033**: one serial chain. 028 fixes the diagnostic baseline for
  annotated shared-object symbols. 029 replaces codec-name parsing with typed
  descriptors; no later plan may add another codec-string or `StartsWith`
  policy check. 030 adds one explicit conversion scope with
  identity-deduplicated reverse cleanup. 031–033 then apply their own null
  policies. `JavaScriptValue?` and direct `SharedObject?` / `SharedRef<T>?`
  bases stay unsupported by design.
- **018**: direct MSBuild, RNW CLI build/deploy/launch, and live-app rebuild
  did not reproduce a PDB lock on the RNW 0.81 / VS 2026 lane (2026-07-23).
  The `ReactNativeDir` resolver helpers stay, but activation waits until
  expo-desktop supports Windows prebuild and mod execution.
- **023 known gap**: direct unpackaged Windows startup of the example app was
  not verified. Tests cover null metadata downstream.

Older execution, reconcile, and verification notes are archived in
`docs/archive/plans/README.md`.

## Unplanned backlog

Ideas without a brief. Discover mode or a user request turns one into a brief
and a status row.

### Modules and compatibility

- **P1 — External authored-module testing**: publish
  `Expo.ModulesCore.Testing` for separate repositories, ship RID-specific
  Hermes/testhost native assets, and add a standalone test command.
  Repo-local package tests already exist.
- **P2 — Upstream compatibility**: opt-in `globalThis.expo.modules`
  registration (probably an `expo-module.config.json` field), Metro and
  package-specifier aliasing. Settled: never automatic, autolinking owns the
  policy, `Expo.ModulesCore` keeps the primitive generic. Undecided: startup
  ordering against expo-desktop.
- **P3 — Separate-repo and app-local modules**: planned future authoring
  workflow; the authoring guide §1 carries the note.
- **P3 — Generated TypeScript and view modules**: left out of the Expo Modules
  2.0 alignment; upstream ships them later too.

### Platform adapters

- **P1 — Desktop packaging cleanup**: macOS HostFXR and NativeAOT example app
  packaging.
- **P1 — Expo Desktop prebuild integration**: if `apps/desktop-app` moves to
  an `expo-desktop` prebuild flow, an Expo config plugin must own the managed
  artifact build phase, `EXPO_DOTNET_LOADER`, the `ExpoModulesDotnetLoader`
  `Info.plist` entry, the `Managed` resource folder, and any Podfile shim.
- **P3 — Mobile NativeAOT hardening**: trimming and export audits plus
  platform constraints for `apps/mobile-app`.
- **P3 — View adapters**: platform-specific native views, props, and events.
  No view concepts in the portable core.

### ABI and codecs

- **P2/P3 — Generalized `instanceof`**: beyond Promise and Error, for typed
  record deserialization and custom class detection.
- **P2/P3 — Record shape extensions**: explicit per-field naming,
  non-positional constructors, unknown-field validation, cyclic graphs.
- **P3 — Script evaluation**: `evaluate_javascript` for dev tooling.

### Runtime, performance, and tooling

- **P2 — Shared-object zero-listener test**: the spec says dispatch with zero
  listeners succeeds, but no test emits with zero listeners.
- **P2 — `HostObjectContext` owned-state disposal**: same exactly-once gap
  that plan 021 fixed for host functions.
- **P3 — Handle allocation cost**: arena or pool allocation for `ValueHandle`
  when profiling shows pressure.
- **P3 — Scheduler priority**: priority stays advisory where the host cannot
  honor it (React Native `CallInvoker` has no lanes).
- **P3 — Structured DevTools errors**: separate message and stack fields, and
  a development-only verbose mode across the ABI.
- **P3 — Shared native bridge build composition**: one source-set mechanism
  for Android CMake, Apple sources, testhost, console app, and Windows
  MSBuild. Build cleanup only; no ABI or lifetime change.
- **P3 — Two-stage lazy module shells**: only if profiling shows root module
  property reads are too expensive.
- **P3 — Installer TurboModule codegen**: native installer glue stays
  hand-written; revisit only for a concrete codegen need.

### CI

- **P2 — Windows heartbeat**: a periodic Windows end-to-end check, so the
  flagship platform does not rot while macOS-hosted Hermes tests stay green.
- **P3 — More CI lanes**: grow `native-tests.yml` as matrix rows: RNW as its
  own workflow, then Android (with Maestro e2e), then iOS/macOS. No macOS
  runners for now.

## Considered and rejected

So nobody audits these again:

- GCHandle leak in `CreateHostFunction`: false. The error path releases the
  handle.
- Exception messages propagated to JS: by design, per
  `docs/specs/host-functions-and-errors.md`.
- Validate raw callback function pointers at the C ABI: not possible.
- `Substring(1)` bounds in generator `LowerCamel`: safe in .NET.
- `int32` overflow in the `isValidUtf8` loop: theoretical; lengths come from
  managed arrays far below `INT32_MAX`.
- Autolinking tsconfig missing strict mode: false; `strict: true` is set.
- `CLAUDE.md` "effectively empty": false; it includes `AGENTS.md` by design.
- Roslyn generator property-name double loop: no measured cost.
- RN 0.81 vs 0.86 workspace catalogs: deliberate, because RNW requires 0.81.
- Stripping the `On` prefix from JS event names: keeps the `onStatus`
  listener convention.
- TypeScript version drift in autolinking and transitive advisories in
  desktop-app build tooling: small; revisit if desktop tooling changes.
