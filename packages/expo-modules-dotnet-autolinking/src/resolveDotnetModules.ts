import * as fs from 'fs';
import * as path from 'path';

import * as semver from 'semver';

import type {
  DotnetLinkingManifest,
  DotnetModule,
  DotnetSubstitution,
  RawDotnetConfig,
} from './types';

export interface DotnetPackageInput {
  packageName: string;
  packageRoot: string;
  dotnetConfig: RawDotnetConfig | undefined;
}

const SUBSTITUTION_FIELDS = ['package', 'versionRange', 'accessor', 'replacement'];

function autolinkingError(message: string): Error {
  return new Error(`[expo-modules-dotnet-autolinking] ${message}`);
}

function isPortableRelativePath(value: string): boolean {
  if (value.length === 0 || value.includes('\\') || value.startsWith('/') || /^[A-Za-z]:/.test(value)) {
    return false;
  }
  return value.split('/').every((segment) => segment.length > 0 && segment !== '..' && segment !== '.');
}

function resolveSubstitutions(input: DotnetPackageInput, hasProjects: boolean): DotnetSubstitution[] {
  const raw: unknown = input.dotnetConfig?.substitutions;
  if (raw === undefined) {
    return [];
  }

  const owner = `Package "${input.packageName}"`;
  if (!Array.isArray(raw)) {
    throw autolinkingError(`${owner} declares dotnet.substitutions that is not a list.`);
  }
  if (!hasProjects) {
    throw autolinkingError(
      `${owner} declares dotnet.substitutions but no dotnet.projects. A substitution needs a dotnet module.`
    );
  }

  const seen = new Set<string>();
  return raw.map((entry: unknown, index) => {
    const at = `${owner} dotnet.substitutions[${index}]`;
    if (typeof entry !== 'object' || entry === null || Array.isArray(entry)) {
      throw autolinkingError(`${at} must be an object.`);
    }
    const fields = entry as Record<string, unknown>;
    for (const key of Object.keys(fields)) {
      if (!SUBSTITUTION_FIELDS.includes(key)) {
        throw autolinkingError(
          `${at} has unknown field "${key}". Allowed fields: ${SUBSTITUTION_FIELDS.join(', ')}.`
        );
      }
    }

    const field = (name: string): string => {
      const value = fields[name];
      if (typeof value !== 'string' || value.length === 0) {
        throw autolinkingError(`${at}.${name} must be a non-empty string.`);
      }
      return value;
    };

    const upstreamPackage = field('package');
    const versionRange = field('versionRange');
    if (semver.validRange(versionRange) === null) {
      throw autolinkingError(`${at}.versionRange "${versionRange}" is not a valid semver range.`);
    }
    for (const name of ['accessor', 'replacement']) {
      if (!isPortableRelativePath(field(name))) {
        throw autolinkingError(
          `${at}.${name} "${String(fields[name])}" must be a relative path with forward slashes and no "." or ".." segments.`
        );
      }
    }

    if (seen.has(upstreamPackage)) {
      throw autolinkingError(`${owner} declares a substitution for "${upstreamPackage}" more than once.`);
    }
    seen.add(upstreamPackage);

    const replacement = field('replacement');
    const replacementPath = path.resolve(input.packageRoot, ...replacement.split('/'));
    if (!fs.existsSync(replacementPath)) {
      throw autolinkingError(
        `${owner} declares a substitution replacement that does not exist: ${replacement} (resolved: ${replacementPath})`
      );
    }

    return { package: upstreamPackage, versionRange, accessor: field('accessor'), replacementPath };
  });
}

export function buildDotnetManifest(inputs: DotnetPackageInput[]): DotnetLinkingManifest {
  const modules: DotnetModule[] = [];
  const assemblyOwners = new Map<string, string>();
  const substitutionOwners = new Map<string, string>();

  for (const input of inputs) {
    const projects = input.dotnetConfig?.projects ?? [];
    const substitutions = resolveSubstitutions(input, projects.length > 0);
    if (projects.length === 0) {
      continue;
    }

    const resolvedProjects = projects.map((project) => {
      const csprojPath = path.resolve(input.packageRoot, project.path);

      if (!fs.existsSync(csprojPath)) {
        throw autolinkingError(
          `Package "${input.packageName}" declares a dotnet project ` +
            `that does not exist: ${project.path} (resolved: ${csprojPath})`
        );
      }

      const assemblyName = project.assemblyName ?? path.basename(csprojPath, '.csproj');
      const owner = assemblyOwners.get(assemblyName);
      if (owner !== undefined) {
        throw autolinkingError(
          `Packages "${owner}" and "${input.packageName}" ` +
            `declare duplicate assembly name "${assemblyName}"`
        );
      }

      assemblyOwners.set(assemblyName, input.packageName);
      return { csprojPath, assemblyName };
    });

    for (const substitution of substitutions) {
      const owner = substitutionOwners.get(substitution.package);
      if (owner !== undefined) {
        throw autolinkingError(
          `Packages "${owner}" and "${input.packageName}" both declare a substitution for "${substitution.package}".`
        );
      }
      substitutionOwners.set(substitution.package, input.packageName);
    }

    resolvedProjects.sort((a, b) => a.assemblyName.localeCompare(b.assemblyName));
    modules.push({
      packageName: input.packageName,
      packageRoot: input.packageRoot,
      projects: resolvedProjects,
      substitutions,
    });
  }

  modules.sort((a, b) => a.packageName.localeCompare(b.packageName));
  return { modules };
}
