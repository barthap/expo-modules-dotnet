import * as path from 'path';
import { Command } from 'commander';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const repoRoot = path.resolve(process.cwd(), '../..');
const appRoot = path.join(repoRoot, 'apps/desktop-app');
const outputDir = path.join(appRoot, '.expo', 'dotnet');
const builtOutputDir = path.join(path.sep, 'built', 'dotnet');
const destination = path.join(appRoot, 'macos', 'Managed');

vi.mock('../discovery', () => ({
  resolveAppRoot: vi.fn(() => appRoot),
}));

vi.mock('../paths', () => ({
  defaultGenerateOutputDir: vi.fn((root: string) => path.join(root, '.expo', 'dotnet')),
}));

vi.mock('../build', () => ({
  buildOutputDir: vi.fn(() => builtOutputDir),
  defaultConfiguration: vi.fn((mode: string) => (mode === 'hostfxr' ? 'Debug' : 'Release')),
  defaultLoaderMode: vi.fn((platform: string) =>
    platform === 'macos' || platform === 'windows' ? 'hostfxr' : 'nativeaot'
  ),
  defaultRid: vi.fn((platform: string) => `${platform}-rid`),
}));

vi.mock('../stage', () => ({
  stageArtifactsAsync: vi.fn(async () => ({
    staged: ['ExpoDotnetHost.dll'],
    skipped: ['ExpoDotnetHost.deps.json', 'ExpoDotnetHost.runtimeconfig.json'],
  })),
  stageDestination: vi.fn(() => destination),
}));

describe('registerStageCommand', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('stages artifacts with default paths and platform-derived build options', async () => {
    const { buildOutputDir, defaultConfiguration, defaultLoaderMode, defaultRid } = await import(
      '../build'
    );
    const { resolveAppRoot } = await import('../discovery');
    const { defaultGenerateOutputDir } = await import('../paths');
    const { stageArtifactsAsync, stageDestination } = await import('../stage');
    const { registerStageCommand } = await import('../commands/stageCommand');
    const program = makeProgram(registerStageCommand);
    const log = vi.spyOn(console, 'log').mockImplementation(() => {});

    await program.parseAsync(['stage', '--platform', 'macos'], { from: 'user' });

    expect(resolveAppRoot).toHaveBeenCalledWith(undefined);
    expect(defaultGenerateOutputDir).toHaveBeenCalledWith(appRoot);
    expect(defaultLoaderMode).toHaveBeenCalledWith('macos');
    expect(defaultConfiguration).toHaveBeenCalledWith('hostfxr');
    expect(defaultRid).toHaveBeenCalledWith('macos', process.arch);
    expect(buildOutputDir).toHaveBeenCalledWith({
      csprojPath: path.join(outputDir, 'ExpoDotnetHost.csproj'),
      mode: 'hostfxr',
      configuration: 'Debug',
      platform: 'macos',
      rid: 'macos-rid',
    });
    expect(stageArtifactsAsync).toHaveBeenCalledWith({
      platform: 'macos',
      appRoot,
      mode: 'hostfxr',
      builtOutputDir,
    });
    expect(stageDestination).toHaveBeenCalledWith('macos', appRoot);
    expect(log).toHaveBeenCalledWith(
      `Staged 1 file, skipped 2 files in ${destination}`
    );

    log.mockRestore();
  });

  it('passes explicit CLI options to the staging delegate', async () => {
    const { buildOutputDir, defaultConfiguration, defaultLoaderMode, defaultRid } = await import(
      '../build'
    );
    const { resolveAppRoot } = await import('../discovery');
    const { defaultGenerateOutputDir } = await import('../paths');
    const { stageArtifactsAsync, stageDestination } = await import('../stage');
    const { registerStageCommand } = await import('../commands/stageCommand');
    const program = makeProgram(registerStageCommand);
    const log = vi.spyOn(console, 'log').mockImplementation(() => {});
    const explicitAppRoot = path.resolve('tmp/staged-app');
    const explicitOutputDir = path.resolve('tmp/generated-dotnet');

    await program.parseAsync(
      [
        'stage',
        '--platform',
        'ios',
        '--project-root',
        'apps/mobile-app',
        '--app-root',
        'tmp/staged-app',
        '--output',
        'tmp/generated-dotnet',
        '--mode',
        'nativeaot',
        '--configuration',
        'RelWithDebInfo',
        '--rid',
        'ios-arm64',
      ],
      { from: 'user' }
    );

    expect(resolveAppRoot).toHaveBeenCalledWith('apps/mobile-app');
    expect(defaultGenerateOutputDir).not.toHaveBeenCalled();
    expect(defaultLoaderMode).not.toHaveBeenCalled();
    expect(defaultConfiguration).not.toHaveBeenCalled();
    expect(defaultRid).not.toHaveBeenCalled();
    expect(buildOutputDir).toHaveBeenCalledWith({
      csprojPath: path.join(explicitOutputDir, 'ExpoDotnetHost.csproj'),
      mode: 'nativeaot',
      configuration: 'RelWithDebInfo',
      platform: 'ios',
      rid: 'ios-arm64',
    });
    expect(stageArtifactsAsync).toHaveBeenCalledWith({
      platform: 'ios',
      appRoot: explicitAppRoot,
      mode: 'nativeaot',
      builtOutputDir,
    });
    expect(stageDestination).toHaveBeenCalledWith('ios', explicitAppRoot);

    log.mockRestore();
  });

  it('surfaces unsupported platforms before staging artifacts', async () => {
    const { stageArtifactsAsync } = await import('../stage');
    const { registerStageCommand } = await import('../commands/stageCommand');
    const program = makeProgram(registerStageCommand);

    await expect(
      program.parseAsync(['stage', '--platform', 'linux'], { from: 'user' })
    ).rejects.toThrow('Unsupported stage platform linux');
    expect(stageArtifactsAsync).not.toHaveBeenCalled();
  });

  it('surfaces unsupported modes before staging artifacts', async () => {
    const { stageArtifactsAsync } = await import('../stage');
    const { registerStageCommand } = await import('../commands/stageCommand');
    const program = makeProgram(registerStageCommand);

    await expect(
      program.parseAsync(['stage', '--platform', 'macos', '--mode', 'debug'], { from: 'user' })
    ).rejects.toThrow('Unsupported stage mode debug');
    expect(stageArtifactsAsync).not.toHaveBeenCalled();
  });

  it('surfaces staging delegate failures without logging success', async () => {
    const { stageArtifactsAsync } = await import('../stage');
    const { registerStageCommand } = await import('../commands/stageCommand');
    const program = makeProgram(registerStageCommand);
    const error = new Error('stage failed');
    vi.mocked(stageArtifactsAsync).mockRejectedValueOnce(error);
    const log = vi.spyOn(console, 'log').mockImplementation(() => {});

    await expect(
      program.parseAsync(['stage', '--platform', 'macos'], { from: 'user' })
    ).rejects.toThrow(error);
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
