import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { describe, expect, it } from 'vitest';

import { generateAggregator } from '../codegen/generateAggregator';
import { discoverDotnetManifestAsync } from '../discovery';

function makeTempRoot(): string {
  return fs.mkdtempSync(path.join(os.tmpdir(), 'dotnet-autolinking-e2e-'));
}

function writeFile(filePath: string, contents: string): void {
  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  fs.writeFileSync(filePath, contents);
}

function makeAdapterPackageRoot(root: string): string {
  const adapterPackageRoot = path.join(root, 'fake-adapter');
  writeFile(
    path.join(adapterPackageRoot, 'managed/packages/Expo.JSI/Expo.JSI.csproj'),
    '<Project Sdk="Microsoft.NET.Sdk" />'
  );
  writeFile(
    path.join(adapterPackageRoot, 'managed/packages/Expo.ModulesCore/Expo.ModulesCore.csproj'),
    '<Project Sdk="Microsoft.NET.Sdk" />'
  );
  return adapterPackageRoot;
}

function makeFixture(): { adapterPackageRoot: string; fixtureRoot: string; outputDir: string } {
  const root = makeTempRoot();
  const fixtureRoot = path.join(root, 'fixture-app');
  const fakeModuleRoot = path.join(fixtureRoot, 'node_modules/fake-module');

  writeFile(
    path.join(fixtureRoot, 'package.json'),
    JSON.stringify(
      {
        name: 'fixture-app',
        dependencies: { 'fake-module': '1.0.0' },
      },
      null,
      2
    )
  );
  writeFile(
    path.join(fakeModuleRoot, 'package.json'),
    JSON.stringify({ name: 'fake-module', version: '1.0.0' }, null, 2)
  );
  writeFile(
    path.join(fakeModuleRoot, 'expo-module.config.json'),
    JSON.stringify(
      {
        platforms: ['dotnet'],
        dotnet: {
          projects: [
            {
              path: 'dotnet/FakeModule/FakeModule.csproj',
              assemblyName: 'FakeModule',
            },
          ],
        },
      },
      null,
      2
    )
  );
  writeFile(
    path.join(fakeModuleRoot, 'dotnet/FakeModule/FakeModule.csproj'),
    '<Project Sdk="Microsoft.NET.Sdk" />'
  );

  return {
    adapterPackageRoot: makeAdapterPackageRoot(root),
    fixtureRoot,
    outputDir: path.join(root, 'generated-dotnet-host'),
  };
}

describe('dotnet autolinking e2e', () => {
  it('discovers dotnet modules from dependencies and generates the host project', async () => {
    const { adapterPackageRoot, fixtureRoot, outputDir } = makeFixture();

    const manifest = await discoverDotnetManifestAsync(fixtureRoot);

    expect(manifest.modules).toHaveLength(1);
    expect(manifest.modules[0]).toMatchObject({
      packageName: 'fake-module',
      projects: [{ assemblyName: 'FakeModule' }],
    });

    generateAggregator(manifest, { outputDir, adapterPackageRoot });

    const provider = fs.readFileSync(path.join(outputDir, 'LinkedExpoModulesProvider.g.cs'), 'utf8');
    expect(provider).toContain('ExpoModulesProvider_FakeModule.Register(context);');

    const csproj = fs.readFileSync(path.join(outputDir, 'ExpoDotnetHost.csproj'), 'utf8');
    const includes = [...csproj.matchAll(/<ProjectReference Include="([^"]+)" \/>/g)].map(
      (match) => match[1]
    );
    const moduleReference = includes.find((include) =>
      include.endsWith('node_modules/fake-module/dotnet/FakeModule/FakeModule.csproj')
    );
    expect(moduleReference).toBeDefined();
    expect(path.isAbsolute(moduleReference!)).toBe(false);
    expect(moduleReference).not.toContain('\\');
  });
});
