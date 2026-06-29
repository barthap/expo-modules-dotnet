# 03 - Implementation Phases

Last refreshed: 2026-06-29.

## Purpose

This roadmap starts from the current implemented repo state. Earlier versions
of this file treated HostFXR smoke tests, ABI skeletons, and headless wrappers
as future work. Those slices now exist in some form. Future work should build
from the real `Expo.JSI` package, native `expo_jsi` ABI, Hermes testhost, and
newer specs.

Each phase must produce working proof, written evidence where useful, and a
stop gate. Do not combine phases to save time.

## Phase 0: Current-State Audit Before Any New Slice

Goal:

Know exactly what changed since the last docs/spec update.

Read:

- `docs/README.md`
- `docs/agent-plan/01-architecture.md`
- the relevant `docs/superpowers/specs/*` file
- implementation files named by that spec

Run:

```sh
git status --short --branch
find managed/packages native/include native/testhost -maxdepth 3 -type f | sort
```

Stop gate:

Stop if repo state, current branch, or package boundary is unclear.

## Phase 1: Finish Low-Level `Expo.JSI` ABI And Wrapper Shape

Goal:

Keep `Expo.JSI` a stable low-level wrapper package over the native `expo_jsi`
ABI.

Current base:

- `native/include/expo_jsi.h`
- `native/testhost/`
- `managed/packages/Expo.JSI/`
- `managed/packages/Expo.JSI.Tests/`
- `docs/superpowers/specs/2026-06-29-jsi-abi-value-handle-slimming-design.md`

Build:

- value-handle slimming where still incomplete;
- matching native testhost updates;
- owned wrapper and scoped-ref cleanup;
- focused runtime, object, array, function, promise, error, host-function, and
  scheduler tests.

Do not build:

- module DSL;
- source generator;
- platform adapter;
- views.

Verification:

```sh
scripts/test-jsi.sh
scripts/format.sh --check --all
git diff --check
```

Stop gate:

Stop if ownership of any runtime, value, promise, argument, string, callback,
or task context cannot be explained precisely.

## Phase 2: Introduce `Expo.ModulesCore`

Goal:

Create the higher-level C# module package above `Expo.JSI`.

Build:

- `managed/packages/Expo.ModulesCore/`;
- `managed/packages/Expo.ModulesCore.Tests/`;
- minimal authored module shape;
- module registry or provider shape;
- generated-looking provider code;
- typed conversion path for the first supported parameter/return types.

Migrate:

- move temporary module behavior tests from
  `managed/packages/Expo.JSI.Tests/Modules/` when equivalent
  `Expo.ModulesCore.Tests` coverage exists.

Do not build:

- Roslyn generator in the first package-boundary slice;
- broad converter library;
- platform adapter;
- views.

Verification:

```sh
scripts/test-jsi.sh
dotnet test managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" managed/packages
scripts/format.sh --check --all
git diff --check
```

Stop gate:

Stop if `Expo.JSI` starts owning module DSL concepts or `Expo.ModulesCore`
requires raw native layouts.

## Phase 3: Source Generator Prototype

Goal:

Generate the provider shape that Phase 2 proved by hand.

Build:

- attributes such as `ExpoModule` and `JS`;
- source generator project;
- generated provider;
- diagnostics for unsupported parameter and return types;
- generated output tests.

Acceptance criteria:

- module registration is static and explicit;
- invocation uses typed wrappers;
- generated code calls module methods directly;
- unsupported type behavior is a generator diagnostic, not runtime guessing.

Verification:

```sh
dotnet test
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|JsonSerializer" managed
scripts/format.sh --check --all
git diff --check
```

Stop gate:

Stop if the generator starts compensating for unclear runtime semantics. Fix the
wrapper or module package first.

## Phase 4: NativeAOT Compatibility Proof

Goal:

Verify that HostFXR development has not invalidated the ABI or generated
binding future.

Build:

- minimal NativeAOT project or proof target;
- `[UnmanagedCallersOnly]` exported entry points where needed;
- symbol inspection note;
- trimming/AOT risk list.

Verification:

```sh
dotnet publish -c Release -r osx-arm64 /p:PublishAot=true
nm -gU <publish-output>
rg "RequiresUnreferencedCode|RequiresDynamicCode|Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke" managed
```

Stop gate:

Stop if AOT requires changing the ABI or generated binding strategy.

## Phase 5: RNW Adapter

Goal:

After the portable core and module layer are proven, integrate them into RNW
through a thin adapter.

Build only after user approval:

- RNW installation hook;
- scheduler/lifecycle mapping;
- expo-desktop connector integration if in scope;
- Windows packaging updates only as needed;
- no portable module logic in adapter files.

Windows verification:

Run on a Windows test machine when Visual Studio, RNW packaging, WinUI, or app
screenshots are needed.

Mac verification:

Use Mac for code review, docs, shared C# tests, and non-Windows build checks.

Stop gate:

Stop before publishing, opening a PR, posting GitHub comments, or treating
Windows packaging blockers as portable-core blockers.

## Phase 6: React Native macOS Adapter Proof

Goal:

Prove the core is not accidentally RNW-specific by mounting it into a future
React Native macOS host.

Build only after user approval:

- host adapter service table implementation for macOS;
- scheduler/lifecycle mapping;
- no AppKit/view work unless specifically requested.

Stop gate:

Stop if the proof would require creating a real host app without approval.

## Phase 7: Platform-Gated Views

Goal:

Add view support behind adapters after headless modules are stable.

Build:

- core view metadata only if it remains platform-neutral;
- headless no-view adapter;
- Windows view adapter for WinUI/RNW;
- future macOS view adapter for AppKit/RN macOS.

Stop gate:

Stop if view code starts pulling platform dependencies into the universal core.
