import * as fs from 'fs';
import * as path from 'path';

import { Command } from 'commander';
import { describe, expect, it, vi } from 'vitest';

import type { DotnetLinkingManifest } from '../types';

const appRoot = path.resolve('fixtures', 'app');
const solutionPath = path.join(appRoot, 'windows', 'App.sln');
const projectPath = path.join(appRoot, 'windows', 'App', 'App.vcxproj');
const packagePath = path.join(appRoot, 'windows', 'App.Package', 'App.Package.wapproj');
const manifest: DotnetLinkingManifest = {
  modules: [{ packageName: 'example-module', packageRoot: 'module', projects: [{ csprojPath: 'module/ExampleModule.csproj', assemblyName: 'ExampleModule' }] }],
};

vi.mock('fs');
vi.mock('../discovery', () => ({ resolveAppRoot: vi.fn(() => appRoot), discoverDotnetManifestAsync: vi.fn(async () => manifest) }));
vi.mock('../paths', () => ({ defaultGenerateOutputDir: vi.fn(() => path.join(appRoot, '.expo', 'dotnet')), findAdapterPackageRoot: vi.fn(() => 'adapter') }));
vi.mock('../codegen/generateAggregator', () => ({ generateAggregator: vi.fn(() => ({ writtenFiles: [], skippedFiles: [] })) }));
vi.mock('../windows/reactNativeCli', () => ({ runReactNativeWindowsAutolink: vi.fn(async () => {}) }));
vi.mock('../windows/packageDebugger', () => ({ findPackageProjectPath: vi.fn(() => packagePath), configureMixedModeDebugger: vi.fn((content) => ({ changed: true, content })) }));
vi.mock('../windows/solution', () => ({ synchronizeManagedSolution: vi.fn((content) => ({ changed: true, content })) }));

describe('registerSyncWindowsCommand', () => {
  it('runs RNW autolinking before generating the managed solution projection', async () => {
    vi.mocked(fs.existsSync).mockReturnValue(true);
    vi.mocked(fs.readFileSync).mockReturnValue('solution');
    const { registerSyncWindowsCommand } = await import('../commands/syncWindowsCommand');
    const { runReactNativeWindowsAutolink } = await import('../windows/reactNativeCli');
    const { generateAggregator } = await import('../codegen/generateAggregator');
    const program = new Command();
    registerSyncWindowsCommand(program);

    await program.parseAsync(['sync-windows', '--sln', 'windows/App.sln', '--proj', 'windows/App/App.vcxproj'], { from: 'user' });

    expect(runReactNativeWindowsAutolink).toHaveBeenCalledWith({ appRoot, solutionPath, projectPath, check: false });
    expect(generateAggregator).toHaveBeenCalledWith(manifest, { outputDir: path.join(appRoot, '.expo', 'dotnet'), adapterPackageRoot: 'adapter' });
  });

  it('checks only the managed projection because RNW --check can report false drift', async () => {
    vi.clearAllMocks();
    vi.mocked(fs.existsSync).mockReturnValue(true);
    vi.mocked(fs.readFileSync).mockReturnValue('solution');
    vi.mocked(await import('../windows/solution').then((module) => module.synchronizeManagedSolution)).mockReturnValue({ changed: false, content: 'solution' });
    const { registerSyncWindowsCommand } = await import('../commands/syncWindowsCommand');
    const { runReactNativeWindowsAutolink } = await import('../windows/reactNativeCli');
    const program = new Command();
    registerSyncWindowsCommand(program);

    await program.parseAsync(['sync-windows', '--sln', 'windows/App.sln', '--proj', 'windows/App/App.vcxproj', '--check'], { from: 'user' });

    expect(runReactNativeWindowsAutolink).not.toHaveBeenCalled();
  });
});
