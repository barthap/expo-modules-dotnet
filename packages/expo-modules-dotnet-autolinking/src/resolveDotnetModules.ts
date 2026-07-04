import * as fs from 'fs';
import * as path from 'path';

import type { DotnetLinkingManifest, DotnetModule, RawDotnetConfig } from './types';

export interface DotnetPackageInput {
  packageName: string;
  packageRoot: string;
  dotnetConfig: RawDotnetConfig | undefined;
}

export function buildDotnetManifest(inputs: DotnetPackageInput[]): DotnetLinkingManifest {
  const modules: DotnetModule[] = [];
  const assemblyOwners = new Map<string, string>();

  for (const input of inputs) {
    const projects = input.dotnetConfig?.projects ?? [];
    if (projects.length === 0) {
      continue;
    }

    const resolvedProjects = projects.map((project) => {
      const csprojPath = path.resolve(input.packageRoot, project.path);

      if (!fs.existsSync(csprojPath)) {
        throw new Error(
          `[expo-modules-dotnet-autolinking] Package "${input.packageName}" declares a dotnet project ` +
            `that does not exist: ${project.path} (resolved: ${csprojPath})`
        );
      }

      const assemblyName = project.assemblyName ?? path.basename(csprojPath, '.csproj');
      const owner = assemblyOwners.get(assemblyName);
      if (owner !== undefined) {
        throw new Error(
          `[expo-modules-dotnet-autolinking] Packages "${owner}" and "${input.packageName}" ` +
            `declare duplicate assembly name "${assemblyName}"`
        );
      }

      assemblyOwners.set(assemblyName, input.packageName);
      return { csprojPath, assemblyName };
    });

    resolvedProjects.sort((a, b) => a.assemblyName.localeCompare(b.assemblyName));
    modules.push({
      packageName: input.packageName,
      packageRoot: input.packageRoot,
      projects: resolvedProjects,
    });
  }

  modules.sort((a, b) => a.packageName.localeCompare(b.packageName));
  return { modules };
}
