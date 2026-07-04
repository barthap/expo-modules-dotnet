import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { describe, expect, it } from 'vitest';

import { buildDotnetManifest } from '../resolveDotnetModules';

function makeTempRoot(): string {
  return fs.mkdtempSync(path.join(os.tmpdir(), 'dotnet-autolink-'));
}

function makePackage(root: string, csprojRel: string): void {
  const csprojPath = path.join(root, csprojRel);
  fs.mkdirSync(path.dirname(csprojPath), { recursive: true });
  fs.writeFileSync(csprojPath, '<Project Sdk="Microsoft.NET.Sdk" />');
}

describe('buildDotnetManifest', () => {
  it('resolves projects and defaults assemblyName to csproj basename', () => {
    const root = makeTempRoot();
    const csprojRel = 'dotnet/ExampleModule/ExampleModule.csproj';
    makePackage(root, csprojRel);

    const manifest = buildDotnetManifest([
      {
        packageName: 'example-module',
        packageRoot: root,
        dotnetConfig: { projects: [{ path: csprojRel }] },
      },
    ]);

    expect(manifest).toEqual({
      modules: [
        {
          packageName: 'example-module',
          packageRoot: root,
          projects: [
            {
              csprojPath: path.join(root, csprojRel),
              assemblyName: 'ExampleModule',
            },
          ],
        },
      ],
    });
  });

  it('honors explicit assemblyName', () => {
    const root = makeTempRoot();
    const csprojRel = 'dotnet/Native/Native.csproj';
    makePackage(root, csprojRel);

    const manifest = buildDotnetManifest([
      {
        packageName: 'native-module',
        packageRoot: root,
        dotnetConfig: {
          projects: [{ path: csprojRel, assemblyName: 'CustomNativeAssembly' }],
        },
      },
    ]);

    expect(manifest.modules[0]?.projects).toEqual([
      {
        csprojPath: path.join(root, csprojRel),
        assemblyName: 'CustomNativeAssembly',
      },
    ]);
  });

  it('skips packages without dotnet config', () => {
    const manifest = buildDotnetManifest([
      {
        packageName: 'plain-package',
        packageRoot: makeTempRoot(),
        dotnetConfig: undefined,
      },
    ]);

    expect(manifest).toEqual({ modules: [] });
  });

  it('throws naming package and path when csproj is missing', () => {
    const root = makeTempRoot();
    const declaredPath = 'missing/Missing.csproj';
    const resolvedPath = path.resolve(root, declaredPath);

    expect(() =>
      buildDotnetManifest([
        {
          packageName: 'broken-pkg',
          packageRoot: root,
          dotnetConfig: { projects: [{ path: declaredPath }] },
        },
      ])
    ).toThrow(
      new RegExp(
        String.raw`\[expo-modules-dotnet-autolinking\].*broken-pkg.*missing/Missing\.csproj.*${resolvedPath.replace(
          /[.*+?^${}()|[\]\\]/g,
          '\\$&'
        )}`,
        's'
      )
    );
  });

  it('throws on duplicate assembly names naming both packages', () => {
    const rootA = makeTempRoot();
    const rootB = makeTempRoot();
    makePackage(rootA, 'dotnet/First/First.csproj');
    makePackage(rootB, 'dotnet/Second/Second.csproj');

    expect(() =>
      buildDotnetManifest([
        {
          packageName: 'pkg-a',
          packageRoot: rootA,
          dotnetConfig: {
            projects: [{ path: 'dotnet/First/First.csproj', assemblyName: 'SharedAssembly' }],
          },
        },
        {
          packageName: 'pkg-b',
          packageRoot: rootB,
          dotnetConfig: {
            projects: [{ path: 'dotnet/Second/Second.csproj', assemblyName: 'SharedAssembly' }],
          },
        },
      ])
    ).toThrow(
      /\[expo-modules-dotnet-autolinking\].*pkg-a.*pkg-b.*SharedAssembly/s
    );
  });

  it('sorts modules by packageName and projects by assemblyName', () => {
    const rootB = makeTempRoot();
    const rootA = makeTempRoot();
    makePackage(rootB, 'dotnet/Zeta/Zeta.csproj');
    makePackage(rootB, 'dotnet/Alpha/Alpha.csproj');
    makePackage(rootA, 'dotnet/Middle/Middle.csproj');

    const manifest = buildDotnetManifest([
      {
        packageName: 'z-package',
        packageRoot: rootB,
        dotnetConfig: {
          projects: [
            { path: 'dotnet/Zeta/Zeta.csproj' },
            { path: 'dotnet/Alpha/Alpha.csproj' },
          ],
        },
      },
      {
        packageName: 'a-package',
        packageRoot: rootA,
        dotnetConfig: {
          projects: [{ path: 'dotnet/Middle/Middle.csproj' }],
        },
      },
    ]);

    expect(manifest.modules.map((module) => module.packageName)).toEqual([
      'a-package',
      'z-package',
    ]);
    expect(manifest.modules[1]?.projects.map((project) => project.assemblyName)).toEqual([
      'Alpha',
      'Zeta',
    ]);
  });
});
