import * as path from 'path';

import type { Command } from 'commander';

import {
  buildOutputDir,
  defaultConfiguration,
  defaultLoaderMode,
  defaultRid,
  type LoaderMode,
} from '../build';
import { resolveAppRoot } from '../discovery';
import { defaultGenerateOutputDir } from '../paths';
import { stageArtifactsAsync, stageDestination, type StagePlatform } from '../stage';

interface StageCommandOptions {
  projectRoot?: string;
  appRoot?: string;
  output?: string;
  mode?: LoaderMode;
  rid?: string;
  configuration?: string;
  platform?: StagePlatform;
}

const supportedPlatforms = new Set(['macos', 'windows', 'ios', 'android']);
const supportedModes = new Set(['hostfxr', 'nativeaot']);

export function registerStageCommand(program: Command): void {
  program
    .command('stage')
    .description('Stage built .NET host artifacts into the app managed directory')
    .requiredOption('--platform <macos|windows|ios|android>', 'Target platform')
    .option('--project-root <dir>', 'App project root (defaults to nearest package.json)')
    .option('--app-root <dir>', 'Destination app root (defaults to resolved app root)')
    .option('--output <dir>', 'Generated output directory (defaults to <app>/.expo/dotnet)')
    .option('--mode <hostfxr|nativeaot>', 'Managed loader mode')
    .option('--configuration <c>', 'MSBuild configuration')
    .option('--rid <rid>', 'Runtime identifier')
    .action(async (options: StageCommandOptions) => {
      const appRoot = resolveAppRoot(options.projectRoot);
      const destinationAppRoot =
        options.appRoot !== undefined ? path.resolve(options.appRoot) : appRoot;
      const outputDir =
        options.output !== undefined ? path.resolve(options.output) : defaultGenerateOutputDir(appRoot);
      const platform = resolvePlatform(options.platform);
      const mode = resolveMode(options.mode, platform);
      const configuration = options.configuration ?? defaultConfiguration(mode);
      const rid = options.rid ?? defaultRid(platform, process.arch);
      const csprojPath = path.join(outputDir, 'ExpoDotnetHost.csproj');
      const result = await stageArtifactsAsync({
        platform,
        appRoot: destinationAppRoot,
        mode,
        builtOutputDir: buildOutputDir({ csprojPath, mode, configuration, rid }),
      });
      const destination = stageDestination(platform, destinationAppRoot);

      console.log(
        `Staged ${formatCount(result.staged.length, 'file')}, skipped ${formatCount(
          result.skipped.length,
          'file'
        )} in ${destination}`
      );
    });
}

function resolvePlatform(platform: string | undefined): StagePlatform {
  if (platform === undefined || !supportedPlatforms.has(platform)) {
    throw new Error(
      `[expo-modules-dotnet-autolinking] Unsupported stage platform ${platform}. Use macos, windows, ios, or android.`
    );
  }

  return platform as StagePlatform;
}

function resolveMode(explicitMode: LoaderMode | undefined, platform: StagePlatform): LoaderMode {
  if (explicitMode !== undefined) {
    if (!supportedModes.has(explicitMode)) {
      throw new Error(
        `[expo-modules-dotnet-autolinking] Unsupported stage mode ${explicitMode}. Use hostfxr or nativeaot.`
      );
    }

    return explicitMode;
  }

  return defaultLoaderMode(platform);
}

function formatCount(count: number, noun: string): string {
  return `${count} ${noun}${count === 1 ? '' : 's'}`;
}
