import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { describe, expect, it } from 'vitest';

import { generateAggregator } from '../codegen/generateAggregator';
import type { DotnetLinkingManifest } from '../types';

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
  writeCsproj(
    path.join(root, 'adapter', 'expo-modules-dotnet-windows'),
    'managed/Expo.ModulesCore.Windows/Expo.ModulesCore.Windows.csproj'
  );
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

  it('generates Windows view entry points and sidecar reference for Windows aggregation', () => {
    const { adapterPackageRoot, manifest, outputDir } = makeFixture();

    generateAggregator(manifest, { outputDir, adapterPackageRoot, platform: 'windows' });

    const csproj = readGenerated(outputDir, 'ExpoDotnetHost.csproj');
    const entryPoints = readGenerated(outputDir, 'EntryPoints.g.cs');
    const provider = readGenerated(outputDir, 'LinkedExpoModulesProvider.g.cs');

    expect(csproj).toContain('<TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>');
    expect(csproj).toContain('<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>');
    expect(csproj).toContain(
      'expo-modules-dotnet-windows/managed/Expo.ModulesCore.Windows/Expo.ModulesCore.Windows.csproj'
    );
    expect(entryPoints).toContain('expo_dotnet_windows_get_view_last_error');
    expect(entryPoints).toContain('expo_dotnet_windows_get_view_count');
    expect(entryPoints).toContain('expo_dotnet_windows_get_view_module_name');
    expect(entryPoints).toContain('expo_dotnet_windows_get_view_component_name');
    expect(entryPoints).toContain('expo_dotnet_windows_get_view_prop_count');
    expect(entryPoints).toContain('expo_dotnet_windows_get_view_prop_name');
    expect(entryPoints).toContain('expo_dotnet_windows_get_view_prop_kind');
    expect(entryPoints).toContain('expo_dotnet_windows_initialize_composition');
    expect(entryPoints).toContain('expo_dotnet_windows_update_layout');
    expect(entryPoints).toContain('expo_dotnet_windows_update_string_prop');
    expect(entryPoints).toContain('expo_dotnet_windows_destroy_view');
    expect(entryPoints).toContain('LinkedExpoModulesProvider.GetViewDefinitions()');
    expect(entryPoints).toContain('RecordWindowsViewException');
    expect(entryPoints).not.toContain('JsonSerializer');
    expect(entryPoints).not.toContain('GetWindowsViewMetadata');
    expect(entryPoints).not.toContain('FreeWindowsBuffer');
    expect(provider).toContain('public static IReadOnlyList<GeneratedViewDefinition> GetViewDefinitions()');
    expect(provider).toContain('ExpoModulesProvider_A.GetViewDefinitions()');
    expect(provider).toContain('ExpoModulesProvider_B.GetViewDefinitions()');
  });

  it('validates duplicate view component names across linked assemblies', () => {
    const { adapterPackageRoot, manifest, outputDir } = makeFixture();

    generateAggregator(manifest, { outputDir, adapterPackageRoot, platform: 'windows' });

    const provider = readGenerated(outputDir, 'LinkedExpoModulesProvider.g.cs');
    const entryPoints = readGenerated(outputDir, 'EntryPoints.g.cs');
    expect(provider).toContain('ValidateUniqueViewComponentNames');
    expect(provider).toContain('Duplicate Expo view component name');
    expect(entryPoints).toContain('GetWindowsViewLastError');
    expect(entryPoints).toContain('WindowsViewLastError');
    expect(provider).not.toContain(
      'if (Expo.ModulesCore.Generated.ExpoModulesProvider_A.GetViewDefinitions().Any(view => view.ComponentName == componentName))'
    );
  });

  it('keeps non-Windows aggregation universal', () => {
    const { adapterPackageRoot, manifest, outputDir } = makeFixture();

    generateAggregator(manifest, { outputDir, adapterPackageRoot, platform: 'macos' });

    const csproj = readGenerated(outputDir, 'ExpoDotnetHost.csproj');
    const entryPoints = readGenerated(outputDir, 'EntryPoints.g.cs');

    expect(csproj).toContain('<TargetFramework>net10.0</TargetFramework>');
    expect(csproj).not.toContain('CopyLocalLockFileAssemblies');
    expect(csproj).not.toContain('Expo.ModulesCore.Windows.csproj');
    expect(entryPoints).not.toContain('expo_dotnet_windows_create_view');
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
