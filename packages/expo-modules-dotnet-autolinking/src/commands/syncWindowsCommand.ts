import * as fs from 'fs';
import * as path from 'path';

import type { Command } from 'commander';

import { generateAggregator } from '../codegen/generateAggregator';
import { writeIfChangedSync } from '../codegen/writeIfChanged';
import { discoverDotnetManifestAsync, resolveAppRoot } from '../discovery';
import { defaultGenerateOutputDir, findAdapterPackageRoot } from '../paths';
import { configureMixedModeDebugger, findPackageProjectPath } from '../windows/packageDebugger';
import { runReactNativeWindowsAutolink } from '../windows/reactNativeCli';
import { synchronizeManagedSolution } from '../windows/solution';

interface SyncWindowsCommandOptions {
  projectRoot?: string;
  sln: string;
  proj: string;
  check?: boolean;
}

export function registerSyncWindowsCommand(program: Command): void {
  program
    .command('sync-windows')
    .description('Synchronize RNW autolinking and the Expo .NET managed solution projection')
    .requiredOption('--sln <path>', 'RNW Visual Studio solution path')
    .requiredOption('--proj <path>', 'RNW app .vcxproj path')
    .option('--project-root <dir>', 'App project root (defaults to nearest package.json)')
    .option('--check', 'Check the managed solution projection without writing')
    .action(async (options: SyncWindowsCommandOptions) => {
      const appRoot = resolveAppRoot(options.projectRoot);
      const solutionPath = path.resolve(appRoot, options.sln);
      const projectPath = path.resolve(appRoot, options.proj);
      if (!fs.existsSync(solutionPath) || !fs.existsSync(projectPath)) {
        throw new Error('[expo-modules-dotnet-autolinking] sync-windows requires existing --sln and --proj paths.');
      }
      const check = options.check === true;
      // RNW's --check currently exits nonzero after a successful no-op
      // autolink-windows run (NeedAutolinking). Do not make this CLI's
      // deterministic managed-projection check depend on that false failure.
      if (!check) {
        await runReactNativeWindowsAutolink({ appRoot, solutionPath, projectPath, check: false });
      }
      const manifest = await discoverDotnetManifestAsync(appRoot);
      const outputDir = defaultGenerateOutputDir(appRoot);
      const adapterPackageRoot = findAdapterPackageRoot(appRoot);
      if (!check) {
        generateAggregator(manifest, { outputDir, adapterPackageRoot });
      }

      const solution = fs.readFileSync(solutionPath, 'utf8');
      const graph = {
        hostProjectPath: path.join(outputDir, 'ExpoDotnetHost.csproj'),
        coreProjectPaths: [
          path.join(adapterPackageRoot, 'managed', 'packages', 'Expo.JSI', 'Expo.JSI.csproj'),
          path.join(adapterPackageRoot, 'managed', 'packages', 'Expo.ModulesCore', 'Expo.ModulesCore.csproj'),
        ],
        moduleProjectPaths: manifest.modules.flatMap((module) => module.projects.map((project) => project.csprojPath)),
      };
      const synchronized = synchronizeManagedSolution(solution, solutionPath, graph, { check });
      if (check) {
        if (synchronized.changed) {
          throw new Error('[expo-modules-dotnet-autolinking] Windows managed solution projection is stale. Run sync-windows without --check.');
        }
        return;
      }

      const packagePath = findPackageProjectPath(solution, solutionPath);
      const packageProject = fs.readFileSync(packagePath, 'utf8');
      const debuggerConfiguration = configureMixedModeDebugger(packageProject);
      writeIfChangedSync(packagePath, debuggerConfiguration.content);
      writeIfChangedSync(solutionPath, synchronized.content);
    });
}
