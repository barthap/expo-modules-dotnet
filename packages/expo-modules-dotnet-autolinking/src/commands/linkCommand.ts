import * as path from 'path';

import type { Command } from 'commander';

import {
  buildOutputDir,
  defaultConfiguration,
  defaultLoaderMode,
  defaultRid,
  runDotnetBuildAsync,
  type LoaderMode,
} from '../build';
import { generateAggregator } from '../codegen/generateAggregator';
import { discoverDotnetManifestAsync, resolveAppRoot } from '../discovery';
import { defaultGenerateOutputDir, findAdapterPackageRoot } from '../paths';
import { stageArtifactsAsync, stageDestination } from '../stage';

type LinkPlatform = 'macos' | 'windows';

interface LinkCommandOptions {
  projectRoot?: string;
  appRoot?: string;
  mode?: LoaderMode;
  rid?: string;
  configuration?: string;
  platform?: LinkPlatform;
}

const supportedPlatforms = new Set(['macos', 'windows']);
const supportedModes = new Set(['hostfxr', 'nativeaot']);

export function registerLinkCommand(program: Command): void {
  program
    .command('link')
    .description('Resolve, generate, build, and stage .NET-backed Expo modules')
    .requiredOption('--platform <macos|windows>', 'Target desktop platform')
    .option('--project-root <dir>', 'App project root (defaults to nearest package.json)')
    .option('--app-root <dir>', 'Destination app root (defaults to resolved app root)')
    .option('--mode <hostfxr|nativeaot>', 'Managed loader mode')
    .option('--configuration <c>', 'MSBuild configuration')
    .option('--rid <rid>', 'Runtime identifier')
    .action(async (options: LinkCommandOptions) => {
      const appRoot = resolveAppRoot(options.projectRoot);
      const destinationAppRoot =
        options.appRoot !== undefined ? path.resolve(options.appRoot) : appRoot;
      const outputDir = defaultGenerateOutputDir(appRoot);
      const adapterPackageRoot = findAdapterPackageRoot(appRoot);
      const platform = resolvePlatform(options.platform);
      const mode = resolveMode(options.mode, platform);
      const configuration = options.configuration ?? defaultConfiguration(mode);
      const rid = options.rid ?? defaultRid(platform, process.arch);
      const csprojPath = path.join(outputDir, 'ExpoDotnetHost.csproj');

      const manifest = await discoverDotnetManifestAsync(appRoot);
      console.log(`Resolved dotnet modules: ${formatCount(manifest.modules.length, 'module')}`);

      const generateResult = generateAggregator(manifest, { outputDir, adapterPackageRoot });
      console.log(
        `Generated dotnet host: wrote ${formatCount(
          generateResult.writtenFiles.length,
          'file'
        )}, skipped ${formatCount(generateResult.skippedFiles.length, 'file')} in ${outputDir}`
      );

      await runDotnetBuildAsync({
        csprojPath,
        mode,
        configuration,
        rid,
        adapterPackageRoot: mode === 'nativeaot' ? adapterPackageRoot : undefined,
      });
      console.log(`Built dotnet host: mode ${mode}, configuration ${configuration}`);

      const stageResult = await stageArtifactsAsync({
        platform,
        appRoot: destinationAppRoot,
        mode,
        builtOutputDir: buildOutputDir({ csprojPath, mode, configuration, rid }),
      });
      const destination = stageDestination(platform, destinationAppRoot);
      console.log(
        `Staged ${formatCount(stageResult.staged.length, 'file')}, skipped ${formatCount(
          stageResult.skipped.length,
          'file'
        )} in ${destination}`
      );
    });
}

function resolvePlatform(platform: LinkPlatform | undefined): LinkPlatform {
  if (platform === undefined || !supportedPlatforms.has(platform)) {
    throw new Error(
      `[expo-modules-dotnet-autolinking] Unsupported link platform ${platform}. Use macos or windows.`
    );
  }
  return platform;
}

function resolveMode(explicitMode: LoaderMode | undefined, platform: LinkPlatform): LoaderMode {
  if (explicitMode !== undefined) {
    if (!supportedModes.has(explicitMode)) {
      throw new Error(
        `[expo-modules-dotnet-autolinking] Unsupported link mode ${explicitMode}. Use hostfxr or nativeaot.`
      );
    }
    return explicitMode;
  }
  return defaultLoaderMode(platform);
}

function formatCount(count: number, noun: string): string {
  return `${count} ${noun}${count === 1 ? '' : 's'}`;
}
