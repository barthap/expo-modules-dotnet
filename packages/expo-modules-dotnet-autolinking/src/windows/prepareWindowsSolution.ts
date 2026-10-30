import * as fs from 'fs';
import * as path from 'path';

import { generateAggregator } from '../codegen/generateAggregator';
import { writeIfChangedSync } from '../codegen/writeIfChanged';
import { discoverDotnetManifestAsync } from '../discovery';
import { defaultGenerateOutputDir, findAdapterPackageRoot } from '../paths';
import { configureMixedModeDebugger, findPackageProjectPath } from './packageDebugger';
import { synchronizeManagedSolution } from './solution';

export interface PrepareWindowsSolutionOptions {
  appRoot: string;
  solutionPath: string;
  check?: boolean;
}

/**
 * Generates the host project, projects the managed projects into the solution,
 * and enables mixed-mode debugging on the package project. It does not run RNW
 * autolink-windows; callers that need it run it first. `changed` reports only
 * the managed solution projection, which is what `sync-windows --check` verifies.
 */
export async function prepareWindowsSolutionAsync(
  options: PrepareWindowsSolutionOptions
): Promise<{ changed: boolean }> {
  const { appRoot, solutionPath } = options;
  const check = options.check === true;
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
  const packagePath = findPackageProjectPath(solution, solutionPath);
  const debuggerConfiguration = configureMixedModeDebugger(fs.readFileSync(packagePath, 'utf8'));
  if (!check) {
    writeIfChangedSync(packagePath, debuggerConfiguration.content);
    writeIfChangedSync(solutionPath, synchronized.content);
  }
  return { changed: synchronized.changed };
}
