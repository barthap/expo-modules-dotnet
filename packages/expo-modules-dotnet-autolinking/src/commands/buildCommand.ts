import * as fs from 'fs';
import * as path from 'path';

import type { Command } from 'commander';

import {
  defaultConfiguration,
  defaultLoaderMode,
  defaultRid,
  runDotnetBuildAsync,
  type LoaderMode,
} from '../build';
import { resolveAppRoot } from '../discovery';
import { defaultGenerateOutputDir, findAdapterPackageRoot } from '../paths';

type BuildPlatform = 'macos' | 'windows' | 'ios' | 'android';

interface BuildCommandOptions {
  projectRoot?: string;
  output?: string;
  mode?: LoaderMode;
  rid?: string;
  configuration?: string;
  platform?: BuildPlatform;
}

const supportedPlatforms = new Set(['macos', 'windows', 'ios', 'android']);
const supportedModes = new Set(['hostfxr', 'nativeaot']);

export function registerBuildCommand(program: Command): void {
  program
    .command('build')
    .description('Build the generated .NET host project')
    .option('--project-root <dir>', 'App project root (defaults to nearest package.json)')
    .option('--output <dir>', 'Generated output directory (defaults to <app>/.expo/dotnet)')
    .option('--mode <hostfxr|nativeaot>', 'Managed loader mode')
    .option('--rid <rid>', 'Runtime identifier for NativeAOT publish')
    .option('--configuration <c>', 'MSBuild configuration')
    .option('--platform <macos|windows|ios|android>', 'Target platform for defaults')
    .action(async (options: BuildCommandOptions) => {
      const appRoot = resolveAppRoot(options.projectRoot);
      const outputDir =
        options.output !== undefined ? path.resolve(options.output) : defaultGenerateOutputDir(appRoot);
      const csprojPath = path.join(outputDir, 'ExpoDotnetHost.csproj');

      if (!fs.existsSync(csprojPath)) {
        throw new Error(
          `[expo-modules-dotnet-autolinking] Generated host project not found at ${csprojPath}. Run generate first.`
        );
      }

      const platform = resolvePlatform(options.platform);
      const mode = resolveMode(options.mode, platform);
      const configuration = options.configuration ?? defaultConfiguration(mode);
      const rid = options.rid ?? (mode === 'nativeaot' ? resolveRidPlatform(platform) : undefined);

      await runDotnetBuildAsync({
        csprojPath,
        mode,
        configuration,
        rid,
        adapterPackageRoot: mode === 'nativeaot' ? findAdapterPackageRoot(appRoot) : undefined,
      });
    });
}

function resolveMode(explicitMode: LoaderMode | undefined, platform: BuildPlatform | undefined): LoaderMode {
  if (explicitMode !== undefined) {
    if (!supportedModes.has(explicitMode)) {
      throw new Error(
        `[expo-modules-dotnet-autolinking] Unsupported build mode ${explicitMode}. Use hostfxr or nativeaot.`
      );
    }
    return explicitMode;
  }

  if (platform !== undefined) {
    return defaultLoaderMode(platform);
  }

  throw new Error(
    '[expo-modules-dotnet-autolinking] Could not infer build mode for this OS. Pass --mode or --platform.'
  );
}

function resolvePlatform(explicitPlatform: BuildPlatform | undefined): BuildPlatform | undefined {
  if (explicitPlatform !== undefined) {
    if (!supportedPlatforms.has(explicitPlatform)) {
      throw new Error(
        `[expo-modules-dotnet-autolinking] Unsupported platform ${explicitPlatform}. Use macos, windows, ios, or android.`
      );
    }
    return explicitPlatform;
  }

  if (process.platform === 'darwin') {
    return 'macos';
  }
  if (process.platform === 'win32') {
    return 'windows';
  }
  return undefined;
}

function resolveRidPlatform(platform: BuildPlatform | undefined): string {
  if (platform === undefined) {
    throw new Error(
      '[expo-modules-dotnet-autolinking] NativeAOT builds require --rid or --platform.'
    );
  }
  return defaultRid(platform, process.arch);
}
