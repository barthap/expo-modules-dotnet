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
   crypto. They exist to replace the expo-desktop native stubs on Windows.
   They register only into `_expoDotnet.modules`.
3. **expo-desktop integration** (in progress): expo-desktop 1.0 prebuild
   now generates the desktop app (039). Next, let a Windows app opt in to
   `*-dotnet` modules in place of the expo-desktop stubs (040). Windows only:
   on macOS, upstream `expo-modules-core` and the upstream Expo packages
   already work, which is why our packages are named `expo-*-dotnet`.
4. **Production hardening**: mobile NativeAOT trimming and export audits,
   and desktop packaging.

Milestones 2 and 3 must show real value: four authored modules and one demo
app that uses upstream Expo, expo-desktop, and .NET modules on all four
platforms.

Standing quality rule: authored modules use no workarounds for missing core
features. Fix the core first, as plans 026 and 027 did.

## Status

Status values: `TODO`, `IN PROGRESS`, `DONE`, `OPEN` (follow-up remains),
`BACKLOG` (nice-to-have), `BLOCKED`, `REJECTED`. Keep cells short; put detail
in the notes below the table.

Open briefs live in `docs/plans/`. A DONE item's brief leaves the repo; git
history keeps it.

| ID  | Title                                               | Pri   | Effort | Depends on   | Status  | Brief                                                                       |
| --- | --------------------------------------------------- | ----- | ------ | ------------ | ------- | --------------------------------------------------------------------------- |
| 025 | `expo-crypto-dotnet`                                | P1    | M      | 024, 036     | TODO    | [025](plans/025-expo-crypto-dotnet.md)                                      |
| 028 | Annotation-insensitive shared-object boundaries     | P2    | S      | —            | DONE    | —                                                                           |
| 029 | Typed codec conversion descriptors                  | P1    | M      | 028          | DONE    | —                                                                           |
| 030 | Composable codec leaf release                       | P1    | L      | 029          | DONE    | —                                                                           |
| 031 | Nullable ArrayBuffer codecs                         | P1    | M      | 030          | DONE    | —                                                                           |
| 032 | Nullable JavaScript callback parameters             | P2    | M      | 031          | TODO    | [032](plans/032-nullable-javascript-callback-parameters.md)                 |
| 033 | Nullable concrete SharedObject classes              | P2    | M      | 032          | TODO    | [033](plans/033-nullable-concrete-shared-object-codecs.md)                  |
| 034 | Base-class shared-object arguments                  | P1    | M      | 030          | TODO    | —                                                                           |
| 035 | Union arguments (`string \| ArrayBuffer`)           | P1    | M      | 030          | TODO    | —                                                                           |
| 036 | Typed-array codecs (`Uint8Array`)                   | P1    | M      | 030          | TODO    | —                                                                           |
| 037 | Coded JavaScript errors                             | P2    | M      | —            | TODO    | —                                                                           |
| 038 | `expo-file-system-dotnet` write, bytes, copy, move  | P1    | M      | 024, 034–036 | TODO    | —                                                                           |
| 040 | Windows opt-in `*-dotnet` substitution              | P1    | L      | 039          | TODO    | [040](plans/040-windows-dotnet-module-substitution.md)                      |
| 011 | Hermes prebuilts as downloadable artifacts          | P3    | M      | 008          | BACKLOG | —                                                                           |
| 001 | Fast CI checks workflow                             | P1    | S      | —            | DONE    | —                                                                           |
| 002 | Autolinking CLI command tests                       | P1    | M      | —            | DONE    | —                                                                           |
| 003 | Bridge edge-case tests                              | P2    | M      | —            | DONE    | —                                                                           |
| 004 | Bridge string hygiene                               | P2    | S      | 003          | DONE    | —                                                                           |
| 005 | Docs refresh and module authoring guide             | P3    | M      | —            | DONE    | —                                                                           |
| 006 | ArrayBuffer and binary codecs                       | P2    | L      | 003          | DONE    | —                                                                           |
| 007 | SharedObject design spike                           | P3    | M      | —            | DONE    | —                                                                           |
| 008 | Linux testhost + native tests workflow              | P1    | M–L    | 001          | DONE    | —                                                                           |
| 009 | Windows testhost teardown crash                     | P1    | M      | 008          | DONE    | —                                                                           |
| 010 | Darwin installer lifecycle locking                  | P1    | M      | —            | DONE    | —                                                                           |
| 012 | Typed JS facade base classes                        | P2    | M      | 005          | DONE    | —                                                                           |
| 013 | camelCase JS naming + `[JS]` properties             | P2    | M      | —            | DONE    | —                                                                           |
| 014 | Typed `[Event]` members                             | P2    | M      | 013          | DONE    | —                                                                           |
| 015 | Promise state on runtime-owned collection           | P2    | M      | —            | DONE    | —                                                                           |
| 016 | NativeAOT publish check in CI                       | P1    | S      | —            | DONE    | —                                                                           |
| 017 | SharedObject authoring surface                      | P2    | L      | 015          | DONE    | —                                                                           |
| 018 | Windows build/deploy reliability + `ReactNativeDir` | P1/P2 | L      | 039          | DONE    | —                                                                           |
| 019 | Typed events on shared objects                      | P2    | M      | 017, 021     | DONE    | —                                                                           |
| 020 | Console app Linux port + loader CI lane             | P1    | M      | 016          | DONE    | —                                                                           |
| 021 | Owned host-function callback-state disposal         | P2    | S–M    | —            | DONE    | —                                                                           |
| 022 | `expo-asset-dotnet`                                 | P1    | M      | 027          | DONE    | —                                                                           |
| 023 | `expo-constants-dotnet`                             | P1    | M      | —            | DONE    | —                                                                           |
| 024 | `expo-file-system-dotnet` local files core          | P1    | L      | —            | DONE    | —                                                                           |
| 026 | Nullable reference-type codecs                      | P1    | M      | —            | DONE    | —                                                                           |
| 027 | Host-supplied app directories                       | P1    | L      | —            | DONE    | —                                                                           |
| 039 | expo-desktop 1.0 prebuild for `desktop-app`         | P1    | L      | —            | DONE    | —                                                                           |

The next free ID is 041.

### Notes on open items

- **024 → 034–036 → 038**: file system ships in three steps. 024 ships the
  local-file subset that the current core carries with the exact upstream
  native shape. 034–036
  add the missing core features, and 038 uses them for `write`, `bytes`,
  `copy`, and `move`. Each new core feature gets a real use case at once.
  024 passed on Windows x64 at `13cf0a7` on 2026-10-22 with a local
  `scripts/test-managed.ps1` run (904 passed, 0 failed, 0 skipped).
- **028–030 before 034–036**: new codecs build on typed conversion
  descriptors (029) and codec leaf release (030), so they add no
  codec-string policy checks.
- **034**: an authored abstract shared-object base with a closed set of
  owned sealed subclasses (`FileSystemPath`) as an argument type. This changes
  the current rule that base/derived compatibility never selects a codec.
- **037**: upstream Expo errors carry a `code`. Generated glue rejects with a
  plain `Error` today. Not a blocker for 038.
- **025**: crypto follows file system and needs typed arrays (036). Plan 027
  already shipped the app directories, so neither package needs ABI work.
- **028–033**: one serial chain. 028 fixes the diagnostic baseline for
  annotated shared-object symbols. It is done: it landed at `b903383` on
  2026-10-30, with a local `scripts/test-managed.sh` run (920 passed,
  0 failed, 0 skipped). 029 replaces codec-name parsing with typed
  descriptors; no later plan may add another codec-string or `StartsWith`
  policy check. 029 is done:
  it landed on 2026-10-30 with a local `scripts/test-managed.sh` run (966
  passed, 0 failed, 0 skipped). It also made non-null lists and dictionaries
  compose in nested, record-field, callback, and event positions, so 030
  builds on them, and it rejects uncompilable codec compositions with build
  diagnostics. 030 is done:
  it landed on 2026-11-06 with a local `scripts/test-managed.sh` run (1070
  passed, 0 failed, 0 skipped). It replaced the first scoped-codec design with
  `Release` on the one codec protocol, so `JavaScriptValue` and `ArrayBuffer`
  leaves now compose in every position. 031 is done: it landed on 2026-11-06
  with a local `scripts/test-managed.sh` run (1129 passed, 0 failed,
  0 skipped). `ArrayBuffer?` now works wherever `ArrayBuffer` does, through
  `NullableReferenceCodec<ArrayBuffer, ArrayBufferCodec>`, and a direct
  `Func<ArrayBuffer?, Task>` event delivers one JavaScript `null` with no
  lease. 032 and 033 then apply their own null policies; each brief needs a
  rebase on this design first. `JavaScriptValue?` and direct `SharedObject?` /
  `SharedRef<T>?` bases stay unsupported by design.
- **018**: direct MSBuild, RNW CLI build/deploy/launch, and live-app rebuild
  did not reproduce a PDB lock on the RNW 0.81 / VS 2026 lane (2026-07-23).
  Closed with 039, which runs the `ReactNativeDir` resolver from the
  `windows` mod under expo-desktop prebuild.
- **039**: `apps/desktop-app` has no committed `macos/` or `windows/`;
  expo-desktop prebuild (SDK 54 template 54.81.5, RNW 0.81.37) and the
  `expo-modules-dotnet` plugin create them. Verified 2026-10-22:
  macOS and Windows `prebuild --clean` plus `expo-desktop run` log
  `[ExampleModule] C# add(22, 20) returned 42`, with no hand edit. On Windows,
  `autolink-windows` keeps the `Expo .NET Managed` solution folder. The macOS
  template enables the App Sandbox, so the plugin turns it off for `hostfxr`.
  macOS with `macos.loader: "nativeaot"` and the sandbox on logs the same
  line (2026-10-22).
  Upstream gap to report: the template Podfile builds the
  `react-native-macos` path from the `react-native` path, which fails under
  pnpm; `apps/desktop-app/plugins/withDesktopAppFixes.js` works around it.
  The draft report is in `docs/references/expo-desktop.md`.
- **039 → 040**: 040 needs the generated expo-desktop Windows app. Its
  recommended mechanism is a Windows-only Metro alias of the upstream
  native-accessor files (`expo-asset/build/ExpoAsset.js`,
  `expo-constants/build/ExponentConstants.js`). The fallback is an eager
  entry in `global.expo.modules`: expo-desktop makes `global.expo` not
  writable and not configurable, but `expo.modules` is a plain object.
  File-system substitution waits for 038.
- **023 known gap**: direct unpackaged Windows startup of the example app was
  not verified. Tests cover null metadata downstream.

Older execution, reconcile, and verification notes are in git history, in
the former `docs/archive/plans/README.md`.

## Unplanned backlog

Ideas without a brief. Discover mode or a user request turns one into a brief
and a status row.

### Modules and compatibility

- **P1 — External authored-module testing**: publish
  `Expo.ModulesCore.Testing` for separate repositories, ship RID-specific
  Hermes/testhost native assets, and add a standalone test command.
  Repo-local package tests already exist.
- **P1 — Four-platform demo app**: one app that uses upstream Expo,
  expo-desktop, and the `*-dotnet` modules on Android, iOS, macOS, and
  Windows. Depends on 025, 038, 039, and 040. Write the brief when 040
  closes.
- **P3 — expo-desktop SDK 55 / RN 0.83**: the current default expo-desktop
  template. It changes the RN 0.81 catalog decision, because it needs
  RNW 0.83.
- **P3 — `File.type` in `expo-file-system-dotnet`**: upstream returns a MIME
  type from the extension. .NET has no portable MIME table, so it needs an
  owned table or a host source.
- **P3 — Separate-repo and app-local modules**: planned future authoring
  workflow; the authoring guide §1 carries the note.
- **P3 — Generated TypeScript and view modules**: left out of the Expo Modules
  2.0 alignment; upstream ships them later too.

### Platform adapters

- **P1 — Desktop packaging cleanup**: macOS HostFXR and NativeAOT example app
  packaging.
- **P3 — Mobile NativeAOT hardening**: trimming and export audits plus
  platform constraints for `apps/mobile-app`.
- **P3 — View adapters**: platform-specific native views, props, and events.
  No view concepts in the portable core.

### ABI and codecs

- **P2 — Callback lifetime**: the runtime context keeps one retained entry
  per decoded callback until teardown.
- **P2 — Async start order**: the authored async method starts before
  `CreatePromise` returns, so a failure in between releases arguments that the
  running Task still uses (from 030).
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
