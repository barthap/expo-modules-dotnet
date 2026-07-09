import { spawn } from 'child_process';
import * as fs from 'fs';
import * as path from 'path';

export type LoaderMode = 'hostfxr' | 'nativeaot';

export interface BuildOptions {
  csprojPath: string;
  mode: LoaderMode;
  configuration: string;
  platform?: 'macos' | 'windows' | 'ios' | 'android';
  rid?: string;
  androidNdkClangPath?: string;
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

  const args = [
    'publish',
    options.csprojPath,
    '-c',
    options.configuration,
    '-r',
    options.rid,
    '/p:PublishAot=true',
    '/p:NativeLib=Shared',
  ];

  if (isMobileRid(options.rid)) {
    args.push('/p:PublishAotUsingRuntimePack=true', '--self-contained', 'true');
  }

  if (options.rid.startsWith('android-')) {
    const clangPath = options.androidNdkClangPath ?? locateAndroidNdkClangSync(process.env);
    args.push(`/p:CppCompilerAndLinker=${clangPath}`, '/p:StripSymbols=false');
  }

  return args;
}

export function defaultLoaderMode(platform: 'macos' | 'windows' | 'ios' | 'android'): LoaderMode {
  return platform === 'macos' || platform === 'windows' ? 'hostfxr' : 'nativeaot';
}

export function defaultRid(
  platform: string,
  arch = process.arch,
  env: NodeJS.ProcessEnv = process.env
): string {
  if (platform === 'macos') {
    return arch === 'arm64' ? 'osx-arm64' : 'osx-x64';
  }
  if (platform === 'windows') {
    return arch === 'arm64' ? 'win-arm64' : 'win-x64';
  }
  if (platform === 'ios') {
    return env.PLATFORM_NAME === 'iphoneos' ? 'ios-arm64' : 'iossimulator-arm64';
  }
  if (platform === 'android') {
    return 'android-arm64';
  }
  throw new Error(`[expo-modules-dotnet-autolinking] No default RID for platform ${platform}.`);
}

export function isMobileRid(rid: string): boolean {
  return rid.startsWith('ios-') || rid.startsWith('iossimulator-') || rid.startsWith('android-');
}

export function locateAndroidNdkClangSync(env: NodeJS.ProcessEnv): string {
  const ndkRoot = resolveAndroidNdkRootSync(env);
  const clangPaths = findAndroidNdkClangPathsSync(ndkRoot).sort(compareVersionLike);
  const clangPath = clangPaths.at(-1);
  if (clangPath === undefined) {
    throw new Error(
      `[expo-modules-dotnet-autolinking] Android NDK clang not found under ${ndkRoot}.`
    );
  }
  return clangPath;
}

function resolveAndroidNdkRootSync(env: NodeJS.ProcessEnv): string {
  if (env.ANDROID_NDK_HOME !== undefined && env.ANDROID_NDK_HOME !== '') {
    if (fs.existsSync(env.ANDROID_NDK_HOME)) {
      return env.ANDROID_NDK_HOME;
    }
    throwAndroidNdkNotFound();
  }

  const ndkRoot = [env.ANDROID_HOME, env.ANDROID_SDK_ROOT]
    .filter((sdkRoot): sdkRoot is string => sdkRoot !== undefined && sdkRoot !== '')
    .flatMap((sdkRoot) => {
      const ndkParent = path.join(sdkRoot, 'ndk');
      if (!fs.existsSync(ndkParent)) {
        return [];
      }
      return fs
        .readdirSync(ndkParent, { withFileTypes: true })
        .filter((entry) => entry.isDirectory())
        .map((entry) => path.join(ndkParent, entry.name));
    })
    .sort(compareVersionLike)
    .at(-1);
  if (ndkRoot === undefined) {
    throwAndroidNdkNotFound();
  }
  return ndkRoot;
}

function findAndroidNdkClangPathsSync(ndkRoot: string): string[] {
  const prebuiltRoot = path.join(ndkRoot, 'toolchains', 'llvm', 'prebuilt');
  if (!fs.existsSync(prebuiltRoot)) {
    return [];
  }

  const clangPaths: string[] = [];
  for (const prebuilt of fs.readdirSync(prebuiltRoot, { withFileTypes: true })) {
    if (!prebuilt.isDirectory()) {
      continue;
    }
    const binRoot = path.join(prebuiltRoot, prebuilt.name, 'bin');
    if (!fs.existsSync(binRoot)) {
      continue;
    }
    for (const file of fs.readdirSync(binRoot, { withFileTypes: true })) {
      if (file.isFile() && /^aarch64-linux-android.*-clang$/.test(file.name)) {
        clangPaths.push(path.join(binRoot, file.name));
      }
    }
  }
  return clangPaths;
}

function compareVersionLike(left: string, right: string): number {
  return left.localeCompare(right, undefined, { numeric: true, sensitivity: 'base' });
}

function throwAndroidNdkNotFound(): never {
  throw new Error(
    '[expo-modules-dotnet-autolinking] Android NDK not found. Set ANDROID_NDK_HOME or ANDROID_HOME.'
  );
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

export function dotnetBinary(env: NodeJS.ProcessEnv = process.env): string {
  return env.DOTNET_BINARY !== undefined && env.DOTNET_BINARY !== '' ? env.DOTNET_BINARY : 'dotnet';
}

export function buildOutputDir(options: BuildOptions): string {
  const targetFramework =
    options.platform === 'windows' ? 'net10.0-windows10.0.19041.0' : 'net10.0';
  const outputDir = path.join(
    path.dirname(options.csprojPath),
    'bin',
    options.configuration,
    targetFramework
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
    const binary = dotnetBinary();
    const child = spawn(binary, args, {
      stdio: 'inherit',
      env: sanitizedDotnetEnv(process.env),
    });

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
        resolve();
        return;
      }
      reject(new Error(`[expo-modules-dotnet-autolinking] dotnet exited with code ${code}.`));
    });
  });
}
