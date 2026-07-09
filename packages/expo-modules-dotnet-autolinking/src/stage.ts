import { spawn } from 'child_process';
import * as fs from 'fs/promises';
import * as path from 'path';

import { defaultRid, dotnetBinary, sanitizedDotnetEnv, type LoaderMode } from './build';

export type StagePlatform = 'macos' | 'windows' | 'ios' | 'android';

export interface StageOptions {
  platform: StagePlatform;
  appRoot: string;
  mode: LoaderMode;
  builtOutputDir: string;
  /**
   * Overrides nethost discovery for tests or callers that already resolved the
   * host pack library.
   */
  nethostLibraryPath?: string;
  runCommandAsync?: (command: string, args: string[]) => Promise<void>;
}

interface StageFile {
  source: string;
  basename: string;
}

const preservedDestinationFiles = new Set(['.gitkeep', '.gitignore']);

export function stageDestination(platform: StagePlatform, appRoot: string): string {
  switch (platform) {
    case 'android':
      return path.join(appRoot, 'android', 'app', 'src', 'main', 'jniLibs', 'arm64-v8a');
    case 'ios':
      return path.join(appRoot, 'ios', 'Managed');
    default:
      return path.join(appRoot, platform, 'Managed');
  }
}

export async function locateNethostLibraryAsync(rid: string): Promise<string> {
  const dotnetInfo = await runDotnetInfoAsync();
  const basePath = parseDotnetBasePath(dotnetInfo);
  if (basePath === undefined) {
    throw new Error('[expo-modules-dotnet-autolinking] Could not find Base Path in dotnet --info.');
  }

  const dotnetRoot = path.resolve(basePath, '..', '..');
  const packRoot = path.join(dotnetRoot, 'packs', `Microsoft.NETCore.App.Host.${rid}`);
  const version = await latestStablePackVersionAsync(packRoot);
  const libraryName = rid.startsWith('win-') ? 'nethost.dll' : 'libnethost.dylib';
  const libraryPath = path.join(packRoot, version, 'runtimes', rid, 'native', libraryName);

  if (!(await fileExistsAsync(libraryPath))) {
    throw new Error(
      `[expo-modules-dotnet-autolinking] Could not find nethost library at ${libraryPath}. Searched host pack ${packRoot}.`
    );
  }

  return libraryPath;
}

export async function stageArtifactsAsync(
  options: StageOptions
): Promise<{ staged: string[]; skipped: string[] }> {
  const destination = stageDestination(options.platform, options.appRoot);
  const stageFiles =
    options.mode === 'hostfxr'
      ? await hostfxrStageFilesAsync(options)
      : nativeAotStageFiles(options);

  await fs.mkdir(destination, { recursive: true });
  await removeStaleFilesAsync(destination, new Set(stageFiles.map((file) => file.basename)));

  const staged: string[] = [];
  const skipped: string[] = [];

  for (const file of stageFiles) {
    const destinationPath = path.join(destination, file.basename);
    if (await filesAreEqualAsync(file.source, destinationPath)) {
      skipped.push(file.basename);
      continue;
    }

    await fs.copyFile(file.source, destinationPath);
    staged.push(file.basename);

    if (options.platform === 'ios' && file.basename === 'libExpoDotnetHost.dylib') {
      await (options.runCommandAsync ?? runCommandAsync)('install_name_tool', [
        '-id',
        '@rpath/libExpoDotnetHost.dylib',
        destinationPath,
      ]);
    }
  }

  return { staged, skipped };
}

async function hostfxrStageFilesAsync(options: StageOptions): Promise<StageFile[]> {
  const entries = await fs.readdir(options.builtOutputDir, { withFileTypes: true });
  const files = entries
    .filter((entry) => entry.isFile())
    .map((entry) => entry.name)
    .filter(isHostfxrArtifact)
    .sort()
    .map((basename) => ({
      source: path.join(options.builtOutputDir, basename),
      basename,
    }));

  const rid = defaultRid(options.platform, process.arch);
  const nethostLibraryPath =
    options.nethostLibraryPath ?? (await locateNethostLibraryAsync(rid));

  return [
    ...files,
    {
      source: nethostLibraryPath,
      basename: path.basename(nethostLibraryPath),
    },
  ];
}

function nativeAotStageFiles(options: StageOptions): StageFile[] {
  const nativeLibrary = nativeAotLibraryNames(options.platform);

  return [
    {
      source: path.join(options.builtOutputDir, nativeLibrary.source),
      basename: nativeLibrary.basename,
    },
  ];
}

function nativeAotLibraryNames(platform: StagePlatform): { source: string; basename: string } {
  switch (platform) {
    case 'android':
      return { source: 'ExpoDotnetHost.so', basename: 'libExpoDotnetHost.so' };
    case 'ios':
      return { source: 'ExpoDotnetHost.dylib', basename: 'libExpoDotnetHost.dylib' };
    case 'macos':
      return { source: 'ExpoDotnetHost.dylib', basename: 'libExpoDotnetHost.dylib' };
    case 'windows':
      return { source: 'ExpoDotnetHost.dll', basename: 'ExpoDotnetHost.dll' };
  }
}

function isHostfxrArtifact(fileName: string): boolean {
  return (
    fileName.endsWith('.dll') ||
    fileName.endsWith('.deps.json') ||
    fileName.endsWith('.runtimeconfig.json')
  );
}

async function removeStaleFilesAsync(destination: string, expectedFiles: Set<string>): Promise<void> {
  const entries = await fs.readdir(destination, { withFileTypes: true });

  await Promise.all(
    entries.map(async (entry) => {
      if (preservedDestinationFiles.has(entry.name) || expectedFiles.has(entry.name)) {
        return;
      }

      await fs.rm(path.join(destination, entry.name), { recursive: true, force: true });
    })
  );
}

async function filesAreEqualAsync(source: string, destination: string): Promise<boolean> {
  let sourceStat;
  let destinationStat;

  try {
    [sourceStat, destinationStat] = await Promise.all([fs.stat(source), fs.stat(destination)]);
  } catch {
    return false;
  }

  if (!sourceStat.isFile() || !destinationStat.isFile() || sourceStat.size !== destinationStat.size) {
    return false;
  }

  const [sourceBuffer, destinationBuffer] = await Promise.all([
    fs.readFile(source),
    fs.readFile(destination),
  ]);
  return sourceBuffer.equals(destinationBuffer);
}

async function latestStablePackVersionAsync(packRoot: string): Promise<string> {
  let entries;

  try {
    entries = await fs.readdir(packRoot, { withFileTypes: true });
  } catch {
    throw new Error(`[expo-modules-dotnet-autolinking] Missing .NET host pack: ${packRoot}.`);
  }

  const versions = entries
    .filter((entry) => entry.isDirectory())
    .map((entry) => entry.name)
    .filter((name) => /^\d+(?:\.\d+){2,}$/.test(name))
    .sort(compareSemver);

  const latest = versions.at(-1);
  if (latest === undefined) {
    throw new Error(
      `[expo-modules-dotnet-autolinking] No stable .NET host pack versions found under ${packRoot}.`
    );
  }

  return latest;
}

function compareSemver(left: string, right: string): number {
  const leftParts = left.split('.').map(Number);
  const rightParts = right.split('.').map(Number);
  const length = Math.max(leftParts.length, rightParts.length);

  for (let index = 0; index < length; index += 1) {
    const difference = (leftParts[index] ?? 0) - (rightParts[index] ?? 0);
    if (difference !== 0) {
      return difference;
    }
  }

  return 0;
}

function parseDotnetBasePath(dotnetInfo: string): string | undefined {
  for (const line of dotnetInfo.split(/\r?\n/)) {
    const match = line.match(/^\s*Base Path:\s*(.+?)\s*$/);
    if (match !== null) {
      return match[1];
    }
  }

  return undefined;
}

function runDotnetInfoAsync(): Promise<string> {
  return new Promise((resolve, reject) => {
    const binary = dotnetBinary();
    const child = spawn(binary, ['--info'], {
      stdio: ['ignore', 'pipe', 'pipe'],
      env: sanitizedDotnetEnv(process.env),
    });
    const stdout: Buffer[] = [];
    const stderr: Buffer[] = [];

    child.stdout.on('data', (chunk: Buffer) => stdout.push(chunk));
    child.stderr.on('data', (chunk: Buffer) => stderr.push(chunk));
    child.on('error', (error: NodeJS.ErrnoException) => {
      if (error.code === 'ENOENT') {
        reject(
          new Error(
            `[expo-modules-dotnet-autolinking] Could not find dotnet executable "${binary}". ` +
              'Set DOTNET_BINARY in .xcode.env or .xcode.env.local.'
          )
        );
        return;
      }
      reject(error);
    });
    child.on('close', (code) => {
      if (code === 0) {
        resolve(Buffer.concat(stdout).toString('utf8'));
        return;
      }

      reject(
        new Error(
          `[expo-modules-dotnet-autolinking] dotnet --info exited with code ${code}: ${Buffer.concat(
            stderr
          ).toString('utf8')}`
        )
      );
    });
  });
}

function runCommandAsync(command: string, args: string[]): Promise<void> {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, {
      stdio: ['ignore', 'ignore', 'pipe'],
    });
    const stderr: Buffer[] = [];

    child.stderr.on('data', (chunk: Buffer) => stderr.push(chunk));
    child.on('error', reject);
    child.on('close', (code) => {
      if (code === 0) {
        resolve();
        return;
      }

      reject(
        new Error(
          `[expo-modules-dotnet-autolinking] ${command} ${args.join(
            ' '
          )} exited with code ${code}: ${Buffer.concat(stderr).toString('utf8')}`
        )
      );
    });
  });
}

async function fileExistsAsync(filePath: string): Promise<boolean> {
  try {
    const stat = await fs.stat(filePath);
    return stat.isFile();
  } catch {
    return false;
  }
}
