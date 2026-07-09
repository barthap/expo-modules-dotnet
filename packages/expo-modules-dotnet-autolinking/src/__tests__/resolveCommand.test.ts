import * as path from 'path';
import { Command } from 'commander';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import type { DotnetLinkingManifest } from '../types';

const manifest: DotnetLinkingManifest = { modules: [] };
const repoRoot = path.resolve(process.cwd(), '../..');
const appRoot = path.join(repoRoot, 'apps/desktop-app');

vi.mock('../discovery', () => ({
  discoverDotnetManifestAsync: vi.fn(async () => manifest),
  resolveAppRoot: vi.fn(() => appRoot),
}));

describe('registerResolveCommand', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('prints the resolved manifest for the default app root', async () => {
    const { discoverDotnetManifestAsync, resolveAppRoot } = await import('../discovery');
    const { registerResolveCommand } = await import('../commands/resolveCommand');
    const program = makeProgram(registerResolveCommand);
    const log = vi.spyOn(console, 'log').mockImplementation(() => {});

    await program.parseAsync(['resolve'], { from: 'user' });

    expect(resolveAppRoot).toHaveBeenCalledWith(undefined);
    expect(discoverDotnetManifestAsync).toHaveBeenCalledWith(appRoot);
    expect(log).toHaveBeenCalledWith(JSON.stringify(manifest, null, 2));

    log.mockRestore();
  });

  it('resolves an explicit project root before discovery', async () => {
    const { discoverDotnetManifestAsync, resolveAppRoot } = await import('../discovery');
    const { registerResolveCommand } = await import('../commands/resolveCommand');
    const program = makeProgram(registerResolveCommand);
    const log = vi.spyOn(console, 'log').mockImplementation(() => {});

    await program.parseAsync(['resolve', '--project-root', 'apps/desktop-app'], { from: 'user' });

    expect(resolveAppRoot).toHaveBeenCalledWith('apps/desktop-app');
    expect(discoverDotnetManifestAsync).toHaveBeenCalledWith(appRoot);

    log.mockRestore();
  });

  it('accepts the json option without changing the printed manifest format', async () => {
    const { registerResolveCommand } = await import('../commands/resolveCommand');
    const program = makeProgram(registerResolveCommand);
    const log = vi.spyOn(console, 'log').mockImplementation(() => {});

    await program.parseAsync(['resolve', '--json'], { from: 'user' });

    expect(log).toHaveBeenCalledWith(JSON.stringify(manifest, null, 2));

    log.mockRestore();
  });

  it('surfaces discovery failures', async () => {
    const { discoverDotnetManifestAsync } = await import('../discovery');
    const { registerResolveCommand } = await import('../commands/resolveCommand');
    const program = makeProgram(registerResolveCommand);
    const error = new Error('discovery failed');
    vi.mocked(discoverDotnetManifestAsync).mockRejectedValueOnce(error);
    const log = vi.spyOn(console, 'log').mockImplementation(() => {});

    await expect(program.parseAsync(['resolve'], { from: 'user' })).rejects.toThrow(error);
    expect(log).not.toHaveBeenCalled();

    log.mockRestore();
  });
});

function makeProgram(registerCommand: (command: Command) => void): Command {
  const command = new Command();
  command.exitOverride();
  command.configureOutput({ writeOut: () => {}, writeErr: () => {} });
  registerCommand(command);
  return command;
}
