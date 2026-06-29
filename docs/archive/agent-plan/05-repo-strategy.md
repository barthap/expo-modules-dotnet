# 05 - Repo Strategy

Last refreshed: 2026-06-29.

## Purpose

This file decides where future implementation should happen. Earlier versions
recommended a clean separate research repo for phase 1. That recommendation is
now stale: this repository is already the implementation home for the portable
C# / JSI bridge core.

Do not create a new repository unless the user explicitly asks for one.

## Current Decision

Recommended path:

```text
This repo: portable core, low-level Expo.JSI package, future Expo.ModulesCore,
Hermes-backed tests, experiments, docs, and eventual adapter source.
Standalone experiments: keep under experiments/ when loader or integration
questions need isolation.
Windows test machine: Windows-only proof and RNW packaging when needed.
```

The current implementation should stay repo-local so specs, code, tests, and
spike evidence evolve together.

## What Belongs In This Repo

Portable core:

- `native/include/expo_jsi.h`
- native bridge/testhost code needed for the headless Hermes suite;
- `managed/packages/Expo.JSI`;
- future `managed/packages/Expo.ModulesCore`;
- future source-generator packages if they are part of the module stack;
- tests for low-level wrappers and module behavior;
- docs, specs, plans, and spike results.

Experiments:

- HostFXR smoke proof;
- NativeAOT smoke proof;
- Hermes/HostFXR console proof;
- future isolated loader or ABI experiments.

Adapters, after approval:

- RNW adapter source;
- React Native macOS adapter source;
- platform-gated view adapter source.

## What Does Not Belong In The Portable Core

Do not put these dependencies in the portable core packages:

- RNW package registration;
- WinUI, XAML, Windows App SDK, or packaging references;
- AppKit or React Native macOS project files;
- expo-desktop host composition;
- Visual Studio/MSBuild app packaging work;
- platform view creation.

Those belong in adapters or host-specific projects.

## Package Boundary Strategy

`Expo.JSI`:

- owns low-level runtime wrappers;
- owns ABI-facing interop;
- owns scoped ref and owned wrapper semantics;
- owns host-function and runtime-task wrapper APIs;
- does not own module DSL or generator concepts.

`Expo.ModulesCore`:

- should own authored module DSL concepts;
- should own module registry/provider abstractions;
- should own generated-binding runtime helpers;
- should own typed converters above `Expo.JSI`;
- should have its own tests.

Source generator:

- should emit direct-call provider code after the hand-written shape is proven;
- should report unsupported signatures as diagnostics;
- should avoid runtime hot-path reflection.

Adapters:

- should install the proven core into a host;
- should provide scheduler/lifecycle/platform services;
- should not own module dispatch logic.

## Experiment Promotion Strategy

When a proof succeeds:

1. Identify stable artifacts:
   - ABI functions and structs;
   - wrapper type names and ownership transitions;
   - generated-looking provider shape;
   - tests;
   - spike evidence.
2. Decide whether the artifact belongs in:
   - `native/include` or native bridge/testhost code;
   - `managed/packages/Expo.JSI`;
   - future `managed/packages/Expo.ModulesCore`;
   - `experiments/` as a permanent sample;
   - docs only.
3. Promote only stable artifacts. Leave disposable loader scaffolding behind
   unless it remains useful as a sample.
4. Update docs and tests in the same slice when public semantics change.

## Branch And Commit Hygiene

- Prefer traditional branches over worktrees.
- Use the `codex/` prefix for new branches unless the user asks otherwise.
- Do not push without explicit approval.
- Do not commit machine-specific paths, usernames, hostnames, or private local
  setup details.
- Keep local workflow notes in gitignored local files.

## Stop Gates

Stop before:

- creating a new repo;
- moving the portable core out of this repo;
- making this repo consume an unpublished local package as the normal path;
- changing final package names;
- changing the ABI after generated code depends on it;
- pushing branch state for Mac/Windows coordination unless the user approves.
