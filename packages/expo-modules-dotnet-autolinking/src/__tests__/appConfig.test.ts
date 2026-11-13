import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { describe, expect, it } from 'vitest';

import { appConfigForPlatformAsync, computeAppConfigAsync, manifestNeedsAppConfig } from '../appConfig';

function write(filePath: string, contents: string): void {
  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  fs.writeFileSync(filePath, contents);
}

function writePackage(root: string, name: string, main: string): void {
  write(path.join(root, 'package.json'), JSON.stringify({ name, version: '1.0.0', main: 'index.js' }));
  write(path.join(root, 'index.js'), main);
}

// A fake @expo/config that records how it was called, so the test can check the
// options, the working directory, and that env files were loaded first.
const fakeConfig = `
const fs = require('fs');
const path = require('path');
exports.getConfig = (projectRoot, options) => {
  if (process.env.FAKE_CONFIG_THROW) throw new Error('app config failed to evaluate');
  const app = JSON.parse(fs.readFileSync(path.join(projectRoot, 'app.json'), 'utf8')).expo;
  return {
    exp: {
      ...app,
      options,
      cwd: process.cwd(),
      envLoaded: process.env.FAKE_ENV_LOADED ?? null,
      macos: { bundleIdentifier: 'com.example.app', config: { secret: 'mac' } },
      windows: { projectGuid: 'guid', config: { secret: 'win' } },
    },
  };
};
`;

function fakeEnv(marker: string): string {
  return `exports.load = (projectRoot) => { process.env.FAKE_ENV_LOADED = '${marker}:' + projectRoot; };`;
}

// The app's node_modules holds only a link to expo-constants, as pnpm lays it
// out. @expo/config and @expo/env exist only next to the real expo-constants
// folder, and a decoy @expo/env sits in a hoisted folder above the app.
function makePnpmProject({ withExpoConstants = true } = {}) {
  const root = fs.realpathSync(fs.mkdtempSync(path.join(os.tmpdir(), 'dotnet-app-config-')));
  const projectRoot = path.join(root, 'app');
  write(path.join(projectRoot, 'package.json'), JSON.stringify({ name: 'app' }));
  write(path.join(projectRoot, 'app.json'), JSON.stringify({ expo: { name: 'Example', slug: 'example' } }));

  const storeModules = path.join(root, '.pnpm', 'expo-constants@18.0.13', 'node_modules');
  const realConstants = path.join(storeModules, 'expo-constants');
  writePackage(realConstants, 'expo-constants', 'module.exports = {};');
  writePackage(path.join(storeModules, '@expo', 'config'), '@expo/config', fakeConfig);
  writePackage(path.join(storeModules, '@expo', 'env'), '@expo/env', fakeEnv('store'));
  writePackage(path.join(root, 'node_modules', '@expo', 'env'), '@expo/env', fakeEnv('hoisted'));

  if (withExpoConstants) {
    fs.mkdirSync(path.join(projectRoot, 'node_modules'), { recursive: true });
    fs.symlinkSync(realConstants, path.join(projectRoot, 'node_modules', 'expo-constants'), 'junction');
  }
  return { projectRoot };
}

describe('computeAppConfigAsync', () => {
  it('computes the public config like upstream, with the packages next to the real expo-constants', async () => {
    const { projectRoot } = makePnpmProject();

    const config = JSON.parse(await computeAppConfigAsync(projectRoot));

    expect(config.name).toBe('Example');
    expect(config.slug).toBe('example');
    expect(config.options).toEqual({ isPublicConfig: true, skipSDKVersionRequirement: true });
    expect(fs.realpathSync(config.cwd)).toBe(fs.realpathSync(projectRoot));
    expect(config.envLoaded).toBe(`store:${projectRoot}`);
  });

  it('removes the desktop config keys and keeps the other desktop keys', async () => {
    const { projectRoot } = makePnpmProject();

    const config = JSON.parse(await computeAppConfigAsync(projectRoot));

    expect(config.macos).toEqual({ bundleIdentifier: 'com.example.app' });
    expect(config.windows).toEqual({ projectGuid: 'guid' });
  });

  it('does not change the environment or working directory of the CLI', async () => {
    const { projectRoot } = makePnpmProject();
    const cwd = process.cwd();

    await computeAppConfigAsync(projectRoot);

    expect(process.cwd()).toBe(cwd);
    expect(process.env.FAKE_ENV_LOADED).toBeUndefined();
  });

  it('tells the user to add expo-constants when the app does not declare it', async () => {
    const { projectRoot } = makePnpmProject({ withExpoConstants: false });

    await expect(computeAppConfigAsync(projectRoot)).rejects.toThrow(
      /expo-constants.*Add expo-constants to the app's dependencies/s
    );
  });

  it('fails with the underlying error when the app config throws', async () => {
    const { projectRoot } = makePnpmProject();
    process.env.FAKE_CONFIG_THROW = '1';
    try {
      await expect(computeAppConfigAsync(projectRoot)).rejects.toThrow(/app config failed to evaluate/);
    } finally {
      delete process.env.FAKE_CONFIG_THROW;
    }
  });
});

describe('appConfigForPlatformAsync', () => {
  const declaring = { packageName: 'a', packageRoot: '/a', projects: [], substitutions: [], appConfig: true };

  it('computes nothing for ios and android or without a declaring module', async () => {
    const missingProject = path.join(os.tmpdir(), 'no-such-project');

    await expect(appConfigForPlatformAsync('ios', missingProject, { modules: [declaring] })).resolves.toBeUndefined();
    await expect(appConfigForPlatformAsync('android', missingProject, { modules: [declaring] })).resolves.toBeUndefined();
    await expect(
      appConfigForPlatformAsync('windows', missingProject, { modules: [{ ...declaring, appConfig: false }] })
    ).resolves.toBeUndefined();
  });

  it('computes the config for windows when a module declares it', async () => {
    const { projectRoot } = makePnpmProject();

    const content = await appConfigForPlatformAsync('windows', projectRoot, { modules: [declaring] });

    expect(JSON.parse(content ?? '{}').slug).toBe('example');
  });
});

describe('manifestNeedsAppConfig', () => {
  it('is true only when a resolved module declares appConfig', () => {
    const module = { packageName: 'a', packageRoot: '/a', projects: [], substitutions: [], appConfig: false };

    expect(manifestNeedsAppConfig({ modules: [module] })).toBe(false);
    expect(manifestNeedsAppConfig({ modules: [module, { ...module, appConfig: true }] })).toBe(true);
  });
});
