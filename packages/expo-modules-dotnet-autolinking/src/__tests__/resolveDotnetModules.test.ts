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
          substitutions: [],
          appConfig: false,
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

  describe('appConfig', () => {
    function buildWithAppConfig(appConfig: unknown) {
      const root = makeTempRoot();
      makePackage(root, 'dotnet/Constants/Constants.csproj');
      return buildDotnetManifest([
        {
          packageName: 'expo-constants-dotnet',
          packageRoot: root,
          dotnetConfig: { projects: [{ path: 'dotnet/Constants/Constants.csproj' }], appConfig } as never,
        },
      ]);
    }

    it('records a declared appConfig', () => {
      expect(buildWithAppConfig(true).modules[0]?.appConfig).toBe(true);
      expect(buildWithAppConfig(false).modules[0]?.appConfig).toBe(false);
      expect(buildWithAppConfig(undefined).modules[0]?.appConfig).toBe(false);
    });

    it('rejects an appConfig that is not a boolean', () => {
      expect(() => buildWithAppConfig('yes')).toThrow(/expo-constants-dotnet.*dotnet\.appConfig.*boolean/s);
    });
  });

  describe('substitutions', () => {
    const csprojRel = 'dotnet/Sub/Sub.csproj';

    function makeSubstitutingPackage(replacement = 'src/index.ts'): string {
      const root = makeTempRoot();
      makePackage(root, csprojRel);
      const replacementPath = path.join(root, replacement);
      fs.mkdirSync(path.dirname(replacementPath), { recursive: true });
      fs.writeFileSync(replacementPath, 'export {};');
      return root;
    }

    function validEntry(overrides: Record<string, unknown> = {}): Record<string, unknown> {
      return {
        package: 'expo-asset',
        versionRange: '~12.0.13',
        accessor: 'build/ExpoAsset.js',
        replacement: 'src/index.ts',
        ...overrides,
      };
    }

    function build(root: string, substitutions: unknown, packageName = 'expo-asset-dotnet') {
      return buildDotnetManifest([
        {
          packageName,
          packageRoot: root,
          dotnetConfig: { projects: [{ path: csprojRel }], substitutions } as never,
        },
      ]);
    }

    it('adds a valid declaration with the absolute replacement path', () => {
      const root = makeSubstitutingPackage();

      const manifest = build(root, [validEntry()]);

      expect(manifest.modules[0]?.substitutions).toEqual([
        {
          package: 'expo-asset',
          versionRange: '~12.0.13',
          accessor: 'build/ExpoAsset.js',
          replacementPath: path.join(root, 'src', 'index.ts'),
        },
      ]);
    });

    it('lists no substitutions when the module declares none', () => {
      const root = makeSubstitutingPackage();

      expect(build(root, undefined).modules[0]?.substitutions).toEqual([]);
    });

    it('rejects a substitutions value that is not a list', () => {
      const root = makeSubstitutingPackage();

      expect(() => build(root, { package: 'expo-asset' })).toThrow(
        /expo-asset-dotnet.*dotnet\.substitutions.*list/s
      );
    });

    it.each([
      ['package', { package: undefined }],
      ['package', { package: '' }],
      ['versionRange', { versionRange: 12 }],
      ['versionRange', { versionRange: 'not a range' }],
      ['accessor', { accessor: '/abs/ExpoAsset.js' }],
      ['accessor', { accessor: '../other/ExpoAsset.js' }],
      ['accessor', { accessor: 'build\\ExpoAsset.js' }],
      ['replacement', { replacement: 'C:/abs/index.ts' }],
      ['replacement', { replacement: 'src/../../index.ts' }],
    ])('rejects an invalid %s field', (field, overrides) => {
      const root = makeSubstitutingPackage();

      expect(() => build(root, [validEntry(overrides)])).toThrow(
        new RegExp(String.raw`expo-asset-dotnet.*substitutions\[0\]\.` + field, 's')
      );
    });

    it('rejects an unknown field', () => {
      const root = makeSubstitutingPackage();

      expect(() => build(root, [validEntry({ platform: 'windows' })])).toThrow(
        /expo-asset-dotnet.*substitutions\[0\].*"platform"/s
      );
    });

    it('rejects a replacement file that does not exist', () => {
      const root = makeSubstitutingPackage();

      expect(() => build(root, [validEntry({ replacement: 'src/missing.ts' })])).toThrow(
        /expo-asset-dotnet.*src\/missing\.ts/s
      );
    });

    it('rejects one package declared twice by one module', () => {
      const root = makeSubstitutingPackage();

      expect(() => build(root, [validEntry(), validEntry()])).toThrow(
        /expo-asset-dotnet.*"expo-asset".*more than once/s
      );
    });

    it('rejects substitutions on a package without dotnet projects', () => {
      const root = makeSubstitutingPackage();

      expect(() =>
        buildDotnetManifest([
          {
            packageName: 'expo-asset-dotnet',
            packageRoot: root,
            dotnetConfig: { substitutions: [validEntry()] } as never,
          },
        ])
      ).toThrow(/expo-asset-dotnet.*substitutions.*dotnet\.projects/s);
    });

    it('rejects two modules that declare the same package, naming both', () => {
      const rootA = makeSubstitutingPackage();
      const rootB = makeSubstitutingPackage();

      expect(() =>
        buildDotnetManifest([
          {
            packageName: 'first-dotnet',
            packageRoot: rootA,
            dotnetConfig: {
              projects: [{ path: csprojRel, assemblyName: 'First' }],
              substitutions: [validEntry()],
            } as never,
          },
          {
            packageName: 'second-dotnet',
            packageRoot: rootB,
            dotnetConfig: {
              projects: [{ path: csprojRel, assemblyName: 'Second' }],
              substitutions: [validEntry()],
            } as never,
          },
        ])
      ).toThrow(/first-dotnet.*second-dotnet.*"expo-asset"/s);
    });
  });
});
