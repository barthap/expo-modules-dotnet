import * as fs from 'fs';
import * as path from 'path';
import { Command } from 'commander';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const repoRoot = path.resolve(process.cwd(), '../..');
const appRoot = path.join(repoRoot, 'apps/desktop-app');
const outputDir = path.join(appRoot, '.expo', 'dotnet');
const adapterPackageRoot = path.join(repoRoot, 'packages/expo-modules-dotnet');

vi.mock('fs', () => ({
  existsSync: vi.fn(() => true),
}));

vi.mock('../discovery', () => ({
  resolveAppRoot: vi.fn(() => appRoot),
}));

vi.mock('../paths', () => ({
  defaultGenerateOutputDir: vi.fn((root: string) => path.join(root, '.expo', 'dotnet')),
  findAdapterPackageRoot: vi.fn(() => adapterPackageRoot),
}));

vi.mock('../build', () => ({
  defaultConfiguration: vi.fn((mode: string) => (mode === 'hostfxr' ? 'Debug' : 'Release')),
  defaultLoaderMode: vi.fn((platform: string) =>
    platform === 'macos' || platform === 'windows' ? 'hostfxr' : 'nativeaot'
  ),
  defaultRid: vi.fn((platform: string) => `${platform}-rid`),
  runDotnetBuildAsync: vi.fn(async () => {}),
}));

describe('registerBuildCommand', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fs.existsSync).mockReturnValue(true);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('builds the generated host project with OS-derived defaults', async () => {
    vi.spyOn(process, 'platform', 'get').mockReturnValue('darwin');
    const { defaultConfiguration, defaultLoaderMode, defaultRid, runDotnetBuildAsync } =
      await import('../build');
    const { resolveAppRoot } = await import('../discovery');
    const { defaultGenerateOutputDir, findAdapterPackageRoot } = await import('../paths');
    const { registerBuildCommand } = await import('../commands/buildCommand');
    const program = makeProgram(registerBuildCommand);

    await program.parseAsync(['build'], { from: 'user' });

    expect(resolveAppRoot).toHaveBeenCalledWith(undefined);
    expect(defaultGenerateOutputDir).toHaveBeenCalledWith(appRoot);
    expect(fs.existsSync).toHaveBeenCalledWith(path.join(outputDir, 'ExpoDotnetHost.csproj'));
    expect(defaultLoaderMode).toHaveBeenCalledWith('macos');
    expect(defaultConfiguration).toHaveBeenCalledWith('hostfxr');
    expect(defaultRid).not.toHaveBeenCalled();
    expect(findAdapterPackageRoot).not.toHaveBeenCalled();
    expect(runDotnetBuildAsync).toHaveBeenCalledWith({
      csprojPath: path.join(outputDir, 'ExpoDotnetHost.csproj'),
      mode: 'hostfxr',
      configuration: 'Debug',
      rid: undefined,
      adapterPackageRoot: undefined,
    });
  });

  it('uses platform defaults for NativeAOT builds', async () => {
    const { defaultConfiguration, defaultLoaderMode, defaultRid, runDotnetBuildAsync } =
      await import('../build');
    const { findAdapterPackageRoot } = await import('../paths');
    const { registerBuildCommand } = await import('../commands/buildCommand');
    const program = makeProgram(registerBuildCommand);

    await program.parseAsync(['build', '--platform', 'ios'], { from: 'user' });

    expect(defaultLoaderMode).toHaveBeenCalledWith('ios');
    expect(defaultConfiguration).toHaveBeenCalledWith('nativeaot');
    expect(defaultRid).toHaveBeenCalledWith('ios', process.arch);
    expect(findAdapterPackageRoot).toHaveBeenCalledWith(appRoot);
    expect(runDotnetBuildAsync).toHaveBeenCalledWith({
      csprojPath: path.join(outputDir, 'ExpoDotnetHost.csproj'),
      mode: 'nativeaot',
      configuration: 'Release',
      rid: 'ios-rid',
      adapterPackageRoot,
    });
  });

  it('passes explicit output, mode, rid, configuration, and project root options', async () => {
    const { defaultConfiguration, defaultLoaderMode, defaultRid, runDotnetBuildAsync } =
      await import('../build');
    const { resolveAppRoot } = await import('../discovery');
    const { defaultGenerateOutputDir, findAdapterPackageRoot } = await import('../paths');
    const { registerBuildCommand } = await import('../commands/buildCommand');
    const program = makeProgram(registerBuildCommand);
    const explicitOutputDir = path.resolve('tmp/generated-dotnet');

    await program.parseAsync(
      [
        'build',
        '--project-root',
        'apps/mobile-app',
        '--output',
        'tmp/generated-dotnet',
        '--mode',
        'nativeaot',
        '--rid',
        'android-arm64',
        '--configuration',
        'RelWithDebInfo',
      ],
      { from: 'user' }
    );

    expect(resolveAppRoot).toHaveBeenCalledWith('apps/mobile-app');
    expect(defaultGenerateOutputDir).not.toHaveBeenCalled();
    expect(defaultLoaderMode).not.toHaveBeenCalled();
    expect(defaultConfiguration).not.toHaveBeenCalled();
    expect(defaultRid).not.toHaveBeenCalled();
    expect(findAdapterPackageRoot).toHaveBeenCalledWith(appRoot);
    expect(runDotnetBuildAsync).toHaveBeenCalledWith({
      csprojPath: path.join(explicitOutputDir, 'ExpoDotnetHost.csproj'),
      mode: 'nativeaot',
      configuration: 'RelWithDebInfo',
      rid: 'android-arm64',
      adapterPackageRoot,
    });
  });

  it('does not pass adapter package root for explicit hostfxr builds', async () => {
    const { runDotnetBuildAsync } = await import('../build');
    const { findAdapterPackageRoot } = await import('../paths');
    const { registerBuildCommand } = await import('../commands/buildCommand');
    const program = makeProgram(registerBuildCommand);

    await program.parseAsync(
      ['build', '--mode', 'hostfxr', '--configuration', 'Release'],
      { from: 'user' }
    );

    expect(findAdapterPackageRoot).not.toHaveBeenCalled();
    expect(runDotnetBuildAsync).toHaveBeenCalledWith({
      csprojPath: path.join(outputDir, 'ExpoDotnetHost.csproj'),
      mode: 'hostfxr',
      configuration: 'Release',
      rid: undefined,
      adapterPackageRoot: undefined,
    });
  });

  it('throws when the generated host project is absent', async () => {
    const { runDotnetBuildAsync } = await import('../build');
    const { registerBuildCommand } = await import('../commands/buildCommand');
    const program = makeProgram(registerBuildCommand);
    vi.mocked(fs.existsSync).mockReturnValueOnce(false);

    await expect(
      program.parseAsync(['build', '--platform', 'macos'], { from: 'user' })
    ).rejects.toThrow('Generated host project not found');
    expect(runDotnetBuildAsync).not.toHaveBeenCalled();
  });

  it('surfaces unsupported platforms before building', async () => {
    const { runDotnetBuildAsync } = await import('../build');
    const { registerBuildCommand } = await import('../commands/buildCommand');
    const program = makeProgram(registerBuildCommand);

    await expect(
      program.parseAsync(['build', '--platform', 'linux'], { from: 'user' })
    ).rejects.toThrow('Unsupported platform linux');
    expect(runDotnetBuildAsync).not.toHaveBeenCalled();
  });

  it('surfaces unsupported modes before building', async () => {
    vi.spyOn(process, 'platform', 'get').mockReturnValue('darwin');
    const { runDotnetBuildAsync } = await import('../build');
    const { registerBuildCommand } = await import('../commands/buildCommand');
    const program = makeProgram(registerBuildCommand);

    await expect(
      program.parseAsync(['build', '--mode', 'debug'], { from: 'user' })
    ).rejects.toThrow('Unsupported build mode debug');
    expect(runDotnetBuildAsync).not.toHaveBeenCalled();
  });

  it('throws when build mode cannot be inferred', async () => {
    vi.spyOn(process, 'platform', 'get').mockReturnValue('linux');
    const { runDotnetBuildAsync } = await import('../build');
    const { registerBuildCommand } = await import('../commands/buildCommand');
    const program = makeProgram(registerBuildCommand);

    await expect(program.parseAsync(['build'], { from: 'user' })).rejects.toThrow(
      'Could not infer build mode for this OS'
    );
    expect(runDotnetBuildAsync).not.toHaveBeenCalled();
  });

  it('surfaces build delegate failures', async () => {
    const { runDotnetBuildAsync } = await import('../build');
    const { registerBuildCommand } = await import('../commands/buildCommand');
    const program = makeProgram(registerBuildCommand);
    const error = new Error('build failed');
    vi.mocked(runDotnetBuildAsync).mockRejectedValueOnce(error);

    await expect(
      program.parseAsync(['build', '--platform', 'macos'], { from: 'user' })
    ).rejects.toThrow(error);
  });
});

function makeProgram(registerCommand: (command: Command) => void): Command {
  const command = new Command();
  command.exitOverride();
  command.configureOutput({ writeOut: () => {}, writeErr: () => {} });
  registerCommand(command);
  return command;
}
