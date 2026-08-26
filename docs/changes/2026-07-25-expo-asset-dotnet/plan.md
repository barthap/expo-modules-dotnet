# Expo Asset Dotnet Implementation Plan

This is the transient, change-local plan the living-spec workflow requires
alongside `spec.md`. The full task-by-task implementation detail already
exists at `docs/plans/022-expo-asset-dotnet.md`; this file only orders the
work into verifiable slices and will be archived once the accepted delta in
`spec.md` merges into `docs/specs/`.

## Slice 1: Package skeleton

Add `package.json`, `expo-module.config.json`, `vitest.config.ts`,
`tsconfig.json`, and `src/index.ts` for `expo-asset-dotnet`; the module csproj
with its `AssemblyInfo.cs` and a stub module class; and the test csproj with
its own `AssemblyInfo.cs`. No behavior yet — this slice only wires the
package into the workspace and the autolinking metadata.

Verify with:

```sh
pnpm install
dotnet build packages/expo-asset-dotnet/dotnet/ExpoAssetDotnet.Tests/ExpoAssetDotnet.Tests.csproj
pnpm --filter expo-asset-dotnet typecheck
```

## Slice 2: Services

Implement the internal validation, cache-path, and download services
described in `spec.md`'s Accepted design: argument validation, URL scheme
classification, cache root resolution and identity, cache-hit detection, and
the download-and-move flow with module-owned cancellation.

Verify with a `dotnet build` of the module project and confirm no generator
diagnostics are reported.

## Slice 3: Tests

Add the pure behavior matrix first (validation, cache identity, cache-hit,
download, cancellation, error messages) against an injected cache root and an
injected fake `HttpMessageHandler`. Add the small Hermes-backed set second
(module visibility, `file:` passthrough, validation rejections only, no
duplication of the pure matrix). Add the two Vitest files for the TypeScript
facade last.

Verify with:

```sh
scripts/test-managed.sh --project packages/expo-asset-dotnet/dotnet/ExpoAssetDotnet.Tests/ExpoAssetDotnet.Tests.csproj
pnpm --filter expo-asset-dotnet test
```

## Slice 4: Living-spec merge and archival

Merge the accepted delta from `spec.md` into `docs/specs/modules-core-boundary.md`
and `docs/specs/dotnet-autolinking.md`, then archive this change folder.

Verify with:

```sh
scripts/test-managed.sh
scripts/format.sh --check --all
git diff --check
```
