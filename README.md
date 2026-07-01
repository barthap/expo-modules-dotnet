# Expo Modules C#

Portable C#/.NET modules for Expo and React Native, connected to JavaScript
through JSI.

The core architecture is intentionally small:

```text
C++ owns JSI mechanics.
C# owns module logic.
A C ABI with opaque handles connects them.
```

The current repo is a pnpm workspace that proves this shape with a public Expo
adapter package, an authored .NET example module, and runnable apps. It is still
early production-readiness work, not a published SDK, but the package boundaries
now match the direction of the project.

## Repository Shape

- `packages/expo-modules-dotnet` is the public Expo adapter package. It owns the
  JavaScript API, TurboModule installer, Android/iOS/macOS glue, reusable C++ JSI
  bridge, managed core packages, and Hermes-backed testhost.
- `packages/example-module` is an authored .NET Expo module package. It owns the
  example C# module, a small JS facade, and the NativeAOT publish/staging proof.
- `apps/mobile-app` is the Expo app that consumes both packages and proves the
  native path on iOS and Android.
- `apps/desktop-app` is the Expo Desktop / React Native macOS app that consumes
  both packages and proves the desktop HostFXR path.
- `apps/hermes-console-app` is a headless Hermes integration app/proof.
- `experiments/` contains narrow smoke proofs only, such as HostFXR and
  NativeAOT loader experiments.
- `docs/specs/` contains the current living specs. `docs/archive/` is historical
  evidence, not the source of truth.

## Quick Start

Install JavaScript workspace dependencies:

```bash
pnpm install
```

Run the managed/Hermes test suite:

```bash
scripts/test-managed.sh
```

Type-check the mobile proof:

```bash
pnpm --filter mobile-app typecheck
```

Build and stage the example NativeAOT module for the mobile app:

```bash
pnpm --filter example-module build:nativeaot
```

For the full iOS and Android run instructions, including native project refresh
steps, see `apps/mobile-app/README.md`.

## Current Status

The mobile proof validates that an Expo app can load the public
`expo-modules-dotnet` adapter, call through an authored `example-module`
facade, and reach C# module logic from React Native Hermes. The desktop proof
validates the same module path on React Native macOS using HostFXR by default.

Some pieces are deliberately temporary:

- .NET module autolinking does not exist yet.
- NativeAOT artifacts are manually staged into the adapter package for now.
- The package boundary still needs Windows/RNW evidence with its own React
  Native version lane.

## Development Docs

Start with `docs/README.md` for the full documentation map and workflow.

Useful next reads:

- `docs/specs/README.md` for how living specs are organized.
- `docs/roadmap.md` for the current roadmap.
- `docs/references/previous-windows-prototype.md` for historical context from
  the earlier Windows-first prototype.

Before finishing code changes, run:

```bash
scripts/format.sh --check --all
```
