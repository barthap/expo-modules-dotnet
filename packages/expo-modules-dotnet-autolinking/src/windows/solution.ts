import { createHash } from 'crypto';
import * as path from 'path';

export interface ManagedSolutionGraph {
  hostProjectPath: string;
  coreProjectPaths: string[];
  moduleProjectPaths: string[];
}

export interface SolutionSynchronizationResult {
  changed: boolean;
  content: string;
}

export const solutionFolderTypeGuid = '{66A26720-8FB5-11D2-AA7E-00C04F688DDE}';
export const csharpProjectTypeGuid = '{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}';

const managedFolderName = 'Expo .NET Managed';

interface SolutionProject {
  typeGuid: string;
  name: string;
  projectPath: string;
  guid: string;
  block: string;
}

interface ManagedProject extends SolutionProject {
  projectPath: string;
}

export function stableSolutionGuid(projectPath: string): string {
  const identity = path.resolve(projectPath).replace(/\\/g, '/').toLowerCase();
  const bytes = createHash('sha256').update(identity).digest().subarray(0, 16);
  bytes[6] = (bytes[6] & 0x0f) | 0x50;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = bytes.toString('hex').toUpperCase();
  return `{${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}}`;
}

export function synchronizeManagedSolution(
  source: string,
  solutionPath: string,
  graph: ManagedSolutionGraph,
  options: { check: boolean }
): SolutionSynchronizationResult {
  const bom = source.startsWith('\uFEFF') ? '\uFEFF' : '';
  const solutionSource = bom === '' ? source : source.slice(1);
  if (!solutionSource.trimStart().startsWith('Microsoft Visual Studio Solution File')) {
    throw new Error('[expo-modules-dotnet-autolinking] Solution does not contain a Visual Studio solution header.');
  }

  const eol = solutionSource.includes('\r\n') ? '\r\n' : '\n';
  const projects = parseProjects(solutionSource);
  const folders = projects.filter(
    (project) => project.name === managedFolderName && project.typeGuid === solutionFolderTypeGuid
  );
  if (folders.length > 1) {
    throw new Error('[expo-modules-dotnet-autolinking] Solution contains multiple Expo .NET Managed folders.');
  }

  const ownedFolder = folders[0];
  const nestedProjects = parseNestedProjectMappings(solutionSource);
  const ownedGuids = new Set<string>(ownedFolder === undefined ? [] : [ownedFolder.guid]);
  if (ownedFolder !== undefined) {
    for (const [child, parent] of nestedProjects) {
      if (parent === ownedFolder.guid) {
        ownedGuids.add(child);
      }
    }
  }

  let updated = solutionSource;
  for (const project of projects) {
    if (ownedGuids.has(project.guid)) {
      updated = updated.replace(project.block, '');
    }
  }

  const managedFolder = createManagedFolder();
  const managedProjects = createManagedProjects(solutionPath, graph);
  updated = insertProjectsBeforeGlobal(updated, [managedFolder, ...managedProjects], eol);
  updated = replaceProjectConfigurations(updated, ownedGuids, managedProjects, eol);
  updated = replaceNestedProjects(updated, ownedGuids, managedFolder.guid, managedProjects, eol);

  const content = `${bom}${updated}`;
  const changed = content !== source;
  return { changed, content: options.check ? source : content };
}

function parseProjects(source: string): SolutionProject[] {
  const expression = /^Project\("([^"]+)"\) = "([^"]+)", "([^"]+)", "(\{[^}]+\})"\r?\nEndProject\r?\n?/gm;
  const projects: SolutionProject[] = [];
  for (const match of source.matchAll(expression)) {
    projects.push({
      typeGuid: match[1].toUpperCase(),
      name: match[2],
      projectPath: match[3],
      guid: match[4].toUpperCase(),
      block: match[0],
    });
  }
  return projects;
}

function parseNestedProjectMappings(source: string): Map<string, string> {
  const section = findGlobalSection(source, 'NestedProjects');
  const mappings = new Map<string, string>();
  if (section === undefined) {
    return mappings;
  }

  for (const match of section.body.matchAll(/^\s*(\{[^}]+\})\s*=\s*(\{[^}]+\})\s*$/gm)) {
    mappings.set(match[1].toUpperCase(), match[2].toUpperCase());
  }
  return mappings;
}

function createManagedFolder(): SolutionProject {
  const guid = stableSolutionGuid('expo-modules-dotnet:managed-solution-folder');
  return {
    typeGuid: solutionFolderTypeGuid,
    name: managedFolderName,
    projectPath: managedFolderName,
    guid,
    block: '',
  };
}

function createManagedProjects(solutionPath: string, graph: ManagedSolutionGraph): ManagedProject[] {
  const inputs = [graph.hostProjectPath, ...graph.coreProjectPaths, ...graph.moduleProjectPaths];
  const seenPaths = new Set<string>();
  const seenNames = new Set<string>();
  const solutionDirectory = path.dirname(solutionPath);

  return inputs.map((projectPath, index) => {
    const normalized = path.resolve(projectPath);
    if (seenPaths.has(normalized)) {
      throw new Error(`[expo-modules-dotnet-autolinking] Managed solution graph contains duplicate project path ${projectPath}.`);
    }
    seenPaths.add(normalized);

    const name = path.basename(projectPath, '.csproj');
    if (seenNames.has(name)) {
      throw new Error(`[expo-modules-dotnet-autolinking] Managed solution graph contains duplicate project name ${name}.`);
    }
    seenNames.add(name);

    return {
      typeGuid: csharpProjectTypeGuid,
      name,
      projectPath: relativeWindowsPath(solutionDirectory, projectPath),
      guid: stableSolutionGuid(normalized),
      block: '',
      order: index,
    };
  }).sort((left, right) => left.order - right.order || left.name.localeCompare(right.name));
}

function relativeWindowsPath(from: string, to: string): string {
  const relative = path.relative(from, to).replace(/\//g, '\\');
  return relative === '' ? path.basename(to) : relative;
}

function insertProjectsBeforeGlobal(
  source: string,
  projects: SolutionProject[],
  eol: string
): string {
  const blocks = projects
    .map(
      (project) =>
        `Project("${project.typeGuid}") = "${project.name}", "${project.projectPath}", "${project.guid}"${eol}EndProject${eol}`
    )
    .join('');
  const marker = `${eol}Global${eol}`;
  const index = source.indexOf(marker);
  if (index < 0) {
    throw new Error('[expo-modules-dotnet-autolinking] Solution does not contain a Global section.');
  }
  const beforeGlobal = source.slice(0, index);
  const separator = beforeGlobal.endsWith(eol) ? '' : eol;
  return `${beforeGlobal}${separator}${blocks}${source.slice(index)}`;
}

function replaceProjectConfigurations(
  source: string,
  ownedGuids: Set<string>,
  managedProjects: ManagedProject[],
  eol: string
): string {
  const section = findGlobalSection(source, 'ProjectConfigurationPlatforms');
  if (section === undefined) {
    throw new Error('[expo-modules-dotnet-autolinking] Solution does not contain ProjectConfigurationPlatforms.');
  }

  const configurations = Array.from(
    source.matchAll(/^\s*(Debug|Release)\|([^\s=]+)\s*=\s*\1\|\2\s*$/gm),
    (match) => `${match[1]}|${match[2]}`
  );
  const retained = section.body
    .split(/\r?\n/)
    .filter((line) => !Array.from(ownedGuids).some((guid) => line.toUpperCase().includes(`${guid}.`)));
  const additions = managedProjects.flatMap((project) =>
    configurations.map((configuration) => {
      const [configurationName, platformName] = configuration.split('|');
      return `\t\t${project.guid}.${configurationName}|${platformName}.ActiveCfg = ${configurationName}|Any CPU`;
    })
  );
  return replaceGlobalSection(source, section, [...retained.filter(Boolean), ...additions].join(eol), eol);
}

function replaceNestedProjects(
  source: string,
  ownedGuids: Set<string>,
  folderGuid: string,
  managedProjects: ManagedProject[],
  eol: string
): string {
  const section = findGlobalSection(source, 'NestedProjects');
  const retained = section?.body
    .split(/\r?\n/)
    .filter(
      (line) =>
        !Array.from(ownedGuids).some((guid) => line.toUpperCase().includes(guid)) &&
        !line.toUpperCase().includes(folderGuid)
    )
    .filter(Boolean) ?? [];
  const additions = managedProjects.map((project) => `\t\t${project.guid} = ${folderGuid}`);

  if (section === undefined) {
    const marker = `${eol}EndGlobal`;
    const index = source.lastIndexOf(marker);
    if (index < 0) {
      throw new Error('[expo-modules-dotnet-autolinking] Solution does not contain EndGlobal.');
    }
    const block = `${eol}\tGlobalSection(NestedProjects) = preSolution${eol}${[...retained, ...additions].join(eol)}${eol}\tEndGlobalSection`;
    return `${source.slice(0, index)}${block}${source.slice(index)}`;
  }

  return replaceGlobalSection(source, section, [...retained, ...additions].join(eol), eol);
}

function findGlobalSection(source: string, name: string): { start: number; end: number; header: string; body: string } | undefined {
  const expression = new RegExp(`^(\\s*GlobalSection\\(${escapeRegExp(name)}\\) = [^\\r\\n]+)(\\r?\\n)([\\s\\S]*?)^\\s*EndGlobalSection`, 'm');
  const match = expression.exec(source);
  if (match === null || match.index === undefined) {
    return undefined;
  }
  return {
    start: match.index,
    end: match.index + match[0].length,
    header: match[1],
    body: match[3].replace(/\r?\n$/, ''),
  };
}

function replaceGlobalSection(
  source: string,
  section: { start: number; end: number; header: string },
  body: string,
  eol: string
): string {
  const replacement = `${section.header}${eol}${body === '' ? '' : `${body}${eol}`}\tEndGlobalSection`;
  return `${source.slice(0, section.start)}${replacement}${source.slice(section.end)}`;
}

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
