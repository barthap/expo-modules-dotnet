import { spawn } from 'child_process';
import * as path from 'path';

export type LoaderMode = 'hostfxr' | 'nativeaot';

export interface BuildOptions {
  csprojPath: string;
  mode: LoaderMode;
  configuration: string;
  rid?: string;
}

const xcodeEnvVarsToStrip = [
  'ACTION',
  'ARCHS',
  'CURRENT_ARCH',
  'PLATFORM_NAME',
  'PRODUCT_NAME',
  'PROJECT_NAME',
  'TARGET_NAME',
  'TARGETNAME',
];

export function dotnetArgsForBuild(options: BuildOptions): string[] {
  if (options.mode === 'hostfxr') {
    return ['build', options.csprojPath, '-c', options.configuration];
  }

  if (options.rid === undefined) {
    throw new Error('[expo-modules-dotnet-autolinking] NativeAOT builds require a RID.');
  }

  return [
    'publish',
    options.csprojPath,
    '-c',
    options.configuration,
    '-r',
    options.rid,
    '/p:PublishAot=true',
    '/p:NativeLib=Shared',
  ];
}

export function defaultLoaderMode(platform: 'macos' | 'windows' | 'ios' | 'android'): LoaderMode {
  return platform === 'macos' || platform === 'windows' ? 'hostfxr' : 'nativeaot';
}

export function defaultRid(platform: string, arch = process.arch): string {
  if (platform === 'macos') {
    return arch === 'arm64' ? 'osx-arm64' : 'osx-x64';
  }
  if (platform === 'windows') {
    return arch === 'arm64' ? 'win-arm64' : 'win-x64';
  }
  throw new Error(`[expo-modules-dotnet-autolinking] No default RID for platform ${platform}.`);
}

export function defaultConfiguration(mode: LoaderMode): string {
  return mode === 'hostfxr' ? 'Debug' : 'Release';
}

export function sanitizedDotnetEnv(env: NodeJS.ProcessEnv): NodeJS.ProcessEnv {
  const sanitized = { ...env };
  for (const name of xcodeEnvVarsToStrip) {
    delete sanitized[name];
  }
  return sanitized;
}

export function buildOutputDir(options: BuildOptions): string {
  const outputDir = path.join(
    path.dirname(options.csprojPath),
    'bin',
    options.configuration,
    'net10.0'
  );

  if (options.mode === 'hostfxr') {
    return outputDir;
  }

  if (options.rid === undefined) {
    throw new Error('[expo-modules-dotnet-autolinking] NativeAOT builds require a RID.');
  }

  return path.join(outputDir, options.rid, 'publish');
}

export async function runDotnetBuildAsync(
  options: BuildOptions & { adapterPackageRoot?: string }
): Promise<void> {
  if (options.mode === 'nativeaot') {
    if (options.adapterPackageRoot === undefined) {
      throw new Error(
        '[expo-modules-dotnet-autolinking] NativeAOT builds require adapterPackageRoot.'
      );
    }

    await runDotnetAsync([
      'build',
      path.join(
        options.adapterPackageRoot,
        'managed',
        'packages',
        'Expo.ModulesCore.Generator',
        'Expo.ModulesCore.Generator.csproj'
      ),
      '-c',
      'Debug',
    ]);
  }

  await runDotnetAsync(dotnetArgsForBuild(options));
}

function runDotnetAsync(args: string[]): Promise<void> {
  return new Promise((resolve, reject) => {
    const child = spawn('dotnet', args, {
      stdio: 'inherit',
      env: sanitizedDotnetEnv(process.env),
    });

    child.on('error', reject);
    child.on('close', (code) => {
      if (code === 0) {
        resolve();
        return;
      }
      reject(new Error(`[expo-modules-dotnet-autolinking] dotnet exited with code ${code}.`));
    });
  });
}
