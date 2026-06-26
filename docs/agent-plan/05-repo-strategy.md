# 05 - Repo Strategy

## Purpose

This file decides where future implementation should happen. The recommendation
is to use a clean separate research repo for phase 1, while keeping this repo
as the planning source and later RNW adapter home.

Do not create the repo during this planning task. Ask the user first.

## Context

The current repository is valuable but busy:

- it contains Windows-specific RNW and expo-desktop work;
- old docs were moved to `docs_old/`;
- the Windows proof is still focused on app integration and packaging;
- portable-ish C# module concepts exist near Windows-specific managed code;
- native code currently mixes loading, JSI wiring, RNW registration,
  expo-desktop composition, and view integration.

Concrete inventory preserved from the historical research note:

- portable-ish managed code currently lives around `dotnet/Expo.Modules.Core`,
  including `Module`, `ModuleDefinition`, `ModuleRegistry`,
  `FunctionDescriptor`, `TypeConverter`, manifest logic, sync invocation,
  async invocation, and event callback concepts;
- the same managed assembly is not yet cleanly portable because
  `Expo.Modules.Core.csproj` targets `net9.0-windows10.0.19041.0`;
- the managed project references `Microsoft.WindowsAppSDK`;
- view-related managed code such as `ExpoView`, `ViewDefinition`, and
  `ViewRegistry` uses WinUI, WinRT, and composition concepts;
- `NativeEntryPoints.cs` includes Windows view entry points, including XAML
  runtime setup and WinRT object creation;
- native code mixes HostFXR loading and managed entry point resolution with JSI
  host object/function wiring;
- native code also includes React Native Windows package registration,
  expo-desktop runtime composition such as `global.expo.modules` delegation,
  and Windows view/composition integration.

This concrete inventory explains why the first portable unit should be smaller
than the current `Expo.Modules.Core` assembly and smaller than the current RNW
native project.

The phase 1 research question is narrower:

```text
Can a portable C# / JSI bridge be developed on macOS through HostFXR first,
while preserving a NativeAOT-compatible C ABI and generated v2 binding shape?
```

That question is easier to answer away from Windows packaging noise.

## Option A: Clean Separate Research Repo

Recommendation: use this for phase 1, with user approval.

What belongs here across the research track:

- HostFXR macOS smoke proof;
- C ABI header and handle skeleton;
- headless C++/JSI bridge proof;
- C# wrapper experiments;
- generated-looking v2 module proof;
- Roslyn source-generator prototype;
- macOS NativeAOT compatibility proof;
- spike result notes.

The first implementation goal inside the clean research repo is narrower:
implement Spike 1 and Spike 2 only. Later spikes can continue in the same
research repo after those first results are reviewed.

Advantages:

- keeps disposable proof files out of production paths;
- makes it obvious which code is research;
- avoids blocking on RNW, Visual Studio, AppX/MSIX, or WinUI;
- encourages small reproducible examples;
- makes macOS-local iteration faster;
- avoids accidental dependencies on current repo layout while it is being
  restructured.

Risks:

- proven code must later be copied, extracted, or packaged back into this repo;
- names, package identity, and final module boundaries may change;
- agents must keep result notes clear so lessons are not lost.

Mitigations:

- write result notes for every spike;
- keep exported ABI and wrapper names close to intended production names;
- avoid research-only hacks in the public shape;
- record promotion candidates explicitly.

## Option B: Current Repo

Use this later for RNW adapter integration and productionization.

What belongs here later:

- RNW package registration;
- expo-desktop connector integration;
- Windows build files;
- adapter implementation that mounts the proven core;
- view adapter work when requested;
- migration of proven portable code once stable.

Advantages:

- close to real Windows integration;
- existing C# module concepts can inform migration;
- final Windows package ownership lives here.

Risks:

- easy to mix portable bridge research with Windows packaging failures;
- current restructuring makes long-lived experiments harder to read;
- proof code may accidentally become production-shaped before architecture is
  proven;
- RNW/WinUI dependencies can leak into the universal core.

Use this repo for phase 1 only if the user declines a separate repo. If so:

- put proof code under a clearly named experimental directory;
- mark files as disposable;
- do not modify production code;
- write every spike result under `docs/spike-results/`;
- stop before touching RNW integration.

## Option C: Long-Lived Branch In Current Repo

Use this only as a fallback when a new repo is not approved but the work needs
branch isolation.

Advantages:

- no repository setup overhead;
- easy to diff against existing code;
- branch can be pushed for Mac/Windows coordination if approved.

Risks:

- research and production history become tangled;
- generated proof files may look more permanent than they are;
- Windows and macOS work can drift on separate machines.

Rules if using this option:

- branch name should make research status obvious, for example
  `codex/csharp-jsi-research`;
- commit docs and proof code separately;
- keep `.sync/` for handoff notes only;
- do not use worktrees unless explicitly asked by the user.

## Decision

Recommended path:

```text
Phase 1: clean separate research repo, after user approval.
This repo: planning source now, RNW adapter home later.
<windows-test-machine>: Windows-only proof and RNW packaging when needed.
```

This is an updated decision relative to the old research note. The old note
treated a clean repo as one possible direction; this plan recommends it because
the Windows repo is actively being restructured and the first useful proof is
headless.

## First Implementation Goal Template

When the user approves implementation, use a goal like:

```text
Create a clean local research repository for the portable C# / JSI bridge phase
1 proof. Implement Spike 1 and Spike 2 only: macOS HostFXR smoke loading,
minimal C ABI header, opaque handle skeleton, and C# ABI declarations. Do not
build RNW integration, views, source generator, or production package. Record
commands and results in docs/spike-results.
```

## Promotion Strategy

After phase 1 and phase 2 prove the shape:

1. Identify stable artifacts:
   - ABI header;
   - wrapper type names;
   - generated-looking provider shape;
   - ownership rules;
   - tests.
2. Decide whether the portable core should become:
   - a subdirectory in this repo;
   - a package consumed by this repo;
   - a separate repository with versioned outputs.
3. Promote only stable artifacts. Leave disposable host/proof scaffolding
   behind unless it remains useful as a sample.
4. Add RNW adapter code in this repo after the portable core has a stable
   interface.

## Stop Gates

Stop before:

- creating the research repo;
- moving code from research repo into this repo;
- making this repo consume a local package;
- changing final package names;
- changing the ABI after generated code depends on it;
- pushing branch state for Mac/Windows coordination unless the user approves.
