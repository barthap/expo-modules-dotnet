import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { describe, expect, it, vi } from 'vitest';

const { withDotnetModules } = require('../../metro.js');
const { createDotnetModulesConfigAsync } = require('../../plugin/metro.js');

type Resolution = { type: 'sourceFile'; filePath: string } | { type: 'empty' };

interface Fixture {
  appRoot: string;
  assetAccessor: string;
  assetOther: string;
  constantsAccessor: string;
  assetReplacement: string;
  constantsReplacement: string;
  manifest: unknown;
}

function writeFile(filePath: string, contents: string): void {
  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  fs.writeFileSync(filePath, contents);
}

function writePackage(root: string, name: string, version: string, files: string[]): void {
  writeFile(path.join(root, 'package.json'), JSON.stringify({ name, version }));
  for (const file of files) {
    writeFile(path.join(root, ...file.split('/')), 'export {};');
  }
}

// expo-asset is a direct dependency of the app. expo-constants is reachable
// only through the app's expo package, as in an app that does not list it.
function makeFixture({
  assetVersion = '12.0.13',
  assetFiles = ['build/ExpoAsset.js', 'build/Asset.js'],
} = {}): Fixture {
  const root = fs.realpathSync(fs.mkdtempSync(path.join(os.tmpdir(), 'dotnet-metro-')));
  const appRoot = path.join(root, 'app');
  writeFile(path.join(appRoot, 'package.json'), JSON.stringify({ name: 'app' }));

  const assetRoot = path.join(appRoot, 'node_modules', 'expo-asset');
  writePackage(assetRoot, 'expo-asset', assetVersion, assetFiles);
  // A nested package.json without a name must not end the owner lookup.
  writeFile(path.join(assetRoot, 'build', 'package.json'), JSON.stringify({ type: 'module' }));

  const expoRoot = path.join(appRoot, 'node_modules', 'expo');
  writePackage(expoRoot, 'expo', '54.0.35', []);
  const constantsRoot = path.join(expoRoot, 'node_modules', 'expo-constants');
  writePackage(constantsRoot, 'expo-constants', '18.0.13', ['build/ExponentConstants.js']);

  const assetModuleRoot = path.join(root, 'expo-asset-dotnet');
  const constantsModuleRoot = path.join(root, 'expo-constants-dotnet');
  const assetReplacement = path.join(assetModuleRoot, 'src', 'index.ts');
  const constantsReplacement = path.join(constantsModuleRoot, 'src', 'index.ts');
  writeFile(assetReplacement, 'export {};');
  writeFile(constantsReplacement, 'export {};');

  return {
    appRoot,
    assetAccessor: path.join(assetRoot, 'build', 'ExpoAsset.js'),
    assetOther: path.join(assetRoot, 'build', 'Asset.js'),
    constantsAccessor: path.join(constantsRoot, 'build', 'ExponentConstants.js'),
    assetReplacement,
    constantsReplacement,
    manifest: {
      modules: [
        {
          packageName: 'expo-asset-dotnet',
          packageRoot: assetModuleRoot,
          projects: [],
          substitutions: [
            {
              package: 'expo-asset',
              versionRange: '~12.0.13',
              accessor: 'build/ExpoAsset.js',
              replacementPath: assetReplacement,
            },
          ],
        },
        {
          packageName: 'expo-constants-dotnet',
          packageRoot: constantsModuleRoot,
          projects: [],
          substitutions: [
            {
              package: 'expo-constants',
              versionRange: '~18.0.13',
              accessor: 'build/ExponentConstants.js',
              replacementPath: constantsReplacement,
            },
          ],
        },
        {
          packageName: 'example-module',
          packageRoot: path.join(root, 'example-module'),
          projects: [],
          substitutions: [],
        },
      ],
    },
  };
}

function baseConfig(fixture: Fixture, resolveRequest?: unknown) {
  return {
    projectRoot: fixture.appRoot,
    watchFolders: ['watched'],
    resolver: { sourceExts: ['ts', 'js'], ...(resolveRequest ? { resolveRequest } : {}) },
    transformer: { marker: true },
  };
}

// The previous resolver resolves a request to the file path given as the
// module name, so a test controls which file Metro "found".
function passthroughResolver() {
  return vi.fn((_context: unknown, moduleName: string): Resolution => ({
    type: 'sourceFile',
    filePath: moduleName,
  }));
}

async function substitutedResolver(fixture: Fixture, substitute: string[]) {
  const previous = passthroughResolver();
  const config = await createDotnetModulesConfigAsync(baseConfig(fixture, previous), substitute, {
    loadManifest: async () => fixture.manifest,
  });
  return { config, previous, resolve: config.resolver.resolveRequest };
}

describe('withDotnetModules options', () => {
  it.each([
    [{ substitutes: ['expo-asset'] }, /unknown option "substitutes"/],
    ['expo-asset', /options must be an object/],
    [{ substitute: 'expo-asset' }, /"substitute" must be a list/],
    [{ substitute: [1] }, /"substitute\[0\]" must be a non-empty string/],
    [{ substitute: [''] }, /"substitute\[0\]" must be a non-empty string/],
    [{ substitute: ['expo-asset', 'expo-asset'] }, /"expo-asset" more than once/],
  ])('throws synchronously for %j', (options, message) => {
    expect(() => withDotnetModules({}, options)).toThrow(message);
  });
});

describe('withDotnetModules opt-in off', () => {
  it.each([[undefined], [{}], [{ substitute: [] }]])(
    'resolves to the same config object for %j without discovery',
    async (options) => {
      const config = { projectRoot: '/does/not/exist', resolver: {} };

      await expect(withDotnetModules(config, options)).resolves.toBe(config);
    }
  );

  it('accepts a Promise of a config', async () => {
    const config = { projectRoot: '/does/not/exist', resolver: {} };

    await expect(withDotnetModules(Promise.resolve(config))).resolves.toBe(config);
  });
});

describe('withDotnetModules discovery', () => {
  it('fails when expo-modules-dotnet-autolinking does not resolve from the app root', async () => {
    const fixture = makeFixture();

    await expect(
      withDotnetModules(baseConfig(fixture), { substitute: ['expo-asset'] })
    ).rejects.toThrow(/could not resolve expo-modules-dotnet-autolinking from app root/);
  });

  it('fails when the config has no projectRoot', async () => {
    await expect(
      createDotnetModulesConfigAsync({ resolver: {} }, ['expo-asset'], {
        loadManifest: async () => ({ modules: [] }),
      })
    ).rejects.toThrow(/projectRoot/);
  });
});

describe('startup checks', () => {
  it('rejects a name that no installed module declares, listing the declared names', async () => {
    const fixture = makeFixture();

    await expect(substitutedResolver(fixture, ['expo-camera'])).rejects.toThrow(
      /"expo-camera".*expo-asset, expo-constants/s
    );
  });

  it('rejects an upstream package that the app cannot resolve', async () => {
    const fixture = makeFixture();
    fs.rmSync(path.join(fixture.appRoot, 'node_modules', 'expo-asset'), { recursive: true });

    await expect(substitutedResolver(fixture, ['expo-asset'])).rejects.toThrow(
      /"expo-asset".*not installed/s
    );
  });

  it('rejects a missing accessor file, naming the package, version, and file', async () => {
    const fixture = makeFixture({ assetFiles: ['build/Asset.js'] });

    await expect(substitutedResolver(fixture, ['expo-asset'])).rejects.toThrow(
      /expo-asset@12\.0\.13.*build\/ExpoAsset\.js.*expo-asset-dotnet/s
    );
  });

  it('rejects an upstream version outside the declared range', async () => {
    const fixture = makeFixture({ assetVersion: '13.0.0' });

    await expect(substitutedResolver(fixture, ['expo-asset'])).rejects.toThrow(
      /expo-asset@13\.0\.0.*~12\.0\.13.*expo-asset-dotnet/s
    );
  });

  it("finds an upstream package through the app's expo package", async () => {
    const fixture = makeFixture();

    await expect(substitutedResolver(fixture, ['expo-constants'])).resolves.toBeDefined();
  });
});

describe('substituting resolver', () => {
  it('resolves each selected accessor to its replacement on windows', async () => {
    const fixture = makeFixture();
    const { resolve } = await substitutedResolver(fixture, ['expo-asset', 'expo-constants']);

    expect(resolve({}, fixture.assetAccessor, 'windows')).toEqual({
      type: 'sourceFile',
      filePath: fixture.assetReplacement,
    });
    expect(resolve({}, fixture.constantsAccessor, 'windows')).toEqual({
      type: 'sourceFile',
      filePath: fixture.constantsReplacement,
    });
  });

  it('leaves other files of a selected package unchanged', async () => {
    const fixture = makeFixture();
    const { resolve } = await substitutedResolver(fixture, ['expo-asset']);

    expect(resolve({}, fixture.assetOther, 'windows')).toEqual({
      type: 'sourceFile',
      filePath: fixture.assetOther,
    });
  });

  it('leaves an unselected module unchanged', async () => {
    const fixture = makeFixture();
    const { resolve } = await substitutedResolver(fixture, ['expo-constants']);

    expect(resolve({}, fixture.assetAccessor, 'windows')).toEqual({
      type: 'sourceFile',
      filePath: fixture.assetAccessor,
    });
  });

  it.each(['macos', 'ios', 'android'])('leaves resolution unchanged for %s', async (platform) => {
    const fixture = makeFixture();
    const { resolve } = await substitutedResolver(fixture, ['expo-asset', 'expo-constants']);

    expect(resolve({}, fixture.assetAccessor, platform)).toEqual({
      type: 'sourceFile',
      filePath: fixture.assetAccessor,
    });
    expect(resolve({}, fixture.constantsAccessor, platform)).toEqual({
      type: 'sourceFile',
      filePath: fixture.constantsAccessor,
    });
  });

  it('returns a non-file resolution unchanged', async () => {
    const fixture = makeFixture();
    const previous = vi.fn((): Resolution => ({ type: 'empty' }));
    const config = await createDotnetModulesConfigAsync(baseConfig(fixture, previous), ['expo-asset'], {
      loadManifest: async () => fixture.manifest,
    });

    expect(config.resolver.resolveRequest({}, 'anything', 'windows')).toEqual({ type: 'empty' });
  });

  it('chains to the config resolver with the original arguments', async () => {
    const fixture = makeFixture();
    const { resolve, previous } = await substitutedResolver(fixture, ['expo-asset']);
    const context = { marker: 'context' };

    resolve(context, fixture.assetAccessor, 'windows');

    expect(previous).toHaveBeenCalledWith(context, fixture.assetAccessor, 'windows');
  });

  it("uses Metro's default resolver when the config has none", async () => {
    const fixture = makeFixture();
    const config = await createDotnetModulesConfigAsync(baseConfig(fixture), ['expo-asset'], {
      loadManifest: async () => fixture.manifest,
    });
    const context = { resolveRequest: passthroughResolver() };

    expect(config.resolver.resolveRequest(context, fixture.assetAccessor, 'windows')).toEqual({
      type: 'sourceFile',
      filePath: fixture.assetReplacement,
    });
    expect(context.resolveRequest).toHaveBeenCalledWith(context, fixture.assetAccessor, 'windows');
  });

  it('changes no other config field', async () => {
    const fixture = makeFixture();
    const input = baseConfig(fixture, passthroughResolver());
    const { config } = await substitutedResolver(fixture, ['expo-asset']);

    const { resolveRequest: _ignored, ...resolverRest } = config.resolver;
    expect(resolverRest).toEqual({ sourceExts: ['ts', 'js'] });
    expect(config.watchFolders).toEqual(input.watchFolders);
    expect(config.transformer).toEqual(input.transformer);
    expect(config.projectRoot).toBe(input.projectRoot);
  });
});
