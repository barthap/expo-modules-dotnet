import * as path from 'path';
import { Command } from 'commander';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import type { DotnetLinkingManifest } from '../types';

const manifest: DotnetLinkingManifest = { modules: [] };
const repoRoot = path.resolve(process.cwd(), '../..');
const appRoot = path.join(repoRoot, 'apps/desktop-app');
const adapterPackageRoot = path.join(repoRoot, 'packages/expo-modules-dotnet');

vi.mock('../discovery', () => ({
  discoverDotnetManifestAsync: vi.fn(async () => manifest),
  resolveAppRoot: vi.fn(() => appRoot),
}));

vi.mock('../codegen/generateAggregator', () => ({
  generateAggregator: vi.fn(() => ({
    writtenFiles: [path.join(path.sep, 'out', 'EntryPoints.g.cs')],
    skippedFiles: [
      path.join(path.sep, 'out', 'ExpoDotnetHost.csproj'),
      path.join(path.sep, 'out', 'LinkedExpoModulesProvider.g.cs'),
    ],
  })),
}));

describe('registerGenerateCommand', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('generates the dotnet host into the default app output directory', async () => {
    const { discoverDotnetManifestAsync, resolveAppRoot } = await import('../discovery');
    const { generateAggregator } = await import('../codegen/generateAggregator');
    const { registerGenerateCommand } = await import('../commands/generateCommand');
    const program = makeProgram();
    const log = vi.spyOn(console, 'log').mockImplementation(() => {});

    await program.parseAsync(['generate', '--project-root', 'apps/desktop-app'], { from: 'user' });

    expect(resolveAppRoot).toHaveBeenCalledWith('apps/desktop-app');
    expect(discoverDotnetManifestAsync).toHaveBeenCalledWith(appRoot);
    expect(generateAggregator).toHaveBeenCalledWith(manifest, {
      outputDir: path.join(appRoot, '.expo', 'dotnet'),
      adapterPackageRoot,
    });
    expect(log).toHaveBeenCalledWith(
      `Generated dotnet host: wrote 1 file, skipped 2 files in ${path.join(
        appRoot,
        '.expo',
        'dotnet'
      )}`
    );

    log.mockRestore();

    function makeProgram(): Command {
      const command = new Command();
      command.exitOverride();
      command.configureOutput({ writeOut: () => {}, writeErr: () => {} });
      registerGenerateCommand(command);
      return command;
    }
  });

  it('resolves an explicit output directory from the current working directory', async () => {
    const { generateAggregator } = await import('../codegen/generateAggregator');
    const { registerGenerateCommand } = await import('../commands/generateCommand');
    const program = new Command();
    const output = path.join('tmp', 'generated-dotnet');
    const log = vi.spyOn(console, 'log').mockImplementation(() => {});
    program.exitOverride();
    program.configureOutput({ writeOut: () => {}, writeErr: () => {} });
    registerGenerateCommand(program);

    await program.parseAsync(['generate', '--output', output], { from: 'user' });

    expect(generateAggregator).toHaveBeenCalledWith(
      manifest,
      expect.objectContaining({ outputDir: path.resolve(output) })
    );

    log.mockRestore();
  });
});
