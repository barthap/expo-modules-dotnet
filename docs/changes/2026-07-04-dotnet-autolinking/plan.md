# .NET Module Autolinking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** TypeScript autolinking CLI that discovers dotnet Expo module packages, generates the app-level `ExpoDotnetHost` aggregator (owning the runtime-context entry points), builds and stages managed artifacts, wired into macOS and Windows app builds.

**Architecture:** Hybrid tool per `docs/changes/2026-07-04-dotnet-autolinking/spec.md`: new workspace package `packages/expo-modules-dotnet-autolinking` reuses `expo-modules-autolinking/exports` for discovery only; all dotnet resolution, codegen, build, and staging logic is local. Generated aggregator replaces `packages/example-module`'s temporary `EntryPoints.cs`; loaders switch to the stable type `Expo.ModulesCore.Generated.EntryPoints, ExpoDotnetHost`.

**Tech Stack:** TypeScript (tsc build), commander, vitest, `expo-modules-autolinking` ^57.0.2, `dotnet` CLI (net10.0).

## Global Constraints

- Aggregator assembly name: `ExpoDotnetHost` (stable, exact).
- Entry-point type: `Expo.ModulesCore.Generated.EntryPoints, ExpoDotnetHost` (exact).
- Entry-point symbols unchanged: `expo_dotnet_create_runtime_context`, `expo_dotnet_teardown_runtime_context`.
- Default generate output dir: `<appRoot>/.expo/dotnet/` (overridable via `--output`).
- Generated registration order deterministic (sort by packageName, then assemblyName).
- Generated files rewritten only when content changes; staging skips byte-identical files.
- NativeAOT: single publish unit (`ExpoDotnetHost`), never per-module native libs.
- No hot-path reflection; no upstream `resolveModulesAsync`/platform-dispatch calls.
- No committed local absolute paths, usernames, or machine names.
- Before finishing: `scripts/format.sh --check --all`, `scripts/test-managed.sh`, `git diff --check`.
- pnpm workspace changes: run `pnpm install` (updates lockfile; this repo adds a new package, so `--frozen-lockfile` will not apply for that step).

---

### Task 1: Scaffold `packages/expo-modules-dotnet-autolinking`

**Files:**
- Create: `packages/expo-modules-dotnet-autolinking/package.json`
- Create: `packages/expo-modules-dotnet-autolinking/tsconfig.json`
- Create: `packages/expo-modules-dotnet-autolinking/bin/expo-modules-dotnet-autolinking.js`
- Create: `packages/expo-modules-dotnet-autolinking/src/index.ts`
- Create: `packages/expo-modules-dotnet-autolinking/vitest.config.ts`
- Create: `packages/expo-modules-dotnet-autolinking/.gitignore`

**Interfaces:**
- Produces: CLI entry `main(argv: string[])` (commander program); `pnpm --filter expo-modules-dotnet-autolinking build|test` scripts.

- [ ] **Step 1: Write package.json**

```json
{
  "name": "expo-modules-dotnet-autolinking",
  "version": "0.1.0",
  "description": "Autolinking tool for .NET-backed Expo modules",
  "main": "build/index.js",
  "bin": {
    "expo-modules-dotnet-autolinking": "bin/expo-modules-dotnet-autolinking.js"
  },
  "files": ["bin", "build"],
  "scripts": {
    "build": "tsc -p tsconfig.json",
    "test": "vitest run",
    "typecheck": "tsc -p tsconfig.json --noEmit"
  },
  "dependencies": {
    "commander": "^12.0.0",
    "expo-modules-autolinking": "^57.0.2"
  },
  "devDependencies": {
    "@types/node": "^22.0.0",
    "typescript": "~5.9.3",
    "vitest": "^3.0.0"
  },
  "license": "MIT"
}
```

- [ ] **Step 2: Write tsconfig.json**

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "commonjs",
    "moduleResolution": "node",
    "outDir": "build",
    "rootDir": "src",
    "strict": true,
    "declaration": true,
    "esModuleInterop": true,
    "skipLibCheck": true
  },
  "include": ["src"],
  "exclude": ["src/**/__tests__/**"]
}
```

- [ ] **Step 3: Write bin shim and minimal index**

`bin/expo-modules-dotnet-autolinking.js`:

```js
#!/usr/bin/env node
require('../build/index.js').main(process.argv.slice(2));
```

`src/index.ts`:

```ts
import { Command } from 'commander';

export function createProgram(): Command {
  const program = new Command('expo-modules-dotnet-autolinking');
  program.description('Autolinking tool for .NET-backed Expo modules');
  return program;
}

export function main(argv: string[]): void {
  createProgram().parseAsync(argv, { from: 'user' }).catch((error) => {
    console.error(error instanceof Error ? error.message : error);
    process.exitCode = 1;
  });
}
```

`vitest.config.ts`:

```ts
import { defineConfig } from 'vitest/config';
export default defineConfig({ test: { include: ['src/**/__tests__/**/*.test.ts'] } });
```

`.gitignore`:

```
build/
```

- [ ] **Step 4: Make the app depend on the CLI (pnpm resolution strategy)**

With pnpm there is no hoisted `node_modules/expo-modules-dotnet-autolinking` at the app root unless
the app depends on it. Add to `apps/desktop-app/package.json` `devDependencies`:

```json
"expo-modules-dotnet-autolinking": "workspace:*"
```

(Do the same for `apps/mobile-app` only when iOS/Android migrate.) All build hooks in later tasks
resolve the CLI through Node module resolution from the app root — never through a hardcoded
`node_modules/...` path.

- [ ] **Step 5: Install and verify**

Run: `pnpm install` (workspace root), then
`pnpm --filter expo-modules-dotnet-autolinking build && node packages/expo-modules-dotnet-autolinking/bin/expo-modules-dotnet-autolinking.js --help`
Expected: help text prints, exit 0. Also verify app-root resolution:
`node --print "require.resolve('expo-modules-dotnet-autolinking', { paths: ['apps/desktop-app'] })"` → resolves.

- [ ] **Step 6: Commit**

```bash
git add packages/expo-modules-dotnet-autolinking apps/desktop-app/package.json pnpm-lock.yaml
git commit -m "feat(autolinking): scaffold expo-modules-dotnet-autolinking package"
```

---

### Task 2: Manifest types and resolution core

**Files:**
- Create: `packages/expo-modules-dotnet-autolinking/src/types.ts`
- Create: `packages/expo-modules-dotnet-autolinking/src/resolveDotnetModules.ts`
- Test: `packages/expo-modules-dotnet-autolinking/src/__tests__/resolveDotnetModules.test.ts`

**Interfaces:**
- Produces:

```ts
// types.ts
export interface DotnetProjectRef { csprojPath: string; assemblyName: string; } // csprojPath absolute
export interface DotnetModule { packageName: string; packageRoot: string; projects: DotnetProjectRef[]; }
export interface DotnetLinkingManifest { modules: DotnetModule[]; }
export interface RawDotnetConfig { projects?: { path: string; assemblyName?: string }[]; }

// resolveDotnetModules.ts
export interface DotnetPackageInput { packageName: string; packageRoot: string; dotnetConfig: RawDotnetConfig | undefined; }
export function buildDotnetManifest(inputs: DotnetPackageInput[]): DotnetLinkingManifest;
```

- [ ] **Step 1: Write failing tests**

Test cases (create fixture csproj files in a `fs.mkdtempSync(path.join(os.tmpdir(), 'dotnet-autolink-'))` dir per test):

```ts
import { describe, expect, it } from 'vitest';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { buildDotnetManifest } from '../resolveDotnetModules';

function makePackage(root: string, csprojRel: string): void {
  const csprojPath = path.join(root, csprojRel);
  fs.mkdirSync(path.dirname(csprojPath), { recursive: true });
  fs.writeFileSync(csprojPath, '<Project Sdk="Microsoft.NET.Sdk" />');
}

describe('buildDotnetManifest', () => {
  it('resolves projects and defaults assemblyName to csproj basename', () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), 'dotnet-autolink-'));
    makePackage(root, 'dotnet/ExampleModule/ExampleModule.csproj');
    const manifest = buildDotnetManifest([
      { packageName: 'example-module', packageRoot: root,
        dotnetConfig: { projects: [{ path: 'dotnet/ExampleModule/ExampleModule.csproj' }] } },
    ]);
    expect(manifest.modules).toEqual([
      { packageName: 'example-module', packageRoot: root,
        projects: [{ csprojPath: path.join(root, 'dotnet/ExampleModule/ExampleModule.csproj'),
                     assemblyName: 'ExampleModule' }] },
    ]);
  });

  it('honors explicit assemblyName', () => { /* dotnetConfig project with assemblyName: 'Custom' → manifest has 'Custom' */ });

  it('skips packages without dotnet config', () => {
    expect(buildDotnetManifest([{ packageName: 'x', packageRoot: '/nope', dotnetConfig: undefined }]).modules).toEqual([]);
  });

  it('throws naming package and path when csproj is missing', () => {
    expect(() => buildDotnetManifest([
      { packageName: 'broken-pkg', packageRoot: os.tmpdir(),
        dotnetConfig: { projects: [{ path: 'missing/Missing.csproj' }] } },
    ])).toThrow(/broken-pkg.*missing\/Missing\.csproj/s);
  });

  it('throws on duplicate assembly names naming both packages', () => { /* two inputs, same effective assemblyName → /pkg-a.*pkg-b/s */ });

  it('sorts modules by packageName and projects by assemblyName', () => { /* pass inputs out of order, assert sorted output */ });
});
```

Fill in the elided test bodies with real assertions following the shown patterns — no `it.todo`.

- [ ] **Step 2: Run tests, verify failure**

Run: `pnpm --filter expo-modules-dotnet-autolinking test`
Expected: FAIL — module `../resolveDotnetModules` not found.

- [ ] **Step 3: Implement**

```ts
// resolveDotnetModules.ts
import * as fs from 'fs';
import * as path from 'path';
import type { DotnetLinkingManifest, DotnetModule, RawDotnetConfig } from './types';

export interface DotnetPackageInput {
  packageName: string;
  packageRoot: string;
  dotnetConfig: RawDotnetConfig | undefined;
}

export function buildDotnetManifest(inputs: DotnetPackageInput[]): DotnetLinkingManifest {
  const modules: DotnetModule[] = [];
  const assemblyOwners = new Map<string, string>();
  for (const input of inputs) {
    const projects = input.dotnetConfig?.projects ?? [];
    if (projects.length === 0) continue;
    const resolved = projects.map((project) => {
      const csprojPath = path.resolve(input.packageRoot, project.path);
      if (!fs.existsSync(csprojPath)) {
        throw new Error(
          `[expo-modules-dotnet-autolinking] Package "${input.packageName}" declares a dotnet project ` +
          `that does not exist: ${project.path} (resolved: ${csprojPath})`
        );
      }
      const assemblyName = project.assemblyName ?? path.basename(csprojPath, '.csproj');
      const owner = assemblyOwners.get(assemblyName);
      if (owner !== undefined) {
        throw new Error(
          `[expo-modules-dotnet-autolinking] Duplicate assembly name "${assemblyName}" ` +
          `declared by packages "${owner}" and "${input.packageName}"`
        );
      }
      assemblyOwners.set(assemblyName, input.packageName);
      return { csprojPath, assemblyName };
    });
    resolved.sort((a, b) => a.assemblyName.localeCompare(b.assemblyName));
    modules.push({ packageName: input.packageName, packageRoot: input.packageRoot, projects: resolved });
  }
  modules.sort((a, b) => a.packageName.localeCompare(b.packageName));
  return { modules };
}
```

- [ ] **Step 4: Run tests, verify pass** — `pnpm --filter expo-modules-dotnet-autolinking test` → PASS.

- [ ] **Step 5: Commit**

```bash
git add packages/expo-modules-dotnet-autolinking/src
git commit -m "feat(autolinking): dotnet manifest resolution with validation"
```

---

### Task 3: `resolve` command (upstream discovery)

**Files:**
- Create: `packages/expo-modules-dotnet-autolinking/src/discovery.ts`
- Create: `packages/expo-modules-dotnet-autolinking/src/commands/resolveCommand.ts`
- Modify: `packages/expo-modules-dotnet-autolinking/src/index.ts` (register command)
- Test: `packages/expo-modules-dotnet-autolinking/src/__tests__/discovery.test.ts`

**Interfaces:**
- Consumes: `buildDotnetManifest` (Task 2).
- Produces:

```ts
// discovery.ts
export function resolveAppRoot(explicit?: string): string;      // findProjectRootSync from upstream, or explicit
export async function discoverDotnetManifestAsync(appRoot: string): Promise<DotnetLinkingManifest>;
```

CLI: `expo-modules-dotnet-autolinking resolve [--project-root <dir>] [--json]` → prints `JSON.stringify(manifest, null, 2)`.

- [ ] **Step 1: Implement discovery (upstream calls are thin glue; test via the e2e in this task's Step 3 rather than mocking upstream)**

```ts
// discovery.ts
import {
  findProjectRootSync,
  makeCachedDependenciesLinker,
  scanExpoModuleResolutionsForPlatform,
} from 'expo-modules-autolinking/exports';
import { buildDotnetManifest, type DotnetPackageInput } from './resolveDotnetModules';
import type { DotnetLinkingManifest, RawDotnetConfig } from './types';

export function resolveAppRoot(explicit?: string): string {
  return explicit ?? findProjectRootSync(process.cwd());
}

export async function discoverDotnetManifestAsync(appRoot: string): Promise<DotnetLinkingManifest> {
  const linker = makeCachedDependenciesLinker({ projectRoot: appRoot });
  // Record<string, PackageRevision>; gated by supportsPlatform('dotnet') exact-match
  const revisions = await scanExpoModuleResolutionsForPlatform(linker, 'dotnet');
  const inputs: DotnetPackageInput[] = Object.entries(revisions).map(([packageName, revision]) => ({
    packageName,
    packageRoot: revision.path,
    dotnetConfig: (revision.config?.toJSON() as { dotnet?: RawDotnetConfig } | undefined)?.dotnet,
  }));
  return buildDotnetManifest(inputs);
}
```

Notes:
- `makeCachedDependenciesLinker` + `scanExpoModuleResolutionsForPlatform` is the non-deprecated exports surface (verified against installed `expo-modules-autolinking@57.0.2`); the exported `findModulesAsync(SearchOptions)` wrapper is `@deprecated` — do not use it. Do NOT call upstream `resolveModulesAsync`/platform dispatch (throws for unknown platforms).
- After `pnpm install`, re-verify these signatures against the version the new package actually resolves (`node_modules/.pnpm/expo-modules-autolinking@*/`) before implementing.

- [ ] **Step 2: Register command**

```ts
// commands/resolveCommand.ts
import type { Command } from 'commander';
import { discoverDotnetManifestAsync, resolveAppRoot } from '../discovery';

export function registerResolveCommand(program: Command): void {
  program
    .command('resolve')
    .description('Discover dotnet Expo module packages and print the linking manifest')
    .option('--project-root <dir>', 'App project root (defaults to nearest package.json)')
    .option('--json', 'Print JSON (default output format)')
    .action(async (options: { projectRoot?: string }) => {
      const manifest = await discoverDotnetManifestAsync(resolveAppRoot(options.projectRoot));
      console.log(JSON.stringify(manifest, null, 2));
    });
}
```

In `src/index.ts` `createProgram()`, add `registerResolveCommand(program);`.

- [ ] **Step 3: E2E against this workspace (will show zero modules until Task 9 adds example-module config)**

Run: `pnpm --filter expo-modules-dotnet-autolinking build && node packages/expo-modules-dotnet-autolinking/bin/expo-modules-dotnet-autolinking.js resolve --project-root apps/desktop-app`
Expected: `{ "modules": [] }` (example-module has no dotnet config yet), exit 0.

- [ ] **Step 4: Commit**

```bash
git add packages/expo-modules-dotnet-autolinking/src
git commit -m "feat(autolinking): resolve command using upstream discovery"
```

---

### Task 4: Aggregator codegen core

**Files:**
- Create: `packages/expo-modules-dotnet-autolinking/src/codegen/writeIfChanged.ts`
- Create: `packages/expo-modules-dotnet-autolinking/src/codegen/generateAggregator.ts`
- Test: `packages/expo-modules-dotnet-autolinking/src/__tests__/generateAggregator.test.ts`

**Interfaces:**
- Consumes: `DotnetLinkingManifest` (Task 2).
- Produces:

```ts
export interface GenerateOptions {
  outputDir: string;                 // absolute; created if missing
  adapterPackageRoot: string;        // absolute path to installed expo-modules-dotnet package
}
export interface GenerateResult { writtenFiles: string[]; skippedFiles: string[]; }
export function generateAggregator(manifest: DotnetLinkingManifest, options: GenerateOptions): GenerateResult;
export function writeIfChangedSync(filePath: string, content: string): boolean; // true if written
```

Files emitted into `outputDir`: `ExpoDotnetHost.csproj`, `LinkedExpoModulesProvider.g.cs`, `EntryPoints.g.cs`.

- [ ] **Step 1: Write failing tests**

```ts
// generateAggregator.test.ts — fixture manifest with two modules; temp outputDir + fake adapter root
// containing managed/packages/Expo.JSI/Expo.JSI.csproj and managed/packages/Expo.ModulesCore/Expo.ModulesCore.csproj stubs.
it('emits three files', ...);                       // csproj + two .g.cs exist
it('registers providers in manifest order', ...);   // LinkedExpoModulesProvider.g.cs contains
  // "ExpoModulesProvider_A.Register(context);" before "ExpoModulesProvider_B.Register(context);"
it('csproj references all module projects and managed core with relative forward-slash paths', ...);
it('emits valid empty provider for zero modules', ...); // Register body has no provider calls, still compiles-shaped
it('second run skips unchanged files', ...);        // writtenFiles empty, skippedFiles has 3 entries; mtime unchanged
```

Write full assertion bodies (string `toContain` / regex checks on emitted content).

- [ ] **Step 2: Run tests, verify failure.**

- [ ] **Step 3: Implement templates**

`writeIfChanged.ts`:

```ts
import * as fs from 'fs';
import * as path from 'path';

export function writeIfChangedSync(filePath: string, content: string): boolean {
  if (fs.existsSync(filePath) && fs.readFileSync(filePath, 'utf8') === content) return false;
  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  fs.writeFileSync(filePath, content);
  return true;
}
```

`generateAggregator.ts` — key content. Every file starts with the banner:

```
// Automatically generated by expo-modules-dotnet-autolinking. Do not edit.
```

(`<!-- ... -->` variant for the csproj.)

`ExpoDotnetHost.csproj` template (relative paths computed with `path.relative(outputDir, target).split(path.sep).join('/')`):

```xml
<!-- Automatically generated by expo-modules-dotnet-autolinking. Do not edit. -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <GenerateRuntimeConfigurationFiles>true</GenerateRuntimeConfigurationFiles>
    <AssemblyName>ExpoDotnetHost</AssemblyName>
    <RootNamespace>Expo.ModulesCore.Generated</RootNamespace>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <PropertyGroup Condition="'$(PublishAot)' == 'true'">
    <NativeLib>Shared</NativeLib>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="LinkedExpoModulesProvider.g.cs" />
    <Compile Include="EntryPoints.g.cs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="{rel to adapterPackageRoot}/managed/packages/Expo.JSI/Expo.JSI.csproj" />
    <ProjectReference Include="{rel to adapterPackageRoot}/managed/packages/Expo.ModulesCore/Expo.ModulesCore.csproj" />
    <!-- one per manifest project: -->
    <ProjectReference Include="{rel to module csprojPath}" />
  </ItemGroup>
</Project>
```

Implementation note: before finalizing, compare the `PublishAot` property group against
`packages/example-module/dotnet/ExampleModule/ExampleModule.csproj` and copy over any additional
AOT-specific properties it defines beyond `NativeLib` so both publish identically.

`LinkedExpoModulesProvider.g.cs`:

```csharp
// Automatically generated by expo-modules-dotnet-autolinking. Do not edit.
using Expo.ModulesCore;

namespace Expo.ModulesCore.Generated;

public static class LinkedExpoModulesProvider
{
    public static void Register(DotnetRuntimeContext context)
    {
        Expo.ModulesCore.Generated.ExpoModulesProvider_{assemblyName}.Register(context);
        // ... one line per manifest project, manifest order
    }
}
```

`EntryPoints.g.cs` — port of `packages/example-module/dotnet/ExampleModule/EntryPoints.cs` with namespace and provider call swapped:

```csharp
// Automatically generated by expo-modules-dotnet-autolinking. Do not edit.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Expo.JSI;
using Expo.ModulesCore;

namespace Expo.ModulesCore.Generated;

public static class EntryPoints
{
    [UnmanagedCallersOnly(
        EntryPoint = "expo_dotnet_create_runtime_context",
        CallConvs = new[] { typeof(CallConvCdecl) }
    )]
    public static nint CreateRuntimeContext(nint api, nint runtimeHandle)
    {
        try
        {
            return CreateRuntimeContextCore(api, runtimeHandle);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 0;
        }
    }

    [UnmanagedCallersOnly(
        EntryPoint = "expo_dotnet_teardown_runtime_context",
        CallConvs = new[] { typeof(CallConvCdecl) }
    )]
    public static void TeardownRuntimeContext(nint runtimeContext)
    {
        TeardownRuntimeContextCore(runtimeContext);
    }

    private static nint CreateRuntimeContextCore(nint api, nint runtimeHandle)
    {
        var runtime = JavaScriptRuntime.FromNative(api, runtimeHandle);
        var context = new DotnetRuntimeContext(runtime);
        try
        {
            LinkedExpoModulesProvider.Register(context);
            return GCHandle.ToIntPtr(GCHandle.Alloc(context));
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static void TeardownRuntimeContextCore(nint runtimeContext)
    {
        if (runtimeContext == 0)
        {
            return;
        }
        var handle = GCHandle.FromIntPtr(runtimeContext);
        if (handle.Target is DotnetRuntimeContext context)
        {
            context.Dispose();
        }
        handle.Free();
    }
}
```

Implementation note: `EntryPoints.g.cs` static content — diff against the current
`packages/example-module/dotnet/ExampleModule/EntryPoints.cs` at implementation time and preserve any
try/dispose semantics present there; only the namespace and the `Register` call change.

- [ ] **Step 4: Run tests, verify pass.**

- [ ] **Step 5: Commit**

```bash
git add packages/expo-modules-dotnet-autolinking/src
git commit -m "feat(autolinking): app-level ExpoDotnetHost aggregator codegen"
```

---

### Task 5: `generate` command

**Files:**
- Create: `packages/expo-modules-dotnet-autolinking/src/commands/generateCommand.ts`
- Create: `packages/expo-modules-dotnet-autolinking/src/paths.ts`
- Modify: `packages/expo-modules-dotnet-autolinking/src/index.ts`

**Interfaces:**
- Consumes: `discoverDotnetManifestAsync`, `generateAggregator`.
- Produces:

```ts
// paths.ts
export function defaultGenerateOutputDir(appRoot: string): string; // path.join(appRoot, '.expo', 'dotnet')
export function findAdapterPackageRoot(appRoot: string): string;   // require.resolve('expo-modules-dotnet/package.json', { paths: [appRoot] }) dirname
```

CLI: `generate [--project-root <dir>] [--output <dir>]` — prints written/skipped file summary.

- [ ] **Step 1: Implement paths.ts and generateCommand.ts** (thin composition; behavior covered by Task 4 unit tests + Task 8 e2e). `--output` overrides the default output dir. Error from `findAdapterPackageRoot` when `expo-modules-dotnet` is not installed must name the appRoot.

- [ ] **Step 2: Manual check**

Run: `pnpm --filter expo-modules-dotnet-autolinking build && node packages/expo-modules-dotnet-autolinking/bin/expo-modules-dotnet-autolinking.js generate --project-root apps/desktop-app`
Expected: creates `apps/desktop-app/.expo/dotnet/` with 3 files (empty provider — no modules configured yet).

- [ ] **Step 3: Gitignore generated dir** — add `.expo/` to `apps/desktop-app/.gitignore` and `apps/mobile-app/.gitignore` if not already ignored (check first: `git check-ignore apps/desktop-app/.expo/dotnet/ExpoDotnetHost.csproj`).

- [ ] **Step 4: Commit**

```bash
git add packages/expo-modules-dotnet-autolinking/src apps/desktop-app/.gitignore apps/mobile-app/.gitignore
git commit -m "feat(autolinking): generate command with default .expo/dotnet output"
```

---

### Task 6: `build` command

**Files:**
- Create: `packages/expo-modules-dotnet-autolinking/src/build.ts`
- Create: `packages/expo-modules-dotnet-autolinking/src/commands/buildCommand.ts`
- Modify: `packages/expo-modules-dotnet-autolinking/src/index.ts`
- Test: `packages/expo-modules-dotnet-autolinking/src/__tests__/build.test.ts`

**Interfaces:**
- Produces:

```ts
export type LoaderMode = 'hostfxr' | 'nativeaot';
export interface BuildOptions { csprojPath: string; mode: LoaderMode; configuration: string; rid?: string; }
export function dotnetArgsForBuild(options: BuildOptions): string[];    // pure, unit-tested
export function defaultLoaderMode(platform: 'macos' | 'windows' | 'ios' | 'android'): LoaderMode;
export function defaultRid(platform: string, arch?: string): string;    // macos: osx-arm64|osx-x64; windows: win-x64|win-arm64
export function sanitizedDotnetEnv(env: NodeJS.ProcessEnv): NodeJS.ProcessEnv; // pure, unit-tested
export function buildOutputDir(options: BuildOptions): string;          // bin/<config>/net10.0[/<rid>/publish]
export async function runDotnetBuildAsync(options: BuildOptions): Promise<void>; // spawn('dotnet', ...)
```

- [ ] **Step 1: Write failing tests for the pure functions**

```ts
it('hostfxr build args', () => {
  expect(dotnetArgsForBuild({ csprojPath: '/x/ExpoDotnetHost.csproj', mode: 'hostfxr', configuration: 'Debug' }))
    .toEqual(['build', '/x/ExpoDotnetHost.csproj', '-c', 'Debug']);
});
it('nativeaot publish args', () => {
  expect(dotnetArgsForBuild({ csprojPath: '/x/ExpoDotnetHost.csproj', mode: 'nativeaot', configuration: 'Release', rid: 'osx-arm64' }))
    .toEqual(['publish', '/x/ExpoDotnetHost.csproj', '-c', 'Release', '-r', 'osx-arm64', '/p:PublishAot=true', '/p:NativeLib=Shared']);
});
it('default modes: macos/windows → hostfxr, ios/android → nativeaot', ...);
it('sanitizedDotnetEnv strips Xcode build vars', () => {
  const env = sanitizedDotnetEnv({ PATH: '/usr/bin', ACTION: 'build', ARCHS: 'arm64', CURRENT_ARCH: 'arm64',
    PLATFORM_NAME: 'macosx', PRODUCT_NAME: 'App', PROJECT_NAME: 'App', TARGET_NAME: 'App', TARGETNAME: 'App' });
  expect(env.PATH).toBe('/usr/bin');
  for (const key of ['ACTION','ARCHS','CURRENT_ARCH','PLATFORM_NAME','PRODUCT_NAME','PROJECT_NAME','TARGET_NAME','TARGETNAME'])
    expect(env[key]).toBeUndefined();
});
it('buildOutputDir hostfxr vs nativeaot layouts', ...);
```

The Xcode env-var strip list comes from `dotnet_build_env` in `apps/desktop-app/scripts/build-managed.sh` — keep it identical (these vars break `dotnet` when invoked from an Xcode script phase).

- [ ] **Step 2: Run tests, verify failure.**

- [ ] **Step 3: Implement.** `runDotnetBuildAsync` uses `child_process.spawn('dotnet', args, { stdio: 'inherit', env: sanitizedDotnetEnv(process.env) })`; reject on nonzero exit with the exit code in the message. For `mode: 'nativeaot'`, first run `dotnet build <adapterPackageRoot>/managed/packages/Expo.ModulesCore.Generator/Expo.ModulesCore.Generator.csproj -c Debug` (AOT publish consumes the generator as a prebuilt analyzer DLL — same prebuild `build-managed.sh` does in `build_generator_analyzer`). Wire `buildCommand.ts`: `build [--project-root] [--output <dir>] [--mode hostfxr|nativeaot] [--rid <rid>] [--configuration <c>]`; configuration defaults: `Debug` for hostfxr, `Release` for nativeaot (same as `build-managed.sh`).

- [ ] **Step 4: Run tests, verify pass. Then a real hostfxr build (requires Task 9's example-module config for a non-empty aggregator; with empty manifest it still must succeed):**

Run: `node packages/expo-modules-dotnet-autolinking/bin/expo-modules-dotnet-autolinking.js generate --project-root apps/desktop-app && node packages/expo-modules-dotnet-autolinking/bin/expo-modules-dotnet-autolinking.js build --project-root apps/desktop-app --mode hostfxr`
Expected: `dotnet build` succeeds; `apps/desktop-app/.expo/dotnet/bin/Debug/net10.0/ExpoDotnetHost.dll` exists.

- [ ] **Step 5: Commit**

```bash
git add packages/expo-modules-dotnet-autolinking/src
git commit -m "feat(autolinking): build command for hostfxr and nativeaot modes"
```

---

### Task 7: `stage` command

**Files:**
- Create: `packages/expo-modules-dotnet-autolinking/src/stage.ts`
- Create: `packages/expo-modules-dotnet-autolinking/src/commands/stageCommand.ts`
- Modify: `packages/expo-modules-dotnet-autolinking/src/index.ts`
- Test: `packages/expo-modules-dotnet-autolinking/src/__tests__/stage.test.ts`

**Interfaces:**
- Consumes: `buildOutputDir`, `LoaderMode` (Task 6).
- Produces:

```ts
export interface StageOptions { platform: 'macos' | 'windows'; appRoot: string; mode: LoaderMode; builtOutputDir: string; }
export function stageDestination(platform: 'macos' | 'windows', appRoot: string): string;
  // macos → <appRoot>/macos/Managed ; windows → <appRoot>/windows/Managed
export async function locateNethostLibraryAsync(rid: string): Promise<string>;
  // dotnet --info "Base Path" → <dotnetRoot>/packs/Microsoft.NETCore.App.Host.<rid>/<latest stable semver>/runtimes/<rid>/native/{libnethost.dylib|nethost.dll}
export async function stageArtifactsAsync(options: StageOptions): Promise<{ staged: string[]; skipped: string[] }>;
```

Behavior (port of `build_hostfxr`/`reset_managed_dir` from `apps/desktop-app/scripts/build-managed.sh` and its `.ps1` sibling):
- hostfxr: clear destination (preserving `.gitkeep`/`.gitignore`) of files not in the new set, copy `*.dll`, `*.deps.json`, `*.runtimeconfig.json` from `builtOutputDir` + the nethost library; skip byte-identical copies (compare size then content).
- nativeaot: copy `libExpoDotnetHost.dylib` (macos) / `ExpoDotnetHost.dll`-equivalent native lib from the publish dir.

- [ ] **Step 1: Write failing tests** — temp source dir with fake `ExpoDotnetHost.dll` + `x.runtimeconfig.json` + `x.deps.json` + an unrelated `.pdb` (must NOT be staged... actually `build-managed.sh` copies only the three patterns; assert `.pdb` excluded), temp appRoot; assert destination contents; assert second run reports all-skipped; assert stale file in destination (not in new set) is removed while `.gitkeep` survives. Test `locateNethostLibraryAsync` only if `dotnet` is on PATH (`it.skipIf(!hasDotnet)`), asserting the returned path exists.

- [ ] **Step 2: Run tests, verify failure.**

- [ ] **Step 3: Implement + wire `stage` command:** `stage --platform <macos|windows> [--project-root] [--app-root <dir>] [--mode] [--configuration] [--rid]` (app-root defaults to project-root).

- [ ] **Step 4: Run tests, verify pass.**

- [ ] **Step 5: Commit**

```bash
git add packages/expo-modules-dotnet-autolinking/src
git commit -m "feat(autolinking): stage command for hostfxr and nativeaot artifacts"
```

---

### Task 8: `link` command + workspace e2e test

**Files:**
- Create: `packages/expo-modules-dotnet-autolinking/src/commands/linkCommand.ts`
- Modify: `packages/expo-modules-dotnet-autolinking/src/index.ts`
- Test: `packages/expo-modules-dotnet-autolinking/src/__tests__/e2e.test.ts`

**Interfaces:**
- Consumes: everything above. `link --platform <macos|windows> [--project-root] [--mode] [--configuration] [--rid]` = resolve → generate → build → stage; logs one summary line per phase.

- [ ] **Step 1: Implement linkCommand** (sequential composition of the exported async functions — no re-parsing of CLI args between phases).

- [ ] **Step 2: E2E test:** run `discoverDotnetManifestAsync` + `generateAggregator` against a fixture app dir inside the test (fixture `node_modules` with one fake dotnet module package containing an `expo-module.config.json` with a dotnet section and a stub csproj) and assert the generated `LinkedExpoModulesProvider.g.cs` registers `ExpoModulesProvider_FakeModule`. This is the discovery e2e that Task 3 deferred.

- [ ] **Step 3: Run tests, verify pass.**

- [ ] **Step 4: Commit**

```bash
git add packages/expo-modules-dotnet-autolinking/src
git commit -m "feat(autolinking): link command and discovery e2e coverage"
```

---

### Task 9: example-module opts into dotnet autolinking

**Files:**
- Create: `packages/example-module/expo-module.config.json`
- Modify: `packages/example-module/scripts/build-nativeaot.sh` (deprecation notice)

**Interfaces:**
- Produces: workspace-real resolution — `resolve --project-root apps/desktop-app` now returns example-module.

- [ ] **Step 1: Write the config**

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

Note: `platforms` is only `["dotnet"]` — the JS-side installer autolinking runs from `expo-modules-dotnet`'s own config; adding `apple`/`android` here would make CocoaPods/Gradle look for native projects this package doesn't have.

- [ ] **Step 2: Verify discovery**

Run: `node packages/expo-modules-dotnet-autolinking/bin/expo-modules-dotnet-autolinking.js resolve --project-root apps/desktop-app`
Expected: manifest contains `example-module` with assemblyName `ExampleModule`. Repeat for `apps/mobile-app`.

- [ ] **Step 3: Full local pipeline smoke (macOS)**

Run: `node packages/expo-modules-dotnet-autolinking/bin/expo-modules-dotnet-autolinking.js link --platform macos --project-root apps/desktop-app`
Expected: `apps/desktop-app/macos/Managed/` contains `ExpoDotnetHost.dll`, `ExampleModule.dll`, `Expo.JSI.dll`, `Expo.ModulesCore.dll`, `ExpoDotnetHost.runtimeconfig.json`, deps files, `libnethost.dylib`.

- [ ] **Step 4: Deprecate build-nativeaot.sh** — prepend to `packages/example-module/scripts/build-nativeaot.sh` after the shebang:

```bash
echo "[deprecated] Per-module NativeAOT staging is superseded by expo-modules-dotnet-autolinking." >&2
echo "[deprecated] iOS/Android app-side staging is documented in docs/specs/dotnet-autolinking.md." >&2
```

Keep the script functional until iOS/Android migrate (it still stages the adapter-vendored mobile artifacts, and `EntryPoints.cs` still exists until Task 10).

- [ ] **Step 5: Commit**

```bash
git add packages/example-module
git commit -m "feat(example-module): declare dotnet autolinking metadata"
```

---

### Task 10: Loader switch to ExpoDotnetHost + gate legacy EntryPoints.cs

**Files:**
- Modify: `packages/expo-modules-dotnet/macos/ManagedLoader.mm` (constants around lines 14-26 and `loadExampleModuleConfig()` around lines 188-198)
- Modify: `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ManagedLoader.cpp` (line 19 constant and `loadExampleModuleConfig()` around lines 275-285)
- Modify: `packages/example-module/dotnet/ExampleModule/ExampleModule.csproj` (compile gate)
- Modify: `packages/example-module/scripts/build-nativeaot.sh` (pass the gate property)

**Interfaces:**
- Consumes: generated aggregator (Task 4) staged by `link` (Task 8).
- Produces: loaders resolve `"Expo.ModulesCore.Generated.EntryPoints, ExpoDotnetHost"`; artifact names `ExpoDotnetHost.dll`, `ExpoDotnetHost.runtimeconfig.json`, `libExpoDotnetHost.dylib`.

- [ ] **Step 1: macOS loader** — in `ManagedLoader.mm`:
  - `kEntryPointType` → `"Expo.ModulesCore.Generated.EntryPoints, ExpoDotnetHost"`
  - In the config-loading function: `libExampleModule` → `libExpoDotnetHost`, `ExampleModule.dll` → `ExpoDotnetHost.dll`, `ExampleModule.runtimeconfig.json` → `ExpoDotnetHost.runtimeconfig.json`; rename `loadExampleModuleConfig()` → `loadManagedHostConfig()` (update its call sites in the same file) and update the NativeAOT error log text.

- [ ] **Step 2: Windows loader** — same renames in `ManagedLoader.cpp` (wide strings: `L"Expo.ModulesCore.Generated.EntryPoints, ExpoDotnetHost"`, `L"ExpoDotnetHost.dll"`, `L"ExpoDotnetHost.runtimeconfig.json"`).

- [ ] **Step 3: Gate `EntryPoints.cs` behind an opt-in MSBuild property.** The aggregator owns these
exports now; compiling `EntryPoints.cs` into a project referenced by `ExpoDotnetHost` would produce
duplicate `UnmanagedCallersOnly` symbols in the aggregator's NativeAOT publish. But the not-yet-migrated
mobile proof (`build-nativeaot.sh`) still publishes `ExampleModule` standalone and needs the legacy
entry points. So: exclude by default, opt in via property. In `ExampleModule.csproj`:

```xml
<ItemGroup Condition="'$(LegacyMobileEntryPoints)' != 'true'">
  <!-- Legacy app-composition entry points, only for the pre-autolinking mobile NativeAOT proof.
       Superseded by the expo-modules-dotnet-autolinking ExpoDotnetHost aggregator. -->
  <Compile Remove="EntryPoints.cs" />
</ItemGroup>
```

In `packages/example-module/scripts/build-nativeaot.sh`, add `/p:LegacyMobileEntryPoints=true` to the
`dotnet publish` invocation (around line 34) so the mobile staging path keeps working. Delete
`EntryPoints.cs` and this gate entirely when iOS/Android migrate to autolinking.

- [ ] **Step 4: Rebuild + restage:** `node packages/expo-modules-dotnet-autolinking/bin/expo-modules-dotnet-autolinking.js link --platform macos --project-root apps/desktop-app`
Expected: succeeds; `ExampleModule.dll` no longer exports entry points but `ExpoDotnetHost.dll` does.
Then verify the legacy mobile path still compiles: `dotnet build packages/example-module/dotnet/ExampleModule/ExampleModule.csproj -c Debug /p:LegacyMobileEntryPoints=true` → succeeds.

- [ ] **Step 5: Managed suite still green:** `scripts/test-managed.sh` → PASS (testhost uses its own harness, not these entry points; if it references `EntryPoints`, fix the reference to the testhost-local composition — do not weaken the `LegacyMobileEntryPoints` gate).

- [ ] **Step 6: Commit**

```bash
git add packages/expo-modules-dotnet packages/example-module
git commit -m "feat(autolinking): loaders resolve stable ExpoDotnetHost entry points"
```

Known transient state: `apps/desktop-app` builds now require running `link` (manual until Tasks 11-12 add hooks); the mobile apps keep working on the previously staged adapter-vendored artifacts.

---

### Task 11: macOS build hook (Podfile helper + script phase)

**Files:**
- Create: `packages/expo-modules-dotnet/scripts/autolinking.rb`
- Modify: `apps/desktop-app/macos/Podfile`
- Delete: `apps/desktop-app/scripts/build-managed.sh`

**Interfaces:**
- Produces: Ruby method `use_expo_modules_dotnet!(project_root:)` adding an Xcode script phase.

- [ ] **Step 1: Write the Podfile helper**

```ruby
# packages/expo-modules-dotnet/scripts/autolinking.rb
def use_expo_modules_dotnet!(options = {})
  project_root = options.fetch(:project_root, File.expand_path('..', __dir__))
  script_phase(
    name: 'Link Expo .NET Modules',
    execution_position: :before_compile,
    always_out_of_date: '1',
    script: <<~SCRIPT
      set -euo pipefail
      cd "#{project_root}"
      node --no-warnings --eval "require('expo-modules-dotnet-autolinking').main(process.argv.slice(1))" \
        link --platform macos --project-root "#{project_root}"
    SCRIPT
  )
end
```

Implementation note: `script_phase` must run inside the app `target` block; if the plain function form doesn't attach, follow the pattern upstream `autolinking_manager.rb` uses (invoked within `target do ... end`). Loader mode inside the phase respects `EXPO_DOTNET_LOADER` since the CLI reads the same defaulting rules; pass `--mode "$EXPO_DOTNET_LOADER"` only when set.

- [ ] **Step 2: Wire the Podfile** — in `apps/desktop-app/macos/Podfile`, inside the app target after `use_expo_modules!`:

```ruby
require File.join(File.dirname(`node --print "require.resolve('expo-modules-dotnet/package.json')"`), "scripts/autolinking.rb")
use_expo_modules_dotnet!(project_root: File.expand_path('..', __dir__))
```

- [ ] **Step 3: Regenerate pods:** `cd apps/desktop-app/macos && bundle exec pod install` (or the repo's documented pod install command). Expected: install succeeds, script phase appears in the generated project.

- [ ] **Step 4: Manual proof — build & run desktop app (macOS)** via the repo's normal Xcode/CLI build. Expected: build runs the phase (visible `dotnet build` output), app boots, `requireDotnetModule('ExampleModule').add(2,3) === 5` path works (use the app's existing example screen). Record commands + results in the task summary.

- [ ] **Step 5: Delete `apps/desktop-app/scripts/build-managed.sh`** and grep for references: `rg -l "build-managed" apps docs packages` — update or remove hits (docs updated fully in Task 13).

- [ ] **Step 6: Commit**

```bash
git add packages/expo-modules-dotnet apps/desktop-app
git commit -m "feat(autolinking): macOS Xcode script phase runs dotnet autolinking"
```

---

### Task 12: Windows build hook (MSBuild targets)

**Files:**
- Create: `packages/expo-modules-dotnet/windows/ExpoDotnetAutolink.targets`
- Modify: `apps/desktop-app/windows/DesktopApp/DesktopApp.vcxproj` (one Import line)
- Delete: `apps/desktop-app/scripts/build-managed.ps1`

**Interfaces:**
- Produces: importable `.targets` running `link --platform windows` pre-build; named target `ExpoDotnetGenerate` for the future hybrid-hook migration.

- [ ] **Step 1: Write the targets file**

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <ExpoDotnetAppRoot Condition="'$(ExpoDotnetAppRoot)' == ''">$(MSBuildProjectDirectory)\..\..</ExpoDotnetAppRoot>
    <!-- Resolve the CLI through Node module resolution (pnpm-safe), not a hardcoded node_modules path. -->
    <ExpoDotnetAutolinkEval>require('expo-modules-dotnet-autolinking').main(process.argv.slice(1))</ExpoDotnetAutolinkEval>
  </PropertyGroup>
  <Target Name="ExpoDotnetGenerate">
    <Exec Command="node --no-warnings --eval &quot;$(ExpoDotnetAutolinkEval)&quot; generate --project-root &quot;$(ExpoDotnetAppRoot)&quot;"
          WorkingDirectory="$(ExpoDotnetAppRoot)" />
  </Target>
  <Target Name="ExpoDotnetLink" BeforeTargets="ClCompile">
    <Exec Command="node --no-warnings --eval &quot;$(ExpoDotnetAutolinkEval)&quot; link --platform windows --project-root &quot;$(ExpoDotnetAppRoot)&quot;"
          WorkingDirectory="$(ExpoDotnetAppRoot)" />
  </Target>
</Project>
```

- [ ] **Step 2: Import from the app vcxproj** — add next to the existing `AutolinkedNativeModules.g.targets` import in `apps/desktop-app/windows/DesktopApp/DesktopApp.vcxproj`:

```xml
<Import Project="$(ExpoDotnetAppRoot)\node_modules\expo-modules-dotnet\windows\ExpoDotnetAutolink.targets" Condition="Exists('$(ExpoDotnetAppRoot)\node_modules\expo-modules-dotnet\windows\ExpoDotnetAutolink.targets')" />
```

(Define `ExpoDotnetAppRoot` in the vcxproj PropertyGroup first if the targets file's default doesn't resolve — verify the relative depth against the actual vcxproj location.)

- [ ] **Step 3: Delete `apps/desktop-app/scripts/build-managed.ps1`**; grep `rg -l "build-managed" apps docs packages` again for stragglers.

- [ ] **Step 4: Verification** — this machine is macOS; run what's runnable here: `node .../expo-modules-dotnet-autolinking.js generate --project-root apps/desktop-app` and MSBuild XML well-formedness (`xmllint --noout packages/expo-modules-dotnet/windows/ExpoDotnetAutolink.targets`). Full Windows build proof happens on `<windows-test-machine>`; record it as a pending manual verification in the final summary — do NOT claim Windows verified without it.

- [ ] **Step 5: Commit**

```bash
git add packages/expo-modules-dotnet apps/desktop-app
git commit -m "feat(autolinking): Windows MSBuild targets run dotnet autolinking"
```

---

### Task 13: Living-spec merge + docs

**Files:**
- Create: `docs/specs/dotnet-autolinking.md`
- Modify: `docs/specs/modules-core-boundary.md` (requirement "App Aggregation Remains Future Autolinking Work")
- Modify: `docs/specs/runtime-and-abi.md` (requirement "Managed Runtime Lifecycle Entry Points")
- Modify: `docs/specs/README.md` (index entry)
- Modify: `docs/README.md` (fix stale link `docs/modules-core-generator-authoring.md` → `docs/assorted/modules-core-generator-authoring.md`; add dotnet-autolinking spec to the front door)
- Modify: `docs/assorted/modules-core-generator-authoring.md` (Stage 2 is now implemented; config shape is now parsed)
- Modify: `docs/roadmap.md` (P1 config metadata + P3 autolinking status)
- Delete (after merge): entire `docs/changes/2026-07-04-dotnet-autolinking/` directory (spec.md + plan.md — accepted deltas live in `docs/specs/`; transient artifacts are removed per repo workflow)

- [ ] **Step 1: Write `docs/specs/dotnet-autolinking.md`** — copy the four ADDED requirements + scenarios from `docs/changes/2026-07-04-dotnet-autolinking/spec.md` verbatim into living-spec format (Purpose + Requirements), plus the documented-not-implemented iOS/Android integration and both migration notes (output dir, hybrid hooks) as non-normative sections.

- [ ] **Step 2: Update `modules-core-boundary.md`** — rewrite "App Aggregation Remains Future Autolinking Work" per the spec's MODIFIED section: aggregation exists; manual HostFXR desktop staging scenario replaced by CLI staging; manual adapter-owned NativeAOT staging remains only for not-yet-migrated iOS/Android (state that explicitly); `requireDotnetModule` scenario unchanged.

- [ ] **Step 3: Update `runtime-and-abi.md`** — entry points requirement: semantics unchanged; owner is the generated `ExpoDotnetHost` aggregator; loaders resolve the stable type name.

- [ ] **Step 4: Update README index, generator-authoring doc, roadmap.**

- [ ] **Step 5: Repo-wide verification**

```bash
scripts/test-managed.sh
scripts/format.sh --check --all   # run scripts/format.sh first if it flags files
git diff --check
pnpm --filter expo-modules-dotnet-autolinking test
pnpm --filter mobile-app typecheck
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|JsonSerializer" packages/expo-modules-dotnet/managed/packages
rg -n "ExampleModule.EntryPoints" packages/expo-modules-dotnet apps   # expect no hits in loaders/apps
# (packages/example-module still contains the LegacyMobileEntryPoints-gated EntryPoints.cs — expected until mobile migrates)
```

Also scan staged content for local absolute paths/usernames before committing.

- [ ] **Step 6: Merge commit for docs, then remove the transient change directory**

```bash
git add docs
git commit -m "docs: merge dotnet autolinking delta into living specs"
git rm -r docs/changes/2026-07-04-dotnet-autolinking
git commit -m "chore: remove transient dotnet autolinking change artifacts"
```
