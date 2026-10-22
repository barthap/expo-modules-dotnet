import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import type { DotnetLinkingManifest } from '../types';

const manifest: DotnetLinkingManifest = {
  modules: [
    {
      packageName: 'example-module',
      packageRoot: 'module',
      projects: [{ csprojPath: path.resolve('module', 'ExampleModule.csproj'), assemblyName: 'ExampleModule' }],
    },
  ],
};

vi.mock('../discovery', () => ({ discoverDotnetManifestAsync: vi.fn(async () => manifest) }));
vi.mock('../paths', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../paths')>()),
  findAdapterPackageRoot: vi.fn(() => path.resolve('adapter')),
}));

const solution = [
  'Microsoft Visual Studio Solution File, Format Version 12.00',
  'Project("{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}") = "MyApp", "MyApp\\MyApp.vcxproj", "{11111111-1111-1111-1111-111111111111}"',
  'EndProject',
  'Project("{C7167F0D-BC9F-4E6E-AFE1-012C56B48DB5}") = "MyApp.Package", "MyApp.Package\\MyApp.Package.wapproj", "{22222222-2222-2222-2222-222222222222}"',
  'EndProject',
  'Global',
  '\tGlobalSection(SolutionConfigurationPlatforms) = preSolution',
  '\t\tDebug|x64 = Debug|x64',
  '\tEndGlobalSection',
  '\tGlobalSection(ProjectConfigurationPlatforms) = postSolution',
  '\t\t{11111111-1111-1111-1111-111111111111}.Debug|x64.ActiveCfg = Debug|x64',
  '\tEndGlobalSection',
  'EndGlobal',
  '',
].join('\r\n');
const packageProject = ['<Project>', '  <PropertyGroup>', '    <DebuggerType>NativeOnly</DebuggerType>', '  </PropertyGroup>', '</Project>', ''].join('\r\n');

let appRoot: string;
let solutionPath: string;
let packagePath: string;

beforeEach(() => {
  appRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'expo-dotnet-prepare-windows-'));
  solutionPath = path.join(appRoot, 'windows', 'MyApp.sln');
  packagePath = path.join(appRoot, 'windows', 'MyApp.Package', 'MyApp.Package.wapproj');
  fs.mkdirSync(path.dirname(packagePath), { recursive: true });
  fs.writeFileSync(solutionPath, solution);
  fs.writeFileSync(packagePath, packageProject);
});

afterEach(() => {
  fs.rmSync(appRoot, { force: true, recursive: true });
});

describe('prepareWindowsSolutionAsync', () => {
  it('generates ExpoDotnetHost.csproj before projecting it into the solution', async () => {
    const { prepareWindowsSolutionAsync } = await import('../index');

    const result = await prepareWindowsSolutionAsync({ appRoot, solutionPath });

    expect(result.changed).toBe(true);
    expect(fs.existsSync(path.join(appRoot, '.expo', 'dotnet', 'ExpoDotnetHost.csproj'))).toBe(true);
    const updated = fs.readFileSync(solutionPath, 'utf8');
    expect(updated).toContain('"Expo .NET Managed"');
    expect(updated).toContain('"ExpoDotnetHost"');
  });

  it('sets mixed-mode debugging on the package project', async () => {
    const { prepareWindowsSolutionAsync } = await import('../index');

    await prepareWindowsSolutionAsync({ appRoot, solutionPath });

    expect(fs.readFileSync(packagePath, 'utf8')).toContain('<DebuggerType>Mixed</DebuggerType>');
  });

  it('is idempotent', async () => {
    const { prepareWindowsSolutionAsync } = await import('../index');
    await prepareWindowsSolutionAsync({ appRoot, solutionPath });
    const first = fs.readFileSync(solutionPath, 'utf8');

    const second = await prepareWindowsSolutionAsync({ appRoot, solutionPath });

    expect(second.changed).toBe(false);
    expect(fs.readFileSync(solutionPath, 'utf8')).toBe(first);
  });

  it('check mode writes nothing and reports drift', async () => {
    const { prepareWindowsSolutionAsync } = await import('../index');

    const result = await prepareWindowsSolutionAsync({ appRoot, solutionPath, check: true });

    expect(result.changed).toBe(true);
    expect(fs.readFileSync(solutionPath, 'utf8')).toBe(solution);
    expect(fs.readFileSync(packagePath, 'utf8')).toBe(packageProject);
    expect(fs.existsSync(path.join(appRoot, '.expo'))).toBe(false);
  });
});
