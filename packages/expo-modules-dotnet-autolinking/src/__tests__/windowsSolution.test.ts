import * as path from 'path';

import { describe, expect, it } from 'vitest';

import {
  synchronizeManagedSolution,
  type ManagedSolutionGraph,
} from '../windows/solution';

const solutionPath = path.join('fixtures', 'app', 'windows', 'App.sln');
const appRoot = path.join('fixtures', 'app');
const adapterRoot = path.join('fixtures', 'adapter');
const moduleRoot = path.join('fixtures', 'module');

const source = [
  'Microsoft Visual Studio Solution File, Format Version 12.00',
  '# Visual Studio Version 17',
  'Project("{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}") = "App", "App\\App.vcxproj", "{11111111-1111-1111-1111-111111111111}"',
  'EndProject',
  'Project("{66A26720-8FB5-11D2-AA7E-00C04F688DDE}") = "User Work", "User Work", "{22222222-2222-2222-2222-222222222222}"',
  'EndProject',
  'Global',
  '\tGlobalSection(SolutionConfigurationPlatforms) = preSolution',
  '\t\tDebug|x64 = Debug|x64',
  '\t\tRelease|ARM64 = Release|ARM64',
  '\tEndGlobalSection',
  '\tGlobalSection(ProjectConfigurationPlatforms) = postSolution',
  '\t\t{11111111-1111-1111-1111-111111111111}.Debug|x64.ActiveCfg = Debug|x64',
  '\t\t{11111111-1111-1111-1111-111111111111}.Debug|x64.Build.0 = Debug|x64',
  '\tEndGlobalSection',
  '\tGlobalSection(NestedProjects) = preSolution',
  '\t\t{33333333-3333-3333-3333-333333333333} = {22222222-2222-2222-2222-222222222222}',
  '\tEndGlobalSection',
  'EndGlobal',
  '',
].join('\r\n');

const graph: ManagedSolutionGraph = {
  hostProjectPath: path.join(appRoot, '.expo', 'dotnet', 'ExpoDotnetHost.csproj'),
  coreProjectPaths: [
    path.join(adapterRoot, 'managed', 'packages', 'Expo.JSI', 'Expo.JSI.csproj'),
    path.join(adapterRoot, 'managed', 'packages', 'Expo.ModulesCore', 'Expo.ModulesCore.csproj'),
  ],
  moduleProjectPaths: [path.join(moduleRoot, 'ExampleModule.csproj')],
};

describe('synchronizeManagedSolution', () => {
  it('adds a deterministic managed folder without building managed projects', () => {
    const result = synchronizeManagedSolution(source, solutionPath, graph, { check: false });

    expect(result.changed).toBe(true);
    expect(result.content).toContain('= "Expo .NET Managed", "Expo .NET Managed",');
    expect(result.content).toContain('= "ExpoDotnetHost", "..\\.expo\\dotnet\\ExpoDotnetHost.csproj",');
    expect(result.content).toContain('= "Expo.JSI", "..\\..\\adapter\\managed\\packages\\Expo.JSI\\Expo.JSI.csproj",');
    expect(result.content).toContain('= "Expo.ModulesCore", "..\\..\\adapter\\managed\\packages\\Expo.ModulesCore\\Expo.ModulesCore.csproj",');
    expect(result.content).toContain('= "ExampleModule", "..\\..\\module\\ExampleModule.csproj",');
    expect(result.content).toContain('= "User Work", "User Work", "{22222222-2222-2222-2222-222222222222}"');
    expect(result.content).toContain('{33333333-3333-3333-3333-333333333333} = {22222222-2222-2222-2222-222222222222}');
    expect(result.content).toContain('.Debug|x64.ActiveCfg = Debug|Any CPU');
    expect(result.content).toContain('.Release|ARM64.ActiveCfg = Release|Any CPU');
    expect(result.content).not.toContain('.Debug|x64.Build.0 = Debug|Any CPU');
    expect(result.content).toContain('\r\n');
  });

  it('reports stale output without writing in check mode', () => {
    const result = synchronizeManagedSolution(source, solutionPath, graph, { check: true });

    expect(result).toEqual({ changed: true, content: source });
  });

  it('preserves a UTF-8 BOM and leading blank line before the solution header', () => {
    const prefixedSource = `\uFEFF\r\n${source}`;

    const result = synchronizeManagedSolution(prefixedSource, solutionPath, graph, { check: false });

    expect(result.content.startsWith('\uFEFF\r\nMicrosoft Visual Studio Solution File')).toBe(true);
  });

  it('creates a separated NestedProjects section when one is absent', () => {
    const withoutNestedProjects = source.replace(
      '\tGlobalSection(NestedProjects) = preSolution\r\n\t\t{33333333-3333-3333-3333-333333333333} = {22222222-2222-2222-2222-222222222222}\r\n\tEndGlobalSection\r\n',
      ''
    );

    const result = synchronizeManagedSolution(withoutNestedProjects, solutionPath, graph, { check: false });

    expect(result.content).toContain('\tEndGlobalSection\r\n\tGlobalSection(NestedProjects) = preSolution');
  });

  it('replaces only the managed folder when a module is removed', () => {
    const initial = synchronizeManagedSolution(source, solutionPath, graph, { check: false });
    const withoutModule = synchronizeManagedSolution(
      initial.content,
      solutionPath,
      { ...graph, moduleProjectPaths: [] },
      { check: false }
    );

    expect(withoutModule.changed).toBe(true);
    expect(withoutModule.content).not.toContain('ExampleModule.csproj');
    expect(withoutModule.content).toContain('User Work');
    expect(withoutModule.content).toContain(
      '{33333333-3333-3333-3333-333333333333} = {22222222-2222-2222-2222-222222222222}'
    );
    expect(synchronizeManagedSolution(withoutModule.content, solutionPath, { ...graph, moduleProjectPaths: [] }, { check: false })).toEqual({
      changed: false,
      content: withoutModule.content,
    });
  });

  it('rejects malformed or ambiguous solution ownership', () => {
    expect(() => synchronizeManagedSolution('not a solution', solutionPath, graph, { check: false })).toThrow(
      'does not contain a Visual Studio solution header'
    );

    const first = synchronizeManagedSolution(source, solutionPath, graph, { check: false }).content;
    const secondFolder = first.replace(
      'Global',
      'Project("{66A26720-8FB5-11D2-AA7E-00C04F688DDE}") = "Expo .NET Managed", "Expo .NET Managed", "{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}"\r\nEndProject\r\nGlobal'
    );
    expect(() => synchronizeManagedSolution(secondFolder, solutionPath, graph, { check: false })).toThrow(
      'contains multiple Expo .NET Managed folders'
    );
  });
});
