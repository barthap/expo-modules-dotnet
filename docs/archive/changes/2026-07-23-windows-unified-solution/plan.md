# Windows Unified Solution Projection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Provide one checked-in React Native Windows solution containing the app's RNW-native projects and the current Expo .NET C# host graph, with HostFXR mixed-mode debugging.

**Architecture:** Add a package-owned `sync-windows` command to the existing autolinking CLI. It first invokes the app-local React Native CLI's `autolink-windows`, then generates the normal app-local `ExpoDotnetHost` project and rewrites only an owned `Expo .NET Managed` solution folder. Existing Windows `link` MSBuild staging remains unchanged and is still the only path that builds and stages runtime artifacts.

**Tech Stack:** TypeScript, Commander, Vitest, Node.js `child_process`, SHA-256 deterministic GUIDs, Visual Studio `.sln` / `.wapproj` text formats, React Native Windows CLI.

---

## File Structure

- `packages/expo-modules-dotnet-autolinking/src/windows/solution.ts` — parses and
  rewrites the package-owned managed portion of a `.sln`; derives stable GUIDs
  and config mappings without altering other projects.
- `packages/expo-modules-dotnet-autolinking/src/windows/reactNativeCli.ts` —
  resolves and launches the app-local `react-native/cli.js` for RNW
  `autolink-windows`, with no global CLI or `npx` fallback.
- `packages/expo-modules-dotnet-autolinking/src/windows/packageDebugger.ts` —
  locates the WAP package project and makes its launch debugger mixed mode.
- `packages/expo-modules-dotnet-autolinking/src/commands/syncWindowsCommand.ts`
  — defines the public CLI command and orders validation, RNW autolinking,
  host generation, debugger configuration, and solution synchronization.
- `packages/expo-modules-dotnet-autolinking/src/index.ts` — registers the new
  command.
- `packages/expo-modules-dotnet-autolinking/src/__tests__/windowsSolution.test.ts`
  — fixture-based tests for deterministic, non-destructive solution changes.
- `packages/expo-modules-dotnet-autolinking/src/__tests__/reactNativeCli.test.ts`
  — tests local CLI resolution, argument forwarding, and child-process errors.
- `packages/expo-modules-dotnet-autolinking/src/__tests__/packageDebugger.test.ts`
  — tests WAP discovery and mixed-mode debugger updates.
- `packages/expo-modules-dotnet-autolinking/src/__tests__/syncWindowsCommand.test.ts`
  — tests command sequencing, normal sync, check mode, and no build/stage work.
- `packages/expo-modules-dotnet-autolinking/README.md` — documents the public
  command.
- `apps/desktop-app/package.json`, `apps/desktop-app/windows/DesktopApp.sln`,
  `apps/desktop-app/windows/DesktopApp.Package/DesktopApp.Package.wapproj`, and
  `apps/desktop-app/README.md` — provide the checked-in proof and a local
  convenience script, while making clear that consumers invoke the published
  command directly.
- `docs/specs/dotnet-autolinking.md` and `docs/README.md` — receive the
  accepted stable contract after implementation.

### Task 1: Build a Deterministic, Non-Destructive Solution Projector

**Files:**

- Create: `packages/expo-modules-dotnet-autolinking/src/windows/solution.ts`
- Create: `packages/expo-modules-dotnet-autolinking/src/__tests__/windowsSolution.test.ts`

- [ ] **Step 1: Write failing fixture tests for a managed solution folder.**

  Create a minimal CRLF `.sln` fixture containing an RNW C++ project, a WAP
  package project, two solution configurations, and a user-owned solution
  folder. Write tests that request the managed graph below and assert all
  existing text remains intact while the generated projects, solution folder,
  `NestedProjects`, and configuration mappings are added.

  ```ts
  const graph: ManagedSolutionGraph = {
    hostProjectPath: path.join(appRoot, '.expo', 'dotnet', 'ExpoDotnetHost.csproj'),
    coreProjectPaths: [
      path.join(adapterRoot, 'managed', 'packages', 'Expo.JSI', 'Expo.JSI.csproj'),
      path.join(adapterRoot, 'managed', 'packages', 'Expo.ModulesCore', 'Expo.ModulesCore.csproj'),
    ],
    moduleProjectPaths: [path.join(moduleRoot, 'example', 'ExampleModule.csproj')],
  };

  const result = synchronizeManagedSolution(fixture, graph, { check: false });

  expect(result.changed).toBe(true);
  expect(result.content).toContain('= "Expo .NET Managed", "Expo .NET Managed",');
  expect(result.content).toContain('= "ExpoDotnetHost", "..\\.expo\\dotnet\\ExpoDotnetHost.csproj",');
  expect(result.content).toContain('= "ExampleModule", "..\\modules\\example\\ExampleModule.csproj",');
  expect(result.content).toContain('User-Owned-Project');
  expect(result.content).not.toContain('.Debug|x64.Build.0 = Debug|Any CPU');
  ```

- [ ] **Step 2: Run the focused test and verify it fails because the module is absent.**

  Run:

  ```powershell
  pnpm --filter expo-modules-dotnet-autolinking test -- windowsSolution.test.ts
  ```

  Expected: FAIL with a module-not-found error for `../windows/solution`.

- [ ] **Step 3: Define the solution model and stable project-ID helper.**

  In `src/windows/solution.ts`, add these exported types and make GUID
  generation depend only on the normalized absolute path, not the current
  machine's path separator or insertion order. Use SHA-256's first 16 bytes,
  set RFC 4122 version and variant bits, and render uppercase braces.

  ```ts
  export interface ManagedSolutionGraph {
    hostProjectPath: string;
    coreProjectPaths: string[];
    moduleProjectPaths: string[];
  }

  export interface SolutionSynchronizationResult {
    changed: boolean;
    content: string;
  }

  export const solutionFolderTypeGuid = '{66A26720-8FB5-11D2-AA7E-00C04F688DDE}';
  export const csharpProjectTypeGuid = '{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}';

  export function stableSolutionGuid(projectPath: string): string {
    const identity = path.resolve(projectPath).replace(/\\/g, '/').toLowerCase();
    const bytes = createHash('sha256').update(identity).digest().subarray(0, 16);
    bytes[6] = (bytes[6] & 0x0f) | 0x50;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;
    const hex = bytes.toString('hex').toUpperCase();
    return `{${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}}`;
  }
  ```

- [ ] **Step 4: Implement owned-folder replacement and configuration mappings.**

  Parse project blocks, the `ProjectConfigurationPlatforms` global section,
  and the `NestedProjects` global section while retaining all unknown lines and
  their CRLF/LF convention. Identify only the `Expo .NET Managed` solution
  folder and projects nested under it as package-owned. Remove that owned
  subset, then append a deterministic folder and the following de-duplicated,
  alphabetically ordered project set: host first, core projects, module
  projects. Project paths must be relative to the solution directory and use
  backslashes.

  For every existing `Debug|*` and `Release|*` solution configuration, emit
  only an `ActiveCfg` mapping to `Debug|Any CPU` or `Release|Any CPU`; do not
  emit `.Build.0`. Add every managed project to `NestedProjects` under the
  owned folder. Return the original text when the final result is byte-equal.

  ```ts
  export function synchronizeManagedSolution(
    source: string,
    graph: ManagedSolutionGraph,
    options: { check: boolean }
  ): SolutionSynchronizationResult {
    const desired = createManagedSolutionEntries(source, graph);
    const content = replaceOwnedManagedEntries(source, desired);
    return { changed: content !== source, content: options.check ? source : content };
  }
  ```

- [ ] **Step 5: Add failure, idempotence, and removal tests.**

  Add tests for duplicate source project paths, a non-solution input, two
  `Expo .NET Managed` folders, a removed module, CRLF preservation, and a
  second normal synchronization. Check mode must report `changed: true` while
  returning the original `content`; normal mode must return identical content
  on the second run.

  ```ts
  expect(() => synchronizeManagedSolution('not a solution', graph, { check: false }))
    .toThrow('does not contain a Visual Studio solution header');
  expect(synchronizeManagedSolution(first.content, graph, { check: false })).toEqual({
    changed: false,
    content: first.content,
  });
  ```

- [ ] **Step 6: Run the focused tests and format check.**

  Run:

  ```powershell
  pnpm --filter expo-modules-dotnet-autolinking test -- windowsSolution.test.ts
  pnpm exec prettier --check packages/expo-modules-dotnet-autolinking/src/windows/solution.ts packages/expo-modules-dotnet-autolinking/src/__tests__/windowsSolution.test.ts
  ```

  Expected: PASS.

- [ ] **Step 7: Commit the projector slice.**

  ```powershell
  git add packages/expo-modules-dotnet-autolinking/src/windows/solution.ts packages/expo-modules-dotnet-autolinking/src/__tests__/windowsSolution.test.ts
  git commit -m "feat: project managed modules into Windows solutions"
  ```

### Task 2: Invoke the App-Local RNW Autolinker

**Files:**

- Create: `packages/expo-modules-dotnet-autolinking/src/windows/reactNativeCli.ts`
- Create: `packages/expo-modules-dotnet-autolinking/src/__tests__/reactNativeCli.test.ts`

- [ ] **Step 1: Write failing tests for local CLI resolution and invocation.**

  Mock `require.resolve` and `child_process.spawn` to prove that the helper
  resolves `react-native/cli.js` from the supplied app root and calls Node with
  `autolink-windows`, `--sln`, and `--proj`. The check path must add exactly
  `--check`.

  ```ts
  await runReactNativeWindowsAutolink({
    appRoot,
    solutionPath,
    projectPath,
    check: true,
  });

  expect(requireResolve).toHaveBeenCalledWith('react-native/cli.js', { paths: [appRoot] });
  expect(spawn).toHaveBeenCalledWith(
    process.execPath,
    [resolvedCli, 'autolink-windows', '--sln', solutionPath, '--proj', projectPath, '--check'],
    { cwd: appRoot, stdio: 'inherit' }
  );
  ```

- [ ] **Step 2: Run the focused test and verify it fails.**

  Run:

  ```powershell
  pnpm --filter expo-modules-dotnet-autolinking test -- reactNativeCli.test.ts
  ```

  Expected: FAIL with a module-not-found error for `../windows/reactNativeCli`.

- [ ] **Step 3: Implement bounded process execution and actionable errors.**

  Use `spawn`, await its `error`/`close` events, and reject on a nonzero exit
  status. Do not use a shell and do not call `npx`. Convert resolution failures
  to an error explaining that the app must install React Native Windows and its
  React Native CLI integration.

  ```ts
  export interface ReactNativeWindowsAutolinkOptions {
    appRoot: string;
    solutionPath: string;
    projectPath: string;
    check: boolean;
  }

  export async function runReactNativeWindowsAutolink(
    options: ReactNativeWindowsAutolinkOptions
  ): Promise<void> {
    const cli = require.resolve('react-native/cli.js', { paths: [options.appRoot] });
    const args = [cli, 'autolink-windows', '--sln', options.solutionPath, '--proj', options.projectPath];
    if (options.check) args.push('--check');
    await runNode(process.execPath, args, options.appRoot);
  }
  ```

- [ ] **Step 4: Add missing-CLI and nonzero-exit tests, then rerun.**

  Assert that an unresolved CLI names the app root and that an exit code of 1
  rejects before any later synchronization work can run.

  Run:

  ```powershell
  pnpm --filter expo-modules-dotnet-autolinking test -- reactNativeCli.test.ts
  ```

  Expected: PASS.

- [ ] **Step 5: Commit the RNW invocation slice.**

  ```powershell
  git add packages/expo-modules-dotnet-autolinking/src/windows/reactNativeCli.ts packages/expo-modules-dotnet-autolinking/src/__tests__/reactNativeCli.test.ts
  git commit -m "feat: invoke app-local RNW autolinking"
  ```

### Task 3: Configure the WAP Package for Mixed-Mode HostFXR Debugging

**Files:**

- Create: `packages/expo-modules-dotnet-autolinking/src/windows/packageDebugger.ts`
- Create: `packages/expo-modules-dotnet-autolinking/src/__tests__/packageDebugger.test.ts`

- [ ] **Step 1: Write failing tests for package discovery and debugger update.**

  Use solution fixtures with one `.wapproj`, zero `.wapproj`, and two
  `.wapproj` project entries. For the single package fixture, assert that a
  `NativeOnly` debugger type becomes `Mixed`, the rest of the XML remains
  unchanged, and a second update makes no write.

  ```ts
  expect(findPackageProjectPath(solutionText, solutionPath)).toBe(
    path.join(appRoot, 'windows', 'App.Package', 'App.Package.wapproj')
  );
  expect(configureMixedModeDebugger(wapText)).toEqual({
    changed: true,
    content: wapText.replace('<DebuggerType>NativeOnly</DebuggerType>', '<DebuggerType>Mixed</DebuggerType>'),
  });
  ```

- [ ] **Step 2: Run the focused test and verify it fails.**

  Run:

  ```powershell
  pnpm --filter expo-modules-dotnet-autolinking test -- packageDebugger.test.ts
  ```

  Expected: FAIL with a module-not-found error for `../windows/packageDebugger`.

- [ ] **Step 3: Implement explicit, conservative WAP discovery and update.**

  Resolve a single `.wapproj` path from the post-RNW solution. Fail with a
  message that names the solution if none or more than one package project is
  present; do not guess. In the package XML, replace or insert exactly one
  `<DebuggerType>Mixed</DebuggerType>` under the first property group. Preserve
  the file's encoding and line endings through UTF-8 text reads/writes. Leave
  `BackgroundTaskDebugEngines` unchanged because it does not control normal app
  launch debugging.

  ```ts
  export function configureMixedModeDebugger(source: string): { changed: boolean; content: string } {
    const replacement = '<DebuggerType>Mixed</DebuggerType>';
    if (source.includes(replacement)) return { changed: false, content: source };
    if (/<DebuggerType>[^<]*<\/DebuggerType>/.test(source)) {
      const content = source.replace(/<DebuggerType>[^<]*<\/DebuggerType>/, replacement);
      return { changed: true, content };
    }
    const content = source.replace(/<PropertyGroup(?:\s[^>]*)?>/, (tag) => `${tag}\n    ${replacement}`);
    if (content === source) throw new Error('does not contain a WAP PropertyGroup');
    return { changed: true, content };
  }
  ```

- [ ] **Step 4: Add malformed XML and multiple-package failures, then rerun.**

  Run:

  ```powershell
  pnpm --filter expo-modules-dotnet-autolinking test -- packageDebugger.test.ts
  ```

  Expected: PASS.

- [ ] **Step 5: Commit the mixed-debugger slice.**

  ```powershell
  git add packages/expo-modules-dotnet-autolinking/src/windows/packageDebugger.ts packages/expo-modules-dotnet-autolinking/src/__tests__/packageDebugger.test.ts
  git commit -m "feat: configure mixed mode Windows debugging"
  ```

### Task 4: Expose and Test the `sync-windows` Public Command

**Files:**

- Create: `packages/expo-modules-dotnet-autolinking/src/commands/syncWindowsCommand.ts`
- Create: `packages/expo-modules-dotnet-autolinking/src/__tests__/syncWindowsCommand.test.ts`
- Modify: `packages/expo-modules-dotnet-autolinking/src/index.ts`

- [ ] **Step 1: Write failing command tests that define ordering and boundaries.**

  Mock the RNW runner, manifest discovery, aggregator generator, solution
  projector, WAP updater, and all build/stage modules. Verify a normal sync
  performs RNW autolinking first, then discovery/generation, debugger update,
  and solution write. Verify `--check` performs RNW check plus discovery and
  stale projection comparison without calling the host writer, WAP writer, or
  `runDotnetBuildAsync` / `stageArtifactsAsync`.

  ```ts
  await program.parseAsync([
    'sync-windows', '--project-root', appRoot, '--sln', 'windows/App.sln', '--proj', 'windows/App/App.vcxproj'
  ], { from: 'user' });

  expect(calls).toEqual([
    'runReactNativeWindowsAutolink',
    'discoverDotnetManifestAsync',
    'generateAggregator',
    'configureMixedModeDebugger',
    'synchronizeManagedSolution',
  ]);
  expect(runDotnetBuildAsync).not.toHaveBeenCalled();
  expect(stageArtifactsAsync).not.toHaveBeenCalled();
  ```

- [ ] **Step 2: Run the command tests and verify failure.**

  Run:

  ```powershell
  pnpm --filter expo-modules-dotnet-autolinking test -- syncWindowsCommand.test.ts
  ```

  Expected: FAIL with a module-not-found error for `../commands/syncWindowsCommand`.

- [ ] **Step 3: Implement the command with path validation and check semantics.**

  Register `sync-windows` with required `--sln <path>` and `--proj <path>`,
  optional `--project-root <dir>`, and `--check`. Resolve relative solution and
  project paths against the app root; verify both exist before calling RNW.
  Normal mode must invoke RNW without `--check`, generate the host into the
  normal `.expo/dotnet` directory, update the unique WAP project, and write
  changed files with `writeIfChangedSync`. Check mode must invoke RNW with
  `--check`, discover the manifest without generating files, calculate the
  expected graph using the conventional host path, and exit unsuccessfully if
  the managed projection differs.

  ```ts
  interface SyncWindowsCommandOptions {
    projectRoot?: string;
    sln: string;
    proj: string;
    check?: boolean;
  }

  program.command('sync-windows')
    .requiredOption('--sln <path>', 'RNW Visual Studio solution path')
    .requiredOption('--proj <path>', 'RNW app .vcxproj path')
    .option('--project-root <dir>', 'App project root (defaults to nearest package.json)')
    .option('--check', 'Check native and managed solution projections without writing')
    .action(async (options: SyncWindowsCommandOptions) => {
      await synchronizeWindowsSolution(options);
    });
  ```

- [ ] **Step 4: Register the command and cover error paths.**

  Add `registerSyncWindowsCommand(program)` in `src/index.ts`. Test unknown
  `.sln` / `.vcxproj` paths, RNW failure, zero/multiple WAP projects, stale
  check output, and idempotent normal output. Confirm that errors leave the
  managed solution and WAP file unchanged.

- [ ] **Step 5: Run focused tests, package tests, and typecheck.**

  Run:

  ```powershell
  pnpm --filter expo-modules-dotnet-autolinking test -- syncWindowsCommand.test.ts
  pnpm --filter expo-modules-dotnet-autolinking test
  pnpm --filter expo-modules-dotnet-autolinking typecheck
  ```

  Expected: PASS.

- [ ] **Step 6: Commit the public command slice.**

  ```powershell
  git add packages/expo-modules-dotnet-autolinking/src/index.ts packages/expo-modules-dotnet-autolinking/src/commands/syncWindowsCommand.ts packages/expo-modules-dotnet-autolinking/src/__tests__/syncWindowsCommand.test.ts
  git commit -m "feat: sync RNW and managed solution projects"
  ```

### Task 5: Integrate the Desktop App as a Checked-In Proof

**Files:**

- Modify: `apps/desktop-app/package.json`
- Modify: `apps/desktop-app/windows/DesktopApp.sln`
- Modify: `apps/desktop-app/windows/DesktopApp.Package/DesktopApp.Package.wapproj`
- Modify: `apps/desktop-app/README.md`

- [ ] **Step 1: Add a failing integration assertion for the desktop solution.**

  Add a CLI integration test that invokes `sync-windows` against a copied
  desktop fixture with its app-local React Native CLI mocked. Assert that the
  result contains `ExpoDotnetHost`, `Expo.JSI`, `Expo.ModulesCore`, and
  `ExampleModule`, and that the WAP project declares `Mixed` debugging.

  ```ts
  expect(readFileSync(solutionPath, 'utf8')).toContain('"ExpoDotnetHost"');
  expect(readFileSync(solutionPath, 'utf8')).toContain('"ExampleModule"');
  expect(readFileSync(wapPath, 'utf8')).toContain('<DebuggerType>Mixed</DebuggerType>');
  ```

- [ ] **Step 2: Run it and verify it fails against the pre-sync fixture.**

  Run:

  ```powershell
  pnpm --filter expo-modules-dotnet-autolinking test -- syncWindowsCommand.test.ts
  ```

  Expected: FAIL because the desktop fixture lacks the managed solution folder
  and still declares `NativeOnly`.

- [ ] **Step 3: Add the example-app convenience command and synchronize checked-in artifacts.**

  Add this script without making it part of the published-package contract:

  ```json
  "autolink:windows": "expo-modules-dotnet-autolinking sync-windows --sln windows/DesktopApp.sln --proj windows/DesktopApp/DesktopApp.vcxproj"
  ```

  Run the command from `apps/desktop-app` to update the checked-in solution and
  WAP debugger property. Do not add `.expo/dotnet` to source control.

- [ ] **Step 4: Verify the solution is build-safe and the regular artifact hook remains separate.**

  Inspect the generated managed solution configuration entries: each must have
  `ActiveCfg` but no `.Build.0`. Confirm
  `packages/expo-modules-dotnet/windows/ExpoDotnetAutolink.targets` still runs
  only `link --platform windows` before `PrepareForBuild`; do not route it
  through `sync-windows`.

  Run:

  ```powershell
  pnpm --filter desktop-app autolink:windows
  pnpm --filter desktop-app exec react-native autolink-windows --check --sln "windows\DesktopApp.sln" --proj "windows\DesktopApp\DesktopApp.vcxproj"
  pnpm --filter desktop-app typecheck
  ```

  Expected: all commands succeed; the second command confirms the normal RNW
  autolink output is still valid independently of the managed projection.

- [ ] **Step 5: Commit the desktop proof slice.**

  ```powershell
  git add apps/desktop-app/package.json apps/desktop-app/windows/DesktopApp.sln apps/desktop-app/windows/DesktopApp.Package/DesktopApp.Package.wapproj packages/expo-modules-dotnet-autolinking/src/__tests__/syncWindowsCommand.test.ts
  git commit -m "feat: expose managed projects in desktop solution"
  ```

### Task 6: Document the Public Workflow and Merge the Living Spec

**Files:**

- Modify: `packages/expo-modules-dotnet-autolinking/README.md`
- Modify: `apps/desktop-app/README.md`
- Modify: `docs/specs/dotnet-autolinking.md`
- Modify: `docs/README.md`
- Remove or archive after merge: `docs/changes/2026-07-23-windows-unified-solution/spec.md`
- Remove or archive after merge: `docs/changes/2026-07-23-windows-unified-solution/plan.md`

- [ ] **Step 1: Add public command documentation.**

  Document this consumer-facing command, including its required RNW paths and
  the fact that it owns only solution synchronization:

  ```powershell
  npx expo-modules-dotnet-autolinking sync-windows `
    --project-root . `
    --sln windows/MyApp.sln `
    --proj windows/MyApp/MyApp.vcxproj
  ```

  State explicitly that developers run it after adding/removing native or
  .NET Expo modules, commit the resulting `.sln` and `.wapproj` changes, and
  continue to rely on the unchanged Windows `link` target for build/staging.

- [ ] **Step 2: Document Visual Studio behavior and NativeAOT boundary.**

  Update the desktop guide to open the synchronized solution, set the WAP
  package project as startup, select Debug / x64, start Metro separately, and
  debug C++ and C# with HostFXR. State that NativeAOT supports native debugging
  only in the published host and is not the C# breakpoint workflow.

- [ ] **Step 3: Merge accepted requirements into the living autolinking spec.**

  Add a `sync-windows` requirement and scenarios to
  `docs/specs/dotnet-autolinking.md`: package-owned invocation, app-local RNW
  CLI resolution, deterministic checked-in managed solution projection,
  idempotent check mode, unchanged `link` hooks, and HostFXR mixed-mode
  debugging. Update `docs/README.md` reading-order text if it needs to point
  contributors to the Windows solution workflow.

- [ ] **Step 4: Run documentation and repository verification.**

  Run:

  ```powershell
  scripts/test-managed.sh
  scripts/format.sh --check --all
  git diff --check
  rg "self[-]contained planning package|planning[ ]artifacts,[ ]not[ ]implementation|expo[-]modules[-]windows[-]core|Phase[ ]1:[ ]clean[ ]separate[ ]research[ ]repo|create[ ]a[ ]clean[ ]local[ ]research[ ]repository" docs/README.md docs/specs docs/roadmap.md AGENTS.md .agents/skills
  ```

  Expected: all commands succeed and the search has no unintended matches.

- [ ] **Step 5: Perform the Windows end-to-end verification.**

  On a Windows Visual Studio machine, run:

  ```powershell
  pnpm install --frozen-lockfile
  pnpm --filter desktop-app autolink:windows
  pnpm --filter desktop-app exec react-native autolink-windows --check --sln "windows\DesktopApp.sln" --proj "windows\DesktopApp\DesktopApp.vcxproj"
  MSBuild.exe apps/desktop-app/windows/DesktopApp.sln /restore /p:Configuration=Debug /p:Platform=x64 /m:1
  ```

  Then launch `DesktopApp.Package` from Visual Studio with Metro running,
  verify a breakpoint in `ExampleMathModule.cs` binds and hits, and verify a
  breakpoint in `ExpoModulesDotnetInstaller.cpp` hits in the same session.

- [ ] **Step 6: Archive transient planning artifacts and commit documentation.**

  After the living spec is updated, move the accepted delta spec and plan into
  the repository's normal archive location or remove them according to the
  current documentation policy; do not leave them as the only source of truth.

  ```powershell
  git add docs packages/expo-modules-dotnet-autolinking/README.md apps/desktop-app/README.md
  git commit -m "docs: document unified Windows solution workflow"
  ```
