# Plan 022: `expo-asset-dotnet` — native asset download and cache for Windows and macOS

> **Executor instructions**: Run the drift check first. Follow the living-spec
> workflow before code changes, including operator approval of the delta spec.
> Complete every verification command and confirm its expected result before
> moving on. Touch only the files in the In-scope list. If any STOP condition
> occurs, stop and report — do not improvise. Do not add Expo global
> registration, Metro aliases, mobile support, or a runtime platform gate.
> Do not edit `scripts/test-managed.sh` or `scripts/test-managed.ps1`.
> Update the plan index when complete unless a reviewer told you they maintain it.
>
> **Drift check (run first)**:
> ```sh
> git diff --stat 9247d75d..HEAD -- packages/expo-modules-dotnet packages/example-module packages/expo-modules-dotnet-autolinking docs/specs/ docs/changes/ docs/module-authoring-guide.md scripts/
> ```
> Compare the live code against the "Current state" excerpts below. A changed
> boundary that invalidates an excerpt is a STOP condition. This check
> deliberately includes `docs/changes/` and `scripts/` because this plan's
> dependency lives there.

## Status

- **Priority**: P1
- **Effort**: M
- **Risk**: MED (cache correctness, download failure cleanup, packaged paths)
- **Depends on**: authored-module test core — **SATISFIED** at `07311a59`.
  See "Dependency" below.
- **Category**: authored module
- **Planned at**: `9247d75d`, 2026-07-24
- **Revised at**: `a0ac5b8d`, 2026-07-24 — refined after recon, an operator
  decision round, and a design consult with the agent authoring the test-core
  spec. Six requirements changed; see "What changed in this revision".
- **Reconciled at**: `07311a59`, 2026-07-25 — the test core landed; every
  "intended" shape below was replaced with verbatim reality. Two of this plan's
  own commands were wrong and are corrected; see "Reconciliation".

## Dependency

The authored-module test core is **implemented and green**. Verified at
`07311a59` (commits `73f06a6d..07311a59`), not taken on faith:

- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Testing/`
  exists and exposes `HermesTestRuntime`, `ExpoModuleTestHost`, and
  `JavaScriptPromiseRejectedException`.
- `packages/example-module/dotnet/ExampleModule.Tests/` exists and is the
  exemplar this plan copies.
- `scripts/test-managed.sh` exits 0 with 650 tests
  (`Expo.ModulesCore.Generator.Tests` 220, `Expo.JSI.Tests` 220,
  `Expo.ModulesCore.Tests` 207, `ExampleModule.Tests` 3), and its output
  includes `==> Running ExampleModule.Tests` — proving glob discovery works.
- `Expo.ModulesCore.Tests.csproj` no longer references `ExampleModule`, so the
  "authored package owns its behavior tests" boundary is real.

Discovery means **no runner-script or workflow edit is ever needed** for this
package. Editing those files is a scope violation, not a fallback.

## Reconciliation

Two commands in the `a0ac5b8d` revision of this plan were wrong. They are fixed
below; this note exists so nobody reintroduces them.

| Wrong at `a0ac5b8d` | Correct at `07311a59` |
|---|---|
| `dotnet test <proj> --filter "Category!=Hermes"` | **No trait convention exists.** A repo-wide search finds zero `[Trait(` attributes. That filter matches nothing and would run the Hermes tests anyway, failing for want of `EXPO_JSI_TESTHOST_LIBRARY`. Use the canonical runner instead. |
| `scripts/test-managed.sh <path-to-csproj>` | `scripts/test-managed.sh --project <repo-relative-path>`. A bare positional argument is forwarded to `dotnet test`, not treated as a project selection. |

Pure and Hermes-backed tests are **not** separated by traits. Separation is by
runner choice, and whether a project needs the native testhost is decided
implicitly by whether any test body calls `ExpoModuleTestHost.Create`. That
check is lazy — see "Pure vs Hermes-backed" below.

## Why this matters

`expo-asset-dotnet` is the first real authored package in this repo. It proves
the package layout, Roslyn-generated bindings, autolinking discovery, and
promise rejection end to end on a capability people actually use, instead of on
a synthetic example. It also becomes the second consumer of the authored-module
test core, which is what validates that design for packages other than
`example-module`.

The JavaScript side deliberately reuses Expo's existing asset source resolution
and `Asset` class. This package owns only the download-and-cache operation, and
makes no claim that `expo-asset` itself resolves through Expo's global module
registry.

## Current state

### Package shape the repo already mandates

`docs/module-authoring-guide.md:19-21` requires this layout:

> A dotnet Expo module package lives at `packages/<name>/dotnet/<AssemblyName>/`
> next to its JavaScript facade in `packages/<name>/src/`.

`packages/example-module/` is the working exemplar. Its full file list is:

```
packages/example-module/package.json
packages/example-module/expo-module.config.json
packages/example-module/src/index.ts
packages/example-module/dotnet/ExampleModule/ExampleModule.csproj
packages/example-module/dotnet/ExampleModule/ExampleMathModule.cs
packages/example-module/dotnet/ExampleModule/ExampleCounter.cs
```

Note what it does **not** have: no `tsconfig.json`, no `test` script, no
`__tests__`, and no `.Tests` project. It is an exemplar for the module project
only, not for tests.

### Module csproj to copy — `packages/example-module/dotnet/ExampleModule/ExampleModule.csproj` (verbatim)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="../../../expo-modules-dotnet/managed/packages/Expo.JSI/Expo.JSI.csproj" />
    <ProjectReference Include="../../../expo-modules-dotnet/managed/packages/Expo.ModulesCore/Expo.ModulesCore.csproj" />
    <ProjectReference
      Include="../../../expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/Expo.ModulesCore.Generator.csproj"
      Condition="'$(PublishAot)' != 'true'"
      OutputItemType="Analyzer"
      ReferenceOutputAssembly="false" />
    <Analyzer
      Include="../../../expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/bin/Debug/netstandard2.0/Expo.ModulesCore.Generator.dll"
      Condition="'$(PublishAot)' == 'true'" />
  </ItemGroup>

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <GenerateRuntimeConfigurationFiles>true</GenerateRuntimeConfigurationFiles>
    <AssemblyName>ExampleModule</AssemblyName>
    <RootNamespace>ExampleModule</RootNamespace>
  </PropertyGroup>

  <PropertyGroup Condition="'$(PublishAot)' == 'true'">
    <NativeLib>Shared</NativeLib>
  </PropertyGroup>

</Project>
```

The `PublishAot` split is load-bearing: normal builds reference the generator as
a live analyzer project, AOT publishes use its prebuilt DLL. Keep both.

### Autolinking metadata — `packages/example-module/expo-module.config.json` (verbatim)

```json
{
  "platforms": ["dotnet"],
  "dotnet": {
    "projects": [
      {
        "path": "dotnet/ExampleModule/ExampleModule.csproj",
        "assemblyName": "ExampleModule"
      }
    ]
  }
}
```

The accepted schema is exactly this, from
`packages/expo-modules-dotnet-autolinking/src/types.ts` (verbatim):

```ts
export interface RawDotnetConfig {
  projects?: { path: string; assemblyName?: string }[];
}
```

**There is no per-platform selector.** A package cannot declare Windows-only or
macOS-only projects. `docs/specs/dotnet-autolinking.md:79-81` also requires one
single aggregated native library. So this package ships **one portable
assembly**. See "Maintenance notes" for why the Windows/macOS restriction is
documentation-only today.

### Module class shape — `ExampleMathModule.cs` (excerpt, verbatim)

```csharp
using Expo.ModulesCore;

namespace ExampleModule;

[ExpoModule("ExampleModule", Classes = new[] { typeof(ExampleCounter) })]
public sealed partial class ExampleMathModule : Module
{
  public ExampleMathModule(DotnetRuntimeContext context)
      : base(context)
  {
  }

  [OnDestroy]
  public void OnDestroy()
  {
    Console.WriteLine("ExampleModule destroyed");
  }

  [JS]
  public async Task<string> GetMessageAsync()
  {
    await Task.Yield();
    return "Hello from async C#";
  }
}
```

**Constructor constraint — this bounds the whole test design.**
`packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs:28`:

```
"Module '{0}' must have a public or internal parameterless constructor or a constructor accepting DotnetRuntimeContext"
```

`DotnetRuntimeContext` exposes only `Runtime`, `Objects`, `ModuleRegistry`,
`Events`, `Dispose` — no service container. **You therefore cannot inject a
fake `HttpMessageHandler` or a temp cache root through the module
constructor.** The module must construct its own services from a default
factory. Consequences, which the test plan already accounts for:

- Pure service tests inject freely and cover the full behavior matrix.
- Hermes-backed tests exercise the real module and can only cover paths that
  need neither the network nor a writable cache root: argument-validation
  rejections and `file:` pass-through.

### Async and rejection semantics — `docs/module-authoring-guide.md:186-204`

A `[JS]` method returning `Task<T>` becomes a promise-returning JavaScript
function. Argument decode failures, synchronous throws, and faulted or canceled
tasks all **reject** the promise rather than throwing synchronously. `[OnCreate]`
and `[OnDestroy]` are supported parameterless lifecycle hooks (guide §6).

No inbound `CancellationToken` is available to a `[JS]` method — the generator's
supported-parameter list does not include it. Cancellation must therefore be
module-owned; see Step 3.

### JS facade pattern — `packages/example-module/src/index.ts` (excerpt, verbatim)

```ts
import {
  DotnetModule,
  requireDotnetModule,
  type EventSubscription,
} from 'expo-modules-dotnet';

declare class ExampleModuleType extends DotnetModule<ExampleModuleEvents> {
  add(a: number, b: number): number;
  getMessageAsync(): Promise<string>;
  readonly ready: boolean;
}

const nativeModule = requireDotnetModule<ExampleModuleType>('ExampleModule');

export function add(a: number, b: number): number {
  return nativeModule.add(a, b);
}
```

`requireDotnetModule` is the only registration target in this plan, from
`packages/expo-modules-dotnet/src/index.ts` (verbatim):

```ts
export function requireDotnetModule<T>(name: string): T {
  ensureInstalled();

  const module = globalThis._expoDotnet?.modules?.[name];
  if (module == null) {
    throw new Error(`Module '${name}' is not registered. Check that it is autolinked correctly.`);
  }

  return module as T;
}
```

### The behavior being ported

Upstream `expo-asset@57.0.2` `src/ExpoAsset.ts` defines the contract this
package must match argument-for-argument:

```ts
export async function downloadAsync(
  url: string,
  md5Hash: string | null,
  type: string
): Promise<string> {
  return AssetModule.downloadAsync(url, md5Hash, type);
}
```

`apps/desktop-app/node_modules/expo-desktop-stubs/windows/ExpoDesktopStubs/ExpoAsset.h`
is the Windows behavioral reference. Its load-bearing decisions:

- `file:` URLs (case-insensitive prefix match) resolve to the **input string
  unchanged**, with no filesystem access:
  `if (IsFileUrl(url)) { promise.Resolve(url); return; }`.
- Cache id is `md5Hash` verbatim when supplied, otherwise the lowercase hex MD5
  of the URL's UTF-8 bytes.
- Filename is `ExponentAsset-<cacheId>.<type>`, written directly into the cache
  root.
- Cache hit requires: no `md5Hash` supplied (trusted), **or** the cached file's
  content MD5 equals the supplied hash. A mismatch or unreadable file falls
  through to a re-download — it is never an error.
- Freshly downloaded bytes are **not** hash-verified.
- Writes go to a `<filename>.download` temp file, then move into place with
  `ReplaceExisting`.
- Error message families, verbatim:
  `"Unable to download asset from url: '" + url + "'"` and
  `"Unable to save asset to directory: '" + localPath + "'"`, each optionally
  followed by `": " + detail`.

Two defects in that reference which this plan deliberately fixes: it
interpolates caller-supplied `md5Hash` and `type` straight into a filename with
no validation (a path-traversal hole), and its fixed `.download` temp name races
between concurrent downloads of the same asset.

### Conventions for the test project

`InternalsVisibleTo` precedent, from
`packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Expo.ModulesCore.csproj:13-15`:

```xml
<AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
  <_Parameter1>Expo.ModulesCore.Tests</_Parameter1>
</AssemblyAttribute>
```

Managed test conventions, from the three existing test projects: xUnit v3
`3.2.0`, `Microsoft.NET.Test.Sdk` `18.0.0`,
`xunit.runner.visualstudio` `3.1.5`, `net10.0`, `Nullable`/`ImplicitUsings`
enabled, `IsPackable=false`. There is **no** `Directory.Packages.props` and no
`global.json`, so pin those versions directly in the csproj. `.editorconfig`
sets 2-space indent for `*.cs` repo-wide.

There is **no existing `HttpClient`, `HttpMessageHandler`, or temp-directory
test helper anywhere in the managed tree.** This package establishes that
convention; you are not matching an existing one.

JavaScript tests use Vitest. `packages/expo-modules-dotnet/vitest.config.ts`
(verbatim) is the config to copy:

```ts
import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    include: ['src/**/__tests__/**/*.test.ts'],
  },
});
```

`scripts/format.py` formats C# per-`.csproj` via `dotnet format <project>
whitespace` and discovers files with `git ls-files`, so **new files must be
`git add`ed before `scripts/format.sh --check --all` sees them.** It does not
format TypeScript.

### `Expo.ModulesCore.Testing` — the real public API at `07311a59`

Three public types only: `ExpoModuleTestHost`, `HermesTestRuntime`,
`JavaScriptPromiseRejectedException`. Everything else is internal, and its
`AssemblyInfo.cs` grants internals to `Expo.ModulesCore.Tests` **only** — an
authored package's test project gets the public surface and nothing more.

`ExpoModuleTestHost` public members (verbatim signatures):

```csharp
public HermesTestRuntime TestRuntime { get; }
public JavaScriptRuntime Runtime { get; }

public static ExpoModuleTestHost Create(
    Action<DotnetRuntimeContext, JavaScriptObject> register
)

public JavaScriptValue Evaluate(
    string source,
    string sourceUrl = "expo-module-test.js"
)

public Task<JavaScriptValue> EvaluatePromiseAsync(
    string expression,
    CancellationToken cancellationToken = default
)

public Task<JavaScriptValue> EvaluatePromiseAsync(
    string expression,
    TimeSpan timeout,
    CancellationToken cancellationToken = default
)

public void Dispose()
```

There is exactly **one** `Create` overload and no parameterless one. The default
promise timeout is 5 seconds. `ActivePromiseEvaluationCount` is **internal** —
do not reference it from this package's tests.

`HermesTestRuntime` public members: `Runtime`, `static Create()`,
`Evaluate(string source, string sourceUrl = "expo-modules-test-core.js")`,
`DrainTasks()`, `WaitUntilIdle()`, `Dispose()`. Reach them via
`host.TestRuntime`.

`JavaScriptPromiseRejectedException` exposes `JavaScriptName` and
`JavaScriptStack` alongside `Message`. Its constructor is internal, so tests
only ever catch it.

### Real host usage — `ExampleModule.Tests/ExampleModuleShowcaseTests.cs` (excerpt, verbatim)

```csharp
using var host = ExpoModuleTestHost.Create(
    ExpoModulesProvider_ExampleModule.Register
);
var result = host.Runtime.Execute(_ =>
{
  using var value = host.Evaluate(
      "const module = globalThis._expoDotnet.modules.ExampleModule; ...",
      "example-counter-shared-object.js"
  );
  return value.AsString();
});
```

The file imports `using Expo.ModulesCore.Generated;` and passes the method group
`ExpoModulesProvider_ExampleModule.Register`. Synchronous evaluation is wrapped
in `host.Runtime.Execute(_ => { ... })`, and each evaluated `JavaScriptValue` is
disposed with `using`.

### Real rejection-assertion style — `Expo.ModulesCore.Tests/Testing/ExpoModuleTestHostTests.cs:112-128` (excerpt, verbatim)

```csharp
var exception = await Assert.ThrowsAsync<JavaScriptPromiseRejectedException>(
    () => host.EvaluatePromiseAsync(
        "Promise.reject(new TypeError('bad input'))",
        TestContext.Current.CancellationToken
    )
);

Assert.Equal("TypeError", exception.JavaScriptName);
Assert.Equal("bad input", exception.Message);
Assert.False(string.IsNullOrWhiteSpace(exception.JavaScriptStack));
```

Copy this shape for the Hermes rejection cases.

### The exemplar test csproj — `packages/example-module/dotnet/ExampleModule.Tests/ExampleModule.Tests.csproj` (verbatim, full file)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.0" />
    <PackageReference Include="xunit.v3" Version="3.2.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../ExampleModule/ExampleModule.csproj" />
    <ProjectReference Include="../../../expo-modules-dotnet/managed/packages/Expo.ModulesCore.Testing/Expo.ModulesCore.Testing.csproj" />
  </ItemGroup>
</Project>
```

`ExampleModule.Tests/AssemblyInfo.cs` (verbatim, full file):

```csharp
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

### Pure vs Hermes-backed

Both live in one project. `ExampleModule.Tests` proves it: `ExampleCounterTests`
constructs `new global::ExampleModule.ExampleCounter(10)` with no Hermes at all,
alongside `ExampleModuleShowcaseTests`, which creates a host.

`EXPO_JSI_TESTHOST_LIBRARY` is read **lazily**, inside
`HermesTestRuntime.Create()`. A test that never creates a host runs fine under a
plain `dotnet test` with no environment variable. There is no project-level
"this project is pure" switch — it is purely a per-test-body property.

### Runner selection — the exact grammar

`scripts/test-managed.sh` parses `--project <repo-relative-path>` (repeatable);
anything else is forwarded to `dotnet test`. It rejects an absolute path, a
missing file, a symlink, a filename not ending in `.Tests.csproj`, a path
resolving outside the repo, and a duplicate selection. Discovery when no
`--project` is given is:

```bash
find "$repo_root/packages" -mindepth 4 -maxdepth 4 -type f \
    -path '*/dotnet/*.Tests/*.Tests.csproj' -print | LC_ALL=C sort
```

`scripts/test-managed.ps1` takes `-Project <string[]>` with the same validation
rules. The `mindepth 4 -maxdepth 4` bound is why the directory must sit directly
under `dotnet/` — a nested test project would not be found.

## What changed in this revision

Six requirements from the `9247d75d` draft were wrong or unbuildable. An
executor following the old text would have produced the wrong module.

| Old requirement | Now |
|---|---|
| Reject non-Windows/macOS at runtime with `PlatformNotSupportedException` | **Removed.** Platform support is a link-time concern, not a runtime check. No OS gate anywhere. |
| "supplied hashes verify existing **and downloaded** bytes" (old Step 1.6, Step 3.4) | **Removed.** Match upstream: verify the cached file only; never verify downloaded bytes; never reject on hash mismatch. |
| Implied hand-registration in the managed runners | **Forbidden.** Test-core glob discovery handles it. Editing those scripts is now a scope violation. |
| "canonical `file:` URLs" | Return the input string **unchanged**, matching the reference. Do not canonicalize. |
| `type` "sanitization" | **Reject** invalid `type` with a catchable error. No silent rewriting. |
| Merge into "authored-module/lifecycle living spec sections" (no such files) | Named targets: `docs/specs/modules-core-boundary.md` and `docs/specs/dotnet-autolinking.md`. |

## Commands you will need

Each verified to exist during recon. `dotnet` is 10.0.201; the Hermes destroot
and `build/jsi-testhost/libexpo_jsi_testhost.dylib` are already present on a
prepared macOS host.

| Purpose | Command | Expected on success |
| --- | --- | --- |
| Install workspace | `pnpm install` | exit 0, `pnpm-lock.yaml` updated |
| Package JS tests | `pnpm --filter expo-asset-dotnet test` | exit 0, all pass |
| Package typecheck | `pnpm --filter expo-asset-dotnet typecheck` | exit 0 |
| Pure tests, before any Hermes test exists | `dotnet test packages/expo-asset-dotnet/dotnet/ExpoAssetDotnet.Tests/ExpoAssetDotnet.Tests.csproj` | exit 0, all pass |
| Whole package test project | `scripts/test-managed.sh --project packages/expo-asset-dotnet/dotnet/ExpoAssetDotnet.Tests/ExpoAssetDotnet.Tests.csproj` | exit 0, all pass |
| Full managed regression | `scripts/test-managed.sh` | exit 0; output contains `==> Running ExpoAssetDotnet.Tests` |
| Autolinking regression | `pnpm --filter expo-modules-dotnet-autolinking test` | exit 0 |
| Formatting | `scripts/format.sh --check --all` | exit 0 |
| Whitespace hygiene | `git diff --check` | no output |

Three cautions. `pnpm install --frozen-lockfile` **will fail** in this plan — you
are adding a workspace package and new devDependencies, so the lockfile must
change; use plain `pnpm install`. Plain `dotnet test` on the package works only
while the project contains no Hermes-backed test; the moment one calls
`ExpoModuleTestHost.Create` it throws
`"EXPO_JSI_TESTHOST_LIBRARY is not set. Run scripts/test-managed.sh or scripts/test-managed.ps1."`,
so switch to the `--project` runner form from then on. And do **not** try to
carve the suite up with `--filter` on a trait: no trait convention exists in
this repo, and inventing one here would diverge from `ExampleModule.Tests`.

## Suggested executor toolkit

- `.agents/skills/living-spec-workflow/SKILL.md` — mandatory for Step 1 and
  Step 5. Note its verification block names `scripts/test-jsi.sh`, which does
  not exist; `AGENTS.md`'s `scripts/test-managed.sh` is authoritative.
- `docs/module-authoring-guide.md` — sections 1-4 and 9 cover everything this
  module needs. Sections 5, 7, 8 (events, callbacks, shared objects) do not
  apply.
- `docs/changes/2026-07-24-authored-module-test-core/spec.md` — read the
  "Public runtime API", "Promise evaluation", and "Managed runners" sections
  before writing any Hermes-backed test.

## Scope

**In scope** — create these files and no others:

- `packages/expo-asset-dotnet/package.json`
- `packages/expo-asset-dotnet/expo-module.config.json`
- `packages/expo-asset-dotnet/vitest.config.ts`
- `packages/expo-asset-dotnet/tsconfig.json`
- `packages/expo-asset-dotnet/src/index.ts`
- `packages/expo-asset-dotnet/src/__tests__/index.test.ts`
- `packages/expo-asset-dotnet/src/__tests__/autolinking.test.ts`
- `packages/expo-asset-dotnet/dotnet/ExpoAssetDotnet/ExpoAssetDotnet.csproj`
- `packages/expo-asset-dotnet/dotnet/ExpoAssetDotnet/ExpoAssetModule.cs`
- `packages/expo-asset-dotnet/dotnet/ExpoAssetDotnet/AssetDownloadService.cs`
- `packages/expo-asset-dotnet/dotnet/ExpoAssetDotnet/AssetCachePaths.cs`
- `packages/expo-asset-dotnet/dotnet/ExpoAssetDotnet/AssetRequestValidation.cs`
- `packages/expo-asset-dotnet/dotnet/ExpoAssetDotnet.Tests/ExpoAssetDotnet.Tests.csproj`
- `packages/expo-asset-dotnet/dotnet/ExpoAssetDotnet.Tests/AssemblyInfo.cs`
- `packages/expo-asset-dotnet/dotnet/ExpoAssetDotnet.Tests/*.cs` (test files)
- `docs/changes/2026-<mm-dd>-expo-asset-dotnet/spec.md` and `plan.md`
- `pnpm-lock.yaml` (regenerated, not hand-edited)
- `docs/specs/modules-core-boundary.md`, `docs/specs/dotnet-autolinking.md`
  (Step 5 only)
- `docs/plans/README.md` (status row only)

**Out of scope** — do not touch, even though they look related:

- `scripts/test-managed.sh`, `scripts/test-managed.ps1` — discovery handles the
  new project. Editing them duplicates the test-core work and will conflict.
- `.github/workflows/*` — discovery means no workflow edit is needed.
- `apps/desktop-app/**`, `apps/mobile-app/**` — adding this package as an app
  dependency mutates the app build graph and the autolinking-generated
  `DesktopApp.sln` for no extra signal. The test plan proves autolinking
  without it.
- `packages/expo-modules-dotnet/**` and `packages/example-module/**` — no
  change to the adapter, the core, or the example module.
- `globalThis.expo.modules`, package-specifier aliasing, Metro config — any
  `expo-asset` drop-in compatibility claim is deferred work.
- Android/iOS, asset metadata resolution, reimplementing Expo's `Asset` class,
  image decoding, cache-management APIs, non-HTTP(S)/`file:` URI schemes.
- A shared filesystem API with plan 024. A common internal helper may be
  extracted later by an explicit follow-up; do not introduce a shared package
  here.

## Git workflow

- Branch: `advisor/022-expo-asset-dotnet` (already exists off `development`).
- Conventional commits, matching `git log` style, one per step:
  `feat(expo-asset-dotnet): add package skeleton and autolinking metadata`.
- Commit the approved `spec.md` on its own, then `plan.md` on its own, before
  implementation.
- Before every commit, scan staged content for machine-local absolute paths,
  usernames, machine names, and private hostnames. Use `<repo>` placeholders in
  docs.
- Do not push, open a PR, or publish without explicit operator approval.

## Steps

### Step 1: Get the module contract approved

Create `docs/changes/2026-<mm-dd>-expo-asset-dotnet/spec.md` through
`.agents/skills/living-spec-workflow/SKILL.md`, then its `plan.md`. The
contract below is already settled by the operator — write it up, present it,
and do not reopen these decisions.

**Identity.** Package `expo-asset-dotnet`; native module name `ExpoAsset`;
assembly and root namespace `ExpoAssetDotnet`. Registration is
`requireDotnetModule<T>('ExpoAsset')` against `_expoDotnet.modules` only.

**Surface.** Exactly one member:
`downloadAsync(url: string, md5Hash: string | null, type: string): Promise<string>`.
Argument order matches upstream `expo-asset` exactly.

**Validation** — all failures reject with a catchable JavaScript `Error`:

- `url`: non-empty after trimming, and parses as an absolute URI.
- `type`: must match `^[A-Za-z0-9]{1,16}$`. Anything else — separators, dots,
  traversal, empty, over-long — rejects. Do not rewrite it.
- `md5Hash`: `null` is accepted. Otherwise must match `^[0-9a-fA-F]{32}$`,
  compared case-insensitively and normalized to lowercase. Malformed rejects.

**URL classes.**

- Scheme `file` (case-insensitive): resolve with the **input string unchanged**.
  No filesystem access, no existence check, no canonicalization. A `file:` URL
  that does not parse as an absolute URI rejects.
- Schemes `http` / `https`: the download path.
- Any other scheme: reject with a message naming the offending scheme.

**Cache root.** Resolved per OS, with no runtime platform gate:

- Windows: `Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)`.
- macOS: `<UserProfile>/Library/Caches`.
- Anything else: `$XDG_CACHE_HOME` when set and absolute, else
  `<UserProfile>/.cache`.

Then append the `ExponentAsset` subdirectory and create it with
`Directory.CreateDirectory`. Never derive the root from
`Environment.CurrentDirectory`. If the resolved root is empty or whitespace, or
creation fails, reject with the underlying cause. The resolved root is a
**constructor dependency of the service**, so tests supply a temp directory.

**Cache identity.** `cacheId` is the normalized lowercase `md5Hash` when
supplied, otherwise the lowercase hex MD5 of the URL's UTF-8 bytes (matching
the reference, so both implementations name files identically). MD5 here is a
cache key, not a security primitive — say so in a code comment. Filename is
`ExponentAsset-<cacheId>.<type>`. Because `cacheId` is 32 hex characters and
`type` is `^[A-Za-z0-9]{1,16}$`, the name cannot contain a separator by
construction; still assert the combined path stays under the cache directory as
defence in depth.

**Cache hit, matching upstream.** If the target file exists: with no `md5Hash`,
it is a hit. With an `md5Hash`, compute the file's content MD5 — equal is a hit,
unequal or unreadable is a **miss that re-downloads**. A hash mismatch is never
an error.

**Download.** GET through an injected `HttpMessageHandler`. Non-2xx rejects with
the status in the message. Write the body to a temp file in the same directory
named `<filename>.<8 hex chars>.download` — unique per attempt, unlike the
reference's fixed `.download`, so concurrent downloads cannot clobber each
other. Then `File.Move(temp, final, overwrite: true)`. Do **not** hash-verify
downloaded bytes. Delete the temp file on every failure path, swallowing delete
errors. Dispose responses, streams, and hashing objects. Resolve with
`new Uri(finalPath).AbsoluteUri`.

**Cancellation.** No inbound token exists from JavaScript. The module owns a
`CancellationTokenSource`, cancels it from `[OnDestroy]`, and passes its token
into every service call. Service methods accept a `CancellationToken`. A
cancelled operation rejects and leaves no temp file.

**Errors.** Reuse the reference's two families —
`Unable to download asset from url: '<url>'[: <detail>]` and
`Unable to save asset to directory: '<dir>'[: <detail>]`. Validation errors get
their own explicit messages. Do not embed local absolute paths in messages;
name the cache subdirectory and the file name, not the full path.

Present the spec for approval. **Commit the approved `spec.md` and `plan.md`
before writing any implementation code.**

### Step 2: Add the package skeleton

Confirm `Expo.ModulesCore.Testing` exists before starting. If it does not, STOP.

Create `package.json` modelled on `packages/example-module/package.json` but
adding the test wiring that example-module lacks: `"test": "vitest run"`,
`"typecheck": "tsc -p tsconfig.json --noEmit"`, and devDependencies on
`vitest` `^3.0.0` and `typescript`. Copy `vitest.config.ts` verbatim from
`packages/expo-modules-dotnet/vitest.config.ts`. Model `tsconfig.json` on
`packages/expo-modules-dotnet/tsconfig.json` (`noEmit`, `include: ["src"]`,
`exclude` the tests) and point `main`/`types` at `src/index.ts`.

Create `expo-module.config.json` declaring one project, path
`dotnet/ExpoAssetDotnet/ExpoAssetDotnet.csproj`, `assemblyName`
`ExpoAssetDotnet`.

Create `ExpoAssetDotnet.csproj` by copying `ExampleModule.csproj` above and
changing `AssemblyName`/`RootNamespace` to `ExpoAssetDotnet`. Keep the
`PublishAot` generator split exactly. Drop `AllowUnsafeBlocks` — this package
has no unsafe code.

Add `dotnet/ExpoAssetDotnet/AssemblyInfo.cs` so the test project can reach the
internal services and the module's cancellation token:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ExpoAssetDotnet.Tests")]
```

Two notes on this, because it is the one place this package deliberately goes
beyond the exemplar. `ExampleModule` has **no** `InternalsVisibleTo` and no
`AssemblyInfo.cs` — its tests use public API only, because `ExampleCounter` is
public. This package differs: its download, cache, and validation services are
implementation detail, and making them public purely to test them would widen
the assembly's surface for no benefit. The pattern used here is the repo's own,
copied from `Expo.ModulesCore.Testing/AssemblyInfo.cs:3`
(`[assembly: InternalsVisibleTo("Expo.ModulesCore.Tests")]`) — an existing
convention applied at a new layer, not a new invention. If a reviewer prefers
public services instead, that is a legitimate alternative; make it a deliberate
decision rather than drifting into it.

Create the test project at
`packages/expo-asset-dotnet/dotnet/ExpoAssetDotnet.Tests/ExpoAssetDotnet.Tests.csproj`.
The directory must sit **directly** under `dotnet/`, end in `.Tests`, and the
file must end in `.Tests.csproj` — the discovery `find` is bounded to
`-mindepth 4 -maxdepth 4`, so a nested layout is silently not found. Copy the
verbatim `ExampleModule.Tests.csproj` from "Current state" above, changing only
the production reference:

```xml
<ProjectReference Include="../ExpoAssetDotnet/ExpoAssetDotnet.csproj" />
```

Keep the `Expo.ModulesCore.Testing` reference as-is. No generator reference —
the generated provider belongs to the production assembly. No direct `Expo.JSI`
or `Expo.ModulesCore` reference.

Add `ExpoAssetDotnet.Tests/AssemblyInfo.cs`, byte-identical to
`ExampleModule.Tests/AssemblyInfo.cs`:

```csharp
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

Write a stub `ExpoAssetModule.cs` with the `[ExpoModule("ExpoAsset")]`
attribute, a `DotnetRuntimeContext` constructor, and a `downloadAsync` that
throws `NotImplementedException`, so the generator runs and the projects
compile.

Write the JS facade `src/index.ts` following the example-module pattern: a
`declare class` extending `DotnetModule`, a
`requireDotnetModule<ExpoAssetNativeModule>('ExpoAsset')` call, and one narrowly
typed exported `downloadAsync`. Do **not** reimplement Expo's asset resolver or
`Asset` class.

**Verify**: `pnpm install` → exit 0. Then
`dotnet build packages/expo-asset-dotnet/dotnet/ExpoAssetDotnet.Tests/ExpoAssetDotnet.Tests.csproj`
→ exit 0. Then `pnpm --filter expo-asset-dotnet typecheck` → exit 0.

### Step 3: Implement validation, cache, and download

Write the three internal services, all OS-agnostic and free of any platform
gate:

- `AssetRequestValidation` — pure static validation of `url`, `md5Hash`, `type`
  per Step 1. Returns a normalized request or throws.
- `AssetCachePaths` — resolves the per-OS cache root, appends `ExponentAsset`,
  builds the filename, and asserts containment. The root is injectable.
- `AssetDownloadService` — takes the cache directory and an
  `HttpMessageHandler` (or `HttpClient`) as constructor dependencies. Owns the
  cache-hit check, hashing, temp-file write, atomic move, and cleanup.

Keep `ExpoAssetModule` thin: own a `CancellationTokenSource`, cancel it in
`[OnDestroy]`, construct the default services, validate, delegate, and return
the resolved URI. Because the generator forbids a DI constructor, the default
service construction lives in the module itself.

Requirements, restated as the checks a reviewer will make:

1. A valid `file:` URL returns the input string unchanged and opens no socket.
2. Filenames are built only from validated components; no separator or traversal
   can reach the path.
3. A cache hit is returned only when no hash was supplied, or the cached file's
   content hash matches.
4. Downloads land via a uniquely named same-directory temp file, then an atomic
   `File.Move(..., overwrite: true)`.
5. Every failure path deletes the temp file and disposes streams, responses,
   cancellation registrations, and hashing objects.
6. Rejections preserve the underlying cause without leaking local absolute
   paths.

**Verify**: `dotnet build packages/expo-asset-dotnet/dotnet/ExpoAssetDotnet/ExpoAssetDotnet.csproj`
→ exit 0, no generator diagnostics.

### Step 4: Write the tests

See "Test plan" below for the full case list. Land the pure service tests first
and confirm them with a plain `dotnet test` (which still works while no test
creates a host), then add the Hermes-backed set and switch permanently to the
`--project` runner form.

**Verify**:
`scripts/test-managed.sh --project packages/expo-asset-dotnet/dotnet/ExpoAssetDotnet.Tests/ExpoAssetDotnet.Tests.csproj`
→ exit 0. Then `pnpm --filter expo-asset-dotnet test` → exit 0.

### Step 5: Merge docs and verify

Commit `89f54cc6` already added "Authored Packages Own Their Behavior Tests" to
`docs/specs/modules-core-boundary.md`, and the runner discovery and selection
rules to `docs/specs/hermes-testhost.md`. **Do not restate those.** Your delta is
only what this package newly establishes:

- `docs/specs/modules-core-boundary.md` — that an authored module cannot receive
  injected dependencies through its constructor (the generator permits only a
  parameterless or `DotnetRuntimeContext` constructor), and the consequence:
  implementation services are internal, exposed to the package's own test
  assembly via `InternalsVisibleTo`, with the full behavior matrix living in
  pure tests and the Hermes tier confined to binding-level proof.
- `docs/specs/dotnet-autolinking.md` — that a package declares one portable
  assembly and that per-platform project selection does not exist.

Add a short section to `docs/module-authoring-guide.md` only if the guide's
existing text would otherwise contradict what landed. Its section 11 already
documents the `.Tests` shape and both runner commands. Archive
`docs/changes/2026-<mm-dd>-expo-asset-dotnet/plan.md` per the living-spec
workflow (the plan is transient; the spec's accepted requirements move into
`docs/specs/`). Mark plan 022 DONE in `docs/plans/README.md`.

**Verify**: the full Done criteria below, in order.

## Test plan

All tests run offline. No test may touch the public network. Every test owns
its own temp cache directory and its own fake handler, and deletes the
directory on dispose.

Both tiers live in **one** project, exactly as `ExampleModule.Tests` mixes
`ExampleCounterTests` (pure) with `ExampleModuleShowcaseTests` (Hermes). Do not
tag them with traits and do not split them into two projects.

**Pure service tests** — no host, no network, no environment variable.
Model the file layout and naming on
`packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Codecs/ArrayBufferCodecTests.cs`:
file-scoped namespace, `public sealed class <Subject>Tests`, `[Fact]` methods
named `MethodUnderTest_ExpectedBehavior`, and
`TestContext.Current.CancellationToken` for async waits.

Validation:
1. Empty and whitespace `url` reject.
2. `url` that does not parse as an absolute URI rejects.
3. Scheme `ftp:` rejects, and the message names the scheme.
4. `type` values `""`, `"../evil"`, `"png/x"`, `"p.g"`, and a 17-character
   string each reject.
5. `md5Hash` values of 31 chars, 33 chars, and non-hex each reject.
6. A valid uppercase `md5Hash` is accepted and normalized to lowercase.

`file:` handling:
7. A valid `file:` URL resolves to the byte-identical input string, and the fake
   handler records zero requests.
8. A malformed `file:` URL rejects.

Cache identity and paths:
9. With `md5Hash` null, the filename is `ExponentAsset-<md5(url)>.<type>`.
10. With `md5Hash` supplied, the filename uses that hash.
11. The `ExponentAsset` subdirectory is created when absent.
12. The resolved path is inside the cache directory.
13. Cache-root resolution returns `%LOCALAPPDATA%` shape on Windows,
    `Library/Caches` on macOS, and an XDG path otherwise — assert per the
    current OS only, so the suite passes on all three.

Cache hits and misses:
14. File exists, `md5Hash` null → hit, zero HTTP requests.
15. File exists, hash matches → hit, zero HTTP requests.
16. File exists, hash mismatches → exactly one HTTP request, file replaced,
    resolves. Asserting it does **not** reject is the point of this test.
17. File exists but is unreadable as a hash source → re-downloads.

Download behavior:
18. 200 with a body writes the file and resolves to its `file:` URI.
19. Downloaded bytes that do not match the supplied hash still resolve, and the
    file is written. This pins the upstream-matching decision.
20. 404 and 500 reject with the status in the message, and no file exists at the
    destination.
21. A handler that throws rejects, and no `.download` temp file remains.
22. A cancelled token rejects and leaves no temp file.
23. `Task.WhenAll` over several concurrent downloads of *different* assets
    inside one test method all succeed with no cross-contamination. Cross-test
    parallelism stays disabled; concurrency inside a single test is the
    supported shape.
24. Two concurrent downloads of the *same* asset both resolve, and exactly one
    file exists at the destination.

Module wiring:
25. `[OnDestroy]` cancels the module's token — assert through
    `InternalsVisibleTo`.

**Hermes-backed tests** — untagged, in the same project, run via the canonical
runner. `using Expo.ModulesCore.Generated;` at the top, then one
`ExpoModuleTestHost` per test disposed with `using`:

```csharp
using var host = ExpoModuleTestHost.Create(
    ExpoModulesProvider_ExpoAssetDotnet.Register
);
```

Wrap synchronous evaluation in `host.Runtime.Execute(_ => { ... })` and dispose
each `JavaScriptValue` with `using`, per the `ExampleModuleShowcaseTests`
excerpt above. For rejections, copy the `Assert.ThrowsAsync` shape from
`ExpoModuleTestHostTests.cs:112-128` and assert on `JavaScriptName` and
`Message`. Do **not** assert `ActivePromiseEvaluationCount` — it is internal to
`Expo.ModulesCore.Testing` and not visible here.

These must **not** repeat the service matrix. They prove binding-level behavior
only, and because the module builds its own services they are limited to paths
needing neither network nor a writable cache:

26. The module is visible as `_expoDotnet.modules.ExpoAsset` with a
    `downloadAsync` function.
27. A `file:` URL round-trips through the real generated binding and resolves to
    the same string.
28. An invalid `type` rejects with `JavaScriptPromiseRejectedException`, whose
    `Message` names the problem.
29. An invalid `md5Hash` rejects the same way.
30. A non-HTTP scheme rejects the same way.

**JavaScript tests** — `pnpm --filter expo-asset-dotnet test`. Model on
`packages/expo-modules-dotnet/src/__tests__/index.test.ts`, which mocks the
native installer with `vi.mock` and `vi.resetModules()`.

31. `src/__tests__/index.test.ts`: the facade requests the module by the exact
    name `'ExpoAsset'`, and forwards `(url, md5Hash, type)` in that order,
    including a `null` `md5Hash`, to the native function.
32. `src/__tests__/autolinking.test.ts`: read this package's **real**
    `expo-module.config.json` from disk and assert (a) `platforms` contains
    `"dotnet"`, (b) `dotnet.projects` has exactly one entry, (c) its
    `assemblyName` is `ExpoAssetDotnet`, and (d) the `path` it declares resolves
    to a file that exists. Together those are precisely the conditions
    `buildDotnetManifest` enforces, so the package is provably autolinkable
    without an app and without a cross-package import.

    **Do not** try `import { buildDotnetManifest } from 'expo-modules-dotnet-autolinking'`.
    That function is defined in that package's `src/resolveDotnetModules.ts` and
    is **not** re-exported from its `src/index.ts`, while the package's `main` is
    `bootstrap.cjs` and its published `files` list is `["bin", "bootstrap.cjs", "build"]`.
    The import does not resolve. If you want the real manifest builder exercised
    against this package, that belongs in
    `packages/expo-modules-dotnet-autolinking/src/__tests__/resolveDotnetModules.test.ts`,
    which is out of scope here — raise it as a follow-up instead of reaching
    across the package boundary.

## Done criteria

Machine-checkable. ALL must hold:

- [ ] `pnpm install` exits 0 and `pnpm-lock.yaml` contains `expo-asset-dotnet`.
- [ ] `pnpm --filter expo-asset-dotnet typecheck` exits 0.
- [ ] `pnpm --filter expo-asset-dotnet test` exits 0, with the two test files
      from cases 31-32 present and passing.
- [ ] `scripts/test-managed.sh --project packages/expo-asset-dotnet/dotnet/ExpoAssetDotnet.Tests/ExpoAssetDotnet.Tests.csproj` exits 0.
- [ ] `scripts/test-managed.sh` exits 0, and its output contains
      `==> Running ExpoAssetDotnet.Tests` — proving discovery picked the project
      up rather than the suite silently skipping it. The pre-existing 650 tests
      still pass.
- [ ] `grep -rn "\[Trait(" packages/expo-asset-dotnet/` returns no matches (no
      invented trait convention landed).
- [ ] `pnpm --filter expo-modules-dotnet-autolinking test` exits 0.
- [ ] `scripts/format.sh --check --all` exits 0.
- [ ] `git diff --check` produces no output.
- [ ] `git diff --name-only 9247d75d..HEAD` lists no file outside the In-scope
      list — in particular neither `scripts/test-managed.sh`,
      `scripts/test-managed.ps1`, nor anything under `.github/` or `apps/`.
- [ ] `grep -rn "PlatformNotSupportedException\|IsOSPlatform" packages/expo-asset-dotnet/` returns no matches (no runtime platform gate landed).
- [ ] `grep -rn "globalThis.expo" packages/expo-asset-dotnet/src/` returns no
      matches (no compatibility aliasing landed).
- [ ] `docs/specs/modules-core-boundary.md` and
      `docs/specs/dotnet-autolinking.md` describe the accepted behavior, and
      `docs/changes/2026-<mm-dd>-expo-asset-dotnet/plan.md` is archived.
- [ ] No committed file contains a local absolute path, username, or machine
      name.
- [ ] `docs/plans/README.md` row for 022 says DONE.

## STOP conditions

Stop and report — do not improvise — if:

- `scripts/test-managed.sh` does not list `ExpoAssetDotnet.Tests` after you
  create it at the required path. Discovery is bounded to
  `-mindepth 4 -maxdepth 4`; re-check the directory depth and the `.Tests`
  suffixes before doing anything else, and never "fix" it by editing the runner.
- Making the tests pass appears to require editing `scripts/test-managed.sh`,
  `scripts/test-managed.ps1`, or any workflow file.
- A Hermes-backed test needs an API that `Expo.ModulesCore.Testing` does not
  expose publicly. Its internals are visible only to `Expo.ModulesCore.Tests`,
  and widening that grant is test-core work, not this plan's.
- The generator rejects the module because it needs a constructor shape other
  than parameterless or `DotnetRuntimeContext`, or because it needs runtime
  reflection.
- A cache root cannot be resolved or created on the host you are running on.
- The package needs `globalThis.expo.modules`, Metro aliasing, or any change
  inside `packages/expo-modules-dotnet` to work. That is deferred
  compatibility work.
- The live code disagrees with a "Current state" excerpt above.
- Any step's verification fails twice after a reasonable fix attempt.

## Maintenance notes

- **The Windows/macOS restriction is documentation-only.** The dotnet
  autolinking schema has no platform selector, so nothing prevents this package
  from linking on an Android or iOS NativeAOT target, where the cache root falls
  back to the XDG branch. Making platform support declarable needs a schema
  affordance in `expo-modules-dotnet-autolinking` — worth its own plan, and a
  prerequisite before anyone claims this package is Windows/macOS-only in
  user-facing docs.
- **Windows behavior is verified by CI, not locally.** `native-tests.yml` runs
  `scripts/test-managed.ps1` on `windows-latest`, so once discovery lands the
  Windows lane covers this project automatically. A macOS-only local run does
  not prove the `%LOCALAPPDATA%` branch.
- **Two properties are not proven by these tests, by construction.** Real
  sandboxed-container cache resolution is only exercised by running the desktop
  app (`apps/desktop-app/macos/desktopapp-macOS/desktopapp.entitlements`
  enables `com.apple.security.app-sandbox`), and crash-atomicity of the rename
  cannot be shown by a process that is never killed mid-rename. What the tests
  do prove is that an atomic API is used and that ordinary failure paths leave
  no partial file. Do not let a future reviewer read case 18 as a crash-safety
  guarantee.
- **What a reviewer should scrutinize**: that case 16 and case 19 really assert
  non-rejection (they are the two places an executor is most likely to
  "helpfully" add hash enforcement back); that no temp file is left by any
  failure path; that the Hermes set stayed small instead of duplicating the
  service matrix; and that no `[Trait(...)]` or second test project crept in.
- **This package is the first to use `InternalsVisibleTo` from an authored
  module.** `ExampleModule` tests public types only. If a third authored package
  copies this, the pattern is worth promoting into the authoring guide; if a
  reviewer instead prefers public services, that decision should be made once
  and applied to all authored packages rather than per-package.
- **There is deliberately no trait-based test categorisation in this repo.** If
  someone later wants `dotnet test --filter` to carve pure tests out of a mixed
  project, that is a test-core change affecting every authored package, not a
  local tweak here.
- **If plan 024 (`expo-file-system-dotnet`) lands**, revisit whether
  `AssetCachePaths` and its hashing helper should move into a shared internal
  helper. Do not pre-emptively share them — the operator explicitly deferred
  that.
- **If Expo's `Asset` class is ever wired to this module**, the argument
  contract in Step 1 is the compatibility surface that must not drift:
  `(url, md5Hash, type)`, in that order, with `md5Hash` nullable.
