import * as path from 'path';

import {
  findProjectRootSync,
  makeCachedDependenciesLinker,
  scanExpoModuleResolutionsForPlatform,
} from 'expo-modules-autolinking/exports';

import { buildDotnetManifest, type DotnetPackageInput } from './resolveDotnetModules';
import type { DotnetLinkingManifest, RawDotnetConfig } from './types';

export function resolveAppRoot(explicit?: string): string {
  const root = explicit !== undefined ? path.resolve(explicit) : findProjectRootSync(process.cwd());
  return path.basename(root) === 'package.json' ? path.dirname(root) : root;
}

export async function discoverDotnetManifestAsync(
  appRoot: string
): Promise<DotnetLinkingManifest> {
  const linker = makeCachedDependenciesLinker({ projectRoot: appRoot });
  const revisions = await scanExpoModuleResolutionsForPlatform(linker, 'dotnet');
  const inputs: DotnetPackageInput[] = Object.entries(revisions).map(
    ([packageName, revision]) => ({
      packageName,
      packageRoot: revision.path,
      dotnetConfig: (revision.config?.toJSON() as { dotnet?: RawDotnetConfig } | undefined)
        ?.dotnet,
    })
  );
  return buildDotnetManifest(inputs);
}
