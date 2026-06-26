# 03 - Implementation Phases After Planning

## Purpose

This roadmap translates the research spikes into future implementation phases.
It is not permission to start implementation during the planning goal. Use it
as the input to a later `/goal`.

Each phase must produce working proof, written evidence, and a stop gate. Do
not combine phases to save time; the boundaries are there to keep loader,
runtime, generator, and host concerns separate.

## Phase 0: User Approval And Workspace Choice

Goal:

Decide where phase 1 work will live.

Prerequisites:

- User has reviewed these docs.
- `agent-plan/05-repo-strategy.md` has been read.
- Current repository state is known with `git status --short --branch`.

Instructions:

1. Ask the user whether to create a clean separate research repo for phase 1.
2. If approved, create the repo and keep it obviously research-only.
3. If not approved, create a clearly marked branch or directory in this repo
   and keep proof files separate from production code.
4. Record the decision in the first spike result note.

Artifacts:

- repo decision note;
- path to research repo or branch;
- statement of what remains in this repo.

Verification:

```sh
git status --short --branch
find docs -maxdepth 3 -type f | sort
```

Stop gate:

Stop until the user approves any new repository creation.

## Phase 1: Mac-Local Loader And Headless ABI Foundation

Goal:

Prove the bridge can start from macOS without Windows or RNW by completing
Spike 1 and Spike 2.

Build:

- HostFXR smoke executable;
- minimal managed entry point assembly;
- C ABI header;
- fake or stub handle table;
- C# ABI declarations;
- ABI layout tests.

Do not build:

- real RNW adapter;
- real app integration;
- views;
- source generator.

Artifacts:

- `native/include/expo_csharp_jsi.h`;
- HostFXR smoke files;
- managed interop declarations;
- tests;
- spike result notes.

Verification:

```sh
dotnet --info
dotnet build
dotnet test
cmake --build build
./build/hostfxr_smoke/hostfxr_smoke
```

Stop gate:

Stop if HostFXR loading, ABI layout, or ownership rules are unclear. Do not
paper over unclear memory ownership with "temporary" code.

## Phase 2: Headless JSI Wrapper Proof

Goal:

Complete Spike 3 by proving C# wrappers can manipulate JSI values through the
C ABI.

Build:

- runtime wrapper;
- borrowed value wrapper;
- owned value wrapper;
- object/function wrappers;
- arguments wrapper;
- string and buffer conversion;
- structured error result handling;
- wrapper lifetime tests.

Expected behavior:

- borrowed primitive argument read;
- object property read/write;
- host function callback;
- owned return value;
- explicit release of retained values;
- no JSON for ordinary values.

Verification:

```sh
dotnet test
cmake --build build
./build/headless-jsi-wrapper-proof
rg "JsonSerializer|Newtonsoft|Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke" .
```

Stop gate:

Stop if the proof cannot explain which side owns every runtime, value, string,
buffer, callback, promise, and error object involved in the call.

## Phase 3: Generated-Looking v2 Binding Proof

Goal:

Complete Spike 4 by hand-writing code that looks like future source-generator
output.

Build:

- sample authored module;
- generated-looking provider;
- module registry;
- typed argument decoder;
- return conversion;
- tests proving direct invocation.

Verification:

```sh
dotnet test
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" managed
```

Acceptance criteria:

- module registration is static and explicit;
- invocation uses typed wrappers;
- generated-looking code calls module methods directly;
- unsupported type behavior is represented as a future generator diagnostic,
  not runtime guessing.

Stop gate:

Stop for user review before building the actual Roslyn source generator.

## Phase 4: NativeAOT Compatibility Proof

Goal:

Complete Spike 5 and verify that HostFXR development has not invalidated a
NativeAOT future.

Build:

- minimal NativeAOT project;
- `[UnmanagedCallersOnly]` exported entry points;
- symbol inspection note;
- trimming/AOT risk list.

Verification:

```sh
dotnet publish -c Release -r osx-arm64 /p:PublishAot=true
nm -gU path/to/publish/*
rg "RequiresUnreferencedCode|RequiresDynamicCode|Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke" managed
```

Stop gate:

Stop if AOT requires changing the ABI or generated binding strategy.

## Phase 5: Source Generator Prototype

Goal:

Only after the generated-looking proof succeeds, build a Roslyn generator that
emits equivalent code.

Build:

- attributes such as `ExpoModule`, `JS`, `Record`;
- generator project;
- generated provider;
- diagnostics for unsupported parameter/return types;
- record converter generation;
- snapshot or generated output tests.

Verification:

```sh
dotnet test
dotnet build
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke" managed generated
```

Stop gate:

Stop if the generator starts compensating for unclear runtime semantics. Fix the
wrapper/ABI proof first.

## Phase 6: RNW Adapter In This Repository

Goal:

After the portable core is proven, integrate it into RNW through a thin adapter.
This phase likely belongs in this repository.

Build:

- RNW installation hook;
- expo-desktop connector integration;
- scheduler/lifecycle mapping;
- Windows packaging updates only as needed;
- no portable module logic in adapter files.

Windows verification:

Run on <windows-test-machine> when Visual Studio, RNW packaging, or app screenshots are
needed. The remote repo path is:

```text
<windows-repo>
```

Mac verification:

Use Mac for code review, docs, shared C# tests, and non-Windows build checks.

Stop gate:

Stop before publishing, opening a PR, or posting GitHub comments.

## Phase 7: React Native macOS Adapter Proof

Goal:

Prove the core is not accidentally RNW-specific by mounting it into a future
React Native macOS host.

Build:

- only after a host app exists or the user approves creating one;
- adapter service table implementation for macOS;
- no AppKit/view work unless specifically requested.

Stop gate:

Stop if the proof would require creating a real host app without approval.

## Phase 8: Platform-Gated Views

Goal:

Add view support behind adapters after headless modules are stable.

Build:

- core view metadata only if it remains platform-neutral;
- `NoViewAdapter` for headless mode;
- `WindowsViewAdapter` for WinUI/RNW;
- future `MacOSViewAdapter` for AppKit/RN macOS.

Stop gate:

Stop if view code starts pulling platform dependencies into the universal core.
