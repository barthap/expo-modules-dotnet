import { spawnSync } from 'child_process';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { describe, expect, it } from 'vitest';

import { generateAggregator } from '../codegen/generateAggregator';
import type { DotnetLinkingManifest } from '../types';

const repoRoot = path.resolve(__dirname, '../../../..');
const realAdapterPackageRoot = path.join(repoRoot, 'packages/expo-modules-dotnet');
const abiHarnessFixture = path.join(__dirname, 'fixtures/entry-points-abi-harness.cs');

const generatedFiles = [
  'EntryPoints.g.cs',
  'ExpoDotnetHost.csproj',
  'LinkedExpoModulesProvider.g.cs',
];

function makeTempRoot(): string {
  return fs.mkdtempSync(path.join(os.tmpdir(), 'dotnet-aggregator-'));
}

function writeCsproj(root: string, relPath: string): string {
  const csprojPath = path.join(root, relPath);
  fs.mkdirSync(path.dirname(csprojPath), { recursive: true });
  fs.writeFileSync(csprojPath, '<Project Sdk="Microsoft.NET.Sdk" />');
  return csprojPath;
}

function makeAdapterPackageRoot(root: string): string {
  const adapterPackageRoot = path.join(root, 'adapter', 'expo-modules-dotnet');
  writeCsproj(adapterPackageRoot, 'managed/packages/Expo.JSI/Expo.JSI.csproj');
  writeCsproj(adapterPackageRoot, 'managed/packages/Expo.ModulesCore/Expo.ModulesCore.csproj');
  return adapterPackageRoot;
}

function makeFixture(): {
  adapterPackageRoot: string;
  manifest: DotnetLinkingManifest;
  outputDir: string;
} {
  const root = makeTempRoot();
  const outputDir = path.join(root, 'generated', 'dotnet-host');
  const adapterPackageRoot = makeAdapterPackageRoot(root);
  const moduleARoot = path.join(root, 'modules', 'a-module');
  const moduleBRoot = path.join(root, 'modules', 'b-module');
  const moduleAProject = writeCsproj(moduleARoot, 'dotnet/A/A.csproj');
  const moduleBProject = writeCsproj(moduleBRoot, 'dotnet/B/B.csproj');

  return {
    adapterPackageRoot,
    outputDir,
    manifest: {
      modules: [
        {
          packageName: 'a-module',
          packageRoot: moduleARoot,
          projects: [{ csprojPath: moduleAProject, assemblyName: 'A' }],
        },
        {
          packageName: 'b-module',
          packageRoot: moduleBRoot,
          projects: [{ csprojPath: moduleBProject, assemblyName: 'B' }],
        },
      ],
    },
  };
}

function readGenerated(outputDir: string, fileName: string): string {
  return fs.readFileSync(path.join(outputDir, fileName), 'utf8');
}

function listOutputFiles(outputDir: string): string[] {
  return fs.readdirSync(outputDir).sort();
}

describe('generateAggregator', () => {
  it('emits exactly three files', () => {
    const { adapterPackageRoot, manifest, outputDir } = makeFixture();

    const result = generateAggregator(manifest, { outputDir, adapterPackageRoot });

    expect(listOutputFiles(outputDir)).toEqual(generatedFiles);
    expect(result.writtenFiles.map((filePath) => path.basename(filePath)).sort()).toEqual(generatedFiles);
    expect(result.skippedFiles).toEqual([]);
  });

  it('registers providers in manifest order', () => {
    const { adapterPackageRoot, manifest, outputDir } = makeFixture();

    generateAggregator(manifest, { outputDir, adapterPackageRoot });

    const provider = readGenerated(outputDir, 'LinkedExpoModulesProvider.g.cs');
    const aIndex = provider.indexOf('Expo.ModulesCore.Generated.ExpoModulesProvider_A.Register(context);');
    const bIndex = provider.indexOf('Expo.ModulesCore.Generated.ExpoModulesProvider_B.Register(context);');
    expect(aIndex).toBeGreaterThan(-1);
    expect(bIndex).toBeGreaterThan(-1);
    expect(aIndex).toBeLessThan(bIndex);
  });

  it('sanitizes provider identifiers like the Roslyn generator', () => {
    const { adapterPackageRoot, manifest, outputDir } = makeFixture();
    manifest.modules[0].projects[0].assemblyName = 'Expo.Test-Modules';

    generateAggregator(manifest, { outputDir, adapterPackageRoot });

    const provider = readGenerated(outputDir, 'LinkedExpoModulesProvider.g.cs');
    expect(provider).toContain(
      'Expo.ModulesCore.Generated.ExpoModulesProvider_Expo_Test_Modules.Register(context);'
    );
    expect(provider).not.toContain('ExpoModulesProvider_Expo.Test-Modules');
  });

  it('emits a structured runtime-context creation result', () => {
    const { adapterPackageRoot, manifest, outputDir } = makeFixture();

    generateAggregator(manifest, { outputDir, adapterPackageRoot });

    const entryPoints = readGenerated(outputDir, 'EntryPoints.g.cs');
    expect(entryPoints).toContain('EntryPoint = "expo_dotnet_create_runtime_context_result_v2"');
    expect(entryPoints).toContain('public static unsafe void CreateRuntimeContextResultV2');
    expect(entryPoints).toContain('result->Ok = 1;');
    expect(entryPoints).toContain('result->Error.Release = &ReleaseRuntimeContextError;');
    expect(entryPoints).toContain(`    public static void TeardownRuntimeContext(nint runtimeContext)
    {
        try
        {
            TeardownRuntimeContextCore(runtimeContext);
        }
        catch (Exception ex)
        {
            LogException(ex);
        }
    }`);
    expect(entryPoints).toContain(`        try
        {
            if (handle.Target is DotnetRuntimeContext context)
            {
                DisposeRuntimeContext(context);
            }
        }
        finally
        {
            // The native adapter passed this opaque context back to us; release
            // its GCHandle even when managed context disposal reports failures.
            handle.Free();
        }`);
    expect(entryPoints).toContain(`    private static void DisposeRuntimeContext(DotnetRuntimeContext context)
    {
        try
        {
            context.Dispose();
        }
        catch (Exception ex)
        {
            LogException(ex);
        }
    }`);
    expect(entryPoints).toContain(`    private static void LogException(Exception exception)
    {
        try
        {
            Console.Error.WriteLine(exception);
        }
        catch
        {
            // Best-effort logging only. Throwing while reporting an exception
            // would reopen the unmanaged boundary this path is protecting.
        }
    }`);
    expect(entryPoints).not.toContain('expo_dotnet_get_last_error');
  });

  it('renames the create entry point so a stale adapter fails resolution', () => {
    const { adapterPackageRoot, manifest, outputDir } = makeFixture();

    generateAggregator(manifest, { outputDir, adapterPackageRoot });

    const entryPoints = readGenerated(outputDir, 'EntryPoints.g.cs');
    // The version field inside the struct only protects the contents of an
    // already signature-safe call. Nothing but a different symbol and method name
    // stops an old adapter from calling the new host through the old
    // three-argument signature.
    expect(entryPoints).not.toContain('EntryPoint = "expo_dotnet_create_runtime_context_result"');
    expect(entryPoints).not.toContain('void CreateRuntimeContextResult(');
    expect(entryPoints).toContain(`    public static unsafe void CreateRuntimeContextResultV2(
        nint api,
        nint runtimeHandle,
        nint appDirectories,
        RuntimeContextResult* result)`);
  });

  it('emits a versioned app-directories mirror and decoder', () => {
    const { adapterPackageRoot, manifest, outputDir } = makeFixture();

    generateAggregator(manifest, { outputDir, adapterPackageRoot });

    const entryPoints = readGenerated(outputDir, 'EntryPoints.g.cs');
    // Partial so the compiled ABI harness can reach the private decoder without
    // the generator widening its visibility in a shipped app.
    expect(entryPoints).toContain('public static partial class EntryPoints');
    expect(entryPoints).toContain('private const uint ExpectedHostAbiVersion = 1;');
    expect(entryPoints).toContain('private static readonly UTF8Encoding StrictUtf8 = new(false, true);');
    // Field order must match expo_dotnet_app_directories in the shared header.
    expect(entryPoints).toContain(`    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct NativeAppDirectories
    {
        public uint Size;
        public uint Version;
        public byte* CacheDirectory;
        public int CacheDirectoryLength;
        public byte* PersistentFilesDirectory;
        public int PersistentFilesDirectoryLength;
    }`);

    // Size is checked before Version, and both before any pointer is read.
    const sizeCheck = entryPoints.indexOf('if (native->Size < expectedSize)');
    const versionCheck = entryPoints.indexOf('if (native->Version != ExpectedHostAbiVersion)');
    const firstPointerRead = entryPoints.indexOf('native->CacheDirectory,');
    expect(sizeCheck).toBeGreaterThan(-1);
    expect(versionCheck).toBeGreaterThan(sizeCheck);
    expect(firstPointerRead).toBeGreaterThan(versionCheck);
    expect(entryPoints).toContain(
      'Expo .NET host ABI version mismatch: native={native->Version} managed={ExpectedHostAbiVersion}.'
    );
    expect(entryPoints).toContain(
      'Expo .NET host app-directories struct is too small. Expected at least {expectedSize}, got {native->Size}.'
    );

    // A null struct pointer means both directories are unconfigured; at field
    // level only (null, 0) does.
    expect(entryPoints).toContain(`        if (pointer == 0)
        {
            return AppDirectories.Unconfigured;
        }`);
    expect(entryPoints).toContain(`        if (length < 0)
        {
            throw new InvalidOperationException($"{fieldName} has a negative byte length.");
        }
        if (data == null)
        {
            if (length == 0)
            {
                return null;
            }
            throw new InvalidOperationException($"{fieldName} has a byte length but no data.");
        }
        return StrictUtf8.GetString(new ReadOnlySpan<byte>(data, length));`);

    // The directories must exist before module registration observes the context.
    const decodeCall = entryPoints.indexOf('var directories = DecodeAppDirectories(appDirectories);');
    const contextConstruction = entryPoints.indexOf(
      'var context = new DotnetRuntimeContext(runtime, directories);'
    );
    expect(decodeCall).toBeGreaterThan(-1);
    expect(contextConstruction).toBeGreaterThan(decodeCall);
    expect(entryPoints).not.toContain('new DotnetRuntimeContext(runtime);');
  });

  it('compiles and runs the generated host against the app-directories ABI', () => {
    // Resolve symlinks: MSBuild resolves ProjectReference paths against the real
    // csproj directory, and on macOS the temp root reaches it through /var.
    const root = fs.realpathSync(makeTempRoot());
    const outputDir = path.join(root, 'dotnet-host');

    // Generate against the real managed core so the harness exercises the real
    // AppDirectories validation, not a stub.
    generateAggregator({ modules: [] }, { outputDir, adapterPackageRoot: realAdapterPackageRoot });

    const harnessFileName = 'EntryPointsAbiHarness.cs';
    fs.copyFileSync(abiHarnessFixture, path.join(outputDir, harnessFileName));

    const csprojPath = path.join(outputDir, 'ExpoDotnetHost.csproj');
    const csproj = fs
      .readFileSync(csprojPath, 'utf8')
      .replace('<AssemblyName>', '<OutputType>Exe</OutputType>\n    <AssemblyName>')
      .replace(
        '<Compile Include="EntryPoints.g.cs" />',
        `<Compile Include="EntryPoints.g.cs" />\n    <Compile Include="${harnessFileName}" />`
      );
    expect(csproj).toContain('<OutputType>Exe</OutputType>');
    expect(csproj).toContain(`<Compile Include="${harnessFileName}" />`);
    fs.writeFileSync(csprojPath, csproj);

    const run = spawnSync('dotnet', ['run', '--project', csprojPath, '--nologo'], {
      cwd: outputDir,
      encoding: 'utf8',
    });

    const output = `${run.stdout ?? ''}\n${run.stderr ?? ''}`;
    expect(run.error, `failed to launch dotnet: ${run.error?.message}`).toBeUndefined();
    expect(run.status, `dotnet run reported failures:\n${output}`).toBe(0);
    expect(output).toContain('entry-points ABI harness: all checks passed.');
  }, 900_000);

  it('references managed core and module projects with relative forward-slash paths', () => {
    const { adapterPackageRoot, manifest, outputDir } = makeFixture();

    generateAggregator(manifest, { outputDir, adapterPackageRoot });

    const csproj = readGenerated(outputDir, 'ExpoDotnetHost.csproj');
    const includes = [...csproj.matchAll(/<ProjectReference Include="([^"]+)" \/>/g)].map(
      (match) => match[1]
    );
    const expectedIncludes = [
      path.relative(outputDir, path.join(adapterPackageRoot, 'managed/packages/Expo.JSI/Expo.JSI.csproj')),
      path.relative(
        outputDir,
        path.join(adapterPackageRoot, 'managed/packages/Expo.ModulesCore/Expo.ModulesCore.csproj')
      ),
      ...manifest.modules.flatMap((module) =>
        module.projects.map((project) => path.relative(outputDir, project.csprojPath))
      ),
    ].map((relativePath) => relativePath.split(path.sep).join('/'));

    expect(includes).toEqual(expectedIncludes);
    for (const include of includes) {
      expect(include).not.toContain('\\');
      expect(path.isAbsolute(include)).toBe(false);
    }
    expect(csproj).not.toContain(outputDir);
    expect(csproj).not.toContain(adapterPackageRoot);
    for (const module of manifest.modules) {
      expect(csproj).not.toContain(module.packageRoot);
    }
  });

  it('emits a valid empty provider for zero modules', () => {
    const root = makeTempRoot();
    const outputDir = path.join(root, 'generated');
    const adapterPackageRoot = makeAdapterPackageRoot(root);

    generateAggregator({ modules: [] }, { outputDir, adapterPackageRoot });

    expect(listOutputFiles(outputDir)).toEqual(generatedFiles);
    const provider = readGenerated(outputDir, 'LinkedExpoModulesProvider.g.cs');
    expect(provider).toContain('public static void Register(DotnetRuntimeContext context)');
    expect(provider).not.toMatch(/ExpoModulesProvider_.*\.Register\(context\);/);
  });

  it('skips unchanged files on a second run', () => {
    const { adapterPackageRoot, manifest, outputDir } = makeFixture();

    generateAggregator(manifest, { outputDir, adapterPackageRoot });
    const mtimes = new Map(
      generatedFiles.map((fileName) => [
        fileName,
        fs.statSync(path.join(outputDir, fileName)).mtimeMs,
      ])
    );

    const result = generateAggregator(manifest, { outputDir, adapterPackageRoot });

    expect(result.writtenFiles).toEqual([]);
    expect(result.skippedFiles.map((filePath) => path.basename(filePath)).sort()).toEqual(generatedFiles);
    for (const fileName of generatedFiles) {
      expect(fs.statSync(path.join(outputDir, fileName)).mtimeMs).toBe(mtimes.get(fileName));
    }
  });
});
