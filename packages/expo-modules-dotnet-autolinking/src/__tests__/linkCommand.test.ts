import * as path from 'path';
import { Command } from 'commander';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import type { DotnetLinkingManifest } from '../types';

const manifest: DotnetLinkingManifest = { modules: [] };
const repoRoot = path.resolve(process.cwd(), '../..');
const appRoot = path.join(repoRoot, 'apps/desktop-app');
const adapterPackageRoot = path.join(repoRoot, 'packages/expo-modules-dotnet');
const outputDir = path.join(appRoot, '.expo', 'dotnet');
const csprojPath = path.join(outputDir, 'ExpoDotnetHost.csproj');
const builtOutputDir = path.join(outputDir, 'bin', 'host');
const destination = path.join(appRoot, 'macos', 'managed');

const generateResult = {
  writtenFiles: [path.join(outputDir, 'EntryPoints.g.cs')],
  skippedFiles: [
    path.join(outputDir, 'ExpoDotnetHost.csproj'),
    path.join(outputDir, 'LinkedExpoModulesProvider.g.cs'),
  ],
};
const stageResult = {
  staged: ['a.dll', 'b.dll', 'c.dll'],
  skipped: ['d.dll'],
};

vi.mock('../discovery', () => ({
  discoverDotnetManifestAsync: vi.fn(async () => manifest),
  resolveAppRoot: vi.fn(() => appRoot),
}));

vi.mock('../codegen/generateAggregator', () => ({
  generateAggregator: vi.fn(() => generateResult),
}));

vi.mock('../build', () => ({
  buildOutputDir: vi.fn(() => builtOutputDir),
  defaultConfiguration: vi.fn(() => 'Debug'),
  defaultLoaderMode: vi.fn(() => 'hostfxr'),
  defaultRid: vi.fn(() => 'osx-arm64'),
  runDotnetBuildAsync: vi.fn(async () => {}),
}));

vi.mock('../paths', () => ({
  defaultGenerateOutputDir: vi.fn(() => outputDir),
  findAdapterPackageRoot: vi.fn(() => adapterPackageRoot),
}));

vi.mock('../stage', () => ({
  stageArtifactsAsync: vi.fn(async () => stageResult),
  stageDestination: vi.fn(() => destination),
}));

describe('registerLinkCommand', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('resolves, generates, builds, and stages with defaults for the target platform', async () => {
    const { discoverDotnetManifestAsync, resolveAppRoot } = await import('../discovery');
    const { generateAggregator } = await import('../codegen/generateAggregator');
    const { buildOutputDir, runDotnetBuildAsync } = await import('../build');
    const { stageArtifactsAsync, stageDestination } = await import('../stage');
    const { registerLinkCommand } = await import('../commands/linkCommand');
    const program = makeProgram(registerLinkCommand);
    const log = vi.spyOn(console, 'log').mockImplementation(() => {});

    await program.parseAsync(['link', '--platform', 'macos'], { from: 'user' });

    expect(resolveAppRoot).toHaveBeenCalledWith(undefined);
    expect(discoverDotnetManifestAsync).toHaveBeenCalledWith(appRoot);
    expect(generateAggregator).toHaveBeenCalledWith(manifest, {
      outputDir,
      adapterPackageRoot,
      platform: 'macos',
    });
    expect(runDotnetBuildAsync).toHaveBeenCalledWith({
      csprojPath,
      mode: 'hostfxr',
      configuration: 'Debug',
      rid: 'osx-arm64',
      adapterPackageRoot: undefined,
    });
    expect(buildOutputDir).toHaveBeenCalledWith({
      csprojPath,
      mode: 'hostfxr',
      configuration: 'Debug',
      rid: 'osx-arm64',
    });
    expect(stageArtifactsAsync).toHaveBeenCalledWith({
      platform: 'macos',
      appRoot,
      mode: 'hostfxr',
      builtOutputDir,
    });
    expect(stageDestination).toHaveBeenCalledWith('macos', appRoot);

    expect(log.mock.calls.map((call) => call[0])).toEqual([
      'Resolved dotnet modules: 0 modules',
      `Generated dotnet host: wrote 1 file, skipped 2 files in ${outputDir}`,
      'Built dotnet host: mode hostfxr, configuration Debug',
      `Staged 3 files, skipped 1 file in ${destination}`,
    ]);

    log.mockRestore();
  });

  it('resolves an explicit project root before discovery', async () => {
    const { resolveAppRoot } = await import('../discovery');
    const { registerLinkCommand } = await import('../commands/linkCommand');
    const program = makeProgram(registerLinkCommand);
    const log = vi.spyOn(console, 'log').mockImplementation(() => {});

    await program.parseAsync(['link', '--platform', 'macos', '--project-root', 'apps/desktop-app'], {
      from: 'user',
    });

    expect(resolveAppRoot).toHaveBeenCalledWith('apps/desktop-app');

    log.mockRestore();
  });

  it('stages into an explicit destination app root', async () => {
    const { stageArtifactsAsync, stageDestination } = await import('../stage');
    const { registerLinkCommand } = await import('../commands/linkCommand');
    const program = makeProgram(registerLinkCommand);
    const destinationAppRoot = path.resolve('tmp', 'dest-app');
    const log = vi.spyOn(console, 'log').mockImplementation(() => {});

    await program.parseAsync(['link', '--platform', 'macos', '--app-root', path.join('tmp', 'dest-app')], {
      from: 'user',
    });

    expect(stageArtifactsAsync).toHaveBeenCalledWith(
      expect.objectContaining({ appRoot: destinationAppRoot })
    );
    expect(stageDestination).toHaveBeenCalledWith('macos', destinationAppRoot);

    log.mockRestore();
  });

  it('passes explicit configuration and rid through to the build', async () => {
    const { buildOutputDir, runDotnetBuildAsync } = await import('../build');
    const { registerLinkCommand } = await import('../commands/linkCommand');
    const program = makeProgram(registerLinkCommand);
    const log = vi.spyOn(console, 'log').mockImplementation(() => {});

    await program.parseAsync(
      ['link', '--platform', 'macos', '--configuration', 'Release', '--rid', 'osx-x64'],
      { from: 'user' }
    );

    expect(runDotnetBuildAsync).toHaveBeenCalledWith(
      expect.objectContaining({ configuration: 'Release', rid: 'osx-x64' })
    );
    expect(buildOutputDir).toHaveBeenCalledWith(
      expect.objectContaining({ configuration: 'Release', rid: 'osx-x64' })
    );
    expect(log).toHaveBeenCalledWith('Built dotnet host: mode hostfxr, configuration Release');

    log.mockRestore();
  });

  it('passes the adapter package root to the build only in nativeaot mode', async () => {
    const { runDotnetBuildAsync } = await import('../build');
    const { registerLinkCommand } = await import('../commands/linkCommand');
    const program = makeProgram(registerLinkCommand);
    const log = vi.spyOn(console, 'log').mockImplementation(() => {});

    await program.parseAsync(['link', '--platform', 'macos', '--mode', 'nativeaot'], { from: 'user' });

    expect(runDotnetBuildAsync).toHaveBeenCalledWith(
      expect.objectContaining({ mode: 'nativeaot', adapterPackageRoot })
    );

    log.mockRestore();
  });

  it('surfaces build delegate failures and stops before staging', async () => {
    const { runDotnetBuildAsync } = await import('../build');
    const { stageArtifactsAsync } = await import('../stage');
    const { registerLinkCommand } = await import('../commands/linkCommand');
    const program = makeProgram(registerLinkCommand);
    const error = new Error('build failed');
    vi.mocked(runDotnetBuildAsync).mockRejectedValueOnce(error);
    const log = vi.spyOn(console, 'log').mockImplementation(() => {});

    await expect(
      program.parseAsync(['link', '--platform', 'macos'], { from: 'user' })
    ).rejects.toThrow(error);
    expect(stageArtifactsAsync).not.toHaveBeenCalled();
    expect(log).toHaveBeenCalledWith('Resolved dotnet modules: 0 modules');
    expect(log).not.toHaveBeenCalledWith('Built dotnet host: mode hostfxr, configuration Debug');

    log.mockRestore();
  });

  it('surfaces unsupported modes before doing any work', async () => {
    const { discoverDotnetManifestAsync } = await import('../discovery');
    const { registerLinkCommand } = await import('../commands/linkCommand');
    const program = makeProgram(registerLinkCommand);

    await expect(
      program.parseAsync(['link', '--platform', 'macos', '--mode', 'debug'], { from: 'user' })
    ).rejects.toThrow('Unsupported link mode debug');
    expect(discoverDotnetManifestAsync).not.toHaveBeenCalled();
  });

  it('surfaces unsupported platforms before doing any work', async () => {
    const { discoverDotnetManifestAsync } = await import('../discovery');
    const { registerLinkCommand } = await import('../commands/linkCommand');
    const program = makeProgram(registerLinkCommand);

    await expect(
      program.parseAsync(['link', '--platform', 'linux'], { from: 'user' })
    ).rejects.toThrow('Unsupported link platform linux');
    expect(discoverDotnetManifestAsync).not.toHaveBeenCalled();
  });
});

function makeProgram(registerCommand: (command: Command) => void): Command {
  const command = new Command();
  command.exitOverride();
  command.configureOutput({ writeOut: () => {}, writeErr: () => {} });
  registerCommand(command);
  return command;
}
