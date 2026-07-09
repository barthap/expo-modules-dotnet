import * as fs from 'fs/promises';
import * as os from 'os';
import * as path from 'path';
import { execFileSync } from 'child_process';

import { afterEach, describe, expect, it } from 'vitest';

import { defaultRid } from '../build';
import {
  locateNethostLibraryAsync,
  stageArtifactsAsync,
  stageDestination,
} from '../stage';

const tempRoots: string[] = [];

async function makeTempRootAsync(): Promise<string> {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'expo-dotnet-stage-'));
  tempRoots.push(root);
  return root;
}

async function writeFileAsync(filePath: string, content: string): Promise<void> {
  await fs.mkdir(path.dirname(filePath), { recursive: true });
  await fs.writeFile(filePath, content);
}

async function pathExistsAsync(filePath: string): Promise<boolean> {
  try {
    await fs.access(filePath);
    return true;
  } catch {
    return false;
  }
}

async function sortedDirectoryEntriesAsync(directory: string): Promise<string[]> {
  return (await fs.readdir(directory)).sort();
}

afterEach(async () => {
  await Promise.all(tempRoots.splice(0).map((root) => fs.rm(root, { recursive: true, force: true })));
});

describe('stageDestination', () => {
  it('returns the staged artifact directory for every supported platform', () => {
    const appRoot = path.join('apps', 'desktop-app');

    expect(stageDestination('macos', appRoot)).toBe(path.join(appRoot, 'macos', 'Managed'));
    expect(stageDestination('windows', appRoot)).toBe(path.join(appRoot, 'windows', 'Managed'));
    expect(stageDestination('ios', appRoot)).toBe(path.join(appRoot, 'ios', 'Managed'));
    expect(stageDestination('android', appRoot)).toBe(
      path.join(appRoot, 'android', 'app', 'src', 'main', 'jniLibs', 'arm64-v8a')
    );
  });
});

describe('stageArtifactsAsync', () => {
  it('stages only hostfxr runtime artifacts from the build output directory', async () => {
    const root = await makeTempRootAsync();
    const appRoot = path.join(root, 'app');
    const builtOutputDir = path.join(root, 'bin');
    const nethostLibraryPath = path.join(root, 'host-pack', 'libnethost.dylib');

    await writeFileAsync(path.join(builtOutputDir, 'ExpoDotnetHost.dll'), 'dll');
    await writeFileAsync(path.join(builtOutputDir, 'ExpoDotnetHost.deps.json'), '{}');
    await writeFileAsync(path.join(builtOutputDir, 'ExpoDotnetHost.runtimeconfig.json'), '{}');
    await writeFileAsync(path.join(builtOutputDir, 'Foo.pdb'), 'symbols');
    await writeFileAsync(nethostLibraryPath, 'nethost');

    const result = await stageArtifactsAsync({
      platform: 'macos',
      appRoot,
      mode: 'hostfxr',
      builtOutputDir,
      nethostLibraryPath,
    });

    const destination = stageDestination('macos', appRoot);
    expect(result.staged.sort()).toEqual([
      'ExpoDotnetHost.deps.json',
      'ExpoDotnetHost.dll',
      'ExpoDotnetHost.loader',
      'ExpoDotnetHost.runtimeconfig.json',
      'libnethost.dylib',
    ]);
    expect(result.skipped).toEqual([]);
    await expect(sortedDirectoryEntriesAsync(destination)).resolves.toEqual([
      'ExpoDotnetHost.deps.json',
      'ExpoDotnetHost.dll',
      'ExpoDotnetHost.loader',
      'ExpoDotnetHost.runtimeconfig.json',
      'libnethost.dylib',
    ]);
    await expect(fs.readFile(path.join(destination, 'ExpoDotnetHost.loader'), 'utf8')).resolves.toBe(
      'hostfxr\n'
    );
    await expect(pathExistsAsync(path.join(destination, 'Foo.pdb'))).resolves.toBe(false);
  });

  it('skips byte-identical artifacts on a second run', async () => {
    const root = await makeTempRootAsync();
    const appRoot = path.join(root, 'app');
    const builtOutputDir = path.join(root, 'bin');
    const nethostLibraryPath = path.join(root, 'host-pack', 'libnethost.dylib');

    await writeFileAsync(path.join(builtOutputDir, 'ExpoDotnetHost.dll'), 'dll');
    await writeFileAsync(path.join(builtOutputDir, 'ExpoDotnetHost.deps.json'), '{}');
    await writeFileAsync(path.join(builtOutputDir, 'ExpoDotnetHost.runtimeconfig.json'), '{}');
    await writeFileAsync(nethostLibraryPath, 'nethost');

    await stageArtifactsAsync({
      platform: 'macos',
      appRoot,
      mode: 'hostfxr',
      builtOutputDir,
      nethostLibraryPath,
    });
    const result = await stageArtifactsAsync({
      platform: 'macos',
      appRoot,
      mode: 'hostfxr',
      builtOutputDir,
      nethostLibraryPath,
    });

    expect(result.staged).toEqual([]);
    expect(result.skipped.sort()).toEqual([
      'ExpoDotnetHost.deps.json',
      'ExpoDotnetHost.dll',
      'ExpoDotnetHost.loader',
      'ExpoDotnetHost.runtimeconfig.json',
      'libnethost.dylib',
    ]);
  });

  it('removes stale destination files while preserving managed directory placeholders', async () => {
    const root = await makeTempRootAsync();
    const appRoot = path.join(root, 'app');
    const builtOutputDir = path.join(root, 'bin');
    const destination = stageDestination('macos', appRoot);
    const nethostLibraryPath = path.join(root, 'host-pack', 'libnethost.dylib');

    await writeFileAsync(path.join(builtOutputDir, 'ExpoDotnetHost.dll'), 'dll');
    await writeFileAsync(nethostLibraryPath, 'nethost');
    await writeFileAsync(path.join(destination, 'stale.dll'), 'old');
    await writeFileAsync(path.join(destination, '.gitkeep'), '');

    await stageArtifactsAsync({
      platform: 'macos',
      appRoot,
      mode: 'hostfxr',
      builtOutputDir,
      nethostLibraryPath,
    });

    await expect(pathExistsAsync(path.join(destination, 'stale.dll'))).resolves.toBe(false);
    await expect(pathExistsAsync(path.join(destination, '.gitkeep'))).resolves.toBe(true);
  });

  it('renames the macOS NativeAOT library from the publish directory', async () => {
    const root = await makeTempRootAsync();
    const appRoot = path.join(root, 'app');
    const builtOutputDir = path.join(root, 'publish');

    await writeFileAsync(path.join(builtOutputDir, 'ExpoDotnetHost.dylib'), 'native');
    await writeFileAsync(path.join(builtOutputDir, 'ExpoDotnetHost.dll'), 'managed');

    const result = await stageArtifactsAsync({
      platform: 'macos',
      appRoot,
      mode: 'nativeaot',
      builtOutputDir,
    });

    const destination = stageDestination('macos', appRoot);
    expect(result.staged.sort()).toEqual(['ExpoDotnetHost.loader', 'libExpoDotnetHost.dylib']);
    await expect(sortedDirectoryEntriesAsync(destination)).resolves.toEqual([
      'ExpoDotnetHost.loader',
      'libExpoDotnetHost.dylib',
    ]);
    await expect(fs.readFile(path.join(destination, 'ExpoDotnetHost.loader'), 'utf8')).resolves.toBe(
      'nativeaot\n'
    );
    await expect(
      fs.readFile(path.join(destination, 'libExpoDotnetHost.dylib'), 'utf8')
    ).resolves.toBe('native');
  });

  it('renames and fixes the iOS NativeAOT dylib after copying it', async () => {
    const root = await makeTempRootAsync();
    const appRoot = path.join(root, 'app');
    const builtOutputDir = path.join(root, 'publish');
    const calls: { command: string; args: string[] }[] = [];

    await writeFileAsync(path.join(builtOutputDir, 'ExpoDotnetHost.dylib'), 'native');
    await writeFileAsync(path.join(builtOutputDir, 'ExpoDotnetHost.dll'), 'managed');

    const result = await stageArtifactsAsync({
      platform: 'ios',
      appRoot,
      mode: 'nativeaot',
      builtOutputDir,
      runCommandAsync: async (command, args) => {
        calls.push({ command, args });
      },
    });

    const destination = stageDestination('ios', appRoot);
    const destinationPath = path.join(destination, 'libExpoDotnetHost.dylib');
    expect(result.staged.sort()).toEqual(['ExpoDotnetHost.loader', 'libExpoDotnetHost.dylib']);
    expect(result.skipped).toEqual([]);
    await expect(sortedDirectoryEntriesAsync(destination)).resolves.toEqual([
      'ExpoDotnetHost.loader',
      'libExpoDotnetHost.dylib',
    ]);
    await expect(fs.readFile(destinationPath, 'utf8')).resolves.toBe('native');
    expect(calls).toEqual([
      {
        command: 'install_name_tool',
        args: ['-id', '@rpath/libExpoDotnetHost.dylib', destinationPath],
      },
    ]);
  });

  it('does not fix the iOS NativeAOT install name when the dylib is byte-identical', async () => {
    const root = await makeTempRootAsync();
    const appRoot = path.join(root, 'app');
    const builtOutputDir = path.join(root, 'publish');
    const calls: { command: string; args: string[] }[] = [];

    await writeFileAsync(path.join(builtOutputDir, 'ExpoDotnetHost.dylib'), 'native');

    await stageArtifactsAsync({
      platform: 'ios',
      appRoot,
      mode: 'nativeaot',
      builtOutputDir,
      runCommandAsync: async (command, args) => {
        calls.push({ command, args });
      },
    });
    const result = await stageArtifactsAsync({
      platform: 'ios',
      appRoot,
      mode: 'nativeaot',
      builtOutputDir,
      runCommandAsync: async (command, args) => {
        calls.push({ command, args });
      },
    });

    expect(result.staged).toEqual([]);
    expect(result.skipped.sort()).toEqual(['ExpoDotnetHost.loader', 'libExpoDotnetHost.dylib']);
    expect(calls).toHaveLength(1);
  });

  it('renames the Android NativeAOT library and preserves jniLibs placeholders', async () => {
    const root = await makeTempRootAsync();
    const appRoot = path.join(root, 'app');
    const builtOutputDir = path.join(root, 'publish');
    const destination = stageDestination('android', appRoot);

    await writeFileAsync(path.join(builtOutputDir, 'ExpoDotnetHost.so'), 'native');
    await writeFileAsync(path.join(builtOutputDir, 'ExpoDotnetHost.dll'), 'managed');
    await writeFileAsync(path.join(destination, 'stale.so'), 'old');
    await writeFileAsync(path.join(destination, '.gitkeep'), '');

    const result = await stageArtifactsAsync({
      platform: 'android',
      appRoot,
      mode: 'nativeaot',
      builtOutputDir,
      runCommandAsync: async () => {
        throw new Error('android staging should not run install_name_tool');
      },
    });

    expect(result.staged.sort()).toEqual(['ExpoDotnetHost.loader', 'libExpoDotnetHost.so']);
    expect(result.skipped).toEqual([]);
    await expect(sortedDirectoryEntriesAsync(destination)).resolves.toEqual([
      '.gitkeep',
      'ExpoDotnetHost.loader',
      'libExpoDotnetHost.so',
    ]);
    await expect(pathExistsAsync(path.join(destination, 'stale.so'))).resolves.toBe(false);
    await expect(fs.readFile(path.join(destination, 'libExpoDotnetHost.so'), 'utf8')).resolves.toBe(
      'native'
    );
  });

  it('does not run install_name_tool for macOS or Windows NativeAOT staging', async () => {
    const root = await makeTempRootAsync();
    const appRoot = path.join(root, 'app');
    const macosOutputDir = path.join(root, 'macos-publish');
    const windowsOutputDir = path.join(root, 'windows-publish');
    const failCommand = async () => {
      throw new Error('desktop staging should not run install_name_tool');
    };

    await writeFileAsync(path.join(macosOutputDir, 'ExpoDotnetHost.dylib'), 'native');
    await writeFileAsync(path.join(windowsOutputDir, 'ExpoDotnetHost.dll'), 'native');

    await expect(
      stageArtifactsAsync({
        platform: 'macos',
        appRoot,
        mode: 'nativeaot',
        builtOutputDir: macosOutputDir,
        runCommandAsync: failCommand,
      })
    ).resolves.toEqual({
      staged: ['libExpoDotnetHost.dylib', 'ExpoDotnetHost.loader'],
      skipped: [],
    });
    await expect(
      stageArtifactsAsync({
        platform: 'windows',
        appRoot,
        mode: 'nativeaot',
        builtOutputDir: windowsOutputDir,
        runCommandAsync: failCommand,
      })
    ).resolves.toEqual({ staged: ['ExpoDotnetHost.dll', 'ExpoDotnetHost.loader'], skipped: [] });
  });
});

describe('locateNethostLibraryAsync', () => {
  const hasDotnet = (() => {
    try {
      execFileSync('dotnet', ['--info'], { stdio: 'ignore' });
      return true;
    } catch {
      return false;
    }
  })();
  const canLocateDesktopNethost =
    hasDotnet && (process.platform === 'darwin' || process.platform === 'win32');

  it.skipIf(!canLocateDesktopNethost)(
    'returns an existing nethost library from the installed host pack',
    async () => {
      const platform = process.platform === 'win32' ? 'windows' : 'macos';
      const rid = defaultRid(platform, process.arch);
      const nethostPath = await locateNethostLibraryAsync(rid);

      await expect(pathExistsAsync(nethostPath)).resolves.toBe(true);
      expect(path.basename(nethostPath)).toBe(
        rid.startsWith('win-') ? 'nethost.dll' : 'libnethost.dylib'
      );
    }
  );
});
